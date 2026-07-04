using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Görev/rol/kazanma sistemi kaldırıldı. Sahne referansları bozulmasın diye stub olarak bırakıldı.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public bool isGameOver { get; private set; } = false;
    public NetworkVariable<bool> isGameStarted = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            isGameStarted.Value = MultiplayerManager.Instance != null && MultiplayerManager.Instance.IsGameInProgress;
            Debug.Log($"[GameManager] OnNetworkSpawn: isGameStarted = {isGameStarted.Value}");
        }
    }

    public void OnPlayerDied(ulong clientId)
    {
        // Stub
    }

    [ServerRpc(RequireOwnership = false)]
    public void CompleteMissionServerRpc(ulong clientId, string missionType)
    {
        // Stub
    }
}
