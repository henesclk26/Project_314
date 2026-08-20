using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Sci-fi-map sahnesinde menü → oyun geçişini yönetir.
/// </summary>
public class SciFiMenuController : MonoBehaviour
{
    [Header("Inspector'dan Ata")]
    public GameObject menuObject;
    public GameObject privateLobbyObject;
    public GameObject publicLobbyObject;
    public FirstPersonController fpc;

    [Header("Main Menu Themes")]
    public VisualTreeAsset classicMainMenuAsset;
    public VisualTreeAsset alternateMainMenuAsset;

    private UIToolkitLobbyBridge _lobbyBridge;
    private bool _showingAlternateMainMenu;
    private Button _mainSettingsButton;
    private Button _mainSettingsBackButton;
    private Button _mainSettingsSaveButton;
    private VisualElement _mainSettingsPanel;
    private VisualElement _mainSettingsRoot;

    private void Awake()
    {
        _lobbyBridge = GetComponent<UIToolkitLobbyBridge>();
        if (_lobbyBridge == null)
            _lobbyBridge = gameObject.AddComponent<UIToolkitLobbyBridge>();

        _lobbyBridge.OnGameStarting += OnNetworkGameStarting;
    }

    private void OnDestroy()
    {
        if (_lobbyBridge != null)
            _lobbyBridge.OnGameStarting -= OnNetworkGameStarting;

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnDisconnectedByHost -= OnHostDisconnected;
    }

    private void OnHostDisconnected()
    {
        // Host left — use ShowMainMenu to fully reset to main menu state
        Debug.Log("[SciFiMenuController] Host disconnected, resetting to main menu.");
        GameplayInteractionGate.SetQuickTestMode(false);
        ShowMainMenu();
    }

    private void Start()
    {
        DisableMenuFpc();

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        // Subscribe to host disconnect so the menu resets if it fires on this scene
        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnDisconnectedByHost += OnHostDisconnected;

        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);

        if (IsInActiveNetworkSession())
        {
            if (GameManager.Instance != null && GameManager.Instance.isGameStarted.Value)
            {
                EnterGameplayMode(hideMenusOnly: true);
                return;
            }
            else if (MultiplayerManager.Instance != null && MultiplayerManager.Instance.HasActiveLobby)
            {
                if (MultiplayerManager.Instance.CurrentLobbyIsPrivate)
                {
                    OpenActivePrivateLobby();
                }
                else
                {
                    OpenActivePublicLobby();
                }
                return;
            }
        }

        if (menuObject != null)
        {
            menuObject.SetActive(true);
            SetMainMenuVisualTree(showAlternate: false);
        }
    }

    private void OpenActivePrivateLobby()
    {
        DisableMenuFpc();

        if (menuObject != null) menuObject.SetActive(false);
        if (privateLobbyObject == null) return;

        privateLobbyObject.SetActive(true);
        var root = privateLobbyObject.GetComponent<UIDocument>().rootVisualElement;
        _lobbyBridge.BindPrivateLobby(root, OnLeaveLobby);
        _lobbyBridge.OpenPrivateLobbyDirectly();
    }

    private void OpenActivePublicLobby()
    {
        DisableMenuFpc();

        if (menuObject != null) menuObject.SetActive(false);
        if (publicLobbyObject == null) return;

        publicLobbyObject.SetActive(true);
        var root = publicLobbyObject.GetComponent<UIDocument>().rootVisualElement;
        _lobbyBridge.BindPublicLobby(root, OnLeavePublicLobby);
        _lobbyBridge.OpenPublicLobbyDirectly();
    }

    private bool _gameplayModeEntered = false;
    // Match-end RPCs can arrive before the isGameStarted NetworkVariable has
    // replicated to a client. Keep that client in the lobby until a new match
    // explicitly enters gameplay instead of allowing Update() to re-enter the
    // map during that one-frame transition window.
    private bool _returningToLobbyAfterMatch;
    public bool IsGameplayModeActive => _gameplayModeEntered;

    private void Update()
    {
        GameplayInteractionGate.ProcessQuickTestInput();

        // The result RPC and the isGameStarted NetworkVariable may arrive in
        // different frames. Keep the lobby guard until this client has
        // actually observed the authoritative lobby state, then allow the
        // next match to enter gameplay normally.
        if (_returningToLobbyAfterMatch &&
            (GameManager.Instance == null || !GameManager.Instance.isGameStarted.Value))
        {
            _returningToLobbyAfterMatch = false;
        }

        if (!_returningToLobbyAfterMatch &&
            !_gameplayModeEntered &&
            IsInActiveNetworkSession() &&
            GameManager.Instance != null &&
            GameManager.Instance.isGameStarted.Value)
        {
            _gameplayModeEntered = true;
            EnterGameplayMode(hideMenusOnly: false);
            return;
        }

        if (menuObject != null && menuObject.activeInHierarchy && Input.GetKeyDown(KeyCode.F1))
            SetMainMenuVisualTree(!_showingAlternateMainMenu);
    }

    private void SetMainMenuVisualTree(bool showAlternate)
    {
        var uiDoc = menuObject != null ? menuObject.GetComponent<UIDocument>() : null;
        if (uiDoc == null)
        {
            Debug.LogError("[SciFiMenuController] menuObject'te UIDocument bulunamadı!");
            return;
        }

        var targetTree = showAlternate ? alternateMainMenuAsset : classicMainMenuAsset;
        if (targetTree == null)
        {
            Debug.LogError($"[SciFiMenuController] {(showAlternate ? "Alternate" : "Classic")} main menu UXML atanmamış!");
            return;
        }

        if (uiDoc.visualTreeAsset != targetTree)
            uiDoc.visualTreeAsset = targetTree;

        _showingAlternateMainMenu = showAlternate;
        SetupMainMenuButtons();
    }

private void SetupMainMenuButtons()
    {
        var uiDoc = menuObject != null ? menuObject.GetComponent<UIDocument>() : null;
        if (uiDoc == null)
        {
            Debug.LogError("[SciFiMenuController] menuObject'te UIDocument bulunamadı!");
            return;
        }

        var root = uiDoc.rootVisualElement;
        BindMenuButton(root.Q<Button>("btn-private-game"), OnPrivateGameButtonClicked);
        BindMenuButton(root.Q<Button>("btn-public-game"), OnPublicGameButtonClicked);
        BindMenuButton(root.Q<Button>("btn-quit-game"), OnQuitGameButtonClicked);
        BindQuickTestButton(root.Q<Button>("btn-quick-test"));

        if (_mainSettingsButton != null)
            _mainSettingsButton.clicked -= OpenMainSettings;
        if (_mainSettingsBackButton != null)
            _mainSettingsBackButton.clicked -= CloseMainSettings;
        if (_mainSettingsSaveButton != null)
            _mainSettingsSaveButton.clicked -= SaveMainSettings;

        _mainSettingsButton = root.Q<Button>("btn-settings");
        _mainSettingsBackButton = root.Q<Button>("settings-back");
        _mainSettingsSaveButton = root.Q<Button>("settings-save");
        _mainSettingsPanel = root.Q<VisualElement>("settings-panel");
        _mainSettingsRoot = root;

        if (_mainSettingsButton != null)
            _mainSettingsButton.clicked += OpenMainSettings;
        if (_mainSettingsBackButton != null)
            _mainSettingsBackButton.clicked += CloseMainSettings;
        if (_mainSettingsSaveButton != null)
            _mainSettingsSaveButton.clicked += SaveMainSettings;

        GameSettingsUI.ConfigureControls(root);
        CloseMainSettings();
    }

    private void OpenMainSettings()
    {
        GameSettingsUI.BeginEdit(_mainSettingsRoot);
        if (_mainSettingsPanel != null)
            _mainSettingsPanel.style.display = DisplayStyle.Flex;
    }

    private void CloseMainSettings()
    {
        GameSettingsUI.Cancel(_mainSettingsRoot);
        if (_mainSettingsPanel != null)
            _mainSettingsPanel.style.display = DisplayStyle.None;
    }

    private void SaveMainSettings()
    {
        GameSettingsUI.Save(_mainSettingsRoot);
    }

private void BindQuickTestButton(Button button)
    {
        if (button == null) return;

        // Button.clicked is the direct UI Toolkit action and remains reliable for runtime menus.
        // when the visual tree is swapped at runtime (F1 alternate menu,
        // returning from a lobby, or a domain reload in the Editor).
        button.clicked -= OnQuickTestClicked;
        button.clicked += OnQuickTestClicked;
    }

    private static void BindMenuButton(Button button, EventCallback<ClickEvent> callback)
    {
        if (button == null) return;

        button.UnregisterCallback(callback);
        button.RegisterCallback(callback);
    }

    private void OnPrivateGameButtonClicked(ClickEvent _) => OnPrivateGameClicked();
    private void OnPublicGameButtonClicked(ClickEvent _) => OnPublicGameClicked();
    private void OnQuitGameButtonClicked(ClickEvent _) => OnQuitGame();
    private void OnQuickTestButtonClicked(ClickEvent _) => OnQuickTestClicked();

    private void OnPrivateGameClicked()
    {
        GameplayInteractionGate.SetQuickTestMode(false);
        DisableMenuFpc();

        if (menuObject != null) menuObject.SetActive(false);
        if (privateLobbyObject == null) return;

        privateLobbyObject.SetActive(true);
        var root = privateLobbyObject.GetComponent<UIDocument>().rootVisualElement;
        _lobbyBridge.BindPrivateLobby(root, OnLeaveLobby);
        UIToolkitLobbyBridge.SwitchPrivatePanel(root, "panel-selection");
    }

    private void OnPublicGameClicked()
    {
        GameplayInteractionGate.SetQuickTestMode(false);
        DisableMenuFpc();

        if (menuObject != null) menuObject.SetActive(false);
        if (publicLobbyObject == null) return;

        publicLobbyObject.SetActive(true);
        var root = publicLobbyObject.GetComponent<UIDocument>().rootVisualElement;
        _lobbyBridge.BindPublicLobby(root, OnLeavePublicLobby);
        UIToolkitLobbyBridge.SwitchPublicPanel(root, "panel-selection");
    }

    private async void OnLeaveLobby()
    {
        if (privateLobbyObject != null)
        {
            var root = privateLobbyObject.GetComponent<UIDocument>()?.rootVisualElement;
            if (root != null)
                await _lobbyBridge.OnLeavePrivateLobbyForMenuAsync();
        }

        _lobbyBridge.Unbind();
        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        ReturnToMainMenuUi();
    }

    private async void OnLeavePublicLobby()
    {
        if (publicLobbyObject != null)
        {
            var root = publicLobbyObject.GetComponent<UIDocument>()?.rootVisualElement;
            if (root != null)
                await _lobbyBridge.OnLeavePublicLobbyForMenuAsync();
        }

        _lobbyBridge.Unbind();
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);
        ReturnToMainMenuUi();
    }

    private void ReturnToMainMenuUi()
    {
        DisableMenuFpc();
        if (menuObject != null) menuObject.SetActive(true);
        SetupMainMenuButtons();
    }

    private void OnNetworkGameStarting()
    {
        GameplayInteractionGate.SetQuickTestMode(false);
        EnterGameplayMode(hideMenusOnly: false);
    }

    private void EnterGameplayMode(bool hideMenusOnly)
    {
        _returningToLobbyAfterMatch = false;
        _gameplayModeEntered = true;
        HideMenuFpcObject();

        if (menuObject != null) menuObject.SetActive(false);
        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);

        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;

        StartCoroutine(RequestSpawnDistributionAfterFrame());
    }

    private IEnumerator RequestSpawnDistributionAfterFrame()
    {
        yield return null;

        var spawnCoordinator = FindFirstObjectByType<PlayerSpawnCoordinator>();
        if (spawnCoordinator != null)
            spawnCoordinator.RequestDistribution();
    }

    private void DisableMenuFpc()
    {
        if (fpc == null) return;

        if (!fpc.gameObject.activeSelf)
            fpc.gameObject.SetActive(true);

        fpc.enabled = false;
        fpc.playerCanMove = false;
        fpc.cameraCanMove = false;

        HideFpcReticle();

        if (fpc.playerCamera != null)
        {
            fpc.playerCamera.gameObject.SetActive(true);
            var listener = fpc.playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;
        }
    }

    private void HideFpcReticle()
    {
        if (fpc == null) return;

        var reticleTransform = fpc.transform.Find("Reticle");
        if (reticleTransform != null)
        {
            reticleTransform.gameObject.SetActive(false);
            return;
        }

        var reticleImage = fpc.GetComponentInChildren<UnityEngine.UI.Image>(true);
        if (reticleImage != null)
            reticleImage.gameObject.SetActive(false);
    }

    private void HideMenuFpcObject()
    {
        if (fpc != null)
            fpc.gameObject.SetActive(false);
    }

    public void ShowLobbyAfterMatch()
    {
        GameplayInteractionGate.SetQuickTestMode(false);
        _returningToLobbyAfterMatch = true;
        _gameplayModeEntered = false;

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.IsGameInProgress = false;

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        DisableMenuFpc();

        if (MultiplayerManager.Instance != null && MultiplayerManager.Instance.HasActiveLobby)
        {
            if (MultiplayerManager.Instance.CurrentLobbyIsPrivate)
                OpenActivePrivateLobby();
            else
                OpenActivePublicLobby();
            return;
        }

        // Quick Test has no Unity Lobby object; keep its safe fallback in the
        // main menu while online matches return to their active lobby panel.
        ReturnToMainMenuUi();
    }

    private static bool IsInActiveNetworkSession()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void OnQuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Quick Test: NetworkManager'ı Host olarak başlatır ve doğrudan oyuna girer.
    /// Multiplayer bağlantısına gerek kalmadan tek başına test yapabilmek için.
    /// </summary>
    private void OnQuickTestClicked()
    {
        GameplayInteractionGate.SetQuickTestMode(true);

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[QuickTest] NetworkManager bulunamadı!");
            return;
        }

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.IsGameInProgress = true;
        }

        // A previous public/private lobby may have left Relay data on the
        // shared UnityTransport. Quick Test is a local host flow, so restore
        // the loopback transport before starting it. Port 0 lets the OS pick
        // a free UDP port, avoiding stale sockets after a failed Play Mode
        // shutdown or repeated Quick Test runs in the same Editor process.
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("127.0.0.1", 0, "0.0.0.0");

        bool hostStarted = NetworkManager.Singleton.StartHost();
        if (!hostStarted)
        {
            Debug.LogError("[QuickTest] Host başlatılamadı; NetworkTransport hazır değil.");
            if (MultiplayerManager.Instance != null)
                MultiplayerManager.Instance.IsGameInProgress = false;
            return;
        }

        EnterGameplayMode(hideMenusOnly: false);
        StartCoroutine(TeleportPlayerToSpawn());
        Debug.Log("[QuickTest] Host başlatıldı, oyuna girildi.");
    }

    private IEnumerator TeleportPlayerToSpawn()
    {
        // Wait up to 2 seconds for the network player to spawn.
        float timeout = 2f;
        FirstPersonController spawnedFpc = null;

        while (timeout > 0)
        {
            var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
            foreach (var pFpc in allFpcs)
            {
                if (pFpc.IsOwner && pFpc != fpc)
                {
                    spawnedFpc = pFpc;
                    break;
                }
            }

            if (spawnedFpc != null)
                break;

            timeout -= Time.deltaTime;
            yield return null;
        }

        if (spawnedFpc != null)
        {
            var spawnCoordinator = FindFirstObjectByType<PlayerSpawnCoordinator>();
            if (spawnCoordinator != null)
            {
                spawnCoordinator.RequestDistribution(true);
            }
            else
            {
                Debug.LogWarning("[QuickTest] PlayerSpawnCoordinator bulunamadi, oyuncu mevcut konumunda birakildi.");
            }

            spawnedFpc.enabled = true;
            spawnedFpc.playerCanMove = true;
            spawnedFpc.cameraCanMove = true;

            if (spawnedFpc.playerCamera != null)
            {
                spawnedFpc.playerCamera.gameObject.SetActive(true);
            }

            Debug.Log("[QuickTest] Oyuncu yeni spawn koordinatoruyle yerlestirildi ve aktif edildi.");
        }
        else
        {
            Debug.LogWarning("[QuickTest] Spawn edilmis oyuncu bulunamadi (zaman asimi)!");
        }
    }

    public async void ShowMainMenu()
    {
        _returningToLobbyAfterMatch = false;
        _gameplayModeEntered = false;
        
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.IsGameInProgress = false;
            if (MultiplayerManager.Instance.HasActiveLobby)
            {
                await MultiplayerManager.Instance.LeaveLobby();
            }
        }

        bool needsSceneReload = false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            needsSceneReload = true;
        }

        if (needsSceneReload)
        {
            // Wait for shutdown to complete then reload the scene to reset the level
            StartCoroutine(ReloadSceneAfterShutdown());
            return;
        }

        _lobbyBridge.Unbind();
        DisableMenuFpc();

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);
        if (menuObject != null) menuObject.SetActive(true);

        SetupMainMenuButtons();
    }

    private IEnumerator ReloadSceneAfterShutdown()
    {
        yield return null; // wait a frame
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
