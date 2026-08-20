using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps distant sci-fi-map lights from contributing to the current view.
/// Lights are toggled with hysteresis so turning around or crossing a room
/// does not make them flicker. The original enabled state is preserved.
/// </summary>
[DefaultExecutionOrder(-9990)]
public sealed class RuntimeMapLightCulling : MonoBehaviour
{
    private const string TargetSceneName = "sci-fi-map";
    private const float UpdateInterval = 0.2f;
    // sci-fi-map has a roughly 116.5m light-cluster diagonal. Keep a 20m
    // hysteresis band around that coverage so long corridors do not darken
    // while still allowing lights outside the playable lighting envelope to
    // be released.
    private const float EnableDistance = 100f;
    private const float DisableDistance = 120f;
    private const float ColoredLightShadowDistance = 65f;
    private const int LowColoredShadowBudget = 8;
    private const int MediumColoredShadowBudget = 8;

    private readonly List<Light> sceneLights = new List<Light>();
    private readonly Dictionary<int, bool> originalEnabled = new Dictionary<int, bool>();
    private readonly Dictionary<int, bool> culled = new Dictionary<int, bool>();

    private float nextUpdate;
    private float nextDiscovery;
    private Camera playerCamera;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        nextDiscovery = 0f;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        RestoreSceneLights();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextUpdate)
            return;

        nextUpdate = Time.unscaledTime + UpdateInterval;

        if (!IsTargetScene())
        {
            RestoreSceneLights();
            return;
        }

        // Remote security-camera feeds are a second view into the map. Keep
        // their rooms lit while the terminal is open, then resume culling.
        if (SecurityCameraUIManager.Instance != null &&
            SecurityCameraUIManager.Instance.IsOpen)
        {
            RestoreSceneLights();
            return;
        }

        if (Time.unscaledTime >= nextDiscovery)
        {
            nextDiscovery = Time.unscaledTime + 1f;
            DiscoverSceneLights();
        }

        FirstPersonController localPlayer = LocalPlayerResolver.Get();
        if (localPlayer != null && localPlayer.playerCamera != null)
        {
            playerCamera = localPlayer.playerCamera;
        }
        else if (playerCamera == null || !playerCamera.isActiveAndEnabled)
        {
            playerCamera = Camera.main;

            if (playerCamera == null)
            {
                Camera[] activeCameras = FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < activeCameras.Length; i++)
                {
                    Camera candidate = activeCameras[i];
                    if (candidate != null && candidate.isActiveAndEnabled &&
                        !candidate.name.StartsWith("__LiveFeedCamera", System.StringComparison.Ordinal))
                    {
                        playerCamera = candidate;
                        break;
                    }
                }
            }
        }

        if (playerCamera == null)
            return;

        // During the spawn window the editor can leave a temporary camera far
        // outside the map. Do not interpret that camera as gameplay and turn
        // every local light off before the networked player exists.
        if (localPlayer == null && !IsNearAnySceneLight(playerCamera.transform.position))
            return;

        CullDistantLights(playerCamera.transform.position);
        UpdateColoredRoomShadows(playerCamera.transform.position);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearTrackedLights();
        nextDiscovery = 0f;
        playerCamera = null;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        ClearTrackedLights();
        nextDiscovery = 0f;
        playerCamera = null;
    }

    private static bool IsTargetScene()
    {
        return SceneManager.GetActiveScene().name == TargetSceneName;
    }

    private void DiscoverSceneLights()
    {
        sceneLights.Clear();

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Light light in lights)
        {
            if (light == null || light.gameObject.scene.name != TargetSceneName)
                continue;

            sceneLights.Add(light);
            int instanceId = light.GetInstanceID();
            if (!originalEnabled.ContainsKey(instanceId))
                originalEnabled[instanceId] = light.enabled;
            if (!culled.ContainsKey(instanceId))
                culled[instanceId] = false;
        }
    }

    private void CullDistantLights(Vector3 cameraPosition)
    {
        float enableDistanceSqr = EnableDistance * EnableDistance;
        float disableDistanceSqr = DisableDistance * DisableDistance;

        foreach (Light light in sceneLights)
        {
            if (light == null)
                continue;

            int instanceId = light.GetInstanceID();
            if (!originalEnabled.TryGetValue(instanceId, out bool wasEnabled) || !wasEnabled)
                continue;

            // Directional lights affect the whole map and are not room-local.
            if (light.type == LightType.Directional)
                continue;

            bool wasCulled = culled.TryGetValue(instanceId, out bool previous) && previous;
            float thresholdSqr = wasCulled ? enableDistanceSqr : disableDistanceSqr;
            Vector3 lightPosition = light.transform.position;
            float distanceSqr = (lightPosition - cameraPosition).sqrMagnitude;
            bool shouldCull = distanceSqr > thresholdSqr;

            if (shouldCull == wasCulled)
                continue;

            culled[instanceId] = shouldCull;
            light.enabled = !shouldCull;
        }
    }

    private bool IsNearAnySceneLight(Vector3 cameraPosition)
    {
        float maxDistanceSqr = DisableDistance * DisableDistance;
        foreach (Light light in sceneLights)
        {
            if (light == null || light.type == LightType.Directional)
                continue;

            if ((light.transform.position - cameraPosition).sqrMagnitude <= maxDistanceSqr)
                return true;
        }

        return false;
    }

    private void UpdateColoredRoomShadows(Vector3 cameraPosition)
    {
        // High keeps the scene-authored shadow setup. Low/Medium reserve a
        // small, camera-near budget for the colored room lights so their
        // illumination cannot pass through a neighboring wall while keeping
        // every distant decorative light from consuming a shadow-map slot.
        if (GamePerformanceSettings.Quality == GamePerformanceSettings.QualityPreset.High)
            return;

        int budget = GamePerformanceSettings.Quality == GamePerformanceSettings.QualityPreset.Low
            ? LowColoredShadowBudget
            : MediumColoredShadowBudget;

        List<Light> candidates = new List<Light>();
        float maxDistanceSqr = ColoredLightShadowDistance * ColoredLightShadowDistance;
        foreach (Light light in sceneLights)
        {
            if (light == null || !light.enabled || !IsColoredRoomLight(light))
                continue;

            Vector3 delta = light.transform.position - cameraPosition;
            if (delta.sqrMagnitude <= maxDistanceSqr)
                candidates.Add(light);
        }

        candidates.Sort((left, right) =>
        {
            // The eight lights fixed in the scene have a deliberately lower
            // bias. Prioritize them so Medium keeps the room-light blockers,
            // then use distance for the remaining colored lights.
            bool leftProtected = left.shadowBias <= 0.025f;
            bool rightProtected = right.shadowBias <= 0.025f;
            if (leftProtected != rightProtected)
                return leftProtected ? -1 : 1;

            return ((left.transform.position - cameraPosition).sqrMagnitude).CompareTo(
                (right.transform.position - cameraPosition).sqrMagnitude);
        });

        HashSet<int> selected = new HashSet<int>();
        for (int i = 0; i < candidates.Count && i < budget; i++)
            selected.Add(candidates[i].GetInstanceID());

        foreach (Light light in sceneLights)
        {
            if (light == null || !IsColoredRoomLight(light))
                continue;

            bool keepShadow = light.enabled && selected.Contains(light.GetInstanceID());
            LightShadows desired = keepShadow ? LightShadows.Soft : LightShadows.None;
            if (light.shadows != desired)
                light.shadows = desired;

            if (keepShadow)
            {
                // Lower bias keeps the shadow attached to thin modular walls.
                light.shadowBias = 0.02f;
                light.shadowNormalBias = 0.15f;
            }
        }
    }

    private static bool IsColoredRoomLight(Light light)
    {
        Color color = light.color;
        return light.type != LightType.Directional &&
               color.b > color.r * 1.2f &&
               color.g > color.r * 1.1f;
    }

    private void RestoreSceneLights()
    {
        foreach (Light light in sceneLights)
        {
            if (light != null && originalEnabled.TryGetValue(light.GetInstanceID(), out bool wasEnabled))
                light.enabled = wasEnabled;
        }
    }

    private void ClearTrackedLights()
    {
        RestoreSceneLights();
        sceneLights.Clear();
        originalEnabled.Clear();
        culled.Clear();
    }
}
