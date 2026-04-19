using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR || UNITY_STANDALONE // Steam works usually on PC platforms
using Steamworks;
using Steamworks.Data;
using Netcode.Transports.Facepunch;
#endif

public class SteamMultiplayerManager : MonoBehaviour
{
    public static SteamMultiplayerManager Instance { get; private set; }
    
    // Steam Lobbies use Lobby ID (ulong) instead of short strings, but we can treat it as a string.
    public string CurrentJoinCode => currentLobby?.Id.ToString() ?? ""; 
    public string CurrentLobbyCode => currentLobby?.Id.ToString() ?? "";
    public bool CurrentLobbyIsPrivate => currentLobby != null && currentLobby?.GetData("IsPrivate") == "true";
    public int CurrentLobbyMaxPlayers => currentLobby?.MaxMembers ?? 14;

#if UNITY_EDITOR || UNITY_STANDALONE
    private Lobby? currentLobby;
#endif
    
    // Steamworks requires an App ID. 480 is the default Spacewar template for testing.
    private const uint SteamAppId = 480;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        try
        {
            SteamClient.Init(SteamAppId, true);
            Debug.Log("Steam bağlantısı başarılı. Oyuncu: " + SteamClient.Name);
            
            // Register callbacks
            SteamMatchmaking.OnLobbyCreated += OnLobbyCreatedHook;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEnteredHook;
            SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoinedHook;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeaveHook;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }
        catch (Exception e)
        {
            Debug.LogError("Steam başlatılamadı. Steam açık mı? Hata: " + e.Message);
        }
#endif
    }

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        SteamClient.RunCallbacks();
#endif
    }

    void OnDestroy()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        SteamMatchmaking.OnLobbyCreated -= OnLobbyCreatedHook;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEnteredHook;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoinedHook;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeaveHook;
        SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        SteamClient.Shutdown();
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    // ---------- CALLBACKS ----------
    private void OnLobbyCreatedHook(Result result, Lobby lobby)
    {
        if (result == Result.OK)
        {
            Debug.Log("Steam Lobi oluşturuldu! ID: " + lobby.Id);
            currentLobby = lobby;
        }
        else
        {
            Debug.LogError("Steam Lobi oluşturma hatası: " + result);
        }
    }

    private void OnLobbyEnteredHook(Lobby lobby)
    {
        Debug.Log("Lobiye girildi: " + lobby.Id);
        currentLobby = lobby;
        
        // Host değilseniz client olarak bağlan
        if (NetworkManager.Singleton.IsHost) return;

        var transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
        if (transport != null)
        {
            transport.targetSteamId = lobby.Owner.Id;
            NetworkManager.Singleton.StartClient();
        }
    }

    private void OnLobbyMemberJoinedHook(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} lobiye katıldı.");
    }

    private void OnLobbyMemberLeaveHook(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} lobiden ayrıldı.");
    }

    private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId id)
    {
        Debug.Log("Steam daveti kabul edildi, oyuna katılınıyor...");
        await JoinById(lobby.Id.ToString());
    }
#endif

    // ─── PUBLIC LOBİ OLUŞTUR ───────────────────────────
    public async Task CreatePublicLobby(string lobbyName, int maxPlayers)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        var transport = SetupTransport();
        
        var lobbyObj = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
        if (!lobbyObj.HasValue)
        {
            Debug.LogError("Lobi kurulamadı.");
            return;
        }

        currentLobby = lobbyObj.Value;
        currentLobby?.SetPublic();
        currentLobby?.SetJoinable(true);
        currentLobby?.SetData("Name", lobbyName);
        currentLobby?.SetData("IsPrivate", "false");
        currentLobby?.SetData("GameId", "B_Project314"); // Bize ait oyunu belirlemek için eşsiz ID

        NetworkManager.Singleton.StartHost();
        Debug.Log($"Public lobi oluşturuldu (Steam) | Lobi Adı: {lobbyName}");
#else
        await Task.CompletedTask;
#endif
    }

    // ─── PRIVATE LOBİ OLUŞTUR ──────────────────────────
    public async Task CreatePrivateLobby(string lobbyName, int maxPlayers)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        var transport = SetupTransport();

        var lobbyObj = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
        if (!lobbyObj.HasValue)
        {
            Debug.LogError("Lobi kurulamadı.");
            return;
        }

        currentLobby = lobbyObj.Value;
        currentLobby?.SetFriendsOnly();
        currentLobby?.SetJoinable(true);
        currentLobby?.SetData("Name", lobbyName);
        currentLobby?.SetData("IsPrivate", "true");
        currentLobby?.SetData("GameId", "B_Project314"); // Bize ait oyunu belirlemek için eşsiz ID

        NetworkManager.Singleton.StartHost();
        Debug.Log($"Private lobi oluşturuldu (Steam) | Lobi Adı: {lobbyName}");
#else
        await Task.CompletedTask;
#endif
    }

    // ─── LOBİYE KOD (ID) İLE KATIL ──────────────────────────
    public async Task JoinByCode(string lobbyCode)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        // Steam'de Join Code, lobi ID'sine tekabül edebilir.
        await JoinById(lobbyCode);
#else
        await Task.CompletedTask;
#endif
    }

    // ─── LOBİYE ID İLE KATIL ───────────────────────────
    public async Task JoinById(string lobbyId)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (ulong.TryParse(lobbyId, out ulong id))
        {
            SetupTransport();
            await SteamMatchmaking.JoinLobbyAsync(id);
            // Bağlantı kısmı OnLobbyEnteredHook içerisinde tetiklenecek.
        }
#else
        await Task.CompletedTask;
#endif
    }

    // ─── PUBLIC LOBİLERİ LİSTELE ───────────────────────
    public async Task<List<Lobby>> GetPublicLobbies() // Note: Modifies return type from Unity Service Lobby to Steamworks Lobby
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        try
        {
            var lobbies = await SteamMatchmaking.LobbyList
                .WithMaxResults(20)
                .WithKeyValue("IsPrivate", "false")
                .WithKeyValue("GameId", "B_Project314") // Sadece bizim ID ile kurulan lobileri bul
                .RequestAsync();

            return new List<Lobby>(lobbies ?? Array.Empty<Lobby>());
        }
        catch (Exception e)
        {
            Debug.LogError("Lobi listesi alınamadı: " + e.Message);
            return null;
        }
#else
        return null;
#endif
    }

    // ─── LOBİDEN AYRIL ─────────────────────────────────
    public async Task LeaveLobby()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        currentLobby?.Leave();
        currentLobby = null;

        NetworkManager.Singleton.Shutdown();
        Debug.Log("Lobiden ayrılındı.");
#endif
        await Task.CompletedTask;
    }

    // ─── OYUNU BAŞLAT ──────────────────────────────────
    public void StartGame(string sceneName)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (NetworkManager.Singleton.IsServer)
        {
            // İsteğe bağlı olarak lobiyi kitleyebiliriz
            currentLobby?.SetJoinable(false);
            Debug.Log("Steam lobisi kilitlendi, oyun başlatılıyor.");
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
#endif
    }

    // ─── TRANSPORT KESTIRMESI ──────────────────────────
    private Component SetupTransport()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        var transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
        if (transport == null)
        {
            // Eski UnityTransport varsa onu kapat/deaktif et
            var unityTransport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (unityTransport != null) unityTransport.enabled = false;

            transport = NetworkManager.Singleton.gameObject.AddComponent<FacepunchTransport>();
        }
        
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        return transport;
#else
        return null;
#endif
    }
}
