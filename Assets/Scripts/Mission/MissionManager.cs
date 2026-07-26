using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

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
        if (!IsServer || !IsPressureMissionActive.Value || IsPressureMissionCompleted.Value)
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
