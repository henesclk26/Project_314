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

    private UIToolkitLobbyBridge _lobbyBridge;

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

        if (menuObject != null) menuObject.SetActive(true);
        SetupMainMenuButtons();
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
        }
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
        root.Q<Button>("btn-private-game")?.RegisterCallback<ClickEvent>(_ => OnPrivateGameClicked());
        root.Q<Button>("btn-public-game")?.RegisterCallback<ClickEvent>(_ => OnPublicGameClicked());
        root.Q<Button>("btn-quit-game")?.RegisterCallback<ClickEvent>(_ => OnQuitGame());
    }

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

    public async void ShowMainMenu()
    {
        _gameplayModeEntered = false;
        if (MultiplayerManager.Instance != null && MultiplayerManager.Instance.HasActiveLobby)
            await MultiplayerManager.Instance.LeaveLobby();

        _lobbyBridge.Unbind();
        DisableMenuFpc();

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        if (privateLobbyObject != null) privateLobbyObject.SetActive(false);
        if (publicLobbyObject != null) publicLobbyObject.SetActive(false);
        if (menuObject != null) menuObject.SetActive(true);

        SetupMainMenuButtons();
    }
}
