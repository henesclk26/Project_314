using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Ana menü ve ESC menüsündeki ortak ayar kontrollerini doldurur.
/// Panelin açılıp kapanması menü controller'larında, kontrol değerlerinin
/// uygulanması burada tutulur.
/// </summary>
public static class GameSettingsUI
{
    private sealed class SettingsBinding
    {
        public SliderInt FpsSlider;
        public Label FpsLabel;
        public DropdownField ResolutionDropdown;
        public DropdownField QualityDropdown;
        public Toggle VSyncToggle;
        public Button SaveButton;
        public PendingSettings Pending;
        public EventCallback<ChangeEvent<int>> FpsCallback;
        public EventCallback<ChangeEvent<string>> ResolutionCallback;
        public EventCallback<ChangeEvent<string>> QualityCallback;
        public EventCallback<ChangeEvent<bool>> VSyncCallback;
        public int SaveFeedbackToken;
    }

    private struct PendingSettings
    {
        public int FpsLimit;
        public int ResolutionWidth;
        public int ResolutionHeight;
        public GamePerformanceSettings.QualityPreset Quality;
        public bool VSyncEnabled;
    }

    private static readonly List<string> QualityChoices = new() { "LOW", "MEDIUM", "HIGH" };
    private static readonly Dictionary<VisualElement, SettingsBinding> Bindings = new();

    public static void ConfigureControls(VisualElement root)
    {
        if (root == null)
            return;

        if (!Bindings.TryGetValue(root, out SettingsBinding binding))
        {
            binding = new SettingsBinding();
            Bindings[root] = binding;
        }

        UnregisterCallbacks(binding);

        binding.FpsSlider = root.Q<SliderInt>("settings-fps-slider");
        binding.FpsLabel = root.Q<Label>("settings-fps-value");
        binding.ResolutionDropdown = root.Q<DropdownField>("settings-resolution-dropdown");
        binding.QualityDropdown = root.Q<DropdownField>("settings-quality-dropdown");
        binding.VSyncToggle = root.Q<Toggle>("settings-vsync-toggle");
        binding.SaveButton = root.Q<Button>("settings-save");
        binding.Pending = ReadCurrentSettings();

        if (binding.FpsSlider != null)
        {
            binding.FpsSlider.lowValue = GamePerformanceSettings.MinFpsLimit;
            binding.FpsSlider.highValue = GamePerformanceSettings.SliderUnlimitedValue;
            binding.FpsCallback = changeEvent => OnFpsChanged(binding, changeEvent);
            binding.FpsSlider.RegisterValueChangedCallback(binding.FpsCallback);
        }

        if (binding.QualityDropdown != null)
        {
            binding.QualityDropdown.choices = new List<string>(QualityChoices);
            binding.QualityCallback = changeEvent => OnQualityChanged(binding, changeEvent);
            binding.QualityDropdown.RegisterValueChangedCallback(binding.QualityCallback);
        }

        if (binding.ResolutionDropdown != null)
        {
            List<GamePerformanceSettings.DisplayResolution> resolutions =
                GamePerformanceSettings.GetAvailableResolutions();
            List<string> choices = new List<string>(resolutions.Count);
            for (int i = 0; i < resolutions.Count; i++)
                choices.Add(resolutions[i].Label);

            binding.ResolutionDropdown.choices = choices;
            binding.ResolutionCallback = changeEvent => OnResolutionChanged(binding, changeEvent);
            binding.ResolutionDropdown.RegisterValueChangedCallback(binding.ResolutionCallback);
        }

        if (binding.VSyncToggle != null)
        {
            binding.VSyncCallback = changeEvent => OnVSyncChanged(binding, changeEvent);
            binding.VSyncToggle.RegisterValueChangedCallback(binding.VSyncCallback);
        }

        RenderPendingSettings(binding);
    }

    public static void BeginEdit(VisualElement root)
    {
        if (root == null)
            return;

        if (!Bindings.TryGetValue(root, out SettingsBinding binding))
        {
            ConfigureControls(root);
            binding = Bindings[root];
        }

        binding.Pending = ReadCurrentSettings();
        ResetSaveFeedback(binding);
        RenderPendingSettings(binding);
    }

    public static void Save(VisualElement root)
    {
        if (root == null || !Bindings.TryGetValue(root, out SettingsBinding binding))
            return;

        int saveToken = ++binding.SaveFeedbackToken;
        if (binding.SaveButton != null)
        {
            binding.SaveButton.RemoveFromClassList("settings-save-complete");
            binding.SaveButton.AddToClassList("settings-save-saving");
        }

        GamePerformanceSettings.ApplyAndSave(
            binding.Pending.FpsLimit,
            binding.Pending.ResolutionWidth,
            binding.Pending.ResolutionHeight,
            binding.Pending.Quality,
            binding.Pending.VSyncEnabled);
        binding.Pending = ReadCurrentSettings();
        RenderPendingSettings(binding);

        if (binding.SaveButton != null)
        {
            binding.SaveButton.schedule.Execute(() =>
            {
                if (Bindings.TryGetValue(root, out SettingsBinding currentBinding) &&
                    ReferenceEquals(currentBinding, binding) &&
                    currentBinding.SaveFeedbackToken == saveToken &&
                    currentBinding.SaveButton != null)
                {
                    currentBinding.SaveButton.RemoveFromClassList("settings-save-saving");
                    currentBinding.SaveButton.AddToClassList("settings-save-complete");
                }
            }).StartingIn(250);
        }
    }

    public static void Cancel(VisualElement root)
    {
        if (root == null || !Bindings.TryGetValue(root, out SettingsBinding binding))
            return;

        binding.Pending = ReadCurrentSettings();
        ResetSaveFeedback(binding);
        RenderPendingSettings(binding);
    }

    private static void OnFpsChanged(SettingsBinding binding, ChangeEvent<int> changeEvent)
    {
        MarkDirty(binding);
        binding.Pending.FpsLimit = GamePerformanceSettings.SliderValueToFps(changeEvent.newValue);

        if (binding.FpsLabel != null)
            binding.FpsLabel.text = GamePerformanceSettings.FpsToDisplayLabel(binding.Pending.FpsLimit);
    }

    private static void OnQualityChanged(SettingsBinding binding, ChangeEvent<string> changeEvent)
    {
        MarkDirty(binding);
        string value = changeEvent.newValue ?? string.Empty;
        binding.Pending.Quality = value == "LOW"
            ? GamePerformanceSettings.QualityPreset.Low
            : value == "HIGH"
                ? GamePerformanceSettings.QualityPreset.High
                : GamePerformanceSettings.QualityPreset.Medium;
    }

    private static void OnResolutionChanged(SettingsBinding binding, ChangeEvent<string> changeEvent)
    {
        MarkDirty(binding);
        if (GamePerformanceSettings.TryParseResolutionLabel(
                changeEvent.newValue,
                out int width,
                out int height))
        {
            binding.Pending.ResolutionWidth = width;
            binding.Pending.ResolutionHeight = height;
        }
    }

    private static void OnVSyncChanged(SettingsBinding binding, ChangeEvent<bool> changeEvent)
    {
        MarkDirty(binding);
        binding.Pending.VSyncEnabled = changeEvent.newValue;
    }

    private static void MarkDirty(SettingsBinding binding)
    {
        binding.SaveFeedbackToken++;
        if (binding.SaveButton != null)
        {
            binding.SaveButton.RemoveFromClassList("settings-save-saving");
            binding.SaveButton.RemoveFromClassList("settings-save-complete");
        }
    }

    private static void ResetSaveFeedback(SettingsBinding binding)
    {
        binding.SaveFeedbackToken++;
        if (binding.SaveButton != null)
        {
            binding.SaveButton.RemoveFromClassList("settings-save-saving");
            binding.SaveButton.RemoveFromClassList("settings-save-complete");
        }
    }

    private static PendingSettings ReadCurrentSettings()
    {
        return new PendingSettings
        {
            FpsLimit = GamePerformanceSettings.FpsLimit,
            ResolutionWidth = GamePerformanceSettings.ResolutionWidth,
            ResolutionHeight = GamePerformanceSettings.ResolutionHeight,
            Quality = GamePerformanceSettings.Quality,
            VSyncEnabled = GamePerformanceSettings.VSyncEnabled
        };
    }

    private static void RenderPendingSettings(SettingsBinding binding)
    {
        if (binding.FpsSlider != null)
            binding.FpsSlider.SetValueWithoutNotify(
                binding.Pending.FpsLimit == GamePerformanceSettings.UnlimitedFpsLimit
                    ? GamePerformanceSettings.SliderUnlimitedValue
                    : Mathf.Clamp(binding.Pending.FpsLimit,
                        GamePerformanceSettings.MinFpsLimit,
                        GamePerformanceSettings.MaxNumericFpsLimit));

        if (binding.FpsLabel != null)
            binding.FpsLabel.text = GamePerformanceSettings.FpsToDisplayLabel(binding.Pending.FpsLimit);

        if (binding.QualityDropdown != null)
            binding.QualityDropdown.SetValueWithoutNotify(QualityToLabel(binding.Pending.Quality));

        if (binding.ResolutionDropdown != null)
            binding.ResolutionDropdown.SetValueWithoutNotify(
                GamePerformanceSettings.GetResolutionLabel(
                    binding.Pending.ResolutionWidth,
                    binding.Pending.ResolutionHeight));

        if (binding.VSyncToggle != null)
            binding.VSyncToggle.SetValueWithoutNotify(binding.Pending.VSyncEnabled);
    }

    private static void UnregisterCallbacks(SettingsBinding binding)
    {
        if (binding.FpsSlider != null && binding.FpsCallback != null)
            binding.FpsSlider.UnregisterValueChangedCallback(binding.FpsCallback);
        if (binding.QualityDropdown != null && binding.QualityCallback != null)
            binding.QualityDropdown.UnregisterValueChangedCallback(binding.QualityCallback);
        if (binding.ResolutionDropdown != null && binding.ResolutionCallback != null)
            binding.ResolutionDropdown.UnregisterValueChangedCallback(binding.ResolutionCallback);
        if (binding.VSyncToggle != null && binding.VSyncCallback != null)
            binding.VSyncToggle.UnregisterValueChangedCallback(binding.VSyncCallback);
    }

    private static string QualityToLabel(GamePerformanceSettings.QualityPreset quality)
    {
        return quality switch
        {
            GamePerformanceSettings.QualityPreset.Low => "LOW",
            GamePerformanceSettings.QualityPreset.High => "HIGH",
            _ => "MEDIUM"
        };
    }
}
