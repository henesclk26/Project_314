using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum MeetingState : byte
{
    None = 0,
    Discussion = 1,
    Voting = 2,
    Results = 3
}

public class MeetingManager : NetworkBehaviour
{
    public static MeetingManager Instance { get; private set; }

    public NetworkVariable<MeetingState> State = new NetworkVariable<MeetingState>(MeetingState.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> StateEndTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> ReporterId = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> DeadBodyId = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> EjectedPlayerId = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> WasTie = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<byte> ReportedBodyAgeBand = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Votes: Key is voter ClientId, Value is voted ClientId (ulong.MaxValue for abstain)
    private Dictionary<ulong, ulong> votes = new Dictionary<ulong, ulong>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestEmergencyMeetingServerRpc(ulong callerId, RpcParams rpcParams = default)
    {
        if (!IsServer || MatchFlowManager.Instance == null)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (callerId != senderClientId)
            return;

        EmergencyButtonInteractable button = FindAnyObjectByType<EmergencyButtonInteractable>();
        if (button == null || !button.IsPlayerInRange(senderClientId))
            return;

        if (MatchFlowManager.Instance.IsEmergencyMeetingAllowed())
            CallMeeting(senderClientId, 0);
    }

    public void CallMeeting(ulong reporterClientId, ulong deadBodyClientId = 0)
    {
        if (!IsServer || MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active) return;
        if (!IsPlayerAlive(reporterClientId)) return;

        ReportedBodyAgeBand.Value = 0;
        if (deadBodyClientId != 0)
        {
            foreach (ReportableBody body in FindObjectsByType<ReportableBody>(FindObjectsSortMode.None))
            {
                if (body.VictimClientId.Value != deadBodyClientId)
                    continue;

                double age = NetworkManager.Singleton.ServerTime.Time - body.DeathTime.Value;
                ReportedBodyAgeBand.Value = (byte)(age <= 10d ? 1 : age <= 25d ? 2 : 3);
                break;
            }
        }

        // Clean up bodies
        var bodies = FindObjectsByType<ReportableBody>(FindObjectsSortMode.None);
        foreach (var body in bodies)
        {
            if (body.NetworkObject != null && body.NetworkObject.IsSpawned)
            {
                body.NetworkObject.Despawn(true);
            }
        }

        ReporterId.Value = reporterClientId;
        DeadBodyId.Value = deadBodyClientId;
        votes.Clear();

        TeleportLivingPlayersToMeetingTable();

        MatchFlowManager.Instance.SetPhase(MatchPhase.Meeting);
        
        SetState(MeetingState.Discussion, DemoBalanceConfig.MeetingDiscussionSeconds);
    }

    private void SetState(MeetingState newState, double duration)
    {
        State.Value = newState;
        StateEndTime.Value = NetworkManager.Singleton.ServerTime.Time + duration;
    }

    private void Update()
    {
        if (!IsServer || State.Value == MeetingState.None) return;

        double currentTime = NetworkManager.Singleton.ServerTime.Time;

        if (currentTime >= StateEndTime.Value)
        {
            if (State.Value == MeetingState.Discussion)
            {
                SetState(MeetingState.Voting, DemoBalanceConfig.MeetingVotingSeconds);
            }
            else if (State.Value == MeetingState.Voting)
            {
                ResolveVoting();
                SetState(MeetingState.Results, DemoBalanceConfig.MeetingResultsSeconds);
            }
            else if (State.Value == MeetingState.Results)
            {
                EndMeeting();
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CastVoteServerRpc(ulong votedId, RpcParams rpcParams = default)
    {
        if (State.Value != MeetingState.Voting) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        
        // Prevent dead players from voting
        if (IsPlayerDead(sender)) return;
        if (votedId != ulong.MaxValue && IsPlayerDead(votedId)) return;

        if (!votes.ContainsKey(sender))
        {
            votes[sender] = votedId;
        }

        // Check if all living players have voted
        int livingCount = 0;
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (!fpc.isDead.Value) livingCount++;
        }

        if (votes.Count >= livingCount)
        {
            StateEndTime.Value = NetworkManager.Singleton.ServerTime.Time; // Force resolve immediately
        }
    }

    private bool IsPlayerDead(ulong clientId)
    {
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (fpc.OwnerClientId == clientId) return fpc.isDead.Value;
        }
        return true;
    }

    private bool IsPlayerAlive(ulong clientId)
    {
        return !IsPlayerDead(clientId);
    }

    private void ResolveVoting()
    {
        Dictionary<ulong, int> voteCounts = new Dictionary<ulong, int>();
        int maxVotes = 0;
        ulong ejectedId = 0;
        bool tie = false;

        foreach (var vote in votes.Values)
        {
            if (vote == ulong.MaxValue) continue; // Abstain
            
            if (!voteCounts.ContainsKey(vote)) voteCounts[vote] = 0;
            voteCounts[vote]++;

            if (voteCounts[vote] > maxVotes)
            {
                maxVotes = voteCounts[vote];
                ejectedId = vote;
                tie = false;
            }
            else if (voteCounts[vote] == maxVotes)
            {
                tie = true;
            }
        }

        WasTie.Value = tie || maxVotes == 0;
        EjectedPlayerId.Value = tie ? 0 : ejectedId;

        if (!WasTie.Value)
        {
            foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            {
                if (fpc.OwnerClientId == EjectedPlayerId.Value)
                {
                    fpc.deathCause.Value = FirstPersonController.PlayerDeathCause.Ejected;
                    fpc.isDead.Value = true;
                    break;
                }
            }
        }
    }

    private void EndMeeting()
    {
        State.Value = MeetingState.None;

        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow == null)
            return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        flow.EmergencyCooldownEndTime.Value = now + DemoBalanceConfig.EmergencyCooldownSeconds;

        // An ejection can immediately satisfy the win condition. Do this
        // before respawning or applying the post-meeting lock.
        flow.CheckWinConditions(allowDuringTransition: true);
        if (flow.CurrentPhase.Value == MatchPhase.Ended)
            return;
        
        // Reset Impostor cooldown
        foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (RoleManager.Instance.GetPlayerRole(fpc.OwnerClientId) == PlayerRole.Impostor)
            {
                double cooldown = UpgradeManager.Instance != null
                    ? UpgradeManager.Instance.GetKillCooldown(fpc.OwnerClientId)
                    : DemoBalanceConfig.BaseKillCooldownSeconds;
                fpc.killCooldownEndTime.Value = NetworkManager.Singleton.ServerTime.Time + cooldown;
            }
        }

        flow.BootProtectionEndTime.Value = now + DemoBalanceConfig.PostMeetingLockSeconds;
        flow.SetPhase(MatchPhase.PostMeetingLock);

        // Respawn using PlayerSpawnCoordinator
        var spawnCoord = FindAnyObjectByType<PlayerSpawnCoordinator>();
        if (spawnCoord != null)
        {
            spawnCoord.RequestDistribution(true);
        }
        
        // After 5s lock, transition to Active will be handled by MatchFlowManager (need to implement in MatchFlowManager)
    }

    private void TeleportLivingPlayersToMeetingTable()
    {
        List<FirstPersonController> livingPlayers = new List<FirstPersonController>();
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player != null && player.IsSpawned && !player.isDead.Value)
                livingPlayers.Add(player);
        }

        if (livingPlayers.Count == 0)
            return;

        livingPlayers.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

        Transform table = GameObject.Find("MeetingTable")?.transform;
        Vector3 center = table != null
            ? table.position
            : new Vector3(-49.3f, 0f, 4.71f);

        const float meetingRadius = 4.2f;
        for (int index = 0; index < livingPlayers.Count; index++)
        {
            FirstPersonController player = livingPlayers[index];
            float angle = (Mathf.PI * 2f * index / livingPlayers.Count) + Mathf.PI * 0.5f;
            Vector3 position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * meetingRadius;
            // Keep the player's existing height so the meeting move respects
            // the character controller/capsule setup used by spawn placement.
            position.y = player.transform.position.y;

            Vector3 lookTarget = new Vector3(center.x, position.y, center.z);
            Vector3 lookDirection = lookTarget - position;
            Quaternion rotation = lookDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                : player.transform.rotation;

            PlayerSpawnCoordinator.TeleportPlayer(player, position, rotation);
        }
    }
}
