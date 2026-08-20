using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the local frame-pacing context without touching match rules.
/// Gameplay keeps the player's saved setting; menus/lobbies are capped and an
/// unfocused application is throttled so it does not heat the GPU in the tray.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class PerformanceRuntimeCoordinator : MonoBehaviour
{
    private const int DemoNetworkTickRate = 30;
    private const float ContextPollInterval = 0.25f;
    private const float GameplayCameraFarClip = 300f;

    private static PerformanceRuntimeCoordinator instance;
    private bool applicationFocused = true;
    private float nextContextPoll;
    private Camera optimizedGameplayCamera;
    private GamePerformanceSettings.FramePacingContext lastContext =
        (GamePerformanceSettings.FramePacingContext)(-1);
    private bool runtimeQualityApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject(nameof(PerformanceRuntimeCoordinator));
        instance = host.AddComponent<PerformanceRuntimeCoordinator>();
        host.AddComponent<RuntimeMapLightCulling>();
        DontDestroyOnLoad(host);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        GamePerformanceSettings.LoadAndApply();
        ApplyNetworkTuning();
        ApplyContext(force: true);
    }

    private void Start()
    {
        // URP volumes and the scene camera are initialized by Start. Reapply
        // once here so Bloom/AA/HDR switches are effective on the first frame.
        GamePerformanceSettings.ApplyCurrentSettings();
        runtimeQualityApplied = true;
        ApplyContext(force: true);
    }

    private void Update()
    {
        if (!runtimeQualityApplied)
        {
            GamePerformanceSettings.ApplyCurrentSettings();
            runtimeQualityApplied = true;
        }

        ApplyNetworkTuning();
        if (Time.unscaledTime < nextContextPoll)
            return;

        nextContextPoll = Time.unscaledTime + ContextPollInterval;
        ApplyCameraOptimization();
        ApplyContext(force: false);
    }

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        ApplyContext(force: true);
    }

    private void OnApplicationPause(bool paused)
    {
        applicationFocused = !paused;
        ApplyContext(force: true);
    }

    private void ApplyContext(bool force)
    {
        GamePerformanceSettings.FramePacingContext context =
            !applicationFocused
                ? GamePerformanceSettings.FramePacingContext.Background
                : IsGameplayActive()
                    ? GamePerformanceSettings.FramePacingContext.Gameplay
                    : GamePerformanceSettings.FramePacingContext.Menu;

        if (!force && context == lastContext)
            return;

        lastContext = context;
        GamePerformanceSettings.SetFramePacingContext(context);
    }

    private static bool IsTargetMapScene()
    {
        return SceneManager.GetActiveScene().name == "sci-fi-map";
    }

    private void ApplyCameraOptimization()
    {
        if (!IsTargetMapScene())
            return;

        FirstPersonController localPlayer = LocalPlayerResolver.Get();
        Camera gameplayCamera = localPlayer != null && localPlayer.playerCamera != null
            ? localPlayer.playerCamera
            : Camera.main;

        // The map's player camera is intentionally not tagged MainCamera. When
        // the resolver is still waiting for the networked player, use the first
        // active non-feed camera so the safe clip is applied in quick tests and
        // during the short spawn window as well.
        if (gameplayCamera == null)
        {
            Camera[] activeCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < activeCameras.Length; i++)
            {
                Camera candidate = activeCameras[i];
                if (candidate != null && candidate.isActiveAndEnabled &&
                    !candidate.name.StartsWith("__LiveFeedCamera", System.StringComparison.Ordinal))
                {
                    gameplayCamera = candidate;
                    break;
                }
            }
        }

        if (gameplayCamera == null || !gameplayCamera.isActiveAndEnabled ||
            gameplayCamera.name.StartsWith("__LiveFeedCamera", System.StringComparison.Ordinal))
            return;

        if (gameplayCamera != optimizedGameplayCamera ||
            gameplayCamera.farClipPlane > GameplayCameraFarClip)
        {
            // The measured map diagonal is about 227m. A 300m clip plane
            // safely covers the map while preventing a 1000m frustum from
            // retaining distant geometry in the main gameplay camera.
            gameplayCamera.farClipPlane = Mathf.Min(gameplayCamera.farClipPlane, GameplayCameraFarClip);
            gameplayCamera.useOcclusionCulling = true;
            optimizedGameplayCamera = gameplayCamera;
        }
    }

    private static bool IsGameplayActive()
    {
        MatchFlowManager flow = MatchFlowManager.Instance;
        if (flow != null)
        {
            MatchPhase phase = flow.CurrentPhase.Value;
            return phase != MatchPhase.Lobby && phase != MatchPhase.Ended;
        }

        return GameManager.Instance != null && GameManager.Instance.isGameStarted.Value;
    }

    private static void ApplyNetworkTuning()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.IsListening || manager.NetworkConfig == null)
            return;

        if (manager.NetworkConfig.TickRate != DemoNetworkTickRate)
            manager.NetworkConfig.TickRate = DemoNetworkTickRate;
    }
}
