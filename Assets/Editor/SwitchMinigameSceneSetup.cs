using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
public static class SwitchMinigameSceneSetup
{
    private const string PlayerPrefabPath = "Assets/ModularFirstPersonController/FirstPersonController/FirstPersonController.prefab";

    [MenuItem("Tools/Switch Toggle Mission/Setup Active Scene")]
    public static void SetupActiveScene()
    {
        SceneSetupResult result = new SceneSetupResult();

        GameObject namedCanvasObject = GameObject.Find("Canvas");
        Canvas canvas = namedCanvasObject != null ? namedCanvasObject.GetComponent<Canvas>() : null;
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        TextMeshProUGUI promptText = EnsureText(canvas.transform, "PromptText");
        ConfigurePromptText(promptText);
        SwitchToggleMission.InteractionPromptUI promptUi = promptText.GetComponent<SwitchToggleMission.InteractionPromptUI>();
        if (promptUi == null)
        {
            promptUi = promptText.gameObject.AddComponent<SwitchToggleMission.InteractionPromptUI>();
        }

        promptUi.promptText = promptText;

        TextMeshProUGUI taskCompleteText = EnsureText(canvas.transform, "TaskCompleteText");
        ConfigureTaskCompleteText(taskCompleteText);

        GameObject panelObject = EnsureUiObject(canvas.transform, "MinigamePanel", typeof(Image), typeof(SwitchToggleMission.SwitchMinigameUI));
        ConfigurePanel(panelObject.GetComponent<RectTransform>(), panelObject.GetComponent<Image>());

        SwitchToggleMission.SwitchMinigameUI minigameUi = panelObject.GetComponent<SwitchToggleMission.SwitchMinigameUI>();
        minigameUi.minigamePanel = panelObject;
        minigameUi.taskCompleteText = taskCompleteText;

        TextMeshProUGUI titleText = EnsureText(panelObject.transform, "TitleText");
        ConfigureTitleText(titleText);

        TextMeshProUGUI statusText = EnsureText(panelObject.transform, "StatusText");
        ConfigureStatusText(statusText);
        minigameUi.statusText = statusText;

        SwitchToggleMission.SwitchToggle[] switches = new SwitchToggleMission.SwitchToggle[3];
        float[] yPositions = { 120f, 20f, -80f };

        for (int i = 0; i < switches.Length; i++)
        {
            GameObject switchRoot = EnsureUiObject(panelObject.transform, $"Switch_{i + 1}", typeof(Image), typeof(Button), typeof(SwitchToggleMission.SwitchToggle));
            ConfigureSwitchRoot(switchRoot.GetComponent<RectTransform>(), switchRoot.GetComponent<Image>(), yPositions[i]);

            Image backgroundImage = EnsureImage(switchRoot.transform, "Background");
            ConfigureSwitchBackground(backgroundImage.rectTransform, backgroundImage);

            Image knobImage = EnsureImage(backgroundImage.transform, "Knob");
            ConfigureSwitchKnob(knobImage.rectTransform, knobImage);

            SwitchToggleMission.SwitchToggle toggle = switchRoot.GetComponent<SwitchToggleMission.SwitchToggle>();
            toggle.knob = knobImage.rectTransform;
            toggle.backgroundImage = backgroundImage;
            toggle.offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            toggle.onColor = Color.green;
            toggle.knobOffX = -25f;
            toggle.knobOnX = 25f;
            toggle.slideDuration = 0.2f;

            Button button = switchRoot.GetComponent<Button>();
            button.targetGraphic = switchRoot.GetComponent<Image>();
            ResetPersistentListeners(button);
            UnityEventTools.AddPersistentListener(button.onClick, minigameUi.UpdateStatus);

            switches[i] = toggle;
        }

        minigameUi.switches = switches;

        GameObject closeButtonObject = EnsureUiObject(panelObject.transform, "CloseButton", typeof(Image), typeof(Button));
        ConfigureCloseButton(closeButtonObject.GetComponent<RectTransform>(), closeButtonObject.GetComponent<Image>());

        TextMeshProUGUI closeLabel = EnsureText(closeButtonObject.transform, "Label");
        ConfigureCloseLabel(closeLabel);

        Button closeButton = closeButtonObject.GetComponent<Button>();
        closeButton.targetGraphic = closeButtonObject.GetComponent<Image>();
        ResetPersistentListeners(closeButton);
        UnityEventTools.AddPersistentListener(closeButton.onClick, minigameUi.CloseMinigame);

        GameObject missionSystem = GameObject.Find("MissionSystem");
        if (missionSystem == null)
        {
            missionSystem = new GameObject("MissionSystem");
        }

        Transform switchMissionRoot = missionSystem.transform.Find("SwitchMissionObjects");
        if (switchMissionRoot == null)
        {
            GameObject rootObject = new GameObject("SwitchMissionObjects");
            rootObject.transform.SetParent(missionSystem.transform, false);
            switchMissionRoot = rootObject.transform;
        }

        EnsureInteractable(switchMissionRoot, "ToggleMission_1", new Vector3(27.5f, 3.9f, 3.6f), "Jeneratörü Çalıştır [F]");
        EnsureInteractable(switchMissionRoot, "ToggleMission_2", new Vector3(32.5f, 3.9f, 3.6f), "Sigortayı Değiştir [F]");

        EnsurePlayerRoleOnPrefab(result);

        promptText.gameObject.SetActive(false);
        taskCompleteText.gameObject.SetActive(false);
        panelObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"Switch toggle mission setup complete. {result.Message}");
    }

    private static void EnsurePlayerRoleOnPrefab(SceneSetupResult result)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (prefabRoot.GetComponent<SwitchToggleMission.PlayerRole>() == null)
            {
                prefabRoot.AddComponent<SwitchToggleMission.PlayerRole>();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
                result.Message = "Player prefab updated with SwitchToggleMission.PlayerRole.";
            }
            else
            {
                result.Message = "Player prefab already had SwitchToggleMission.PlayerRole.";
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void EnsureInteractable(Transform parent, string objectName, Vector3 position, string prompt)
    {
        Transform existing = parent.Find(objectName);
        GameObject interactableObject;

        if (existing != null)
        {
            interactableObject = existing.gameObject;
        }
        else
        {
            interactableObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            interactableObject.name = objectName;
            interactableObject.transform.SetParent(parent, true);
        }

        interactableObject.transform.position = position;
        interactableObject.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);

        MeshRenderer renderer = interactableObject.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            renderer.sharedMaterial.color = new Color(0.2f, 0.75f, 0.7f, 1f);
        }

        BoxCollider collider = interactableObject.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = interactableObject.AddComponent<BoxCollider>();
        }

        SwitchToggleMission.InteractableObject interactable = interactableObject.GetComponent<SwitchToggleMission.InteractableObject>();
        if (interactable == null)
        {
            interactable = interactableObject.AddComponent<SwitchToggleMission.InteractableObject>();
        }

        interactable.promptText = prompt;
        interactable.interactRange = 3f;
    }

    private static GameObject EnsureUiObject(Transform parent, string objectName, params System.Type[] extraComponents)
    {
        Transform existing = parent.Find(objectName);
        GameObject uiObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform));

        if (existing == null)
        {
            uiObject.transform.SetParent(parent, false);
        }

        for (int i = 0; i < extraComponents.Length; i++)
        {
            if (uiObject.GetComponent(extraComponents[i]) == null)
            {
                uiObject.AddComponent(extraComponents[i]);
            }
        }

        return uiObject;
    }

    private static TextMeshProUGUI EnsureText(Transform parent, string objectName)
    {
        GameObject textObject = EnsureUiObject(parent, objectName, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        text.raycastTarget = false;
        return text;
    }

    private static Image EnsureImage(Transform parent, string objectName)
    {
        GameObject imageObject = EnsureUiObject(parent, objectName, typeof(CanvasRenderer), typeof(Image));
        return imageObject.GetComponent<Image>();
    }

    private static void ConfigurePromptText(TextMeshProUGUI text)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 110f);
        rect.sizeDelta = new Vector2(700f, 70f);

        text.text = string.Empty;
        text.fontSize = 32f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void ConfigureTaskCompleteText(TextMeshProUGUI text)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 220f);
        rect.sizeDelta = new Vector2(900f, 120f);

        text.text = "Görev Tamamlandı!";
        text.fontSize = 48f;
        text.color = Color.green;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void ConfigurePanel(RectTransform rect, Image image)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(720f, 500f);

        image.color = new Color(0f, 0f, 0f, 0.78f);
    }

    private static void ConfigureTitleText(TextMeshProUGUI text)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -38f);
        rect.sizeDelta = new Vector2(520f, 70f);

        text.text = "Sistemi Aktive Et";
        text.fontSize = 34f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void ConfigureStatusText(TextMeshProUGUI text)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 85f);
        rect.sizeDelta = new Vector2(420f, 60f);

        text.text = "Aktif: 0 / 3";
        text.fontSize = 28f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void ConfigureSwitchRoot(RectTransform rect, Image image, float yPosition)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yPosition);
        rect.sizeDelta = new Vector2(180f, 70f);

        image.color = new Color(1f, 1f, 1f, 0.02f);
    }

    private static void ConfigureSwitchBackground(RectTransform rect, Image image)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(140f, 46f);

        image.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        image.raycastTarget = false;
    }

    private static void ConfigureSwitchKnob(RectTransform rect, Image image)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-25f, 0f);
        rect.sizeDelta = new Vector2(42f, 42f);

        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static void ConfigureCloseButton(RectTransform rect, Image image)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 35f);
        rect.sizeDelta = new Vector2(220f, 56f);

        image.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);
    }

    private static void ConfigureCloseLabel(TextMeshProUGUI text)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        text.text = "Kapat";
        text.fontSize = 24f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void ResetPersistentListeners(Button button)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }
    }

    private sealed class SceneSetupResult
    {
        public string Message = "Scene setup refreshed.";
    }
}
