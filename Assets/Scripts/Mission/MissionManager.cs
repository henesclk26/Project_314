using Unity.Netcode;
using UnityEngine;
using System;

public class MissionManager : NetworkBehaviour
{
    public static MissionManager Instance { get; private set; }

    // Mission states
    public NetworkVariable<bool> IsBatteryRoomUnlocked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsBatteryCollected = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsGeneratorActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Valve Mission states
    public NetworkVariable<bool> IsValveMissionActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> ValvesTurned = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action OnBatteryRoomUnlocked;
    public event Action OnBatteryCollected;
    public event Action OnGeneratorActivated;
    public event Action OnValveMissionStarted;
    public event Action OnValveMissionCompleted;

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
        IsValveMissionActive.OnValueChanged += (oldVal, newVal) => { 
            if (newVal) OnValveMissionStarted?.Invoke();
            else if (!newVal && oldVal) OnValveMissionCompleted?.Invoke();
        };
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
}
