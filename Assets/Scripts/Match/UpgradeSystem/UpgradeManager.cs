using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class UpgradeManager : NetworkBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public NetworkList<PlayerUpgradeState> PlayerStates = new NetworkList<PlayerUpgradeState>();
    public NetworkList<AutomaticDefenseState> AutomaticDefenses = new NetworkList<AutomaticDefenseState>();
    public NetworkVariable<bool> SystemBlackoutActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> SystemBlackoutEndTime = new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IdentityScrambleActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> IdentityScrambleEndTime = new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> IdentityScrambleColor = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        UpgradeUIManager.CreateIfNeeded(this);
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            MatchFlowManager flow = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
            if (flow != null)
                flow.CurrentPhase.OnValueChanged += HandlePhaseChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        UpgradeUIManager.ForceClose();

        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            MatchFlowManager flow = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
            if (flow != null)
                flow.CurrentPhase.OnValueChanged -= HandlePhaseChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleClientConnected(ulong clientId)
    {
        EnsureState(clientId);
    }

    private void Update()
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        for (int i = 0; i < PlayerStates.Count; i++)
        {
            FirstPersonController player = FindPlayer(PlayerStates[i].ClientId);
            if (player == null || player.isDead.Value)
                ClearPlayerUpgrades(PlayerStates[i].ClientId);
        }

        for (int i = AutomaticDefenses.Count - 1; i >= 0; i--)
        {
            if (AutomaticDefenses[i].EndTime > 0d && now >= AutomaticDefenses[i].EndTime)
                AutomaticDefenses.RemoveAt(i);
        }
        if (SystemBlackoutActive.Value && now >= SystemBlackoutEndTime.Value)
        {
            SystemBlackoutActive.Value = false;
            SystemBlackoutEndTime.Value = 0d;
        }

        if (IdentityScrambleActive.Value && now >= IdentityScrambleEndTime.Value)
        {
            IdentityScrambleActive.Value = false;
            IdentityScrambleEndTime.Value = 0d;
            IdentityScrambleColor.Value = 0;
            ApplyColorOverride(0);
        }
    }

    private void HandlePhaseChanged(MatchPhase previous, MatchPhase current)
    {
        if (!IsServer)
            return;

        if (current == MatchPhase.Ended || current == MatchPhase.Lobby)
        {
            PlayerStates.Clear();
            AutomaticDefenses.Clear();
            ClearDisruptionEffects();
        }
        else if (current == MatchPhase.BootProtection &&
                 (previous == MatchPhase.Lobby || previous == MatchPhase.Ended))
        {
            // A fresh match must never inherit a pending selection or an
            // automatic defense from a previous session.
            PlayerStates.Clear();
            AutomaticDefenses.Clear();
            ClearDisruptionEffects();
            if (NetworkManager != null)
            {
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    EnsureState(clientId);
            }
        }
        else if (current == MatchPhase.Meeting)
        {
            CancelPendingSelections();
            AutomaticDefenses.Clear();
            ClearDisruptionEffects();
        }
    }

    public void EnsureState(ulong clientId)
    {
        if (!IsServer || GetStateIndex(clientId) >= 0)
            return;

        PlayerStates.Add(new PlayerUpgradeState
        {
            ClientId = clientId,
            Points = 0,
            SelectionCount = 0,
            PendingSelection = UpgradeSelectionKind.None,
            Passive = PassiveUpgradeId.None,
            Tool = ActiveToolId.None,
            ToolArmed = false,
            ToolConsumed = false
        });
    }

    public void AwardTaskPoint(ulong clientId)
    {
        if (!IsServer || RoleManager.Instance == null)
            return;

        EnsureState(clientId);
        int index = GetStateIndex(clientId);
        if (index < 0)
            return;

        PlayerUpgradeState state = PlayerStates[index];
        state.Points++;
        if (state.SelectionCount == 0 && state.Points >= 2)
            state.PendingSelection = UpgradeSelectionKind.Passive;
        else if (state.SelectionCount == 1 && state.Points >= 4)
            state.PendingSelection = UpgradeSelectionKind.Tool;

        PlayerStates[index] = state;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ChooseUpgradeRpc(byte choice, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int index = GetStateIndex(sender);
        if (index < 0 || !IsPlayerEligible(sender))
            return;

        PlayerUpgradeState state = PlayerStates[index];
        if (state.PendingSelection == UpgradeSelectionKind.None || state.SelectionCount >= 2)
            return;

        ActiveToolId selectedTool = ActiveToolId.None;
        if (state.PendingSelection == UpgradeSelectionKind.Passive)
        {
            PassiveUpgradeId passive = ResolvePassive(sender, choice);
            if (passive == PassiveUpgradeId.None)
                return;
            state.Passive = passive;
        }
        else
        {
            selectedTool = ResolveTool(sender, choice);
            if (selectedTool == ActiveToolId.None || !IsToolEligible(sender, selectedTool))
                return;
            state.Tool = selectedTool;
            state.ToolArmed = true;
            state.ToolConsumed = false;
        }

        state.SelectionCount++;
        state.PendingSelection = UpgradeSelectionKind.None;
        PlayerStates[index] = state;

        bool activated = true;
        if (selectedTool == ActiveToolId.ValveOverride)
            activated = MissionManager.Instance != null && MissionManager.Instance.TryStartValveOverride(sender);
        else if (selectedTool == ActiveToolId.SystemBlackout)
            activated = ActivateSystemBlackout();
        else if (selectedTool == ActiveToolId.IdentityScramble)
            activated = ActivateIdentityScramble();

        if (!activated)
        {
            state.Tool = ActiveToolId.None;
            state.ToolArmed = false;
            state.ToolConsumed = false;
            state.SelectionCount--;
            state.PendingSelection = UpgradeSelectionKind.Tool;
            PlayerStates[index] = state;
            return;
        }

        // Killer tools activate immediately and are single-use. Villager
        // defenses remain armed until the corresponding disruption occurs.
        if (selectedTool == ActiveToolId.ValveOverride ||
            selectedTool == ActiveToolId.SystemBlackout ||
            selectedTool == ActiveToolId.IdentityScramble)
        {
            state.ToolArmed = false;
            state.ToolConsumed = true;
            PlayerStates[index] = state;
        }
    }

    private bool ActivateSystemBlackout()
    {
        if (!IsServer || NetworkManager.Singleton == null || MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active ||
            SystemBlackoutActive.Value)
            return false;

        SystemBlackoutActive.Value = true;
        SystemBlackoutEndTime.Value = NetworkManager.Singleton.ServerTime.Time + DemoBalanceConfig.SystemBlackoutSeconds;
        ArmPriorityUplinkBypasses(NetworkManager.Singleton.ServerTime.Time);
        CloseCrewTaskUIsClientRpc();
        return true;
    }

    private bool ActivateIdentityScramble()
    {
        if (!IsServer || NetworkManager.Singleton == null || MatchFlowManager.Instance == null ||
            MatchFlowManager.Instance.CurrentPhase.Value != MatchPhase.Active ||
            IdentityScrambleActive.Value)
            return false;

        IdentityScrambleActive.Value = true;
        IdentityScrambleEndTime.Value = NetworkManager.Singleton.ServerTime.Time + DemoBalanceConfig.IdentityScrambleSeconds;
        IdentityScrambleColor.Value = Random.Range(1, 17);
        ArmIdentityAnchors();
        ApplyColorOverride(IdentityScrambleColor.Value);
        return true;
    }

    private void ClearDisruptionEffects()
    {
        if (!IsServer)
            return;

        SystemBlackoutActive.Value = false;
        SystemBlackoutEndTime.Value = 0d;
        IdentityScrambleActive.Value = false;
        IdentityScrambleEndTime.Value = 0d;
        IdentityScrambleColor.Value = 0;
        ApplyColorOverride(0);
    }

    private static void CloseCrewTaskUIs()
    {
        ComputerUIManager.Instance?.CloseComputer();
        CircuitMissionUIManager.Instance?.Close();
        WaveFrequencyUIManager.Instance?.Close();
        PressureMissionUIManager.Instance?.Close();
        ReactorMissionUIManager.Instance?.Close();
    }

    [ClientRpc]
    private void CloseCrewTaskUIsClientRpc()
    {
        CloseCrewTaskUIs();
    }

    private void ApplyColorOverride(int colorIndex)
    {
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.IsServer && (!IdentityScrambleActive.Value || !IsIdentityAnchorActive(player.OwnerClientId)))
                player.effectiveColorOverride.Value = colorIndex;
        }
    }

    private void ArmPriorityUplinkBypasses(double now)
    {
        for (int i = 0; i < PlayerStates.Count; i++)
        {
            PlayerUpgradeState state = PlayerStates[i];
            if (!state.ToolArmed || state.Tool != ActiveToolId.PriorityUplink || !IsPlayerEligible(state.ClientId))
                continue;

            state.ToolArmed = false;
            state.ToolConsumed = true;
            PlayerStates[i] = state;
            AutomaticDefenses.Add(new AutomaticDefenseState { ClientId = state.ClientId, EndTime = now + 8d });
        }
    }

    private void ArmIdentityAnchors()
    {
        for (int i = 0; i < PlayerStates.Count; i++)
        {
            PlayerUpgradeState state = PlayerStates[i];
            if (!state.ToolArmed || state.Tool != ActiveToolId.IdentityAnchor || !IsPlayerEligible(state.ClientId))
                continue;

            state.ToolArmed = false;
            state.ToolConsumed = true;
            PlayerStates[i] = state;
            AutomaticDefenses.Add(new AutomaticDefenseState { ClientId = state.ClientId, EndTime = IdentityScrambleEndTime.Value });
        }
    }

    public bool IsSystemBlackoutBlocking(ulong clientId)
    {
        if (!SystemBlackoutActive.Value)
            return false;

        for (int i = 0; i < AutomaticDefenses.Count; i++)
        {
            if (AutomaticDefenses[i].ClientId == clientId &&
                AutomaticDefenses[i].EndTime > NetworkManager.Singleton.ServerTime.Time)
                return false;
        }
        return true;
    }

    private bool IsIdentityAnchorActive(ulong clientId)
    {
        if (!IdentityScrambleActive.Value)
            return false;

        for (int i = 0; i < AutomaticDefenses.Count; i++)
        {
            if (AutomaticDefenses[i].ClientId == clientId &&
                AutomaticDefenses[i].EndTime >= IdentityScrambleEndTime.Value)
                return true;
        }
        return false;
    }

    public void CancelPendingSelections()
    {
        if (!IsServer)
            return;

        for (int i = 0; i < PlayerStates.Count; i++)
        {
            PlayerUpgradeState state = PlayerStates[i];
            state.PendingSelection = UpgradeSelectionKind.None;
            PlayerStates[i] = state;
        }
    }

    public void ClearPlayerUpgrades(ulong clientId)
    {
        if (!IsServer)
            return;

        int index = GetStateIndex(clientId);
        if (index < 0)
            return;

        PlayerUpgradeState state = PlayerStates[index];
        if (state.Points == 0 && state.SelectionCount == 0 &&
            state.PendingSelection == UpgradeSelectionKind.None &&
            state.Passive == PassiveUpgradeId.None && state.Tool == ActiveToolId.None)
            return;

        state.Points = 0;
        state.SelectionCount = 0;
        state.PendingSelection = UpgradeSelectionKind.None;
        state.Passive = PassiveUpgradeId.None;
        state.Tool = ActiveToolId.None;
        state.ToolArmed = false;
        state.ToolConsumed = false;
        PlayerStates[index] = state;

        for (int i = AutomaticDefenses.Count - 1; i >= 0; i--)
        {
            if (AutomaticDefenses[i].ClientId == clientId)
                AutomaticDefenses.RemoveAt(i);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CancelPendingSelectionRpc(RpcParams rpcParams = default)
    {
        int index = GetStateIndex(rpcParams.Receive.SenderClientId);
        if (index < 0)
            return;

        PlayerUpgradeState state = PlayerStates[index];
        state.PendingSelection = UpgradeSelectionKind.None;
        PlayerStates[index] = state;
    }

    public PlayerUpgradeState? GetState(ulong clientId)
    {
        int index = GetStateIndex(clientId);
        return index >= 0 ? PlayerStates[index] : null;
    }

    private PassiveUpgradeId ResolvePassive(ulong clientId, byte choice)
    {
        bool killer = RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor;
        if (choice > 2)
            return PassiveUpgradeId.None;

        if (!killer)
            return (PassiveUpgradeId)((byte)PassiveUpgradeId.OverdriveServos + choice);

        return (PassiveUpgradeId)((byte)PassiveUpgradeId.PursuitProtocol + choice);
    }

    private ActiveToolId ResolveTool(ulong clientId, byte choice)
    {
        bool killer = RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor;
        if (!killer)
        {
            return choice switch
            {
                0 => ActiveToolId.PriorityUplink,
                1 => ActiveToolId.IdentityAnchor,
                _ => ActiveToolId.None
            };
        }

        List<ActiveToolId> tools = new List<ActiveToolId>
        {
            ActiveToolId.SystemBlackout,
            ActiveToolId.IdentityScramble
        };

        if (MissionManager.Instance != null && MissionManager.Instance.IsValveOverrideUnlocked.Value)
            tools.Insert(0, ActiveToolId.ValveOverride);

        return choice < tools.Count ? tools[choice] : ActiveToolId.None;
    }

    public List<ActiveToolId> GetAvailableToolChoices(ulong clientId)
    {
        bool killer = RoleManager.Instance != null &&
                      RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor;
        if (!killer)
            return new List<ActiveToolId> { ActiveToolId.PriorityUplink, ActiveToolId.IdentityAnchor };

        List<ActiveToolId> tools = new List<ActiveToolId>
        {
            ActiveToolId.SystemBlackout,
            ActiveToolId.IdentityScramble
        };
        if (MissionManager.Instance != null && MissionManager.Instance.IsValveOverrideUnlocked.Value)
            tools.Insert(0, ActiveToolId.ValveOverride);
        return tools;
    }

    public bool HasPassive(ulong clientId, PassiveUpgradeId passive)
    {
        PlayerUpgradeState? state = GetState(clientId);
        return state.HasValue && state.Value.Passive == passive;
    }

    public float GetMovementMultiplier(ulong clientId)
    {
        float multiplier = HasPassive(clientId, PassiveUpgradeId.OverdriveServos) ? 1.10f : 1f;
        FirstPersonController player = FindPlayer(clientId);
        if (player != null && player.escapeRoutineEndTime.Value > NetworkManager.Singleton.ServerTime.Time)
            multiplier *= 1.15f;
        return multiplier;
    }

    public double GetKillCooldown(ulong clientId)
    {
        return HasPassive(clientId, PassiveUpgradeId.PursuitProtocol)
            ? 25d
            : DemoBalanceConfig.BaseKillCooldownSeconds;
    }

    public float GetKillRange(ulong clientId)
    {
        return HasPassive(clientId, PassiveUpgradeId.AmbushProtocol)
            ? 4.75f
            : DemoBalanceConfig.BaseKillRangeMeters;
    }

    public void NotifySuccessfulKill(ulong killerClientId, ulong victimClientId)
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        if (HasPassive(killerClientId, PassiveUpgradeId.EscapeRoutine))
        {
            FirstPersonController killer = FindPlayer(killerClientId);
            if (killer != null)
                killer.escapeRoutineEndTime.Value = NetworkManager.Singleton.ServerTime.Time + 5d;
        }

        FirstPersonController killerPlayer = FindPlayer(killerClientId);
        if (killerPlayer == null)
            return;

        foreach (PlayerUpgradeState state in PlayerStates)
        {
            if (state.Passive != PassiveUpgradeId.ThreatSensor || state.ClientId == victimClientId)
                continue;

            FirstPersonController sensorOwner = FindPlayer(state.ClientId);
            if (sensorOwner == null || sensorOwner.isDead.Value ||
                Vector3.Distance(sensorOwner.transform.position, killerPlayer.transform.position) > DemoBalanceConfig.ThreatSensorRangeMeters)
                continue;

            ThreatSensorWarningClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { state.ClientId } }
            });
        }
    }

    [ClientRpc]
    private void ThreatSensorWarningClientRpc(ClientRpcParams clientRpcParams = default)
    {
        GameplayStatusUIManager.ShowLocalAlert(
            "WARNING // NEARBY UNIT OFFLINE",
            string.Empty,
            2.5f);
    }

    private bool IsToolEligible(ulong clientId, ActiveToolId tool)
    {
        PlayerUpgradeState? state = GetState(clientId);
        if (!state.HasValue || state.Value.Tool != ActiveToolId.None)
            return false;

        if (tool == ActiveToolId.ValveOverride)
        {
            return RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor &&
                   MissionManager.Instance != null &&
                   MissionManager.Instance.SharedValveSession.Value == SharedValveSessionState.Idle &&
                   MissionManager.Instance.IsValveOverrideUnlocked.Value &&
                   MissionManager.Instance.ValvesTurned.Value < 3 &&
                   MatchFlowManager.Instance != null &&
                   MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Active;
        }

        if (tool == ActiveToolId.SystemBlackout || tool == ActiveToolId.IdentityScramble)
            return MatchFlowManager.Instance != null &&
                   MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Active;

        return true;
    }

    private bool IsPlayerEligible(ulong clientId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId))
            return false;

        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId == clientId)
                return !player.isDead.Value;
        }

        return false;
    }

    private int GetStateIndex(ulong clientId)
    {
        for (int i = 0; i < PlayerStates.Count; i++)
        {
            if (PlayerStates[i].ClientId == clientId)
                return i;
        }

        return -1;
    }

    private static FirstPersonController FindPlayer(ulong clientId)
    {
        foreach (FirstPersonController player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId == clientId)
                return player;
        }
        return null;
    }
}
