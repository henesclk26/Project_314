using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections;

public enum MatchPhase : byte
{
    Lobby = 0,
    BootProtection = 1,
    Active = 2,
    Meeting = 3,
    PostMeetingLock = 4,
    Ended = 5
}

public enum MatchWinner : byte
{
    None = 0,
    Villagers = 1,
    Killer = 2
}

public class MatchFlowManager : NetworkBehaviour
{
    public static MatchFlowManager Instance { get; private set; }

    public NetworkVariable<MatchPhase> CurrentPhase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Using NetworkVariables to sync end times, relative to NetworkManager.ServerTime.Time
    public NetworkVariable<double> BootProtectionEndTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> FirstEmergencyLockEndTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> EmergencyCooldownEndTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<MatchWinner> Winner = new NetworkVariable<MatchWinner>(MatchWinner.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public GameObject reportableBodyPrefab;

    public event Action<MatchPhase> OnPhaseChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        GameplayStatusUIManager.CreateIfNeeded();
        CurrentPhase.OnValueChanged += HandlePhaseChange;
        
        // Trigger initial state if joined late
        if (IsClient)
        {
            OnPhaseChanged?.Invoke(CurrentPhase.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CurrentPhase.OnValueChanged -= HandlePhaseChange;
    }

    private void HandlePhaseChange(MatchPhase oldPhase, MatchPhase newPhase)
    {
        if (newPhase == MatchPhase.Meeting ||
            newPhase == MatchPhase.PostMeetingLock ||
            newPhase == MatchPhase.Ended ||
            newPhase == MatchPhase.Lobby)
        {
            CloseGameplayScreensForPhaseLock();
        }

        OnPhaseChanged?.Invoke(newPhase);
        Debug.Log($"[MatchFlowManager] Phase changed: {oldPhase} -> {newPhase}");
    }

    private static void CloseGameplayScreensForPhaseLock()
    {
        ComputerUIManager.Instance?.CloseComputer();
        CircuitMissionUIManager.Instance?.Close();
        WaveFrequencyUIManager.Instance?.Close();
        PressureMissionUIManager.Instance?.Close();
        ReactorMissionUIManager.Instance?.Close();
        SecurityCameraUIManager.Instance?.Close();
        PuzzleUIManager.Instance?.ClosePuzzle();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        double currentTime = NetworkManager.Singleton.LocalTime.Time;

        // Transitions
        if (CurrentPhase.Value == MatchPhase.BootProtection)
        {
            if (currentTime >= BootProtectionEndTime.Value)
            {
                SetPhase(MatchPhase.Active);
            }
        }
        else if (CurrentPhase.Value == MatchPhase.PostMeetingLock)
        {
            if (currentTime >= BootProtectionEndTime.Value) // Reusing the BootProtectionEndTime variable to save space, or just use a new one. Let's create a new one.
            {
                SetPhase(MatchPhase.Active);
            }
        }
        else if (CurrentPhase.Value == MatchPhase.Active)
        {
            CheckWinConditions();
        }
    }

    public void StartMatch()
    {
        if (!IsServer) return;
        if (CurrentPhase.Value != MatchPhase.Lobby && CurrentPhase.Value != MatchPhase.Ended) return;

        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.DistributeRolesServer();
        }

        double currentTime = NetworkManager.Singleton.LocalTime.Time;
        BootProtectionEndTime.Value = currentTime + DemoBalanceConfig.BootProtectionSeconds;
        FirstEmergencyLockEndTime.Value = currentTime + DemoBalanceConfig.FirstEmergencyLockSeconds;
        EmergencyCooldownEndTime.Value = 0d;
        Winner.Value = MatchWinner.None;
        
        SetPhase(MatchPhase.BootProtection);
    }

    public void ResetMatch()
    {
        if (!IsServer) return;

        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            player.isDead.Value = false;
            player.deathCause.Value = FirstPersonController.PlayerDeathCause.None;
            player.corpseHidden.Value = false;
            player.killCooldownEndTime.Value = 0d;
            player.escapeRoutineEndTime.Value = 0d;
            player.effectiveColorOverride.Value = 0;
            player.ReviveForNewMatch();
        }

        foreach (ReportableBody body in FindObjectsByType<ReportableBody>(FindObjectsSortMode.None))
        {
            if (body.NetworkObject != null && body.NetworkObject.IsSpawned)
                body.NetworkObject.Despawn(true);
            else
                Destroy(body.gameObject);
        }

        SetPhase(MatchPhase.Lobby);
        EmergencyCooldownEndTime.Value = 0d;
        Winner.Value = MatchWinner.None;
        if (GameManager.Instance != null)
            GameManager.Instance.SetGameOver(false);
        SetGameOverClientRpc(false);
        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.ClearRoles();
        }

        PlayerSpawnCoordinator spawnCoordinator = FindAnyObjectByType<PlayerSpawnCoordinator>();
        spawnCoordinator?.RequestDistribution(true);
    }

    public void SetPhase(MatchPhase newPhase)
    {
        if (!IsServer) return;
        CurrentPhase.Value = newPhase;
    }

    public bool IsEmergencyMeetingAllowed()
    {
        if (CurrentPhase.Value != MatchPhase.Active) return false;
        if (NetworkManager.Singleton.LocalTime.Time < FirstEmergencyLockEndTime.Value) return false;
        if (NetworkManager.Singleton.LocalTime.Time < EmergencyCooldownEndTime.Value) return false;
        return true;
    }

    public void CheckWinConditions(bool allowDuringTransition = false)
    {
        if (!IsServer || CurrentPhase.Value == MatchPhase.Ended) return;
        if (!allowDuringTransition && CurrentPhase.Value != MatchPhase.Active) return;

        // A match can enter Active on the same network tick that the last
        // player object/role snapshot arrives. Do not interpret that short
        // initialization window as a Villager win just because the killer
        // object has not been observed by the server yet.
        if (!HasCompletePlayerSnapshot())
            return;

        int livingKillers = 0;
        int livingVillagers = 0;
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.isDead.Value || RoleManager.Instance == null)
                continue;

            if (RoleManager.Instance.GetPlayerRole(player.OwnerClientId) == PlayerRole.Impostor)
                livingKillers++;
            else if (RoleManager.Instance.GetPlayerRole(player.OwnerClientId) == PlayerRole.Villager)
                livingVillagers++;
        }

        bool taskTargetMet = TaskManager.Instance != null &&
                             TaskManager.Instance.CrewTaskTarget.Value > 0 &&
                             TaskManager.Instance.CrewTaskProgress.Value >= TaskManager.Instance.CrewTaskTarget.Value;

        if (livingKillers == 0 || taskTargetMet)
        {
            EndMatch(MatchWinner.Villagers);
        }
        else if (livingKillers >= livingVillagers)
        {
            EndMatch(MatchWinner.Killer);
        }
    }

    private bool HasCompletePlayerSnapshot()
    {
        if (NetworkManager.Singleton == null || RoleManager.Instance == null)
            return false;

        if (NetworkManager.Singleton.ConnectedClientsIds.Count < 3)
            return false;

        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.None)
                return false;

            bool hasSpawnedPlayer = false;
            foreach (FirstPersonController player in players)
            {
                if (player != null && player.IsSpawned && player.OwnerClientId == clientId)
                {
                    hasSpawnedPlayer = true;
                    break;
                }
            }

            if (!hasSpawnedPlayer)
                return false;
        }

        return true;
    }

    public void EndMatch(MatchWinner winner)
    {
        if (!IsServer || CurrentPhase.Value == MatchPhase.Ended)
            return;

        Winner.Value = winner;
        if (GameManager.Instance != null)
            GameManager.Instance.SetGameOver(true);
        SetGameOverClientRpc(true);
        SetPhase(MatchPhase.Ended);
        Debug.Log($"[MatchFlowManager] Match ended. Winner: {winner}.");
        StartCoroutine(ReturnToLobbyAfterResult());
    }

    private IEnumerator ReturnToLobbyAfterResult()
    {
        yield return new WaitForSeconds(8f);
        if (!IsServer || CurrentPhase.Value != MatchPhase.Ended)
            yield break;

        ReturnToLobbyClientRpc();

        if (GameManager.Instance != null)
            GameManager.Instance.isGameStarted.Value = false;
        ResetMatch();
    }

    [ClientRpc]
    private void ReturnToLobbyClientRpc()
    {
        SciFiMenuController menuController = FindAnyObjectByType<SciFiMenuController>();
        if (menuController != null)
            menuController.ShowMainMenu();
    }

    [ClientRpc]
    private void SetGameOverClientRpc(bool value)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.SetGameOver(value);
    }

    public void SpawnBody(Vector3 position, ulong victimId)
    {
        if (!IsServer || reportableBodyPrefab == null) return;
        
        GameObject bodyObj = Instantiate(reportableBodyPrefab, position, Quaternion.identity);
        NetworkObject netObj = bodyObj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            var rb = bodyObj.GetComponent<ReportableBody>();
            if (rb != null)
            {
                rb.VictimClientId.Value = victimId;
                rb.DeathTime.Value = NetworkManager.Singleton.LocalTime.Time;
            }
        }
    }
}
