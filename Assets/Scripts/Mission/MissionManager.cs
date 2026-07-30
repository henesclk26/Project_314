using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

public enum FileSabotagePhase : byte
{
    AwaitingExecutable,
    Copying,
    ReadyToDelete,
    Deleting,
    Completed
}

public class MissionManager : NetworkBehaviour
{
    public static MissionManager Instance { get; private set; }

    // Mission states
    public NetworkVariable<bool> IsBatteryRoomUnlocked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsBatteryCollected = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsGeneratorActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsWaveFrequencyMissionCompleted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsCircuitMissionCompleted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsPressureMissionActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsPressureMissionCompleted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> CurrentPressure = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> PressureTargetMin = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> PressureTargetMax = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> Valve003Effect = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> Valve004Effect = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> PressureStabilizeProgress = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Valve003TurnSequence = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Valve004TurnSequence = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Valve003TurnDirection = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Valve004TurnDirection = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // MissionComputer file sabotage state
    public NetworkVariable<FileSabotagePhase> FileSabotageState = new NetworkVariable<FileSabotagePhase>(
        FileSabotagePhase.AwaitingExecutable,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> FileSabotageDeletedFolderMask = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> FileSabotageActiveFolderIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<double> FileSabotageOperationEndTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // CircuitMission power diversion sabotage state
    public NetworkVariable<bool> IsCircuitSabotageInitialized = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> CircuitSabotageTemplateIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> CircuitSabotagePackedState = new NetworkVariable<ulong>(
        0UL,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> CircuitSabotageRevision = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsCircuitSabotageCompleted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // WaveFrequencyTerminal satellite routing sabotage state
    public NetworkVariable<bool> IsWaveSatelliteSabotageInitialized = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> WaveSatelliteSabotageSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> WaveSatelliteSabotagePackedConnections = new NetworkVariable<ulong>(
        WaveSatelliteSabotageLayout.EmptyConnections,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<int> WaveSatelliteSabotageRevision = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsWaveSatelliteSabotageCompleted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    // Valve Mission states
    public NetworkVariable<bool> IsValveMissionActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> ValvesTurned = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action OnBatteryRoomUnlocked;
    public event Action OnBatteryCollected;
    public event Action OnGeneratorActivated;
    public event Action OnWaveFrequencyMissionCompleted;
    public event Action OnCircuitMissionCompleted;
    public event Action OnValveMissionStarted;
    public event Action OnValveMissionCompleted;

    private const float PressureMaximum = 100f;
    private const float PressureOverpressureThreshold = 93f;
    private const float PressureResponseSpeed = 16f;
    private const float PressureInputDelay = 0.35f;
    private const float ValveInputCooldown = 1f;
    private const float PressureStabilizeDuration = 1.5f;
    private const int PressureGenerationAttempts = 512;
    public const float FileSabotageCopyDuration = 3f;
    public const float FileSabotageDeleteDuration = 1.5f;
    public const int FileSabotageFolderCount = 5;
    private const int FileSabotageAllFoldersMask = (1 << FileSabotageFolderCount) - 1;

    private readonly List<PendingPressureAdjustment> pendingPressureAdjustments = new List<PendingPressureAdjustment>();
    private float pressureTarget;
    private float valve003NextInputTime;
    private float valve004NextInputTime;
    private bool isPressureStabilizing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        IsBatteryRoomUnlocked.OnValueChanged += (oldVal, newVal) => { if (newVal) OnBatteryRoomUnlocked?.Invoke(); };
        IsBatteryCollected.OnValueChanged += (oldVal, newVal) => { if (newVal) OnBatteryCollected?.Invoke(); };
        IsGeneratorActive.OnValueChanged += (oldVal, newVal) => { if (newVal) OnGeneratorActivated?.Invoke(); };
        IsWaveFrequencyMissionCompleted.OnValueChanged += (oldVal, newVal) => { if (newVal) OnWaveFrequencyMissionCompleted?.Invoke(); };
        IsCircuitMissionCompleted.OnValueChanged += (oldVal, newVal) => { if (newVal) OnCircuitMissionCompleted?.Invoke(); };
        IsValveMissionActive.OnValueChanged += (oldVal, newVal) => { 
            if (newVal) OnValveMissionStarted?.Invoke();
            else if (!newVal && oldVal) OnValveMissionCompleted?.Invoke();
        };
    }

    private void Update()
    {
        if (!CanSimulateServerState())
            return;

        UpdateFileSabotage();

        if (!IsPressureMissionActive.Value || IsPressureMissionCompleted.Value)
            return;

        for (int i = pendingPressureAdjustments.Count - 1; i >= 0; i--)
        {
            PendingPressureAdjustment adjustment = pendingPressureAdjustments[i];
            if (Time.time < adjustment.ExecuteAt)
                continue;

            pressureTarget = Mathf.Clamp(pressureTarget + adjustment.Delta, 0f, PressureMaximum);
            pendingPressureAdjustments.RemoveAt(i);
        }

        CurrentPressure.Value = Mathf.MoveTowards(
            CurrentPressure.Value,
            pressureTarget,
            PressureResponseSpeed * Time.deltaTime);

        UpdatePressureStabilization();
    }

    public void RequestStartFileCopy()
    {
        if (IsSpawned)
            RequestStartFileCopyRpc();
        else
            StartFileCopy();
    }

    public void RequestDeleteFolder(int folderIndex)
    {
        if (IsSpawned)
            RequestDeleteFolderRpc(folderIndex);
        else
            StartFolderDeletion(folderIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartFileCopyRpc()
    {
        StartFileCopy();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDeleteFolderRpc(int folderIndex)
    {
        StartFolderDeletion(folderIndex);
    }

    public float GetFileSabotageOperationProgress()
    {
        float duration;
        if (FileSabotageState.Value == FileSabotagePhase.Copying)
            duration = FileSabotageCopyDuration;
        else if (FileSabotageState.Value == FileSabotagePhase.Deleting)
            duration = FileSabotageDeleteDuration;
        else
            return FileSabotageState.Value == FileSabotagePhase.Completed ? 1f : 0f;

        double remaining = FileSabotageOperationEndTime.Value - GetSynchronizedTime();
        return Mathf.Clamp01(1f - (float)(remaining / duration));
    }

    public bool IsFileSabotageFolderDeleted(int folderIndex)
    {
        if (folderIndex < 0 || folderIndex >= FileSabotageFolderCount)
            return false;

        return (FileSabotageDeletedFolderMask.Value & (1 << folderIndex)) != 0;
    }

    public void RequestInitializeCircuitSabotage()
    {
        if (IsSpawned)
            InitializeCircuitSabotageRpc();
        else
            InitializeCircuitSabotage();
    }

    public void RequestRotateCircuitSabotageNode(int slot, int direction)
    {
        if (IsSpawned)
            RotateCircuitSabotageNodeRpc(slot, direction);
        else
            RotateCircuitSabotageNode(slot, direction);
    }

    public void RequestResetCircuitSabotage()
    {
        if (IsSpawned)
            ResetCircuitSabotageRpc();
        else
            ResetCircuitSabotage();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InitializeCircuitSabotageRpc()
    {
        InitializeCircuitSabotage();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RotateCircuitSabotageNodeRpc(int slot, int direction)
    {
        RotateCircuitSabotageNode(slot, direction);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ResetCircuitSabotageRpc()
    {
        ResetCircuitSabotage();
    }

    private void InitializeCircuitSabotage()
    {
        if (!CanSimulateServerState() || IsCircuitSabotageInitialized.Value)
            return;

        int templateIndex = UnityEngine.Random.Range(0, CircuitSabotageTemplates.All.Length);
        CircuitSabotageTemplates.Template template = CircuitSabotageTemplates.All[templateIndex];
        CircuitSabotageTemplateIndex.Value = templateIndex;
        CircuitSabotagePackedState.Value = template.InitialPackedState;
        CircuitSabotageRevision.Value++;
        IsCircuitSabotageInitialized.Value = true;
        Debug.Log($"[MissionManager] Circuit sabotage initialized with template {templateIndex + 1}.");
    }

    private void RotateCircuitSabotageNode(int slot, int direction)
    {
        if (!CanSimulateServerState() ||
            !IsCircuitSabotageInitialized.Value ||
            IsCircuitSabotageCompleted.Value ||
            (direction != -1 && direction != 1))
        {
            return;
        }

        int templateIndex = CircuitSabotageTemplateIndex.Value;
        if (templateIndex < 0 || templateIndex >= CircuitSabotageTemplates.All.Length)
            return;

        CircuitSabotageTemplates.Template template = CircuitSabotageTemplates.All[templateIndex];
        if (slot < 0 || slot >= template.RotatableCount)
            return;

        ulong rotated = CircuitSabotageTemplates.Rotate(
            template,
            CircuitSabotagePackedState.Value,
            slot,
            direction);
        CircuitSabotagePackedState.Value = rotated;
        CircuitSabotageRevision.Value++;

        if (CircuitSabotageTemplates.Evaluate(
                template,
                rotated,
                out _,
                out _))
        {
            IsCircuitSabotageCompleted.Value = true;
            Debug.Log("[MissionManager] Circuit power diversion sabotage completed.");
        }
    }

    private void ResetCircuitSabotage()
    {
        if (!CanSimulateServerState() ||
            !IsCircuitSabotageInitialized.Value ||
            IsCircuitSabotageCompleted.Value)
        {
            return;
        }

        int templateIndex = CircuitSabotageTemplateIndex.Value;
        if (templateIndex < 0 || templateIndex >= CircuitSabotageTemplates.All.Length)
            return;

        CircuitSabotagePackedState.Value =
            CircuitSabotageTemplates.All[templateIndex].InitialPackedState;
        CircuitSabotageRevision.Value++;
        Debug.Log("[MissionManager] Circuit power diversion reset.");
    }

    public void RequestInitializeWaveSatelliteSabotage()
    {
        if (IsSpawned)
            InitializeWaveSatelliteSabotageRpc();
        else
            InitializeWaveSatelliteSabotage();
    }

    public void RequestConnectWaveSatellite(int satelliteIndex)
    {
        if (IsSpawned)
            ConnectWaveSatelliteRpc(satelliteIndex);
        else
            ConnectWaveSatellite(satelliteIndex);
    }

    public void RequestDisconnectWaveSatellite(int satelliteIndex)
    {
        if (IsSpawned)
            DisconnectWaveSatelliteRpc(satelliteIndex);
        else
            DisconnectWaveSatellite(satelliteIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InitializeWaveSatelliteSabotageRpc()
    {
        InitializeWaveSatelliteSabotage();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ConnectWaveSatelliteRpc(int satelliteIndex)
    {
        ConnectWaveSatellite(satelliteIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DisconnectWaveSatelliteRpc(int satelliteIndex)
    {
        DisconnectWaveSatellite(satelliteIndex);
    }

    private void InitializeWaveSatelliteSabotage()
    {
        if (!CanSimulateServerState() ||
            IsWaveSatelliteSabotageInitialized.Value)
        {
            return;
        }

        WaveSatelliteSabotageSeed.Value =
            UnityEngine.Random.Range(1, int.MaxValue);
        WaveSatelliteSabotagePackedConnections.Value =
            WaveSatelliteSabotageLayout.EmptyConnections;
        WaveSatelliteSabotageRevision.Value++;
        IsWaveSatelliteSabotageInitialized.Value = true;
        Debug.Log("[MissionManager] Wave satellite sabotage initialized.");
    }

    private void ConnectWaveSatellite(int satelliteIndex)
    {
        if (!CanSimulateServerState() ||
            !IsWaveSatelliteSabotageInitialized.Value ||
            IsWaveSatelliteSabotageCompleted.Value ||
            satelliteIndex < 0 ||
            satelliteIndex >= WaveSatelliteSabotageLayout.SatelliteCount)
        {
            return;
        }

        ulong current = WaveSatelliteSabotagePackedConnections.Value;
        ulong updated = WaveSatelliteSabotageLayout.ConnectToFirstEmptyPort(
            current,
            satelliteIndex,
            out int assignedPort);
        if (assignedPort < 0 || updated == current)
            return;

        WaveSatelliteSabotagePackedConnections.Value = updated;
        WaveSatelliteSabotageRevision.Value++;
        EvaluateWaveSatelliteSabotage(updated);
    }

    private void DisconnectWaveSatellite(int satelliteIndex)
    {
        if (!CanSimulateServerState() ||
            !IsWaveSatelliteSabotageInitialized.Value ||
            IsWaveSatelliteSabotageCompleted.Value ||
            satelliteIndex < 0 ||
            satelliteIndex >= WaveSatelliteSabotageLayout.SatelliteCount)
        {
            return;
        }

        ulong current = WaveSatelliteSabotagePackedConnections.Value;
        ulong updated = WaveSatelliteSabotageLayout.DisconnectSatellite(
            current,
            satelliteIndex,
            out int disconnectedPort);
        if (disconnectedPort < 0 || updated == current)
            return;

        WaveSatelliteSabotagePackedConnections.Value = updated;
        WaveSatelliteSabotageRevision.Value++;
    }

    private void EvaluateWaveSatelliteSabotage(ulong packedConnections)
    {
        WaveSatelliteSabotageLayout.Layout layout =
            WaveSatelliteSabotageLayout.Create(
                WaveSatelliteSabotageSeed.Value);
        if (!WaveSatelliteSabotageLayout.IsComplete(
                layout,
                packedConnections))
        {
            return;
        }

        IsWaveSatelliteSabotageCompleted.Value = true;
        Debug.Log("[MissionManager] Wave satellite sabotage completed.");
    }

    private void StartFileCopy()
    {
        if (!CanSimulateServerState() ||
            FileSabotageState.Value != FileSabotagePhase.AwaitingExecutable)
        {
            return;
        }

        FileSabotageActiveFolderIndex.Value = -1;
        FileSabotageOperationEndTime.Value =
            GetSynchronizedTime() + FileSabotageCopyDuration;
        FileSabotageState.Value = FileSabotagePhase.Copying;
        Debug.Log("[MissionManager] MissionComputer file copy started.");
    }

    private void StartFolderDeletion(int folderIndex)
    {
        if (!CanSimulateServerState() ||
            FileSabotageState.Value != FileSabotagePhase.ReadyToDelete ||
            folderIndex < 0 ||
            folderIndex >= FileSabotageFolderCount ||
            IsFileSabotageFolderDeleted(folderIndex))
        {
            return;
        }

        FileSabotageActiveFolderIndex.Value = folderIndex;
        FileSabotageOperationEndTime.Value =
            GetSynchronizedTime() + FileSabotageDeleteDuration;
        FileSabotageState.Value = FileSabotagePhase.Deleting;
        Debug.Log($"[MissionManager] MissionComputer folder deletion started: {folderIndex}.");
    }

    private void UpdateFileSabotage()
    {
        FileSabotagePhase phase = FileSabotageState.Value;
        if ((phase != FileSabotagePhase.Copying &&
             phase != FileSabotagePhase.Deleting) ||
            GetSynchronizedTime() < FileSabotageOperationEndTime.Value)
        {
            return;
        }

        FileSabotageOperationEndTime.Value = 0d;

        if (phase == FileSabotagePhase.Copying)
        {
            FileSabotageState.Value = FileSabotagePhase.ReadyToDelete;
            Debug.Log("[MissionManager] MissionComputer file copy completed.");
            return;
        }

        int folderIndex = FileSabotageActiveFolderIndex.Value;
        if (folderIndex >= 0 && folderIndex < FileSabotageFolderCount)
            FileSabotageDeletedFolderMask.Value |= 1 << folderIndex;

        FileSabotageActiveFolderIndex.Value = -1;
        if (FileSabotageDeletedFolderMask.Value == FileSabotageAllFoldersMask)
        {
            FileSabotageState.Value = FileSabotagePhase.Completed;
            Debug.Log("[MissionManager] MissionComputer file sabotage completed.");
        }
        else
        {
            FileSabotageState.Value = FileSabotagePhase.ReadyToDelete;
        }
    }

    private bool CanSimulateServerState()
    {
        return IsServer ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening;
    }

    private static double GetSynchronizedTime()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return NetworkManager.Singleton.ServerTime.Time;

        return Time.timeAsDouble;
    }

    [ServerRpc(RequireOwnership = false)]
    public void UnlockBatteryRoomServerRpc()
    {
        IsBatteryRoomUnlocked.Value = true;
        Debug.Log("[MissionManager] Battery room unlocked!");
    }

    [ServerRpc(RequireOwnership = false)]
    public void CollectBatteryServerRpc()
    {
        if (IsBatteryRoomUnlocked.Value)
        {
            IsBatteryCollected.Value = true;
            Debug.Log("[MissionManager] Battery collected!");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ActivateGeneratorServerRpc()
    {
        if (IsBatteryCollected.Value)
        {
            IsGeneratorActive.Value = true;
            IsBatteryRoomUnlocked.Value = false; // Lock the door again as requested
            Debug.Log("[MissionManager] Generator activated and door locked!");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ActivateValveMissionServerRpc()
    {
        if (!IsValveMissionActive.Value && ValvesTurned.Value < 3)
        {
            IsValveMissionActive.Value = true;
            Debug.Log("[MissionManager] Valve mission activated!");
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CompleteWaveFrequencyMissionRpc()
    {
        if (IsWaveFrequencyMissionCompleted.Value)
            return;

        IsWaveFrequencyMissionCompleted.Value = true;
        Debug.Log("[MissionManager] Wave frequency mission completed!");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CompleteCircuitMissionRpc()
    {
        if (IsCircuitMissionCompleted.Value)
            return;

        IsCircuitMissionCompleted.Value = true;
        Debug.Log("[MissionManager] Circuit mission completed!");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ActivatePressureMissionRpc()
    {
        if (IsPressureMissionCompleted.Value)
            return;

        if (IsPressureMissionActive.Value)
            return;

        GeneratePressureChallenge(
            out float startingPressure,
            out float lowEffect,
            out float highEffect,
            out float targetMin,
            out float targetMax);
        bool valve003IsLow = UnityEngine.Random.value > 0.5f;

        Valve003Effect.Value = valve003IsLow ? lowEffect : highEffect;
        Valve004Effect.Value = valve003IsLow ? highEffect : lowEffect;

        PressureTargetMin.Value = targetMin;
        PressureTargetMax.Value = targetMax;
        CurrentPressure.Value = startingPressure;
        pressureTarget = CurrentPressure.Value;
        pendingPressureAdjustments.Clear();
        valve003NextInputTime = 0f;
        valve004NextInputTime = 0f;
        isPressureStabilizing = false;
        PressureStabilizeProgress.Value = 0f;
        IsPressureMissionActive.Value = true;
        Debug.Log("[MissionManager] Pressure calibration mission activated!");
    }

    private static void GeneratePressureChallenge(
        out float startingPressure,
        out float lowEffect,
        out float highEffect,
        out float targetMin,
        out float targetMax)
    {
        for (int attempt = 0; attempt < PressureGenerationAttempts; attempt++)
        {
            float candidateStart = UnityEngine.Random.Range(16f, 24f);
            float candidateLow = UnityEngine.Random.Range(6.5f, 9.5f);
            float candidateHigh = UnityEngine.Random.Range(12f, 17f);
            int lowTurns = UnityEngine.Random.Range(2, 5);
            int highTurns = UnityEngine.Random.Range(1, 4);
            int totalTurns = lowTurns + highTurns;

            if (totalTurns < 5 || totalTurns > 8)
                continue;

            float targetCenter = candidateStart +
                candidateLow * lowTurns +
                candidateHigh * highTurns;
            if (targetCenter < 58f || targetCenter > 76f)
                continue;

            const float targetHalfWidth = 1.75f;
            float candidateMin = targetCenter - targetHalfWidth;
            float candidateMax = targetCenter + targetHalfWidth;
            if (CanSingleValveReachTarget(candidateStart, candidateLow, candidateMin, candidateMax) ||
                CanSingleValveReachTarget(candidateStart, candidateHigh, candidateMin, candidateMax))
            {
                continue;
            }

            startingPressure = candidateStart;
            lowEffect = candidateLow;
            highEffect = candidateHigh;
            targetMin = candidateMin;
            targetMax = candidateMax;
            return;
        }

        startingPressure = 20f;
        lowEffect = 7f;
        highEffect = 13f;
        targetMin = 65.5f;
        targetMax = 68.5f;
    }

    private static bool CanSingleValveReachTarget(
        float startingPressure,
        float valveEffect,
        float targetMin,
        float targetMax)
    {
        float[] pressureAnchors = { startingPressure, 0f, PressureMaximum };
        foreach (float anchor in pressureAnchors)
        {
            for (int turns = -32; turns <= 32; turns++)
            {
                float result = Mathf.Clamp(
                    anchor + valveEffect * turns,
                    0f,
                    PressureMaximum);
                if (result >= targetMin && result <= targetMax)
                    return true;
            }
        }

        return false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AdjustPressureValveRpc(int valveId, int direction)
    {
        if (!IsPressureMissionActive.Value || IsPressureMissionCompleted.Value || direction == 0)
            return;

        direction = direction > 0 ? 1 : -1;
        float effect;

        if (valveId == 3)
        {
            if (Time.time < valve003NextInputTime)
                return;

            valve003NextInputTime = Time.time + ValveInputCooldown;
            effect = Valve003Effect.Value;
            Valve003TurnDirection.Value = direction;
            Valve003TurnSequence.Value++;
        }
        else if (valveId == 4)
        {
            if (Time.time < valve004NextInputTime)
                return;

            valve004NextInputTime = Time.time + ValveInputCooldown;
            effect = Valve004Effect.Value;
            Valve004TurnDirection.Value = direction;
            Valve004TurnSequence.Value++;
        }
        else
        {
            return;
        }

        if (direction > 0 && GetProjectedPressureTarget() >= PressureOverpressureThreshold)
            return;

        pendingPressureAdjustments.Add(new PendingPressureAdjustment
        {
            ExecuteAt = Time.time + PressureInputDelay,
            Delta = effect * direction
        });
    }

    private float GetProjectedPressureTarget()
    {
        float projectedPressure = pressureTarget;
        foreach (PendingPressureAdjustment adjustment in pendingPressureAdjustments)
            projectedPressure += adjustment.Delta;

        return Mathf.Clamp(projectedPressure, 0f, PressureMaximum);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void BeginPressureStabilizationRpc()
    {
        if (!IsPressureMissionActive.Value || IsPressureMissionCompleted.Value)
            return;

        if (!IsPressureInOptimalRange())
            return;

        isPressureStabilizing = true;
        PressureStabilizeProgress.Value = 0f;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CancelPressureStabilizationRpc()
    {
        if (!IsPressureMissionActive.Value || IsPressureMissionCompleted.Value)
            return;

        isPressureStabilizing = false;
        PressureStabilizeProgress.Value = 0f;
    }

    private void UpdatePressureStabilization()
    {
        if (!isPressureStabilizing)
            return;

        if (!IsPressureInOptimalRange())
        {
            isPressureStabilizing = false;
            PressureStabilizeProgress.Value = 0f;
            return;
        }

        PressureStabilizeProgress.Value = Mathf.Min(
            1f,
            PressureStabilizeProgress.Value + Time.deltaTime / PressureStabilizeDuration);

        if (PressureStabilizeProgress.Value < 1f)
            return;

        isPressureStabilizing = false;
        IsPressureMissionCompleted.Value = true;
        IsPressureMissionActive.Value = false;
        pendingPressureAdjustments.Clear();
        Debug.Log("[MissionManager] Pressure calibration mission completed!");
    }

    private bool IsPressureInOptimalRange()
    {
        return CurrentPressure.Value >= PressureTargetMin.Value &&
            CurrentPressure.Value <= PressureTargetMax.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void TurnValveServerRpc()
    {
        if (IsValveMissionActive.Value)
        {
            ValvesTurned.Value++;
            Debug.Log($"[MissionManager] Valve turned! Total: {ValvesTurned.Value}/3");
            if (ValvesTurned.Value >= 3)
            {
                StartCoroutine(CompleteValveMissionDelayed());
            }
        }
    }

    private System.Collections.IEnumerator CompleteValveMissionDelayed()
    {
        yield return new WaitForSeconds(2f);
        IsValveMissionActive.Value = false; // Mission complete
        Debug.Log("[MissionManager] All valves turned! Valve mission complete after delay!");
    }

    private struct PendingPressureAdjustment
    {
        public float ExecuteAt;
        public float Delta;
    }
}
