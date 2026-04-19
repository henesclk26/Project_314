#define USE_STEAM // Steam'i iptal edip UGS'ye donmek icin bu satiri silin veya basina // koyun

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

#if USE_STEAM
using Steamworks.Data;
#else
using Unity.Services.Lobbies.Models;
#endif

public class LobbyUIManager : MonoBehaviour
{
    [Header("Panels / İç İçe Menüler")]
    public GameObject selectionPanel; // İlk çıkan ekran (Private/Public seçimi)
    public GameObject privateGamePanel; // Host ve Şifre girilen menü
    public GameObject publicGamePanel; // Public server tarayıcı menüsü
    public GameObject inLobbyPanel; // Lobiye girilince çıkacak panel

    [Header("Menü Navigasyon Butonları")]
    public Button navToPrivateBtn; // Private menüsünü aç
    public Button navToPublicBtn; // Public menüsünü aç
    public Button backFromPrivateBtn; // Geri dön
    public Button backFromPublicBtn; // Geri dön
    public Button exitGameBtn; // Oyundan çıkış butonu

    [Header("Host Settings")]
    public TMP_InputField lobbyNameInput;
    public Slider maxPlayersSlider;
    public TMP_Text maxPlayersText;
    public Button createPublicBtn;
    public Button createPrivateBtn;

    [Header("Join Private Lobby")]
    public TMP_InputField joinCodeInput;
    public Button joinPrivateBtn;

    [Header("Public Lobbies Browser")]
    public Transform lobbyListContainer; // Parent for lobby entry prefabs
    public GameObject lobbyEntryPrefab; // Prefab with LobbyListEntry script
    public Button refreshLobbiesBtn;

    [Header("Current Lobby Status (Optional)")]
    public TMP_Text currentLobbyInfoText;
    public Button leaveLobbyBtn;
    public TMP_Text joinCodeDisplay;
    public Button startGameBtn;

    [Header("Lobby Waiting Room Players")]
    public Transform playerListContainer;
    public GameObject playerNamePrefab;

#if USE_STEAM
    private SteamMultiplayerManager NetworkMgr => SteamMultiplayerManager.Instance;
#else
    private MultiplayerManager NetworkMgr => MultiplayerManager.Instance;
#endif

    private void Start()
    {
        // --- SÜPER UI RESET ---
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Birden fazla EventSystem varsa eskileri temizle (Tıklama sorununu çözer)
        var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
        if (eventSystems.Length > 1)
        {
            for (int i = 1; i < eventSystems.Length; i++) Destroy(eventSystems[i].gameObject);
        }
        // --- --- ---

        // Setup slider
        if (maxPlayersSlider != null)
        {
            maxPlayersSlider.minValue = 4;
            maxPlayersSlider.maxValue = 100;
            maxPlayersSlider.value = 14; // Varsayılan 14
            maxPlayersSlider.onValueChanged.AddListener(UpdateMaxPlayersText);
            UpdateMaxPlayersText(maxPlayersSlider.value);
        }

        // Navigasyon Butonları (İç İçe Menü Geçişleri)
        navToPrivateBtn?.onClick.AddListener(() => SwitchPanel(privateGamePanel));
        navToPublicBtn?.onClick.AddListener(() => {
            SwitchPanel(publicGamePanel);
            OnRefreshPublicLobbies(); // Public'e basınca listeyi otomatik yenile
        });
        backFromPrivateBtn?.onClick.AddListener(() => SwitchPanel(selectionPanel));
        backFromPublicBtn?.onClick.AddListener(() => SwitchPanel(selectionPanel));
        exitGameBtn?.onClick.AddListener(OnExitGame);

        // Network Butonları
        createPublicBtn?.onClick.AddListener(OnCreatePublic);
        createPrivateBtn?.onClick.AddListener(OnCreatePrivate);
        joinPrivateBtn?.onClick.AddListener(OnJoinPrivate);
        refreshLobbiesBtn?.onClick.AddListener(OnRefreshPublicLobbies);
        leaveLobbyBtn?.onClick.AddListener(OnLeaveLobby);
        startGameBtn?.onClick.AddListener(() => NetworkMgr.StartGame("test_map"));

        // Oyun başlarken ilk seçimi (Selection) göster:
        SwitchPanel(selectionPanel);
        inLobbyPanel?.SetActive(false);
    }

    private void SwitchPanel(GameObject targetPanel)
    {
        // Bütün panelleri kapatıp sadece istenileni açan fonksiyon
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (privateGamePanel != null) privateGamePanel.SetActive(false);
        if (publicGamePanel != null) publicGamePanel.SetActive(false);

        if (targetPanel != null) targetPanel.SetActive(true);
    }

    private void OnExitGame()
    {
        Debug.Log("Oyundan çıkılıyor...");
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    private void UpdateMaxPlayersText(float val)
    {
        if (maxPlayersText != null) maxPlayersText.text = $"Maks. Oyuncu: {Mathf.RoundToInt(val)}";
    }

    private async void OnCreatePublic()
    {
        if (NetworkMgr == null) return;

        string lName = lobbyNameInput != null && !string.IsNullOrEmpty(lobbyNameInput.text) ? lobbyNameInput.text : "Public Lobby";
        int maxP = maxPlayersSlider != null ? Mathf.RoundToInt(maxPlayersSlider.value) : 14;
        
        await NetworkMgr.CreatePublicLobby(lName, maxP);
        ShowInLobby("Public Lobi Kuruldu: " + lName);
    }

    private async void OnCreatePrivate()
    {
        if (NetworkMgr == null) return;

        string lName = lobbyNameInput != null && !string.IsNullOrEmpty(lobbyNameInput.text) ? lobbyNameInput.text : "Private Lobby";
        int maxP = maxPlayersSlider != null ? Mathf.RoundToInt(maxPlayersSlider.value) : 14;
        
        await NetworkMgr.CreatePrivateLobby(lName, maxP);
        ShowInLobby("Private Lobi Kuruldu: " + lName);
    }

    private async void OnJoinPrivate()
    {
        if (joinCodeInput == null || NetworkMgr == null) return;

        string code = joinCodeInput.text;
        if (string.IsNullOrEmpty(code)) return;

        await NetworkMgr.JoinByCode(code);
        ShowInLobby("Lobiye Katılındı.");
    }

    public async void OnRefreshPublicLobbies()
    {
        if (lobbyListContainer == null || lobbyEntryPrefab == null || NetworkMgr == null) 
        {
            Debug.LogWarning("lobbyListContainer veya lobbyEntryPrefab Inspector'da tanımlanmamış!");
            return;
        }

        // Clear previous list
        foreach (Transform child in lobbyListContainer)
        {
            Destroy(child.gameObject);
        }

        var lobbies = await NetworkMgr.GetPublicLobbies();
        if (lobbies == null) 
        {
            Debug.LogWarning("Hiç public lobi bulunamadı veya sunucu yanıt vermedi.");
            return;
        }
        
        Debug.Log($"[LobbyUIManager] {lobbies.Count} adet public lobi bulundu ve listeleniyor.");

        foreach (var l in lobbies)
        {
            GameObject entry = Instantiate(lobbyEntryPrefab, lobbyListContainer);
            entry.SetActive(true); // GİZLİ PREFABIN KLONLARI KENDİLİĞİNDEN KAPANMASIN DİYE
            entry.transform.localScale = Vector3.one; // CANVAS SCALE BUG'INI ÖNLE
            entry.transform.localPosition = Vector3.zero; // VLG'İN İLK KAREDE YAMULMASINI ÖNLE
            
            LobbyListEntry entryScript = entry.GetComponent<LobbyListEntry>();
            if (entryScript != null)
            {
                entryScript.Setup(l, this);
            }
        }
    }

    public async void JoinLobbyById(string lobbyId)
    {
        await NetworkMgr.JoinById(lobbyId);
        ShowInLobby("Public Lobiye Katılındı.");
    }

    private async void OnLeaveLobby()
    {
        await NetworkMgr.LeaveLobby();
        
        inLobbyPanel?.SetActive(false);
        SwitchPanel(selectionPanel);
    }

    private void ShowInLobby(string statusText)
    {
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (privateGamePanel != null) privateGamePanel.SetActive(false);
        if (publicGamePanel != null) publicGamePanel.SetActive(false);

        if (inLobbyPanel != null) inLobbyPanel.SetActive(true);
        if (currentLobbyInfoText != null) currentLobbyInfoText.text = statusText;

        if (joinCodeDisplay != null) 
        {
            if (NetworkMgr.CurrentLobbyIsPrivate)
            {
                joinCodeDisplay.text = $"Oda Şifresi: {NetworkMgr.CurrentLobbyCode} (Kapasite: {NetworkMgr.CurrentLobbyMaxPlayers})";
            }
            else
            {
                joinCodeDisplay.text = $"Herkese Açık (Public) Lobi - (Kapasite: {NetworkMgr.CurrentLobbyMaxPlayers})";
            }
        }

        if (startGameBtn != null)
        {
            // Sadece Host (Sunucu) oyunu başlatabilir
            startGameBtn.gameObject.SetActive(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
        }

        EnsureNetworkCallbacks();
        RefreshPlayerList();
    }

    private void EnsureNetworkCallbacks()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
            
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientChanged;
        }
    }

    private void OnClientChanged(ulong clientId)
    {
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (playerListContainer == null || playerNamePrefab == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
        
        foreach (Transform child in playerListContainer) Destroy(child.gameObject);
        
        int index = 1;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsIds)
        {
            var go = Instantiate(playerNamePrefab, playerListContainer);
            var txt = go.GetComponent<TMP_Text>();
            if (txt != null) {
                txt.text = $"Oyuncu {index}";
            }
            index++;
        }
    }
}
