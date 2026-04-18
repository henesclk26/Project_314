using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class MultiplayerManager : MonoBehaviour
{
    public static MultiplayerManager Instance { get; private set; }
    public string CurrentJoinCode { get; private set; } // Relay Katılım Şifresi
    public string CurrentLobbyCode => currentLobby?.LobbyCode; // Gerçek Lobby (Oda) Şifresi
    public bool CurrentLobbyIsPrivate => currentLobby != null && currentLobby.IsPrivate;
    public int CurrentLobbyMaxPlayers => currentLobby != null ? currentLobby.MaxPlayers : 14;

    private Lobby currentLobby;
    private CancellationTokenSource heartbeatToken;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        var options = new InitializationOptions();
        // Aynı bilgisayarda iki oyun açıldığında (Editor ve Build) kimliklerin karışmaması için rastgele profil oluşturuyoruz
        options.SetProfile("Player_" + UnityEngine.Random.Range(0, 100000).ToString());

        await UnityServices.InitializeAsync(options);
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        Debug.Log("UGS bağlantısı başarılı. Player ID: " + AuthenticationService.Instance.PlayerId);
    }

    // ─── PUBLIC LOBİ OLUŞTUR ───────────────────────────
    public async Task CreatePublicLobby(string lobbyName, int maxPlayers)
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        CurrentJoinCode = relayJoinCode;

        var options = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                { "RelayCode", new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) }
            }
        };

        currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

        SetRelayHostData(allocation);
        NetworkManager.Singleton.StartHost();
        StartHeartbeat();

        Debug.Log($"Public lobi oluşturuldu | Kod: {currentLobby.LobbyCode}");
    }

    // ─── PRIVATE LOBİ OLUŞTUR ──────────────────────────
    public async Task CreatePrivateLobby(string lobbyName, int maxPlayers)
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        CurrentJoinCode = relayJoinCode;

        var options = new CreateLobbyOptions
        {
            IsPrivate = true,
            Data = new Dictionary<string, DataObject>
            {
                { "RelayCode", new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
            }
        };

        currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);

        SetRelayHostData(allocation);
        NetworkManager.Singleton.StartHost();
        StartHeartbeat();

        Debug.Log($"Private lobi oluşturuldu | Lobi Kodu: {currentLobby.LobbyCode}");
    }

    // ─── LOBİYE KOD İLE KATIL ──────────────────────────
    public async Task JoinByCode(string lobbyCode)
    {
        try
        {
            currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            string relayCode = currentLobby.Data["RelayCode"].Value;
            CurrentJoinCode = relayCode;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

            SetRelayClientData(joinAllocation);
            NetworkManager.Singleton.StartClient();

            Debug.Log($"Lobiye katılındı: {currentLobby.Name}");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Lobiye katılamadı. Kod yanlış olabilir: " + e.Message);
        }
    }

    // ─── LOBİYE ID İLE KATIL ───────────────────────────
    public async Task JoinById(string lobbyId)
    {
        currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

        string relayCode = currentLobby.Data["RelayCode"].Value;
        CurrentJoinCode = relayCode;
        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);

        SetRelayClientData(joinAllocation);
        NetworkManager.Singleton.StartClient();

        Debug.Log($"Lobiye ID ile katılındı: {currentLobby.Name}");
    }

    // ─── PUBLIC LOBİLERİ LİSTELE ───────────────────────
    public async Task<List<Lobby>> GetPublicLobbies()
    {
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
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyService] Public lobiler getirilirken geçici hata: {e.Message}");
            return null;
        }
    }

    // ─── LOBİDEN AYRIL ─────────────────────────────────
    public async Task LeaveLobby()
    {
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
            catch { /* Ignored if lobby deleted */ }
        }

        NetworkManager.Singleton.Shutdown();
        currentLobby = null;
        CurrentJoinCode = "";
    }

    // ─── OYUNU BAŞLAT ──────────────────────────────────
    public async void StartGame(string sceneName)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            if (currentLobby != null)
            {
                try
                {
                    await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions { IsLocked = true });
                    Debug.Log("Lobi başlatıldı, yeni oyuncu girişine kapatıldı.");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Lobi kilitlenirken hata: " + e.Message);
                }
            }
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    // ─── YARDIMCI FONKSİYONLAR ─────────────────────────
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
        heartbeatToken = new CancellationTokenSource();
        _ = HeartbeatLoop(heartbeatToken.Token);
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            await Task.Delay(15000, token);
        }
    }

    void OnDestroy()
    {
        heartbeatToken?.Cancel();
    }
}