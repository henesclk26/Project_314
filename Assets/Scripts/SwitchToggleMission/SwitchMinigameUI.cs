using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class SwitchMinigameUI : MonoBehaviour
    {
        public static SwitchMinigameUI Instance { get; private set; }

        public GameObject minigamePanel;
        public SwitchToggle[] switches;
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI taskCompleteText;

        private InteractableObject currentObject;
        private FirstPersonController cachedPlayerController;
        private bool cachedCameraCanMove;
        private bool cachedPlayerCanMove;
        private bool hasCachedCameraState;

        public static SwitchMinigameUI EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            SwitchMinigameUI existing = FindFirstObjectByType<SwitchMinigameUI>();
            if (existing != null)
            {
                existing.EnsureUi();
                Instance = existing;
                return existing;
            }

            GameObject root = new GameObject("SwitchMinigameUI");
            SwitchMinigameUI created = root.AddComponent<SwitchMinigameUI>();
            created.EnsureUi();
            Instance = created;
            return created;
        }

        public static bool HasOpenInstance()
        {
            return Instance != null && Instance.minigamePanel != null && Instance.minigamePanel.activeSelf;
        }

        public static Transform GetOrCreateCanvasRoot()
        {
            Canvas existingCanvas = GameObject.Find("Canvas") != null
                ? GameObject.Find("Canvas").GetComponent<Canvas>()
                : FindFirstObjectByType<Canvas>();

            if (existingCanvas != null)
            {
                return existingCanvas.transform;
            }

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return canvasObject.transform;
        }

        public static TMP_FontAsset GetDefaultFont()
        {
            return TMP_Settings.defaultFontAsset != null
                ? TMP_Settings.defaultFontAsset
                : Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureUi();
            SetPanelActive(false);
            SetTaskCompleteVisible(false);
        }

        private void OnDisable()
        {
            ReleaseCurrentObject();
        }

        private void OnDestroy()
        {
            CancelInvoke();
            ReleaseCurrentObject();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OpenMinigame(InteractableObject source)
        {
            EnsureUi();
            CancelInvoke();

            currentObject = source;
            ResetAllSwitches();
            SetPanelActive(true);
            SetTaskCompleteVisible(false);
            SetPlayerCameraLocked(true);
            SetCursorState(true);
            UpdateStatus();
        }

        public void CloseMinigame()
        {
            CancelInvoke(nameof(CloseMinigame));
            SetPanelActive(false);
            ReleaseCurrentObject();
            SetPlayerCameraLocked(false);
            SetCursorState(false);
        }

        public void UpdateStatus()
        {
            int total = switches != null ? switches.Length : 0;
            int activeCount = 0;

            if (switches != null)
            {
                for (int i = 0; i < switches.Length; i++)
                {
                    if (switches[i] != null && switches[i].IsOn)
                    {
                        activeCount++;
                    }
                }
            }

            if (statusText != null)
            {
                statusText.color = Color.white;
                statusText.text = $"Aktif: {activeCount} / {total}";
            }

            if (total > 0 && activeCount == total)
            {
                if (statusText != null)
                {
                    statusText.text = "✓ Sistem Aktif!";
                    statusText.color = Color.green;
                }

                ShowTaskComplete();
                if (GameManager.Instance != null && Unity.Netcode.NetworkManager.Singleton != null)
                {
                    GameManager.Instance.CompleteMissionServerRpc(Unity.Netcode.NetworkManager.Singleton.LocalClientId, "Switch");
                }
                CancelInvoke(nameof(CloseMinigame));
                Invoke(nameof(CloseMinigame), 1.5f);
            }
        }

        private void EnsureUi()
        {
            if (minigamePanel != null && switches != null && switches.Length == 3 && statusText != null && taskCompleteText != null)
            {
                return;
            }

            Transform canvas = GetOrCreateCanvasRoot();
            TMP_FontAsset font = GetDefaultFont();

            taskCompleteText = GetOrCreateText(canvas, "TaskCompleteText", font);
            ConfigureTaskCompleteText(taskCompleteText.rectTransform, taskCompleteText);

            minigamePanel = GetOrCreateUiObject(canvas, "MinigamePanel", typeof(CanvasRenderer), typeof(Image));
            Image panelImage = minigamePanel.GetComponent<Image>();
            ConfigurePanel(minigamePanel.GetComponent<RectTransform>(), panelImage);

            GetOrCreateText(minigamePanel.transform, "TitleText", font).text = "Sistemi Aktive Et";
            ConfigureTitleText(GetText("TitleText"));

            statusText = GetOrCreateText(minigamePanel.transform, "StatusText", font);
            ConfigureStatusText(statusText.rectTransform, statusText);

            switches = new SwitchToggle[3];
            float[] yOffsets = { 110f, 20f, -70f };

            for (int i = 0; i < switches.Length; i++)
            {
                GameObject switchRoot = GetOrCreateUiObject(minigamePanel.transform, $"Switch_{i + 1}", typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(SwitchToggle));
                ConfigureSwitchRoot(switchRoot.GetComponent<RectTransform>(), switchRoot.GetComponent<Image>(), yOffsets[i]);

                Image background = GetOrCreateImage(switchRoot.transform, "Background");
                ConfigureBackground(background.rectTransform, background);

                Image knob = GetOrCreateImage(background.transform, "Knob");
                ConfigureKnob(knob.rectTransform, knob);

                SwitchToggle toggle = switchRoot.GetComponent<SwitchToggle>();
                toggle.knob = knob.rectTransform;
                toggle.backgroundImage = background;

                Button button = switchRoot.GetComponent<Button>();
                button.targetGraphic = switchRoot.GetComponent<Image>();
                button.onClick.RemoveAllListeners();

                switches[i] = toggle;
            }

            GameObject closeButtonObject = GetOrCreateUiObject(minigamePanel.transform, "CloseButton", typeof(CanvasRenderer), typeof(Image), typeof(Button));
            ConfigureCloseButton(closeButtonObject.GetComponent<RectTransform>(), closeButtonObject.GetComponent<Image>());

            TextMeshProUGUI closeLabel = GetOrCreateText(closeButtonObject.transform, "Label", font);
            ConfigureCloseLabel(closeLabel.rectTransform, closeLabel);

            Button closeButton = closeButtonObject.GetComponent<Button>();
            closeButton.targetGraphic = closeButtonObject.GetComponent<Image>();
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseMinigame);

            SetPanelActive(false);
            SetTaskCompleteVisible(false);
        }

        private TextMeshProUGUI GetText(string name)
        {
            return minigamePanel.transform.Find(name).GetComponent<TextMeshProUGUI>();
        }

        private static GameObject GetOrCreateUiObject(Transform parent, string name, params System.Type[] extraComponents)
        {
            Transform existing = parent.Find(name);
            GameObject uiObject = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform));

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

        private static TextMeshProUGUI GetOrCreateText(Transform parent, string name, TMP_FontAsset font)
        {
            GameObject textObject = GetOrCreateUiObject(parent, name, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.raycastTarget = false;
            return text;
        }

        private static Image GetOrCreateImage(Transform parent, string name)
        {
            GameObject imageObject = GetOrCreateUiObject(parent, name, typeof(CanvasRenderer), typeof(Image));
            return imageObject.GetComponent<Image>();
        }

        private void ResetAllSwitches()
        {
            if (switches == null)
            {
                return;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] != null)
                {
                    switches[i].ResetSwitch();
                }
            }
        }

        private void ShowTaskComplete()
        {
            if (taskCompleteText == null)
            {
                return;
            }

            taskCompleteText.text = "Görev Tamamlandı!";
            taskCompleteText.color = Color.green;
            taskCompleteText.gameObject.SetActive(true);

            CancelInvoke(nameof(HideTaskComplete));
            Invoke(nameof(HideTaskComplete), 2f);
        }

        private void HideTaskComplete()
        {
            SetTaskCompleteVisible(false);
        }

        private void SetPanelActive(bool isActive)
        {
            if (minigamePanel != null)
            {
                minigamePanel.SetActive(isActive);
            }
        }

        private void SetTaskCompleteVisible(bool isActive)
        {
            if (taskCompleteText != null)
            {
                taskCompleteText.gameObject.SetActive(isActive);
            }
        }

        private void SetCursorState(bool freeCursor)
        {
            if (!freeCursor && GameManager.Instance != null && GameManager.Instance.isGameOver)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                return;
            }
            Cursor.visible = freeCursor;
            Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void ReleaseCurrentObject()
        {
            if (currentObject != null)
            {
                currentObject.ReleaseObject();
                currentObject = null;
            }
        }

        private void SetPlayerCameraLocked(bool isLocked)
        {
            if (cachedPlayerController == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    cachedPlayerController = playerObject.GetComponent<FirstPersonController>();
                }
            }

            if (cachedPlayerController == null)
            {
                return;
            }

            if (isLocked)
            {
                if (!hasCachedCameraState)
                {
                    cachedCameraCanMove = cachedPlayerController.cameraCanMove;
                    cachedPlayerCanMove = cachedPlayerController.playerCanMove;
                    hasCachedCameraState = true;
                }

                cachedPlayerController.cameraCanMove = false;
                cachedPlayerController.playerCanMove = false;
                return;
            }

            if (hasCachedCameraState)
            {
                if (GameManager.Instance == null || !GameManager.Instance.isGameOver)
                {
                    cachedPlayerController.cameraCanMove = cachedCameraCanMove;
                    cachedPlayerController.playerCanMove = cachedPlayerCanMove;
                }
                hasCachedCameraState = false;
            }
        }

        private static void ConfigurePanel(RectTransform rect, Image image)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(640f, 420f);
            image.color = new Color(0f, 0f, 0f, 0.82f);
        }

        private static void ConfigureTitleText(TextMeshProUGUI text)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -28f);
            rect.sizeDelta = new Vector2(420f, 50f);
            text.fontSize = 28f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }

        private static void ConfigureStatusText(RectTransform rect, TextMeshProUGUI text)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 86f);
            rect.sizeDelta = new Vector2(360f, 44f);
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.text = "Aktif: 0 / 3";
        }

        private static void ConfigureTaskCompleteText(RectTransform rect, TextMeshProUGUI text)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 200f);
            rect.sizeDelta = new Vector2(700f, 80f);
            text.fontSize = 42f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.green;
            text.text = "Görev Tamamlandı!";
        }

        private static void ConfigureSwitchRoot(RectTransform rect, Image image, float yOffset)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, yOffset);
            rect.sizeDelta = new Vector2(180f, 70f);
            image.color = new Color(1f, 1f, 1f, 0.01f);
        }

        private static void ConfigureBackground(RectTransform rect, Image image)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(130f, 44f);
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            image.raycastTarget = false;
        }

        private static void ConfigureKnob(RectTransform rect, Image image)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-25f, 0f);
            rect.sizeDelta = new Vector2(40f, 40f);
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static void ConfigureCloseButton(RectTransform rect, Image image)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 34f);
            rect.sizeDelta = new Vector2(180f, 46f);
            image.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        }

        private static void ConfigureCloseLabel(RectTransform rect, TextMeshProUGUI text)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.text = "Kapat";
            text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }
    }
}
