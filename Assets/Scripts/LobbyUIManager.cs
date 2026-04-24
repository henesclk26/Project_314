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
    public GameObject lobbyNamePanel; // Public lobi adı giriş paneli

    [Header("Menü Navigasyon Butonları")]
    public Button navToPrivateBtn; // Private menüsünü aç
    public Button navToPublicBtn; // Public menüsünü aç
    public Button backFromPrivateBtn; // Geri dön
    public Button backFromPublicBtn; // Geri dön
    public Button exitGameBtn; // Oyundan çıkış butonu

    [Header("Host Settings - Public")]
    public TMP_InputField lobbyNameInput; // Lobi adı giriş alanı (lobbyNamePanel içinde)
    public TMP_Text lobbyNameErrorText; // Hata mesajı (örn: "Maks 16 karakter!")
    public Button confirmPublicBtn; // Lobi adını onayladıktan sonra lobi kur
    public Button backFromNamePanelBtn; // Lobi adı panelinden geri dön
    public Slider maxPlayersSlider; // Sadece public lobi için
    public TMP_Text maxPlayersText; // Sadece public lobi için
    public Button createPublicBtn; // Public Game ekranındaki Host butonu

    [Header("Host Settings - Private")]
    public Button createPrivateBtn;
    public TMP_Text privatePlayerCountText; // İnLobbyPanel'de "X/14 oyuncu" gösterir

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

        // Slider kurulumu (LobbyNamePanel'deki public lobi slider'ı)
        if (maxPlayersSlider != null)
        {
            maxPlayersSlider.minValue = 4;
            maxPlayersSlider.maxValue = 14;
            maxPlayersSlider.value = 14; // Varsayılan 14
            maxPlayersSlider.wholeNumbers = true; // Tam sayı olsun
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
        backFromNamePanelBtn?.onClick.AddListener(() => SwitchPanel(publicGamePanel));
        exitGameBtn?.onClick.AddListener(OnExitGame);

        // Lobi adı InputField karakter limiti
        if (lobbyNameInput != null)
        {
            lobbyNameInput.characterLimit = 16;
            lobbyNameInput.onValueChanged.AddListener(OnLobbyNameChanged);
        }

        // Network Butonları
        createPublicBtn?.onClick.AddListener(OnShowLobbyNamePanel); // Host → adı giriş paneline git
        confirmPublicBtn?.onClick.AddListener(OnCreatePublic);      // Onayla → lobi kur
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
        if (lobbyNamePanel != null) lobbyNamePanel.SetActive(false);

        if (targetPanel != null) targetPanel.SetActive(true);
    }

    private void OnShowLobbyNamePanel()
    {
        // Lobi adı giriş panelini aç, alanı temizle
        if (lobbyNameInput != null) lobbyNameInput.text = "";
        if (lobbyNameErrorText != null) lobbyNameErrorText.text = "";
        SwitchPanel(lobbyNamePanel);
    }

    private void OnLobbyNameChanged(string value)
    {
        // Karakter sayısını hata metninde göster
        if (lobbyNameErrorText != null)
        {
            if (value.Length >= 16)
                lobbyNameErrorText.text = "Maksimum 16 karakter!";
            else
                lobbyNameErrorText.text = $"{value.Length}/16";
        }
    }

    public void OnExitGame()
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
        if (maxPlayersText != null) maxPlayersText.text = $"Maksimum: {Mathf.RoundToInt(val)} oyuncu";
    }

    private async void OnCreatePublic()
    {
        if (NetworkMgr == null) return;

        // Boş bırakılmış mı kontrol et
        string lName = lobbyNameInput != null ? lobbyNameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(lName))
        {
            if (lobbyNameErrorText != null) lobbyNameErrorText.text = "Lütfen bir lobi adı girin!";
            return;
        }

        int maxP = maxPlayersSlider != null ? Mathf.RoundToInt(maxPlayersSlider.value) : 14;
        
        await NetworkMgr.CreatePublicLobby(lName, maxP);
        ShowInLobby(lName);
    }

    private async void OnCreatePrivate()
    {
        if (NetworkMgr == null) return;

        // Lobi adı: "[SteamAdı]'s Lobby"
        string steamName = "Player";
#if USE_STEAM
        steamName = Steamworks.SteamClient.Name;
#endif
        string lName = $"{steamName}'s Lobby";
        int maxP = 14; // Sabit maksimum
        
        await NetworkMgr.CreatePrivateLobby(lName, maxP);
        ShowInLobby(lName);
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
#if USE_STEAM
        // Kapasite kontrolü: Lobi doluysa girişi engelle
        var steamMgr = SteamMultiplayerManager.Instance;
        if (steamMgr != null && steamMgr.HasActiveLobby)
        {
            int memberCount = steamMgr.GetLobbyMembers().Count;
            int maxP = NetworkMgr.CurrentLobbyMaxPlayers;
            if (memberCount >= maxP)
            {
                Debug.LogWarning("Lobi dolu, katılınamadı.");
                return;
            }
        }
#endif
        await NetworkMgr.JoinById(lobbyId);
        string lobbName = "Lobiye Katılındı";
#if USE_STEAM
        if (SteamMultiplayerManager.Instance != null && SteamMultiplayerManager.Instance.HasActiveLobby)
            lobbName = SteamMultiplayerManager.Instance.GetLobbyName();
#endif
        ShowInLobby(lobbName);
    }

    private async void OnLeaveLobby()
    {
        await NetworkMgr.LeaveLobby();
        
        inLobbyPanel?.SetActive(false);
        SwitchPanel(selectionPanel);
    }

    private void ShowInLobby(string lobbyName)
    {
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (privateGamePanel != null) privateGamePanel.SetActive(false);
        if (publicGamePanel != null) publicGamePanel.SetActive(false);
        if (lobbyNamePanel != null) lobbyNamePanel.SetActive(false);

        if (inLobbyPanel != null) inLobbyPanel.SetActive(true);
        if (currentLobbyInfoText != null) currentLobbyInfoText.text = lobbyName;

        // Private lobide şifreyi gizle, sadece isim göster
        if (joinCodeDisplay != null)
        {
            if (NetworkMgr.CurrentLobbyIsPrivate)
                joinCodeDisplay.gameObject.SetActive(false); // Private'ta şifre yok, gizle
            else
                joinCodeDisplay.text = $"Herkese Açık (Public) Lobi - (Kapasite: {NetworkMgr.CurrentLobbyMaxPlayers})";
        }

        if (startGameBtn != null)
        {
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

#if USE_STEAM
        // Steam üye değişikliklerini de dinle
        if (SteamMultiplayerManager.Instance != null)
        {
            SteamMultiplayerManager.Instance.OnLobbyMembersChanged -= RefreshPlayerList;
            SteamMultiplayerManager.Instance.OnLobbyMembersChanged += RefreshPlayerList;
        }
#endif
    }

    private void OnClientChanged(ulong clientId)
    {
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (playerListContainer == null || playerNamePrefab == null) return;

        foreach (Transform child in playerListContainer) Destroy(child.gameObject);

#if USE_STEAM
        // Steam: Lobideki üyelerin isimlerini doğrudan Steam'den çek
        var steamMgr = SteamMultiplayerManager.Instance;
        if (steamMgr == null || !steamMgr.HasActiveLobby)
        {
            ShowNetcodePlayerList();
            return;
        }

        var members = steamMgr.GetLobbyMembers();

        foreach (var member in members)
        {
            var go = Instantiate(playerNamePrefab, playerListContainer);
            // Root'ta TMP_Text yoksa child'larda ara (PlayerEntry prefabı için)
            var txt = go.GetComponent<TMP_Text>() ?? go.GetComponentInChildren<TMP_Text>();
            if (txt != null)
                txt.text = member;
        }

        // Oyuncu sayısı text'lerini güncelle
        if (privatePlayerCountText != null)
        {
            if (NetworkMgr.CurrentLobbyIsPrivate)
                // Private: sadece kaç kişi var (örn: "3")
                privatePlayerCountText.text = $"{members.Count}";
            else
                // Public: kaç/maksimum (örn: "1/9")
                privatePlayerCountText.text = $"{members.Count}/{NetworkMgr.CurrentLobbyMaxPlayers}";
        }
#else
        ShowNetcodePlayerList();
#endif
    }


    private void ShowNetcodePlayerList()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        int index = 1;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsIds)
        {
            var go = Instantiate(playerNamePrefab, playerListContainer);
            var txt = go.GetComponent<TMP_Text>() ?? go.GetComponentInChildren<TMP_Text>();
            if (txt != null)
                txt.text = $"Oyuncu {index}";
            index++;
        }
    }
}

