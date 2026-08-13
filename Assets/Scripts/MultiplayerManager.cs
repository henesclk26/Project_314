using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;


public struct LobbyPlayerInfo
{
    public string Id;
    public string DisplayName;
}

public class MultiplayerManager : MonoBehaviour
{
    public static MultiplayerManager Instance { get; private set; }

    private const int DemoMinimumPlayers = 3;
    private const int DemoMaximumPlayers = 8;

    public string CurrentJoinCode { get; private set; }
    public string CurrentLobbyCode => currentLobby?.LobbyCode;
    public string CurrentLobbyName => currentLobby?.Name ?? string.Empty;
    public bool CurrentLobbyIsPrivate => currentLobby != null && currentLobby.IsPrivate;
    public int CurrentLobbyMaxPlayers => currentLobby != null ? currentLobby.MaxPlayers : DemoMaximumPlayers;
    public bool HasActiveLobby => currentLobby != null;
    public bool IsReady { get; private set; }
    public bool IsGameInProgress { get; set; } = false;

    public event Action OnLobbyPlayersChanged;
    public event Action OnDisconnectedByHost;

    private Lobby currentLobby;
    private CancellationTokenSource heartbeatToken;
    private bool _isLeavingIntentionally;
    private NetworkManager clientAdmissionNetworkManager;
    private bool matchStartRequested;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        TryRegisterClientAdmissionCallback();
    }

    async void Start()
    {
        TryRegisterClientAdmissionCallback();
        try
        {
            var options = new InitializationOptions();
            options.SetProfile("Player_" + UnityEngine.Random.Range(0, 100000).ToString());

            // Vivox kurulu ama kullanılmıyor; otomatik init hatasını önlemek için devre dışı bırak
            options.SetOption("com.unity.services.vivox", false);

            await UnityServices.InitializeAsync(options);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            IsReady = true;
            Debug.Log("UGS bağlantısı başarılı. Player ID: " + AuthenticationService.Instance.PlayerId);
        }
        catch (Exception e)
        {
            IsReady = false;
            Debug.LogError("[MultiplayerManager] UGS başlatılamadı: " + e.Message);
        }
    }

    public async Task<bool> WaitUntilReadyAsync(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!IsReady && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        return IsReady;
    }

    public IReadOnlyList<LobbyPlayerInfo> GetLobbyPlayers()
    {
        if (currentLobby?.Players == null)
            return Array.Empty<LobbyPlayerInfo>();

        return currentLobby.Players
            .Select(p => new LobbyPlayerInfo
            {
                Id = p.Id,
                DisplayName = GetPlayerDisplayName(p)
            })
            .ToList();
    }

    public async Task RefreshCurrentLobbyAsync()
    {
        if (currentLobby == null) return;

        try
        {
            currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
            OnLobbyPlayersChanged?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MultiplayerManager] Lobi yenilenemedi: " + e.Message);
        }
    }

    public async Task<bool> CreatePublicLobby(string lobbyName, int maxPlayers)
    {
        if (!await EnsureReadyAsync()) return false;

        maxPlayers = Mathf.Clamp(maxPlayers, DemoMinimumPlayers, DemoMaximumPlayers);

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            CurrentJoinCode = relayJoinCode;

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = CreateLocalPlayerData(),
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayCode", new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) }
                }
            };

            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

            SetRelayHostData(allocation);
            NetworkManager.Singleton.StartHost();
            StartHeartbeat();
            OnLobbyPlayersChanged?.Invoke();

            Debug.Log($"Public lobi oluşturuldu | Kod: {currentLobby.LobbyCode}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[MultiplayerManager] Public lobi oluşturulamadı: " + e.Message);
            return false;
        }
    }

    public async Task<bool> CreatePrivateLobby(string lobbyName, int maxPlayers)
    {
        if (!await EnsureReadyAsync()) return false;

        maxPlayers = Mathf.Clamp(maxPlayers, DemoMinimumPlayers, DemoMaximumPlayers);

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            CurrentJoinCode = relayJoinCode;

            var options = new CreateLobbyOptions
            {
                IsPrivate = true,
                Player = CreateLocalPlayerData(),
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayCode", new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };

            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

            SetRelayHostData(allocation);
            NetworkManager.Singleton.StartHost();
            StartHeartbeat();
            OnLobbyPlayersChanged?.Invoke();

            Debug.Log($"Private lobi oluşturuldu | Lobi Kodu: {currentLobby.LobbyCode}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[MultiplayerManager] Private lobi oluşturulamadı: " + e.Message);
            return false;
        }
    }

    public async Task<bool> JoinByCode(string lobbyCode)
    {
        if (!await EnsureReadyAsync()) return false;

        try
        {
            currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            string relayCode = currentLobby.Data["RelayCode"].Value;
            CurrentJoinCode = relayCode;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            SetRelayClientData(joinAllocation);
            SubscribeToNetworkShutdown();
            NetworkManager.Singleton.StartClient();
            OnLobbyPlayersChanged?.Invoke();

            Debug.Log($"Lobiye katılındı: {currentLobby.Name}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("Lobiye katılamadı. Kod yanlış olabilir: " + e.Message);
            return false;
        }
    }

    public async Task<bool> JoinById(string lobbyId)
    {
        if (!await EnsureReadyAsync()) return false;

        try
        {
            currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

            string relayCode = currentLobby.Data["RelayCode"].Value;
            CurrentJoinCode = relayCode;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            SetRelayClientData(joinAllocation);
            SubscribeToNetworkShutdown();
            NetworkManager.Singleton.StartClient();
            OnLobbyPlayersChanged?.Invoke();

            Debug.Log($"Lobiye ID ile katılındı: {currentLobby.Name}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[MultiplayerManager] Lobiye katılamadı: " + e.Message);
            return false;
        }
    }

    public async Task<List<Lobby>> GetPublicLobbies()
    {
        if (!await EnsureReadyAsync()) return new List<Lobby>();

        var options = new QueryLobbiesOptions
        {
            Count = 20,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
            }
        };

        try
        {
            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
            return response.Results;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyService] Public lobiler getirilirken hata: {e.Message}");
            return new List<Lobby>();
        }
    }

    public async Task LeaveLobby()
    {
        _isLeavingIntentionally = true;
        heartbeatToken?.Cancel();

        if (currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    currentLobby.Id,
                    AuthenticationService.Instance.PlayerId
                );
            }
            catch { /* lobby already deleted */ }
        }

        UnsubscribeFromNetworkShutdown();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        currentLobby = null;
        CurrentJoinCode = "";
        IsGameInProgress = false;
        matchStartRequested = false;
        _isLeavingIntentionally = false;
        OnLobbyPlayersChanged?.Invoke();
    }

    public async void StartGame(string sceneName)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer ||
            IsGameInProgress || matchStartRequested)
            return;

        if (NetworkManager.Singleton.ConnectedClientsIds.Count < DemoMinimumPlayers)
        {
            Debug.LogWarning($"[MultiplayerManager] Match start rejected: at least {DemoMinimumPlayers} connected players are required.");
            return;
        }

        matchStartRequested = true;

        if (currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions { IsLocked = true });
                Debug.Log("Lobi başlatıldı, yeni oyuncu girişine kapatıldı.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Lobi kilitlenirken hata: " + e.Message);
            }
        }

        IsGameInProgress = true;

        // The demo keeps the lobby UI and the gameplay map in the same scene.
        // Loading that already-active scene does not respawn the networked scene
        // objects, so the normal GameManager.OnNetworkSpawn start hook is not
        // reached. Start the server-authoritative match explicitly instead.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.isGameStarted.Value = true;

            StartCoroutine(StartMatchInCurrentScene());
        }
        else
        {
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    private IEnumerator StartMatchInCurrentScene()
    {
        while (NetworkManager.Singleton == null ||
               !NetworkManager.Singleton.IsServer ||
               NetworkManager.Singleton.ConnectedClientsIds.Count < DemoMinimumPlayers)
        {
            yield return new WaitForSeconds(1f);
        }

        var matchFlow = MatchFlowManager.Instance ?? FindAnyObjectByType<MatchFlowManager>();
        matchFlow?.StartMatch();
    }

    private static Player CreateLocalPlayerData()
    {
        string shortId = AuthenticationService.Instance.PlayerId;
        if (shortId.Length > 4) shortId = shortId.Substring(0, 4);

        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, $"Oyuncu {shortId}") }
            }
        };
    }

    private async Task<bool> EnsureReadyAsync()
    {
        if (IsReady) return true;
        Debug.LogWarning("[MultiplayerManager] UGS henüz hazır değil, bekleniyor...");
        return await WaitUntilReadyAsync();
    }

    private static string GetPlayerDisplayName(Player player)
    {
        if (player.Data != null && player.Data.TryGetValue("PlayerName", out var data))
            return data.Value;

        return "Oyuncu " + player.Id.Substring(0, Math.Min(4, player.Id.Length));
    }

    private void SetRelayHostData(Allocation allocation)
    {
        var relayServerData = allocation.ToRelayServerData("dtls");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
    }

    private void SetRelayClientData(JoinAllocation allocation)
    {
        var relayServerData = allocation.ToRelayServerData("dtls");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
    }

    private void StartHeartbeat()
    {
        heartbeatToken?.Cancel();
        heartbeatToken = new CancellationTokenSource();
        _ = HeartbeatLoop(heartbeatToken.Token);
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerManager] Heartbeat hatası: " + e.Message);
            }

            await Task.Delay(15000, token);
        }
    }

    private void SubscribeToNetworkShutdown()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientStopped += OnClientStopped;
    }

    private void UnsubscribeFromNetworkShutdown()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
    }

    private void TryRegisterClientAdmissionCallback()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || clientAdmissionNetworkManager == networkManager)
            return;

        if (clientAdmissionNetworkManager != null)
            clientAdmissionNetworkManager.OnClientConnectedCallback -= HandleClientAdmission;

        clientAdmissionNetworkManager = networkManager;
        clientAdmissionNetworkManager.OnClientConnectedCallback += HandleClientAdmission;
    }

    private void HandleClientAdmission(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer ||
            clientId == networkManager.LocalClientId || !IsGameInProgress)
            return;

        Debug.LogWarning($"[MultiplayerManager] Client {clientId} rejected: match already started.");
        networkManager.DisconnectClient(clientId, "This demo match has already started.");
    }

    private void OnClientStopped(bool isHost)
    {
        // Only handle unexpected disconnects (host left while we were a client)
        if (_isLeavingIntentionally || _isQuitting) return;

        Debug.LogWarning("[MultiplayerManager] Host disconnected — returning to main menu.");

        UnsubscribeFromNetworkShutdown();
        heartbeatToken?.Cancel();
        currentLobby = null;
        CurrentJoinCode = "";
        IsGameInProgress = false;
        matchStartRequested = false;

        OnDisconnectedByHost?.Invoke();
    }

    private bool _isQuitting;

    void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    void OnDestroy()
    {
        heartbeatToken?.Cancel();
        if (clientAdmissionNetworkManager != null)
        {
            clientAdmissionNetworkManager.OnClientConnectedCallback -= HandleClientAdmission;
            clientAdmissionNetworkManager = null;
        }
        if (!_isQuitting)
            UnsubscribeFromNetworkShutdown();
    }
}
