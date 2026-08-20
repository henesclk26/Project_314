using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Oyuncunun performans tercihlerini uygular ve yerel olarak saklar.
/// Oyun mekaniğini değiştirmeden FPS, VSync ve grafik kalite seviyesini yönetir.
/// </summary>
public static class GamePerformanceSettings
{
    public enum FramePacingContext
    {
        Gameplay,
        Menu,
        Background
    }

    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public readonly struct DisplayResolution
    {
        public DisplayResolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
        public string Label => $"{Width} × {Height}";
    }

    public const int MinFpsLimit = 1;
    public const int MaxNumericFpsLimit = 999;
    public const int SliderUnlimitedValue = 1000;
    public const int UnlimitedFpsLimit = -1;
    public const int DefaultFpsLimit = 90;
    public const int DefaultResolutionWidth = 1920;
    public const int DefaultResolutionHeight = 1080;
    public const QualityPreset DefaultQuality = QualityPreset.Medium;
    public const bool DefaultVSync = true;
    public const int MenuFrameRateCap = 60;
    public const int BackgroundFrameRateCap = 15;

    private const string FpsLimitKey = "Project314.Settings.FpsLimit";
    private const string ResolutionWidthKey = "Project314.Settings.ResolutionWidth";
    private const string ResolutionHeightKey = "Project314.Settings.ResolutionHeight";
    private const string QualityKey = "Project314.Settings.Quality";
    private const string VSyncKey = "Project314.Settings.VSync";
    private const string SettingsVersionKey = "Project314.Settings.Version";
    private const int CurrentSettingsVersion = 6;

    private static readonly Dictionary<int, LightShadows> OriginalLightShadows =
        new Dictionary<int, LightShadows>();

    public static int FpsLimit { get; private set; } = DefaultFpsLimit;
    public static int ResolutionWidth { get; private set; } = DefaultResolutionWidth;
    public static int ResolutionHeight { get; private set; } = DefaultResolutionHeight;
    public static QualityPreset Quality { get; private set; } = DefaultQuality;
    public static bool VSyncEnabled { get; private set; } = DefaultVSync;
    public static FramePacingContext CurrentFramePacingContext { get; private set; } = FramePacingContext.Menu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeSceneLoad()
    {
        LoadAndApply();
    }

    public static void LoadAndApply()
    {
        int settingsVersion = PlayerPrefs.GetInt(SettingsVersionKey, 0);
        int storedFpsLimit = PlayerPrefs.GetInt(FpsLimitKey, DefaultFpsLimit);
        DisplayResolution defaultResolution = GetDefaultResolution();
        int storedResolutionWidth = PlayerPrefs.GetInt(ResolutionWidthKey, defaultResolution.Width);
        int storedResolutionHeight = PlayerPrefs.GetInt(ResolutionHeightKey, defaultResolution.Height);

        // Version 4 shipped with a 60 FPS default. Move only that old default
        // to 90 so a player's explicit FPS choice is not overwritten.
        bool migratedDefaultFps = settingsVersion < CurrentSettingsVersion && storedFpsLimit == 60;
        if (migratedDefaultFps)
            storedFpsLimit = DefaultFpsLimit;

        // Resolution selection was introduced in version 5. The first real
        // resolution release starts at 1080p; later user selections remain
        // untouched because this migration only runs once for version 5.
        bool migratedDefaultResolution = settingsVersion == 5;
        if (migratedDefaultResolution)
        {
            storedResolutionWidth = DefaultResolutionWidth;
            storedResolutionHeight = DefaultResolutionHeight;
        }

        FpsLimit = NormalizeFps(storedFpsLimit);
        DisplayResolution normalizedResolution = NormalizeResolution(storedResolutionWidth, storedResolutionHeight);
        ResolutionWidth = normalizedResolution.Width;
        ResolutionHeight = normalizedResolution.Height;
        Quality = NormalizeQuality(PlayerPrefs.GetInt(QualityKey, (int)DefaultQuality));
        VSyncEnabled = PlayerPrefs.GetInt(VSyncKey, DefaultVSync ? 1 : 0) != 0;

        if (settingsVersion < CurrentSettingsVersion)
        {
            PlayerPrefs.SetInt(SettingsVersionKey, CurrentSettingsVersion);
            if (migratedDefaultFps)
                PlayerPrefs.SetInt(FpsLimitKey, FpsLimit);
            PlayerPrefs.SetInt(ResolutionWidthKey, ResolutionWidth);
            PlayerPrefs.SetInt(ResolutionHeightKey, ResolutionHeight);
            PlayerPrefs.Save();
        }

        ApplyCurrentSettings();
    }

    public static void SetFpsLimit(int fpsLimit)
    {
        ApplyAndSave(fpsLimit, Quality, VSyncEnabled);
    }

    public static void SetQuality(QualityPreset quality)
    {
        ApplyAndSave(FpsLimit, quality, VSyncEnabled);
    }

    public static void SetVSync(bool enabled)
    {
        ApplyAndSave(FpsLimit, Quality, enabled);
    }

    public static void ApplyAndSave(int fpsLimit, QualityPreset quality, bool vsyncEnabled)
    {
        ApplyAndSave(fpsLimit, ResolutionWidth, ResolutionHeight, quality, vsyncEnabled);
    }

    public static void ApplyAndSave(
        int fpsLimit,
        int resolutionWidth,
        int resolutionHeight,
        QualityPreset quality,
        bool vsyncEnabled)
    {
        FpsLimit = NormalizeFps(fpsLimit);
        DisplayResolution normalizedResolution = NormalizeResolution(resolutionWidth, resolutionHeight);
        ResolutionWidth = normalizedResolution.Width;
        ResolutionHeight = normalizedResolution.Height;
        Quality = NormalizeQuality((int)quality);
        VSyncEnabled = vsyncEnabled;

        PlayerPrefs.SetInt(FpsLimitKey, FpsLimit);
        PlayerPrefs.SetInt(ResolutionWidthKey, ResolutionWidth);
        PlayerPrefs.SetInt(ResolutionHeightKey, ResolutionHeight);
        PlayerPrefs.SetInt(QualityKey, (int)Quality);
        PlayerPrefs.SetInt(VSyncKey, VSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(SettingsVersionKey, CurrentSettingsVersion);
        PlayerPrefs.Save();
        ApplyCurrentSettings();
    }

    public static void ApplyCurrentSettings()
    {
        ApplyResolution();
        ApplyQualityPreset(Quality);
        ApplyFramePacing();
    }

    public static List<DisplayResolution> GetAvailableResolutions()
    {
        List<DisplayResolution> options = new List<DisplayResolution>();
        HashSet<long> seen = new HashSet<long>();

        Resolution[] supported = Screen.resolutions;
        for (int i = 0; i < supported.Length; i++)
            AddResolutionOption(options, seen, supported[i].width, supported[i].height);

        // Windowed builds can use a size that is not listed as a fullscreen
        // display mode. Keep the current size selectable when that happens.
        AddResolutionOption(options, seen, Screen.width, Screen.height);

        // Keep the standard 2K option visible even when the current display
        // driver does not report it in Screen.resolutions.
        AddResolutionOption(options, seen, 2560, 1440);

        // Keep the UI usable in the editor or on platforms that do not expose
        // display modes through Screen.resolutions.
        if (options.Count == 0)
        {
            AddResolutionOption(options, seen, 1280, 720);
            AddResolutionOption(options, seen, 1600, 900);
            AddResolutionOption(options, seen, 1920, 1080);
            AddResolutionOption(options, seen, 2560, 1440);
        }

        options.Sort((left, right) =>
        {
            int widthComparison = left.Width.CompareTo(right.Width);
            return widthComparison != 0 ? widthComparison : left.Height.CompareTo(right.Height);
        });
        return options;
    }

    public static string GetResolutionLabel(int width, int height)
    {
        return $"{width} × {height}";
    }

    public static bool TryParseResolutionLabel(string label, out int width, out int height)
    {
        List<DisplayResolution> options = GetAvailableResolutions();
        for (int i = 0; i < options.Count; i++)
        {
            if (!string.Equals(options[i].Label, label, StringComparison.Ordinal))
                continue;

            width = options[i].Width;
            height = options[i].Height;
            return true;
        }

        width = ResolutionWidth;
        height = ResolutionHeight;
        return false;
    }

    public static void SetFramePacingContext(FramePacingContext context)
    {
        if (CurrentFramePacingContext == context)
            return;

        CurrentFramePacingContext = context;
        ApplyFramePacing();
    }

    private static void ApplyFramePacing()
    {
        bool isGameplay = CurrentFramePacingContext == FramePacingContext.Gameplay;
        bool isBackground = CurrentFramePacingContext == FramePacingContext.Background;
        int effectiveFps = FpsLimit;

        if (isBackground)
        {
            effectiveFps = BackgroundFrameRateCap;
        }
        else if (!isGameplay)
        {
            // Menus and lobbies should not keep a high-refresh GPU busy.
            effectiveFps = FpsLimit == UnlimitedFpsLimit
                ? MenuFrameRateCap
                : Mathf.Min(FpsLimit, MenuFrameRateCap);
        }

        bool hasExplicitGameplayCap = isGameplay && FpsLimit != UnlimitedFpsLimit;
        Application.targetFrameRate = effectiveFps == UnlimitedFpsLimit ? -1 : effectiveFps;

        // Unity ignores targetFrameRate while VSync is active. The FPS slider
        // must therefore win for numeric limits; otherwise a 144/165/240 Hz
        // monitor can drive the GPU far above the selected FPS cap.
        QualitySettings.vSyncCount = VSyncEnabled && isGameplay && !isBackground &&
                                     !hasExplicitGameplayCap ? 1 : 0;
    }

    private static void ApplyResolution()
    {
        if (ResolutionWidth <= 0 || ResolutionHeight <= 0)
            return;

        if (Screen.width != ResolutionWidth || Screen.height != ResolutionHeight)
            Screen.SetResolution(ResolutionWidth, ResolutionHeight, Screen.fullScreenMode);
    }

    private static DisplayResolution GetDefaultResolution()
    {
        return NormalizeResolution(DefaultResolutionWidth, DefaultResolutionHeight);
    }

    private static DisplayResolution NormalizeResolution(int width, int height)
    {
        List<DisplayResolution> options = GetAvailableResolutions();
        if (options.Count == 0)
            return new DisplayResolution(
                width > 0 ? width : DefaultResolutionWidth,
                height > 0 ? height : DefaultResolutionHeight);

        DisplayResolution best = options[0];
        long bestDistance = long.MaxValue;
        for (int i = 0; i < options.Count; i++)
        {
            long widthDistance = options[i].Width - (long)width;
            long heightDistance = options[i].Height - (long)height;
            long distance = widthDistance * widthDistance + heightDistance * heightDistance;
            if (distance < bestDistance)
            {
                best = options[i];
                bestDistance = distance;
            }
        }

        return best;
    }

    private static void AddResolutionOption(
        List<DisplayResolution> options,
        HashSet<long> seen,
        int width,
        int height)
    {
        if (width < 640 || height < 360)
            return;

        long key = ((long)width << 32) | (uint)height;
        if (seen.Add(key))
            options.Add(new DisplayResolution(width, height));
    }

    private static void ApplyQualityPreset(QualityPreset preset)
    {
        float renderScale;

        switch (preset)
        {
            case QualityPreset.Low:
                QualitySettings.shadowDistance = 18f;
                QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Low;
                QualitySettings.shadowCascades = 0;
                QualitySettings.lodBias = 0.75f;
                QualitySettings.maximumLODLevel = 1;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = false;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.antiAliasing = 0;
                QualitySettings.globalTextureMipmapLimit = 2;
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMemoryBudget = 256f;
                QualitySettings.streamingMipmapsRenderersPerFrame = 128;
                renderScale = 0.75f;
                break;

            case QualityPreset.High:
                QualitySettings.shadowDistance = 40f;
                QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
                QualitySettings.shadowCascades = 2;
                QualitySettings.lodBias = 2f;
                QualitySettings.maximumLODLevel = 0;
                QualitySettings.realtimeReflectionProbes = true;
                QualitySettings.softParticles = true;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.antiAliasing = 4;
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMemoryBudget = 768f;
                QualitySettings.streamingMipmapsRenderersPerFrame = 512;
                renderScale = 1f;
                break;

            default:
                QualitySettings.shadowDistance = 30f;
                QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
                QualitySettings.shadowCascades = 2;
                QualitySettings.lodBias = 1f;
                QualitySettings.maximumLODLevel = 0;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = true;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.antiAliasing = 2;
                QualitySettings.globalTextureMipmapLimit = 1;
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMemoryBudget = 512f;
                QualitySettings.streamingMipmapsRenderersPerFrame = 256;
                renderScale = 0.9f;
                break;
        }

        GraphicsSettings.useScriptableRenderPipelineBatching = true;

        UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.renderScale = renderScale;
            urp.msaaSampleCount = preset == QualityPreset.Low ? 1 : preset == QualityPreset.High ? 4 : 2;
            urp.supportsHDR = preset != QualityPreset.Low;
            // No project shader samples _CameraOpaqueTexture. Avoid the full
            // screen color copy on Low/Medium while retaining it for High.
            urp.supportsCameraOpaqueTexture = preset == QualityPreset.High;
            urp.supportsCameraDepthTexture = true;
            urp.maxAdditionalLightsCount = preset == QualityPreset.Low ? 2 : preset == QualityPreset.High ? 8 : 4;
            urp.shadowDistance = preset == QualityPreset.Low ? 18f : preset == QualityPreset.High ? 40f : 30f;
            urp.shadowCascadeCount = preset == QualityPreset.Low ? 1 : 2;
        }

        ApplySceneLightingPreset(preset);

        ApplyPostProcessingPreset(preset);

        // URP destekli donanımlarda çözünürlük ölçeğini düşürerek GPU yükünü azaltır.
        ScalableBufferManager.ResizeBuffers(renderScale, renderScale);
    }

    private static void ApplySceneLightingPreset(QualityPreset preset)
    {
        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (Light light in lights)
        {
            if (light == null)
                continue;

            int instanceId = light.GetInstanceID();
            if (!OriginalLightShadows.ContainsKey(instanceId))
                OriginalLightShadows[instanceId] = light.shadows;
        }

        if (preset == QualityPreset.High)
        {
            foreach (Light light in lights)
            {
                if (light != null && OriginalLightShadows.TryGetValue(light.GetInstanceID(), out LightShadows original))
                    light.shadows = original;
            }
            return;
        }

        int localShadowLightBudget = preset == QualityPreset.Medium ? 2 : 0;
        HashSet<int> selectedLocalShadowLights = lights
            .Where(light => light != null && light.type != LightType.Directional &&
                            OriginalLightShadows.TryGetValue(light.GetInstanceID(), out LightShadows original) &&
                            original != LightShadows.None)
            .OrderByDescending(light => light.intensity)
            .Take(localShadowLightBudget)
            .Select(light => light.GetInstanceID())
            .ToHashSet();

        foreach (Light light in lights)
        {
            if (light == null || !OriginalLightShadows.TryGetValue(light.GetInstanceID(), out LightShadows original))
                continue;

            bool keepDirectionalShadow = light.type == LightType.Directional && preset == QualityPreset.Medium;
            bool keepLocalShadow = selectedLocalShadowLights.Contains(light.GetInstanceID());
            light.shadows = keepDirectionalShadow || keepLocalShadow ? original : LightShadows.None;
        }
    }

    private static void ApplyPostProcessingPreset(QualityPreset preset)
    {
        if (!VolumeManager.instance.isInitialized || VolumeManager.instance.stack == null)
            return;

        VolumeStack stack = VolumeManager.instance.stack;
        Bloom bloom = stack.GetComponent<Bloom>();
        MotionBlur motionBlur = stack.GetComponent<MotionBlur>();
        Vignette vignette = stack.GetComponent<Vignette>();
        DepthOfField depthOfField = stack.GetComponent<DepthOfField>();
        ChromaticAberration chromaticAberration = stack.GetComponent<ChromaticAberration>();

        if (bloom != null)
            bloom.active = preset != QualityPreset.Low;
        if (motionBlur != null)
            motionBlur.active = preset == QualityPreset.High;
        if (vignette != null)
            vignette.active = preset != QualityPreset.Low;
        if (depthOfField != null)
            depthOfField.active = false;
        if (chromaticAberration != null)
            chromaticAberration.active = preset == QualityPreset.High;
    }

    public static int FpsToSliderValue()
    {
        return FpsLimit == UnlimitedFpsLimit
            ? SliderUnlimitedValue
            : Mathf.Clamp(FpsLimit, MinFpsLimit, MaxNumericFpsLimit);
    }

    public static int SliderValueToFps(int sliderValue)
    {
        return sliderValue >= SliderUnlimitedValue
            ? UnlimitedFpsLimit
            : Mathf.Clamp(sliderValue, MinFpsLimit, MaxNumericFpsLimit);
    }

    public static string FpsToDisplayLabel(int fpsLimit)
    {
        return fpsLimit == UnlimitedFpsLimit
            ? "UNLIMITED"
            : $"{Mathf.Clamp(fpsLimit, MinFpsLimit, MaxNumericFpsLimit)} FPS";
    }

    private static int NormalizeFps(int fpsLimit)
    {
        return fpsLimit == UnlimitedFpsLimit
            ? UnlimitedFpsLimit
            : Mathf.Clamp(fpsLimit, MinFpsLimit, MaxNumericFpsLimit);
    }

    private static QualityPreset NormalizeQuality(int quality)
    {
        return quality <= (int)QualityPreset.Low
            ? QualityPreset.Low
            : quality >= (int)QualityPreset.High
                ? QualityPreset.High
                : QualityPreset.Medium;
    }
}
