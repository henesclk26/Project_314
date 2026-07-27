using System.Collections;
using Unity.Netcode;
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

    private void Update()
    {
        if (!_gameplayModeEntered && IsInActiveNetworkSession() && GameManager.Instance != null && GameManager.Instance.isGameStarted.Value)
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
        BindMenuButton(root.Q<Button>("btn-quick-test"), OnQuickTestButtonClicked);
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
        EnterGameplayMode(hideMenusOnly: false);
    }

    private void EnterGameplayMode(bool hideMenusOnly)
    {
        HideMenuFpcObject();

        if (menuObject != null) menuObject.SetActive(false);
        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);

        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
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
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[QuickTest] NetworkManager bulunamadı!");
            return;
        }

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.IsGameInProgress = true;
        }

        NetworkManager.Singleton.StartHost();
        EnterGameplayMode(hideMenusOnly: false);
        StartCoroutine(TeleportPlayerToSpawn());
        Debug.Log("[QuickTest] Host başlatıldı, oyuna girildi.");
    }

    private IEnumerator TeleportPlayerToSpawn()
    {
        // Wait up to 2 seconds for the network player to spawn
        float timeout = 2f;
        FirstPersonController spawnedFpc = null;

        while (timeout > 0)
        {
            var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
            foreach (var pFpc in allFpcs)
            {
                if (pFpc.IsOwner && pFpc != fpc) // Not the menu FPC
                {
                    spawnedFpc = pFpc;
                    break;
                }
            }

            if (spawnedFpc != null) break;
            
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (spawnedFpc != null)
        {
            // Find spawn point
            Vector3 spawnPos = new Vector3(-48.5f, 2.49f, 1.82f); // Default fallback
            var spawnObj = GameObject.Find("Spawn_1");
            if (spawnObj != null) spawnPos = spawnObj.transform.position;

            var cc = spawnedFpc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            
            spawnedFpc.transform.position = spawnPos;
            
            if (cc != null) cc.enabled = true;
            
            spawnedFpc.enabled = true;
            spawnedFpc.playerCanMove = true;
            spawnedFpc.cameraCanMove = true;

            // Make sure the camera is enabled
            if (spawnedFpc.playerCamera != null)
            {
                spawnedFpc.playerCamera.gameObject.SetActive(true);
            }

            Debug.Log($"[QuickTest] Oyuncu {spawnPos} konumuna ışınlandı ve aktif edildi.");
        }
        else
        {
            Debug.LogWarning("[QuickTest] Spawn edilmiş oyuncu bulunamadı (zaman aşımı)!");
        }
    }

    public async void ShowMainMenu()
    {
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
