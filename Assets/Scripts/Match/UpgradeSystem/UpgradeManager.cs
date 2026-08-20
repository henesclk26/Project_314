using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class UpgradeManager : NetworkBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    public NetworkList<PlayerUpgradeState> PlayerStates = new NetworkList<PlayerUpgradeState>();
    public NetworkList<UpgradeOfferState> UpgradeOffers = new NetworkList<UpgradeOfferState>();
    public NetworkList<AutomaticDefenseState> AutomaticDefenses = new NetworkList<AutomaticDefenseState>();
    public NetworkVariable<bool> SystemBlackoutActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> SystemBlackoutEndTime = new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IdentityScrambleActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> IdentityScrambleEndTime = new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> IdentityScrambleColor = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private const int UpgradePointInterval = 2;
    private const byte MaxCardCopies = 2;
    private readonly Dictionary<ulong, UpgradeCardId[]> pendingOffers = new Dictionary<ulong, UpgradeCardId[]>();

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
            UpgradeOffers.Clear();
            AutomaticDefenses.Clear();
            ClearDisruptionEffects();
        }
        else if (current == MatchPhase.BootProtection &&
                 (previous == MatchPhase.Lobby || previous == MatchPhase.Ended))
        {
            // A fresh match must never inherit a pending selection or an
            // automatic defense from a previous session.
            PlayerStates.Clear();
            UpgradeOffers.Clear();
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
            UpgradeOffers.Clear();
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
        PlayerStates[index] = state;

        if (state.Points > 0 && state.Points % UpgradePointInterval == 0 &&
            state.PendingSelection == UpgradeSelectionKind.None)
        {
            PrepareUpgradeSelection(clientId, index);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ChooseUpgradeRpc(byte choice, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int index = GetStateIndex(sender);
        if (index < 0 || !IsPlayerEligible(sender))
            return;

        PlayerUpgradeState state = PlayerStates[index];
        if (state.PendingSelection == UpgradeSelectionKind.None ||
            !pendingOffers.TryGetValue(sender, out UpgradeCardId[] offers) ||
            offers.Length != 3 || choice >= offers.Length)
            return;

        UpgradeCardId selectedCard = offers[choice];
        bool allBlank = offers[0] == UpgradeCardId.None &&
                        offers[1] == UpgradeCardId.None &&
                        offers[2] == UpgradeCardId.None;
        if (selectedCard == UpgradeCardId.None)
        {
            if (!allBlank)
                return;

            CompleteSelection(sender, index, state);
            return;
        }

        if (!IsCardForPlayer(sender, selectedCard) ||
            GetCardCount(state, selectedCard) >= MaxCardCopies ||
            !IsCardCurrentlyEligible(sender, selectedCard))
        {
            PrepareUpgradeSelection(sender, index);
            return;
        }

        if (!TryApplyUpgradeCard(sender, ref state, selectedCard))
        {
            PrepareUpgradeSelection(sender, index);
            return;
        }

        state.SelectionCount++;
        state.PendingSelection = UpgradeSelectionKind.None;
        pendingOffers.Remove(sender);
        ClearNetworkOffers(sender);
        PlayerStates[index] = state;
        ClearUpgradeOffersClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        });
        PrepareNextSelectionIfDue(sender, index);
    }

    private void PrepareUpgradeSelection(ulong clientId, int index)
    {
        PlayerUpgradeState state = PlayerStates[index];
        state.PendingSelection = UpgradeSelectionKind.Upgrade;
        PlayerStates[index] = state;

        UpgradeCardId[] offers = BuildRandomOffers(clientId, state);
        pendingOffers[clientId] = offers;
        SetNetworkOffers(clientId, offers);
        SendUpgradeOffersClientRpc(
            new[] { (byte)offers[0], (byte)offers[1], (byte)offers[2] },
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } });
    }

    private UpgradeCardId[] BuildRandomOffers(ulong clientId, PlayerUpgradeState state)
    {
        List<UpgradeCardId> eligible = GetRoleCardPool(clientId)
            .Where(card => GetCardCount(state, card) < MaxCardCopies &&
                           IsCardCurrentlyEligible(clientId, card))
            .ToList();

        for (int i = eligible.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            UpgradeCardId swap = eligible[i];
            eligible[i] = eligible[swapIndex];
            eligible[swapIndex] = swap;
        }

        return new[]
        {
            eligible.Count > 0 ? eligible[0] : UpgradeCardId.None,
            eligible.Count > 1 ? eligible[1] : UpgradeCardId.None,
            eligible.Count > 2 ? eligible[2] : UpgradeCardId.None
        };
    }

    private void SetNetworkOffers(ulong clientId, UpgradeCardId[] offers)
    {
        ClearNetworkOffers(clientId);
        UpgradeOffers.Add(new UpgradeOfferState
        {
            ClientId = clientId,
            Card0 = offers[0],
            Card1 = offers[1],
            Card2 = offers[2]
        });
    }

    private void ClearNetworkOffers(ulong clientId)
    {
        for (int i = UpgradeOffers.Count - 1; i >= 0; i--)
        {
            if (UpgradeOffers[i].ClientId == clientId)
                UpgradeOffers.RemoveAt(i);
        }
    }

    private List<UpgradeCardId> GetRoleCardPool(ulong clientId)
    {
        bool killer = RoleManager.Instance != null &&
                      RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor;
        return killer
            ? new List<UpgradeCardId>
            {
                UpgradeCardId.PursuitProtocol,
                UpgradeCardId.EscapeRoutine,
                UpgradeCardId.AmbushProtocol,
                UpgradeCardId.ValveOverride,
                UpgradeCardId.SystemBlackout,
                UpgradeCardId.IdentityScramble
            }
            : new List<UpgradeCardId>
            {
                UpgradeCardId.OverdriveServos,
                UpgradeCardId.ForensicCache,
                UpgradeCardId.ThreatSensor,
                UpgradeCardId.PriorityUplink,
                UpgradeCardId.IdentityAnchor
            };
    }

    private bool IsCardForPlayer(ulong clientId, UpgradeCardId card)
    {
        return GetRoleCardPool(clientId).Contains(card);
    }

    private bool IsCardCurrentlyEligible(ulong clientId, UpgradeCardId card)
    {
        if (card == UpgradeCardId.ValveOverride)
        {
            return RoleManager.Instance != null &&
                   RoleManager.Instance.GetPlayerRole(clientId) == PlayerRole.Impostor &&
                   MissionManager.Instance != null &&
                   MissionManager.Instance.SharedValveSession.Value == SharedValveSessionState.Idle &&
                   MissionManager.Instance.IsValveOverrideUnlocked.Value &&
                   MissionManager.Instance.ValvesTurned.Value < 3 &&
                   MatchFlowManager.Instance != null &&
                   MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Active;
        }

        if (card == UpgradeCardId.SystemBlackout)
            return MatchFlowManager.Instance != null &&
                   MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Active &&
                   !SystemBlackoutActive.Value;

        if (card == UpgradeCardId.IdentityScramble)
            return MatchFlowManager.Instance != null &&
                   MatchFlowManager.Instance.CurrentPhase.Value == MatchPhase.Active &&
                   !IdentityScrambleActive.Value;

        return true;
    }

    private bool TryApplyUpgradeCard(ulong clientId, ref PlayerUpgradeState state, UpgradeCardId card)
    {
        IncrementCardCount(ref state, card);

        if (card == UpgradeCardId.PursuitProtocol)
        {
            FirstPersonController killer = FindPlayer(clientId);
            if (killer != null && NetworkManager.Singleton != null)
            {
                double now = NetworkManager.Singleton.ServerTime.Time;
                double currentEndTime = killer.killCooldownEndTime.Value;
                if (currentEndTime <= 0d && MatchFlowManager.Instance != null)
                    currentEndTime = MatchFlowManager.Instance.FirstKillLockEndTime.Value;
                killer.killCooldownEndTime.Value = System.Math.Max(
                    now,
                    currentEndTime - DemoBalanceConfig.KillCooldownReductionPerUpgradeSeconds);
            }
        }

        if (card == UpgradeCardId.PriorityUplink)
        {
            state.PriorityUplinkCharges++;
            state.Tool = ActiveToolId.PriorityUplink;
            state.ToolArmed = true;
            state.ToolConsumed = false;
            return true;
        }

        if (card == UpgradeCardId.IdentityAnchor)
        {
            state.IdentityAnchorCharges++;
            state.Tool = ActiveToolId.IdentityAnchor;
            state.ToolArmed = true;
            state.ToolConsumed = false;
            return true;
        }

        ActiveToolId selectedTool = ToToolId(card);
        if (selectedTool == ActiveToolId.None)
        {
            state.Passive = ToPassiveId(card);
            return true;
        }

        bool activated = selectedTool == ActiveToolId.ValveOverride
            ? MissionManager.Instance != null && MissionManager.Instance.TryStartValveOverride(clientId)
            : selectedTool == ActiveToolId.SystemBlackout
                ? ActivateSystemBlackout()
                : ActivateIdentityScramble();

        if (!activated)
        {
            DecrementCardCount(ref state, card);
            return false;
        }

        state.Tool = selectedTool;
        state.ToolArmed = false;
        state.ToolConsumed = true;
        return true;
    }

    private void CompleteSelection(ulong clientId, int index, PlayerUpgradeState state)
    {
        state.SelectionCount++;
        state.PendingSelection = UpgradeSelectionKind.None;
        PlayerStates[index] = state;
        pendingOffers.Remove(clientId);
        ClearNetworkOffers(clientId);
        ClearUpgradeOffersClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
        PrepareNextSelectionIfDue(clientId, index);
    }

    private void PrepareNextSelectionIfDue(ulong clientId, int index)
    {
        PlayerUpgradeState state = PlayerStates[index];
        int nextThreshold = (state.SelectionCount + 1) * UpgradePointInterval;
        if (state.PendingSelection == UpgradeSelectionKind.None && state.Points >= nextThreshold)
            PrepareUpgradeSelection(clientId, index);
    }

    [ClientRpc]
    private void SendUpgradeOffersClientRpc(byte[] offers, ClientRpcParams clientRpcParams = default)
    {
        UpgradeUIManager.ReceiveOffers(offers);
    }

    [ClientRpc]
    private void ClearUpgradeOffersClientRpc(ClientRpcParams clientRpcParams = default)
    {
        UpgradeUIManager.ClearOffers();
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
            if (state.PriorityUplinkCharges == 0 || !IsPlayerEligible(state.ClientId))
                continue;

            state.PriorityUplinkCharges--;
            state.ToolArmed = state.PriorityUplinkCharges > 0;
            state.ToolConsumed = state.PriorityUplinkCharges == 0;
            PlayerStates[i] = state;
            AutomaticDefenses.Add(new AutomaticDefenseState
            {
                ClientId = state.ClientId,
                Tool = ActiveToolId.PriorityUplink,
                EndTime = now + 8d
            });
        }
    }

    private void ArmIdentityAnchors()
    {
        for (int i = 0; i < PlayerStates.Count; i++)
        {
            PlayerUpgradeState state = PlayerStates[i];
            if (state.IdentityAnchorCharges == 0 || !IsPlayerEligible(state.ClientId))
                continue;

            state.IdentityAnchorCharges--;
            state.ToolArmed = state.IdentityAnchorCharges > 0;
            state.ToolConsumed = state.IdentityAnchorCharges == 0;
            PlayerStates[i] = state;
            AutomaticDefenses.Add(new AutomaticDefenseState
            {
                ClientId = state.ClientId,
                Tool = ActiveToolId.IdentityAnchor,
                EndTime = IdentityScrambleEndTime.Value
            });
        }
    }

    public bool IsSystemBlackoutBlocking(ulong clientId)
    {
        if (!SystemBlackoutActive.Value)
            return false;

        for (int i = 0; i < AutomaticDefenses.Count; i++)
        {
            if (AutomaticDefenses[i].ClientId == clientId &&
                AutomaticDefenses[i].Tool == ActiveToolId.PriorityUplink &&
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
                AutomaticDefenses[i].Tool == ActiveToolId.IdentityAnchor &&
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
            pendingOffers.Remove(state.ClientId);
            ClearNetworkOffers(state.ClientId);
            ClearUpgradeOffersClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { state.ClientId } }
            });
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
            state.Passive == PassiveUpgradeId.None && state.Tool == ActiveToolId.None &&
            GetOwnedCardCount(state) == 0)
            return;

        state.Points = 0;
        state.SelectionCount = 0;
        state.PendingSelection = UpgradeSelectionKind.None;
        state.Passive = PassiveUpgradeId.None;
        state.Tool = ActiveToolId.None;
        state.ToolArmed = false;
        state.ToolConsumed = false;
        state.OverdriveServosCount = 0;
        state.ForensicCacheCount = 0;
        state.ThreatSensorCount = 0;
        state.PursuitProtocolCount = 0;
        state.EscapeRoutineCount = 0;
        state.AmbushProtocolCount = 0;
        state.PriorityUplinkCount = 0;
        state.IdentityAnchorCount = 0;
        state.ValveOverrideCount = 0;
        state.SystemBlackoutCount = 0;
        state.IdentityScrambleCount = 0;
        state.PriorityUplinkCharges = 0;
        state.IdentityAnchorCharges = 0;
        PlayerStates[index] = state;
        pendingOffers.Remove(clientId);
        ClearNetworkOffers(clientId);

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
        pendingOffers.Remove(rpcParams.Receive.SenderClientId);
        ClearNetworkOffers(rpcParams.Receive.SenderClientId);
    }

    public PlayerUpgradeState? GetState(ulong clientId)
    {
        int index = GetStateIndex(clientId);
        return index >= 0 ? PlayerStates[index] : null;
    }

    public bool TryGetUpgradeOffers(ulong clientId, out byte[] offers)
    {
        for (int i = 0; i < UpgradeOffers.Count; i++)
        {
            UpgradeOfferState state = UpgradeOffers[i];
            if (state.ClientId != clientId)
                continue;

            offers = new[] { (byte)state.Card0, (byte)state.Card1, (byte)state.Card2 };
            return true;
        }

        offers = null;
        return false;
    }

    public byte GetUpgradeCount(ulong clientId, UpgradeCardId card)
    {
        PlayerUpgradeState? state = GetState(clientId);
        return state.HasValue ? GetCardCount(state.Value, card) : (byte)0;
    }

    public byte GetPassiveCount(ulong clientId, PassiveUpgradeId passive)
    {
        return GetUpgradeCount(clientId, ToCardId(passive));
    }

    public byte GetToolCount(ulong clientId, ActiveToolId tool)
    {
        return GetUpgradeCount(clientId, ToCardId(tool));
    }

    private static PassiveUpgradeId ToPassiveId(UpgradeCardId card)
    {
        return card >= UpgradeCardId.OverdriveServos && card <= UpgradeCardId.AmbushProtocol
            ? (PassiveUpgradeId)(byte)card
            : PassiveUpgradeId.None;
    }

    private static ActiveToolId ToToolId(UpgradeCardId card)
    {
        return card >= UpgradeCardId.PriorityUplink && card <= UpgradeCardId.IdentityScramble
            ? (ActiveToolId)((byte)card - (byte)UpgradeCardId.PriorityUplink + (byte)ActiveToolId.PriorityUplink)
            : ActiveToolId.None;
    }

    private static UpgradeCardId ToCardId(PassiveUpgradeId passive)
    {
        return passive == PassiveUpgradeId.None ? UpgradeCardId.None : (UpgradeCardId)(byte)passive;
    }

    private static UpgradeCardId ToCardId(ActiveToolId tool)
    {
        return tool == ActiveToolId.None
            ? UpgradeCardId.None
            : (UpgradeCardId)((byte)tool - (byte)ActiveToolId.PriorityUplink + (byte)UpgradeCardId.PriorityUplink);
    }

    private static byte GetCardCount(PlayerUpgradeState state, UpgradeCardId card)
    {
        return card switch
        {
            UpgradeCardId.OverdriveServos => state.OverdriveServosCount,
            UpgradeCardId.ForensicCache => state.ForensicCacheCount,
            UpgradeCardId.ThreatSensor => state.ThreatSensorCount,
            UpgradeCardId.PursuitProtocol => state.PursuitProtocolCount,
            UpgradeCardId.EscapeRoutine => state.EscapeRoutineCount,
            UpgradeCardId.AmbushProtocol => state.AmbushProtocolCount,
            UpgradeCardId.PriorityUplink => state.PriorityUplinkCount,
            UpgradeCardId.IdentityAnchor => state.IdentityAnchorCount,
            UpgradeCardId.ValveOverride => state.ValveOverrideCount,
            UpgradeCardId.SystemBlackout => state.SystemBlackoutCount,
            UpgradeCardId.IdentityScramble => state.IdentityScrambleCount,
            _ => 0
        };
    }

    private static int GetOwnedCardCount(PlayerUpgradeState state)
    {
        return state.OverdriveServosCount + state.ForensicCacheCount + state.ThreatSensorCount +
               state.PursuitProtocolCount + state.EscapeRoutineCount + state.AmbushProtocolCount +
               state.PriorityUplinkCount + state.IdentityAnchorCount + state.ValveOverrideCount +
               state.SystemBlackoutCount + state.IdentityScrambleCount;
    }

    private static void IncrementCardCount(ref PlayerUpgradeState state, UpgradeCardId card)
    {
        switch (card)
        {
            case UpgradeCardId.OverdriveServos: state.OverdriveServosCount++; break;
            case UpgradeCardId.ForensicCache: state.ForensicCacheCount++; break;
            case UpgradeCardId.ThreatSensor: state.ThreatSensorCount++; break;
            case UpgradeCardId.PursuitProtocol: state.PursuitProtocolCount++; break;
            case UpgradeCardId.EscapeRoutine: state.EscapeRoutineCount++; break;
            case UpgradeCardId.AmbushProtocol: state.AmbushProtocolCount++; break;
            case UpgradeCardId.PriorityUplink: state.PriorityUplinkCount++; break;
            case UpgradeCardId.IdentityAnchor: state.IdentityAnchorCount++; break;
            case UpgradeCardId.ValveOverride: state.ValveOverrideCount++; break;
            case UpgradeCardId.SystemBlackout: state.SystemBlackoutCount++; break;
            case UpgradeCardId.IdentityScramble: state.IdentityScrambleCount++; break;
        }
    }

    private static void DecrementCardCount(ref PlayerUpgradeState state, UpgradeCardId card)
    {
        switch (card)
        {
            case UpgradeCardId.OverdriveServos: if (state.OverdriveServosCount > 0) state.OverdriveServosCount--; break;
            case UpgradeCardId.ForensicCache: if (state.ForensicCacheCount > 0) state.ForensicCacheCount--; break;
            case UpgradeCardId.ThreatSensor: if (state.ThreatSensorCount > 0) state.ThreatSensorCount--; break;
            case UpgradeCardId.PursuitProtocol: if (state.PursuitProtocolCount > 0) state.PursuitProtocolCount--; break;
            case UpgradeCardId.EscapeRoutine: if (state.EscapeRoutineCount > 0) state.EscapeRoutineCount--; break;
            case UpgradeCardId.AmbushProtocol: if (state.AmbushProtocolCount > 0) state.AmbushProtocolCount--; break;
            case UpgradeCardId.PriorityUplink: if (state.PriorityUplinkCount > 0) state.PriorityUplinkCount--; break;
            case UpgradeCardId.IdentityAnchor: if (state.IdentityAnchorCount > 0) state.IdentityAnchorCount--; break;
            case UpgradeCardId.ValveOverride: if (state.ValveOverrideCount > 0) state.ValveOverrideCount--; break;
            case UpgradeCardId.SystemBlackout: if (state.SystemBlackoutCount > 0) state.SystemBlackoutCount--; break;
            case UpgradeCardId.IdentityScramble: if (state.IdentityScrambleCount > 0) state.IdentityScrambleCount--; break;
        }
    }

    public bool HasPassive(ulong clientId, PassiveUpgradeId passive)
    {
        return GetPassiveCount(clientId, passive) > 0;
    }

    public float GetMovementMultiplier(ulong clientId)
    {
        float multiplier = 1f + (0.10f * GetPassiveCount(clientId, PassiveUpgradeId.OverdriveServos));
        FirstPersonController player = FindPlayer(clientId);
        if (player != null && player.escapeRoutineEndTime.Value > NetworkManager.Singleton.ServerTime.Time)
            multiplier *= 1f + (0.15f * GetPassiveCount(clientId, PassiveUpgradeId.EscapeRoutine));
        return multiplier;
    }

    public double GetKillCooldown(ulong clientId)
    {
        return System.Math.Max(
            DemoBalanceConfig.MinimumKillCooldownSeconds,
            DemoBalanceConfig.BaseKillCooldownSeconds -
            (DemoBalanceConfig.KillCooldownReductionPerUpgradeSeconds * GetPassiveCount(clientId, PassiveUpgradeId.PursuitProtocol)));
    }

    public float GetKillRange(ulong clientId)
    {
        return DemoBalanceConfig.BaseKillRangeMeters +
               (1.75f * GetPassiveCount(clientId, PassiveUpgradeId.AmbushProtocol));
    }

    public void NotifySuccessfulKill(ulong killerClientId, ulong victimClientId)
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        if (GetPassiveCount(killerClientId, PassiveUpgradeId.EscapeRoutine) > 0)
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
            if (state.ThreatSensorCount == 0 || state.ClientId == victimClientId)
                continue;

            FirstPersonController sensorOwner = FindPlayer(state.ClientId);
            if (sensorOwner == null || sensorOwner.isDead.Value ||
                Vector3.Distance(sensorOwner.transform.position, killerPlayer.transform.position) >
                DemoBalanceConfig.ThreatSensorRangeMeters * state.ThreatSensorCount)
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
        return NetworkPlayerLookup.Find(clientId);
    }
}
