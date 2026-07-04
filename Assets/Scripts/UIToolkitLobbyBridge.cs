using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit lobi panellerini UGS MultiplayerManager'a bağlar.
/// </summary>
public class UIToolkitLobbyBridge : MonoBehaviour
{
    public const string GameSceneName = "sci-fi-map";
    private const int PrivateMaxPlayers = 14;
    private const float LobbyRefreshInterval = 3f;

    private MultiplayerManager _network;
    private VisualElement _publicRoot;
    private VisualElement _privateRoot;
    private string _selectedLobbyId;
    private VisualElement _selectedLobbyRow;
    private Coroutine _lobbyRefreshRoutine;
    private bool _isBusy;

    public event Action OnGameStarting;

    public void BindPublicLobby(VisualElement root, Action onBackToMenu = null)
    {
        _publicRoot = root;
        WirePublicButtons(onBackToMenu);
    }

    public void BindPrivateLobby(VisualElement root, Action onBackToMenu = null)
    {
        _privateRoot = root;
        WirePrivateButtons(onBackToMenu);
    }

    public void Unbind()
    {
        StopLobbyRefresh();
        if (_network != null)
            _network.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;

        _publicRoot = null;
        _privateRoot = null;
    }

    private MultiplayerManager Network => _network != null ? _network : (_network = MultiplayerManager.Instance);

    private void OnDestroy()
    {
        Unbind();
    }

    private void WirePublicButtons(Action onBackToMenu)
    {
        if (_publicRoot == null) return;

        if (_network != null)
            _network.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
        if (Network != null)
            Network.OnLobbyPlayersChanged += OnLobbyPlayersChanged;

        RegisterClick("btn-host-game", () => SwitchPublicPanel(_publicRoot, "panel-host-setup"), _publicRoot);
        RegisterClick("btn-join-game", OnOpenPublicBrowser, _publicRoot);
        if (onBackToMenu != null)
            RegisterClick("btn-back-to-menu", onBackToMenu, _publicRoot);
        RegisterClick("btn-back-to-selection", () => SwitchPublicPanel(_publicRoot, "panel-selection"), _publicRoot);
        RegisterClick("btn-back-host-setup", () => SwitchPublicPanel(_publicRoot, "panel-selection"), _publicRoot);
        RegisterClick("btn-confirm-host", () => _ = OnCreatePublicLobbyAsync(), _publicRoot);
        RegisterClick("btn-confirm-join", () => _ = OnJoinSelectedPublicLobbyAsync(), _publicRoot);
        RegisterClick("leaveBtn", () => _ = OnLeavePublicLobbyAsync(), _publicRoot);
        RegisterClick("startBtn", OnStartGame, _publicRoot);

        var slider = _publicRoot.Q<SliderInt>("playerCountSlider");
        var countLabel = _publicRoot.Q<Label>("playerCountLabel");
        if (slider != null && countLabel != null)
        {
            countLabel.text = slider.value.ToString();
            slider.RegisterValueChangedCallback(evt => countLabel.text = evt.newValue.ToString());
        }
    }

    private void WirePrivateButtons(Action onBackToMenu)
    {
        if (_privateRoot == null) return;

        if (_network != null)
            _network.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
        if (Network != null)
            Network.OnLobbyPlayersChanged += OnLobbyPlayersChanged;

        RegisterClick("btn-host-game", () => _ = OnCreatePrivateLobbyAsync(), _privateRoot);
        RegisterClick("btn-join-game", () => SwitchPrivatePanel(_privateRoot, "panel-join"), _privateRoot);
        if (onBackToMenu != null)
            RegisterClick("btn-back-to-menu", onBackToMenu, _privateRoot);
        RegisterClick("btn-back-to-selection", () => SwitchPrivatePanel(_privateRoot, "panel-selection"), _privateRoot);
        RegisterClick("btn-confirm-join", () => _ = OnJoinPrivateByCodeAsync(), _privateRoot);
        RegisterClick("leaveBtn", () => _ = OnLeavePrivateLobbyAsync(), _privateRoot);
        RegisterClick("startBtn", OnStartGame, _privateRoot);
    }

    private static void RegisterClick(string name, Action handler, VisualElement root)
    {
        var btn = root.Q<Button>(name);
        if (btn != null)
            btn.clicked += handler;
    }

    private async void OnOpenPublicBrowser()
    {
        SwitchPublicPanel(_publicRoot, "panel-browser");
        await RefreshPublicLobbyListAsync();
    }

    private async Awaitable OnCreatePublicLobbyAsync()
    {
        if (_isBusy || _publicRoot == null) return;
        _isBusy = true;
        SetPublicButtonsEnabled(false);

        var roomNameInput = _publicRoot.Q<TextField>("roomNameInput");
        var slider = _publicRoot.Q<SliderInt>("playerCountSlider");
        string roomName = roomNameInput != null ? roomNameInput.value.Trim() : string.Empty;
        if (string.IsNullOrEmpty(roomName))
            roomName = "Adsız Oda";

        int maxPlayers = slider != null ? slider.value : 8;
        bool ok = await Network.CreatePublicLobby(roomName, maxPlayers);

        _isBusy = false;
        SetPublicButtonsEnabled(true);

        if (ok)
        {
            ShowPublicLobbyScreen();
            StartLobbyRefresh();
        }
        else
        {
            SetPublicStatus("Lobi oluşturulamadı. UGS bağlantısını kontrol edin.");
        }
    }

    private async Awaitable OnJoinSelectedPublicLobbyAsync()
    {
        if (_isBusy || string.IsNullOrEmpty(_selectedLobbyId))
        {
            SetPublicStatus("Lütfen bir lobi seçin.");
            return;
        }

        _isBusy = true;
        SetPublicButtonsEnabled(false);

        bool ok = await Network.JoinById(_selectedLobbyId);

        _isBusy = false;
        SetPublicButtonsEnabled(true);

        if (ok)
        {
            ShowPublicLobbyScreen();
            StartLobbyRefresh();
        }
        else
        {
            SetPublicStatus("Lobiye katılınamadı.");
        }
    }

    public async Awaitable OnLeavePublicLobbyForMenuAsync() => await OnLeavePublicLobbyAsync();
    public async Awaitable OnLeavePrivateLobbyForMenuAsync() => await OnLeavePrivateLobbyAsync();

    private async Awaitable OnLeavePublicLobbyAsync()
    {
        StopLobbyRefresh();
        await Network.LeaveLobby();
        _selectedLobbyId = null;
        _selectedLobbyRow = null;
        SwitchPublicPanel(_publicRoot, "panel-selection");
    }

    private async Awaitable OnCreatePrivateLobbyAsync()
    {
        if (_isBusy || _privateRoot == null) return;
        _isBusy = true;

        string lobbyName = $"Oyuncu {AuthenticationService.Instance.PlayerId.Substring(0, 4)}";
        bool ok = await Network.CreatePrivateLobby(lobbyName, PrivateMaxPlayers);

        _isBusy = false;

        if (ok)
        {
            var codeLabel = _privateRoot.Q<Label>("roomCodeLabel");
            if (codeLabel != null)
                codeLabel.text = Network.CurrentLobbyCode ?? "------";

            ShowPrivateLobbyScreen();
            StartLobbyRefresh();
        }
        else
        {
            SetPrivateStatus("Private lobi oluşturulamadı.");
        }
    }

    private async Awaitable OnJoinPrivateByCodeAsync()
    {
        if (_isBusy || _privateRoot == null) return;

        var joinInput = _privateRoot.Q<TextField>("joinCodeInput");
        string code = joinInput != null ? joinInput.value.Trim().ToUpper() : string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            SetPrivateStatus("Lütfen oda kodunu girin.");
            return;
        }

        _isBusy = true;
        bool ok = await Network.JoinByCode(code);
        _isBusy = false;

        if (ok)
        {
            ShowPrivateLobbyScreen();
            StartLobbyRefresh();
        }
        else
        {
            SetPrivateStatus("Geçersiz oda kodu veya lobi dolu.");
        }
    }

    private async Awaitable OnLeavePrivateLobbyAsync()
    {
        StopLobbyRefresh();
        await Network.LeaveLobby();
        SwitchPrivatePanel(_privateRoot, "panel-selection");
    }

    private void OnStartGame()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        OnGameStarting?.Invoke();
        Network.StartGame(GameSceneName);
    }

    public async Awaitable RefreshPublicLobbyListAsync()
    {
        if (_publicRoot == null) return;

        var scrollView = _publicRoot.Q<ScrollView>("lobbyListContainer");
        if (scrollView == null) return;

        scrollView.contentContainer.Clear();
        _selectedLobbyId = null;
        _selectedLobbyRow = null;

        var joinBtn = _publicRoot.Q<Button>("btn-confirm-join");
        if (joinBtn != null) joinBtn.SetEnabled(false);

        List<Lobby> lobbies = await Network.GetPublicLobbies();
        if (lobbies.Count == 0)
        {
            var empty = new Label("Açık lobi bulunamadı.");
            empty.AddToClassList("lobby-item-name");
            scrollView.contentContainer.Add(empty);
            return;
        }

        foreach (var lobby in lobbies)
        {
            bool isFull = lobby.AvailableSlots <= 0;
            var row = CreateLobbyRow(lobby, isFull);
            scrollView.contentContainer.Add(row);
        }
    }

    private VisualElement CreateLobbyRow(Lobby lobby, bool isFull)
    {
        var row = new VisualElement();
        row.AddToClassList("lobby-item");
        if (isFull) row.SetEnabled(false);

        var nameLabel = new Label(string.IsNullOrEmpty(lobby.Name) ? "Adsız Oda" : lobby.Name);
        nameLabel.AddToClassList("lobby-item-name");

        var playersLabel = new Label($"{lobby.Players.Count} / {lobby.MaxPlayers}");
        playersLabel.AddToClassList("lobby-item-players");

        var statusLabel = new Label(isFull ? "DOLU" : "AÇIK");
        statusLabel.AddToClassList("lobby-item-status");
        if (isFull) statusLabel.AddToClassList("lobby-item-status-full");

        row.Add(nameLabel);
        row.Add(playersLabel);
        row.Add(statusLabel);

        if (!isFull)
        {
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (_selectedLobbyRow != null)
                    _selectedLobbyRow.RemoveFromClassList("lobby-item-selected");

                _selectedLobbyRow = row;
                _selectedLobbyId = lobby.Id;
                row.AddToClassList("lobby-item-selected");

                var joinBtn = _publicRoot?.Q<Button>("btn-confirm-join");
                if (joinBtn != null) joinBtn.SetEnabled(true);
            });
        }

        return row;
    }

    public void OpenPrivateLobbyDirectly()
    {
        ShowPrivateLobbyScreen();
        StartLobbyRefresh();
    }

    public void OpenPublicLobbyDirectly()
    {
        ShowPublicLobbyScreen();
        StartLobbyRefresh();
    }

    private void ShowPublicLobbyScreen()
    {
        if (_publicRoot == null) return;

        var roomNameLabel = _publicRoot.Q<Label>("roomNameLabel");
        var countLabel = _publicRoot.Q<Label>("lobbyPlayerCountLabel");
        if (roomNameLabel != null)
            roomNameLabel.text = Network.CurrentLobbyName;

        RefreshLobbyPlayerCount(countLabel, true);
        RefreshPlayerList(_publicRoot);
        UpdateStartButton(_publicRoot);

        SwitchPublicPanel(_publicRoot, "panel-lobby");
    }

    private void ShowPrivateLobbyScreen()
    {
        if (_privateRoot == null) return;

        var codeLabel = _privateRoot.Q<Label>("roomCodeLabel");
        if (codeLabel != null && Network.HasActiveLobby)
            codeLabel.text = Network.CurrentLobbyCode ?? "------";

        RefreshPlayerList(_privateRoot);
        UpdateStartButton(_privateRoot);
        SwitchPrivatePanel(_privateRoot, "panel-host-lobby");
    }

    private void OnLobbyPlayersChanged()
    {
        if (_publicRoot != null && IsPanelVisible(_publicRoot, "panel-lobby"))
        {
            var countLabel = _publicRoot.Q<Label>("lobbyPlayerCountLabel");
            RefreshLobbyPlayerCount(countLabel, true);
            RefreshPlayerList(_publicRoot);
            UpdateStartButton(_publicRoot);
        }

        if (_privateRoot != null && IsPanelVisible(_privateRoot, "panel-host-lobby"))
        {
            RefreshPlayerList(_privateRoot);
            UpdateStartButton(_privateRoot);
        }
    }

    private void RefreshLobbyPlayerCount(Label countLabel, bool isPublic)
    {
        if (countLabel == null) return;
        int count = Network.GetLobbyPlayers().Count;
        if (count == 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            count = NetworkManager.Singleton.ConnectedClientsIds.Count;

        countLabel.text = isPublic
            ? $"{count} / {Network.CurrentLobbyMaxPlayers}"
            : $"{count}";
    }

    private void RefreshPlayerList(VisualElement root)
    {
        var container = root.Q<ScrollView>("playerListContainer");
        if (container == null) return;

        container.contentContainer.Clear();

        var players = Network.GetLobbyPlayers();
        if (players.Count == 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            int i = 1;
            foreach (var _ in NetworkManager.Singleton.ConnectedClientsIds)
            {
                container.contentContainer.Add(CreatePlayerRow($"Oyuncu {i}"));
                i++;
            }
            return;
        }

        foreach (var player in players)
            container.contentContainer.Add(CreatePlayerRow(player.DisplayName));

        if (container.contentContainer.childCount == 0)
            container.contentContainer.Add(CreatePlayerRow("Bekleniyor..."));
    }

    private static VisualElement CreatePlayerRow(string playerName)
    {
        var row = new VisualElement();
        row.AddToClassList("player-item");

        var icon = new VisualElement();
        icon.AddToClassList("player-icon");

        var name = new Label(playerName);
        name.AddToClassList("player-name");

        row.Add(icon);
        row.Add(name);
        return row;
    }

    private static void UpdateStartButton(VisualElement root)
    {
        var startBtn = root.Q<Button>("startBtn");
        if (startBtn == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        startBtn.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void StartLobbyRefresh()
    {
        StopLobbyRefresh();
        _lobbyRefreshRoutine = StartCoroutine(LobbyRefreshLoop());
    }

    private void StopLobbyRefresh()
    {
        if (_lobbyRefreshRoutine != null)
        {
            StopCoroutine(_lobbyRefreshRoutine);
            _lobbyRefreshRoutine = null;
        }
    }

    private IEnumerator LobbyRefreshLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(LobbyRefreshInterval);
            if (Network != null && Network.HasActiveLobby)
                _ = Network.RefreshCurrentLobbyAsync();
        }
    }

    private void SetPublicButtonsEnabled(bool enabled)
    {
        if (_publicRoot == null) return;
        _publicRoot.Q<Button>("btn-confirm-host")?.SetEnabled(enabled);
        _publicRoot.Q<Button>("btn-confirm-join")?.SetEnabled(enabled && !string.IsNullOrEmpty(_selectedLobbyId));
    }

    private void SetPublicStatus(string message)
    {
        var subtitle = _publicRoot?.Q<VisualElement>("panel-host-setup")?.Q<Label>(className: "panel-subtitle")
                       ?? _publicRoot?.Q<VisualElement>("panel-browser")?.Q<Label>(className: "panel-subtitle");
        if (subtitle != null) subtitle.text = message;
    }

    private void SetPrivateStatus(string message)
    {
        var subtitle = _privateRoot?.Q<VisualElement>("panel-join")?.Q<Label>(className: "panel-subtitle");
        if (subtitle != null) subtitle.text = message;
    }

    public static void SwitchPublicPanel(VisualElement root, string panelName)
    {
        SetPanelVisible(root, "panel-selection", panelName == "panel-selection");
        SetPanelVisible(root, "panel-browser", panelName == "panel-browser");
        SetPanelVisible(root, "panel-host-setup", panelName == "panel-host-setup");
        SetPanelVisible(root, "panel-lobby", panelName == "panel-lobby");
    }

    public static void SwitchPrivatePanel(VisualElement root, string panelName)
    {
        SetPanelVisible(root, "panel-selection", panelName == "panel-selection");
        SetPanelVisible(root, "panel-host-lobby", panelName == "panel-host-lobby");
        SetPanelVisible(root, "panel-join", panelName == "panel-join");
    }

    private static void SetPanelVisible(VisualElement root, string panelName, bool visible)
    {
        root.Q<VisualElement>(panelName)?.EnableInClassList("hidden", !visible);
    }

    private static bool IsPanelVisible(VisualElement root, string panelName)
    {
        var panel = root.Q<VisualElement>(panelName);
        return panel != null && !panel.ClassListContains("hidden");
    }
}
