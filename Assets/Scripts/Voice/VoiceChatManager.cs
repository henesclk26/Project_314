using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Server-state-driven Vivox voice routing.
///
/// The match phase and each player's alive state are server-authoritative
/// NetworkVariables. Clients only use those replicated values to select their
/// local Vivox channel, so a living player cannot be routed to the ghost
/// channel by normal game state and a ghost cannot hear the living channels.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class VoiceChatManager : MonoBehaviour
{
    public enum VoiceChannelMode : byte
    {
        None = 0,
        Proximity = 1,
        Meeting = 2,
        Ghost = 3
    }

    private const string ChannelPrefix = "p314_voice_";
    private const int ProximityAudibleDistance = 24;
    private const int ProximityConversationalDistance = 12;
    private const float PositionUpdateInterval = 0.1f;

    public static VoiceChatManager Instance { get; private set; }

    public VoiceChannelMode CurrentMode { get; private set; }
    public bool IsVoiceReady { get; private set; }
    public bool IsMicrophoneMuted => IsVoiceReady && VivoxService.Instance.IsInputDeviceMuted;
    public event Action VoiceStateChanged;

    private string joinedChannel;
    private string desiredChannel;
    private bool desiredChannelIsPositional;
    private bool reconcileInProgress;
    private bool reconcileRequested;
    private float nextPositionUpdate;
    private FirstPersonController localPlayer;
    private bool vivoxEventsSubscribed;
    private string cachedSessionSource;
    private string cachedSessionKey;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        GameObject voiceObject = new GameObject(nameof(VoiceChatManager));
        Instance = voiceObject.AddComponent<VoiceChatManager>();
        DontDestroyOnLoad(voiceObject);
    }

    private async void Start()
    {
        try
        {
            MultiplayerManager manager = await WaitForMultiplayerManagerAsync();
            if (manager == null || !await manager.WaitUntilReadyAsync())
            {
                Debug.LogWarning("[VoiceChat] UGS hazır olmadığı için Vivox başlatılamadı.");
                return;
            }

            await InitializeVivoxAsync();
        }
        catch (Exception exception)
        {
            IsVoiceReady = false;
            Debug.LogWarning("[VoiceChat] Vivox kullanılamıyor: " + exception.Message);
        }
    }

    private async Task<MultiplayerManager> WaitForMultiplayerManagerAsync()
    {
        const int maxAttempts = 150;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (MultiplayerManager.Instance != null)
                return MultiplayerManager.Instance;

            await Task.Delay(100);
        }

        return null;
    }

    private async Task InitializeVivoxAsync()
    {
        if (VivoxService.Instance.InitializationState != VivoxInitializationState.Initialized)
            await VivoxService.Instance.InitializeAsync();

        if (VivoxService.Instance.InitializationState != VivoxInitializationState.Initialized)
            throw new InvalidOperationException("Vivox initialization did not complete successfully.");

        if (!VivoxService.Instance.IsLoggedIn)
        {
            ulong clientId = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId
                : 0;

            await VivoxService.Instance.LoginAsync(new LoginOptions
            {
                DisplayName = "Player " + clientId,
                ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
            });
        }

        VivoxService.Instance.EnableAcousticEchoCancellation();
        await VivoxService.Instance.EnableAutoVoiceActivityDetectionAsync();
        SubscribeVivoxEvents();

        IsVoiceReady = true;
        VoiceStateChanged?.Invoke();
        RequestChannelReconcile();
        Debug.Log("[VoiceChat] Vivox hazır; ses kanalı oyun fazına göre yönetilecek.");
    }

    private void SubscribeVivoxEvents()
    {
        if (vivoxEventsSubscribed)
            return;

        VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
        VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
        VivoxService.Instance.ConnectionRecovered += OnConnectionRecovered;
        VivoxService.Instance.ConnectionFailedToRecover += OnConnectionFailedToRecover;
        vivoxEventsSubscribed = true;
    }

    private void UnsubscribeVivoxEvents()
    {
        if (!vivoxEventsSubscribed || VivoxService.Instance == null)
            return;

        VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
        VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
        VivoxService.Instance.ConnectionRecovered -= OnConnectionRecovered;
        VivoxService.Instance.ConnectionFailedToRecover -= OnConnectionFailedToRecover;
        vivoxEventsSubscribed = false;
    }

    private void OnConnectionRecovered()
    {
        IsVoiceReady = VivoxService.Instance.IsLoggedIn;
        VoiceStateChanged?.Invoke();
        RequestChannelReconcile();
    }

    private void OnConnectionFailedToRecover()
    {
        IsVoiceReady = false;
        CurrentMode = VoiceChannelMode.None;
        joinedChannel = null;
        VoiceStateChanged?.Invoke();
    }

    private void OnParticipantAdded(VivoxParticipant participant)
    {
        if (participant == null)
            return;

        participant.ParticipantSpeechDetected += OnParticipantSpeechChanged;
        participant.ParticipantMuteStateChanged += OnParticipantSpeechChanged;
        VoiceStateChanged?.Invoke();
    }

    private void OnParticipantRemoved(VivoxParticipant participant)
    {
        if (participant == null)
            return;

        participant.ParticipantSpeechDetected -= OnParticipantSpeechChanged;
        participant.ParticipantMuteStateChanged -= OnParticipantSpeechChanged;
        VoiceStateChanged?.Invoke();
    }

    private void OnParticipantSpeechChanged()
    {
        VoiceStateChanged?.Invoke();
    }

    private void Update()
    {
        if (!IsVoiceReady || VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn)
            return;

        if (Input.GetKeyDown(KeyCode.M))
            ToggleMicrophoneMute();

        FirstPersonController currentLocalPlayer = FindLocalPlayer();
        string nextChannel = GetDesiredChannel(currentLocalPlayer, out bool positional);
        if (nextChannel != desiredChannel || positional != desiredChannelIsPositional)
        {
            desiredChannel = nextChannel;
            desiredChannelIsPositional = positional;
            RequestChannelReconcile();
        }

        if (CurrentMode == VoiceChannelMode.Proximity &&
            !string.IsNullOrEmpty(joinedChannel) &&
            currentLocalPlayer != null &&
            Time.unscaledTime >= nextPositionUpdate)
        {
            VivoxService.Instance.Set3DPosition(currentLocalPlayer.gameObject, joinedChannel);
            nextPositionUpdate = Time.unscaledTime + PositionUpdateInterval;
        }
    }

    private FirstPersonController FindLocalPlayer()
    {
        if (localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
            return localPlayer;

        localPlayer = null;
        foreach (FirstPersonController candidate in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.IsSpawned && candidate.IsOwner)
            {
                localPlayer = candidate;
                break;
            }
        }

        return localPlayer;
    }

    private string GetDesiredChannel(FirstPersonController player, out bool positional)
    {
        positional = false;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening ||
            MatchFlowManager.Instance == null || player == null)
        {
            return null;
        }

        MatchPhase phase = MatchFlowManager.Instance.CurrentPhase.Value;
        if (phase == MatchPhase.Lobby || phase == MatchPhase.Ended)
            return null;

        string sessionKey = GetSessionKey();
        if (player.isDead.Value)
            return BuildChannelName(sessionKey, "ghost");

        if (phase == MatchPhase.Meeting)
            return BuildChannelName(sessionKey, "meeting");

        positional = true;
        return BuildChannelName(sessionKey, "proximity");
    }

    private static string BuildChannelName(string sessionKey, string channelType)
    {
        return ChannelPrefix + sessionKey + "_" + channelType;
    }

    private static string GetSessionKey()
    {
        MultiplayerManager manager = MultiplayerManager.Instance;
        string raw = manager != null ? manager.CurrentLobbyCode : null;
        if (string.IsNullOrEmpty(raw) && manager != null)
            raw = manager.CurrentJoinCode;

        if (string.IsNullOrEmpty(raw))
            raw = "quicktest";

        if (Instance != null && raw == Instance.cachedSessionSource &&
            !string.IsNullOrEmpty(Instance.cachedSessionKey))
        {
            return Instance.cachedSessionKey;
        }

        char[] characters = raw.Where(char.IsLetterOrDigit).ToArray();
        string sanitized = new string(characters).ToLowerInvariant();
        string result = string.IsNullOrEmpty(sanitized) ? "quicktest" : sanitized;
        if (Instance != null)
        {
            Instance.cachedSessionSource = raw;
            Instance.cachedSessionKey = result;
        }

        return result;
    }

    private void RequestChannelReconcile()
    {
        if (!IsVoiceReady)
            return;

        if (reconcileInProgress)
        {
            reconcileRequested = true;
            return;
        }

        ReconcileChannelsAsync();
    }

    private async void ReconcileChannelsAsync()
    {
        if (reconcileInProgress)
        {
            reconcileRequested = true;
            return;
        }

        reconcileInProgress = true;
        do
        {
            reconcileRequested = false;
            string targetChannel = desiredChannel;
            bool positional = desiredChannelIsPositional;

            try
            {
                await LeaveOwnedChannelsExceptAsync(targetChannel);

                if (string.IsNullOrEmpty(targetChannel))
                {
                    joinedChannel = null;
                    CurrentMode = VoiceChannelMode.None;
                }
                else if (targetChannel != joinedChannel)
                {
                    if (positional)
                    {
                        Channel3DProperties properties = new Channel3DProperties(
                            ProximityAudibleDistance,
                            ProximityConversationalDistance,
                            1f,
                            AudioFadeModel.InverseByDistance);
                        await VivoxService.Instance.JoinPositionalChannelAsync(
                            targetChannel,
                            ChatCapability.AudioOnly,
                            properties);
                        CurrentMode = VoiceChannelMode.Proximity;
                    }
                    else
                    {
                        await VivoxService.Instance.JoinGroupChannelAsync(targetChannel, ChatCapability.AudioOnly);
                        CurrentMode = targetChannel.EndsWith("_ghost", StringComparison.Ordinal)
                            ? VoiceChannelMode.Ghost
                            : VoiceChannelMode.Meeting;
                    }

                    joinedChannel = targetChannel;
                    nextPositionUpdate = 0f;
                }

                VoiceStateChanged?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VoiceChat] Kanal geçişi başarısız: " + exception.Message);
                IsVoiceReady = false;
                CurrentMode = VoiceChannelMode.None;
                joinedChannel = null;
                VoiceStateChanged?.Invoke();
            }
        }
        while (reconcileRequested && IsVoiceReady);

        reconcileInProgress = false;
    }

    private async Task LeaveOwnedChannelsExceptAsync(string keepChannel)
    {
        if (VivoxService.Instance.ActiveChannels == null)
            return;

        List<string> channels = VivoxService.Instance.ActiveChannels.Keys
            .Where(channel => channel.StartsWith(ChannelPrefix, StringComparison.Ordinal))
            .Where(channel => !string.Equals(channel, keepChannel, StringComparison.Ordinal))
            .ToList();

        foreach (string channel in channels)
            await VivoxService.Instance.LeaveChannelAsync(channel);
    }

    public void ToggleMicrophoneMute()
    {
        if (!IsVoiceReady)
            return;

        if (VivoxService.Instance.IsInputDeviceMuted)
            VivoxService.Instance.UnmuteInputDevice();
        else
            VivoxService.Instance.MuteInputDevice();

        VoiceStateChanged?.Invoke();
    }

    public bool IsRemotePlayerSpeaking()
    {
        return GetSpeakingSummary().Length > 0;
    }

    public string GetSpeakingSummary()
    {
        if (!IsVoiceReady || VivoxService.Instance.ActiveChannels == null)
            return string.Empty;

        List<string> speakers = new List<string>();
        foreach (var channel in VivoxService.Instance.ActiveChannels.Values)
        {
            foreach (VivoxParticipant participant in channel)
            {
                if (participant == null || participant.IsSelf || !participant.SpeechDetected)
                    continue;

                string displayName = string.IsNullOrEmpty(participant.DisplayName)
                    ? "REMOTE UNIT"
                    : participant.DisplayName;
                if (!speakers.Contains(displayName))
                    speakers.Add(displayName);
            }
        }

        return string.Join(", ", speakers.Take(2)) + (speakers.Count > 2 ? " +" : string.Empty);
    }

    public string GetModeLabel()
    {
        switch (CurrentMode)
        {
            case VoiceChannelMode.Proximity: return "VOICE // PROXIMITY";
            case VoiceChannelMode.Meeting: return "VOICE // MEETING";
            case VoiceChannelMode.Ghost: return "VOICE // GHOSTS";
            default: return string.Empty;
        }
    }

    private async void OnDestroy()
    {
        UnsubscribeVivoxEvents();

        if (Instance == this)
            Instance = null;

        if (IsVoiceReady && VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
        {
            try
            {
                await VivoxService.Instance.LeaveAllChannelsAsync();
                await VivoxService.Instance.LogoutAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VoiceChat] Vivox kapanırken hata: " + exception.Message);
            }
        }
    }
}
