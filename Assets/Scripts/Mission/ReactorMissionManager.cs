using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ReactorMissionPhase
{
    Inactive,
    Fueling,
    Ready,
    Synchronizing,
    InsufficientEnergy,
    Completed
}

public class ReactorMissionManager : NetworkBehaviour
{
    private const ulong AvailableCan = ulong.MaxValue;
    private const ulong DepositedCan = ulong.MaxValue - 1;
    private const int GasCanCount = 6;
    private const int FuelPerCan = 20;
    private const float LeverSyncWindow = 1f;
    private const float FailureMessageDuration = 2f;

    public static ReactorMissionManager Instance { get; private set; }

    public NetworkVariable<bool> IsMissionActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsMissionCompleted = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> FuelPercent = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ReactorMissionPhase> Phase = new(
        ReactorMissionPhase.Inactive,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> LeverMask = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> LeverSyncProgress = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> FailureSequence = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> gasCan1Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> gasCan2Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> gasCan3Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> gasCan4Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> gasCan5Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> gasCan6Carrier = new(
        AvailableCan, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly HashSet<ulong> leverParticipants = new();
    private float leverTimer;
    private float failureRecoveryTime;

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
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    // Called by the server when a repeatable cooperative TaskRun is assigned.
    // The task assignment owns lifecycle reset; opening the terminal does not.
    public void ResetForTaskAssignment()
    {
        if (!IsServer)
            return;

        IsMissionActive.Value = false;
        IsMissionCompleted.Value = false;
        FuelPercent.Value = 0;
        Phase.Value = ReactorMissionPhase.Inactive;
        LeverMask.Value = 0;
        LeverSyncProgress.Value = 0f;
        leverParticipants.Clear();
        SetAllCanStates(AvailableCan);
    }

    private void Update()
    {
        if (!IsServer || !IsMissionActive.Value || IsMissionCompleted.Value)
            return;

        if (Phase.Value == ReactorMissionPhase.Synchronizing)
        {
            leverTimer += Time.deltaTime;
            LeverSyncProgress.Value = Mathf.Clamp01(leverTimer / LeverSyncWindow);
            if (leverTimer >= LeverSyncWindow)
                FailLeverSynchronization();
        }
        else if (Phase.Value == ReactorMissionPhase.InsufficientEnergy &&
                 Time.time >= failureRecoveryTime)
        {
            Phase.Value = ReactorMissionPhase.Fueling;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ActivateMissionRpc()
    {
        if (IsMissionActive.Value || IsMissionCompleted.Value)
            return;

        IsMissionActive.Value = true;
        FuelPercent.Value = 0;
        Phase.Value = ReactorMissionPhase.Fueling;
        LeverMask.Value = 0;
        LeverSyncProgress.Value = 0f;
        leverParticipants.Clear();
        SetAllCanStates(AvailableCan);
        Debug.Log("[ReactorMission] Mission activated.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PickupGasCanRpc(int gasCanId, RpcParams rpcParams = default)
    {
        if (!CanHandleFuel() || !IsValidGasCanId(gasCanId))
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (IsClientCarrying(clientId) || GetGasCanState(gasCanId) != AvailableCan)
            return;

        SetGasCanState(gasCanId, clientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DepositGasCanRpc(RpcParams rpcParams = default)
    {
        if (!CanHandleFuel())
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        int gasCanId = GetCarriedGasCanId(clientId);
        if (gasCanId < 0)
            return;

        SetGasCanState(gasCanId, DepositedCan);
        FuelPercent.Value = Mathf.Min(100, FuelPercent.Value + FuelPerCan);
        if (FuelPercent.Value < 100)
            return;

        Phase.Value = ReactorMissionPhase.Ready;
        EnsureReserveCanAvailable();
        Debug.Log("[ReactorMission] Fuel at 100%. Lever synchronization enabled.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PullLeverRpc(int leverId, RpcParams rpcParams = default)
    {
        if (!IsMissionActive.Value || IsMissionCompleted.Value ||
            FuelPercent.Value < 100 || leverId < 0 || leverId > 2)
        {
            return;
        }

        int bit = 1 << leverId;
        ulong clientId = rpcParams.Receive.SenderClientId;
        if ((LeverMask.Value & bit) != 0 || leverParticipants.Contains(clientId))
            return;

        if (LeverMask.Value == 0)
        {
            leverTimer = 0f;
            LeverSyncProgress.Value = 0f;
            Phase.Value = ReactorMissionPhase.Synchronizing;
        }

        leverParticipants.Add(clientId);
        LeverMask.Value |= bit;
        if (LeverMask.Value != 0b111)
            return;

        IsMissionCompleted.Value = true;
        IsMissionActive.Value = false;
        Phase.Value = ReactorMissionPhase.Completed;
        LeverSyncProgress.Value = 1f;
        Debug.Log("[ReactorMission] Mission completed.");
    }

    public bool IsGasCanAvailable(int gasCanId)
    {
        return IsValidGasCanId(gasCanId) && GetGasCanState(gasCanId) == AvailableCan;
    }

    public bool IsClientCarrying(ulong clientId)
    {
        return GetCarriedGasCanId(clientId) >= 0;
    }

    public bool IsLeverPulled(int leverId)
    {
        return leverId >= 0 && leverId <= 2 && (LeverMask.Value & (1 << leverId)) != 0;
    }

    public int AvailableGasCanCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < GasCanCount; i++)
            {
                if (GetGasCanState(i) == AvailableCan)
                    count++;
            }

            return count;
        }
    }

    private bool CanHandleFuel()
    {
        return IsMissionActive.Value &&
               !IsMissionCompleted.Value &&
               Phase.Value == ReactorMissionPhase.Fueling &&
               FuelPercent.Value < 100;
    }

    private void FailLeverSynchronization()
    {
        FuelPercent.Value = 40;
        LeverMask.Value = 0;
        LeverSyncProgress.Value = 0f;
        leverParticipants.Clear();
        RespawnRandomDepositedCans(2);
        Phase.Value = ReactorMissionPhase.InsufficientEnergy;
        FailureSequence.Value++;
        failureRecoveryTime = Time.time + FailureMessageDuration;
        Debug.Log("[ReactorMission] Synchronization failed. Fuel reduced to 40%.");
    }

    private void EnsureReserveCanAvailable()
    {
        if (AvailableGasCanCount > 0)
            return;

        List<int> deposited = GetCansWithState(DepositedCan);
        if (deposited.Count == 0)
            return;

        int selected = deposited[Random.Range(0, deposited.Count)];
        SetGasCanState(selected, AvailableCan);
    }

    private void RespawnRandomDepositedCans(int count)
    {
        List<int> deposited = GetCansWithState(DepositedCan);
        for (int i = 0; i < count && deposited.Count > 0; i++)
        {
            int selection = Random.Range(0, deposited.Count);
            SetGasCanState(deposited[selection], AvailableCan);
            deposited.RemoveAt(selection);
        }
    }

    private List<int> GetCansWithState(ulong state)
    {
        List<int> result = new();
        for (int i = 0; i < GasCanCount; i++)
        {
            if (GetGasCanState(i) == state)
                result.Add(i);
        }

        return result;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        int gasCanId = GetCarriedGasCanId(clientId);
        if (gasCanId >= 0)
            SetGasCanState(gasCanId, AvailableCan);
    }

    private int GetCarriedGasCanId(ulong clientId)
    {
        for (int i = 0; i < GasCanCount; i++)
        {
            if (GetGasCanState(i) == clientId)
                return i;
        }

        return -1;
    }

    private ulong GetGasCanState(int gasCanId)
    {
        return gasCanId switch
        {
            0 => gasCan1Carrier.Value,
            1 => gasCan2Carrier.Value,
            2 => gasCan3Carrier.Value,
            3 => gasCan4Carrier.Value,
            4 => gasCan5Carrier.Value,
            5 => gasCan6Carrier.Value,
            _ => DepositedCan
        };
    }

    private void SetGasCanState(int gasCanId, ulong value)
    {
        switch (gasCanId)
        {
            case 0: gasCan1Carrier.Value = value; break;
            case 1: gasCan2Carrier.Value = value; break;
            case 2: gasCan3Carrier.Value = value; break;
            case 3: gasCan4Carrier.Value = value; break;
            case 4: gasCan5Carrier.Value = value; break;
            case 5: gasCan6Carrier.Value = value; break;
        }
    }

    private void SetAllCanStates(ulong value)
    {
        for (int i = 0; i < GasCanCount; i++)
            SetGasCanState(i, value);
    }

    private static bool IsValidGasCanId(int gasCanId)
    {
        return gasCanId >= 0 && gasCanId < GasCanCount;
    }
}
