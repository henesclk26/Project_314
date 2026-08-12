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
            bool hasOnlineLobby = MultiplayerManager.Instance != null &&
                                  MultiplayerManager.Instance.HasActiveLobby;
            bool isQuickTest = !hasOnlineLobby;
            bool onlineMatchRequested = hasOnlineLobby &&
                                         MultiplayerManager.Instance.IsGameInProgress;
            isGameStarted.Value = isQuickTest || onlineMatchRequested;
            
            Debug.Log($"[GameManager] OnNetworkSpawn: isGameStarted = {isGameStarted.Value}, " +
                      $"quickTest = {isQuickTest}, onlineMatch = {onlineMatchRequested}");

            if (isGameStarted.Value)
            {
                if (onlineMatchRequested)
                {
                    // Online matches may start once three players are connected.
                    StartCoroutine(WaitAndStartMatch());
                }
                else
                {
                    var mfm = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
                    if (mfm != null) mfm.StartMatch();
                }
            }
        }
    }

    private System.Collections.IEnumerator WaitAndStartMatch()
    {
        const int minimumOnlinePlayers = 3;
        Debug.Log($"[GameManager] Waiting for {minimumOnlinePlayers} players to start online match...");
        while (NetworkManager.Singleton.ConnectedClientsIds.Count < minimumOnlinePlayers)
        {
            yield return new WaitForSeconds(1f);
        }
        
        Debug.Log($"[GameManager] {minimumOnlinePlayers} players connected. Starting match!");
        var mfm = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
        if (mfm != null)
        {
            mfm.StartMatch();
        }
    }

    public void OnPlayerDied(ulong clientId)
    {
        // Stub
    }

    public void SetGameOver(bool value)
    {
        isGameOver = value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void CompleteMissionServerRpc(ulong clientId, string missionType)
    {
        // Stub
    }
}
