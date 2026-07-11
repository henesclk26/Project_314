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

    public event Action OnBatteryRoomUnlocked;
    public event Action OnBatteryCollected;
    public event Action OnGeneratorActivated;

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
}
