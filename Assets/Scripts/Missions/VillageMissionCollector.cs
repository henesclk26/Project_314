using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class VillageMissionCollector : NetworkBehaviour
{
    private const string CollectPromptText = "Eşya Topla [F]";
    private const string PlacePromptText = "Eşyaları Yerleştir [F]";
    private const string MissionCompletedText = "Görev Tamamlandı";

    [Header("Mission Settings")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private int requiredGarbageCount = 3;
    [SerializeField] private Color promptColor = Color.white;
    [SerializeField] private Color progressColor = Color.white;
    [SerializeField] private Color completionColor = new Color(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private float completionMessageDuration = 2.5f;

    private readonly HashSet<string> collectedGarbageIds = new HashSet<string>();

    private FirstPersonController firstPersonController;
    private VillageMissionInteractable currentInteractable;
    private Canvas playerCanvas;
    private RectTransform missionUiRoot;
    private TextMeshProUGUI promptText;
    private TextMeshProUGUI completionText;
    private GameObject progressContainer;
    private Image progressIcon;
    private TextMeshProUGUI progressCountText;
    private Coroutine completionCoroutine;

    private static Sprite fallbackSprite;

    private void Awake()
    {
        firstPersonController = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        TryBuildUi();
        HidePrompt();
        UpdateProgressUi();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentInteractable = null;

        if (scene.name == "MainMenu")
        {
            HidePrompt();
            return;
        }

        TryBuildUi();
        RefreshCurrentInteractable();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (!CanUseMission())
        {
            currentInteractable = null;
            HidePrompt();
            return;
        }

        TryBuildUi();
        RefreshCurrentInteractable();

        if (currentInteractable == null)
        {
            HidePrompt();
            return;
        }

        ShowPromptForCurrentInteractable();

        if (Input.GetKeyDown(interactKey))
        {
            HandleInteraction(currentInteractable);
        }
    }

    private bool CanUseMission()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu")
        {
            return false;
        }

        if (RoleManager.Instance == null || RoleManager.Instance.GetLocalPlayerRole() != PlayerRole.Crewmate)
        {
            HideProgressUi();
            return false;
        }

        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive)
        {
            return false;
        }

        if (firstPersonController == null)
        {
            return false;
        }

        return !firstPersonController.isDead.Value;
    }

    private void RefreshCurrentInteractable()
    {
        float closestDistance = float.MaxValue;
        VillageMissionInteractable closestInteractable = null;

        IReadOnlyList<VillageMissionInteractable> interactables = VillageMissionInteractable.All;
        for (int i = 0; i < interactables.Count; i++)
        {
            VillageMissionInteractable interactable = interactables[i];
            if (interactable == null || !interactable.isActiveAndEnabled)
            {
                continue;
            }

            if (!CanInteractWith(interactable))
            {
                continue;
            }

            float distance = interactable.DistanceTo(transform.position);
            if (distance > interactable.InteractionRange)
            {
                continue;
            }

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestInteractable = interactable;
            }
        }

        currentInteractable = closestInteractable;
    }

    private bool CanInteractWith(VillageMissionInteractable interactable)
    {
        if (interactable.InteractableType == VillageMissionInteractable.MissionInteractableType.GarbageBin)
        {
            return !collectedGarbageIds.Contains(interactable.InteractableId);
        }

        if (interactable.InteractableType == VillageMissionInteractable.MissionInteractableType.Shelf)
        {
            return collectedGarbageIds.Count >= requiredGarbageCount;
        }

        return false;
    }

    private void HandleInteraction(VillageMissionInteractable interactable)
    {
        if (interactable == null)
        {
            return;
        }

        switch (interactable.InteractableType)
        {
            case VillageMissionInteractable.MissionInteractableType.GarbageBin:
                CollectFromGarbage(interactable);
                break;
            case VillageMissionInteractable.MissionInteractableType.Shelf:
                CompleteMission();
                break;
        }
    }

    private void CollectFromGarbage(VillageMissionInteractable interactable)
    {
        if (!collectedGarbageIds.Add(interactable.InteractableId))
        {
            return;
        }

        UpdateProgressUi(interactable.ItemSprite);
    }

    private void CompleteMission()
    {
        if (collectedGarbageIds.Count < requiredGarbageCount)
        {
            return;
        }

        collectedGarbageIds.Clear();
        UpdateProgressUi();
        ShowMissionCompleted();

        if (GameManager.Instance != null && NetworkManager.Singleton != null)
        {
            GameManager.Instance.CompleteMissionServerRpc(NetworkManager.Singleton.LocalClientId, "Collector");
        }
    }

    private void ShowPromptForCurrentInteractable()
    {
        if (promptText == null)
        {
            return;
        }

        promptText.gameObject.SetActive(true);
        promptText.color = promptColor;
        promptText.text = currentInteractable.InteractableType == VillageMissionInteractable.MissionInteractableType.GarbageBin
            ? CollectPromptText
            : PlacePromptText;
    }

    private void HidePrompt()
    {
        if (promptText != null)
        {
            promptText.gameObject.SetActive(false);
        }
    }

    private void ShowMissionCompleted()
    {
        if (completionCoroutine != null)
        {
            StopCoroutine(completionCoroutine);
        }

        completionCoroutine = StartCoroutine(ShowCompletionRoutine());
    }

    private IEnumerator ShowCompletionRoutine()
    {
        if (completionText == null)
        {
            yield break;
        }

        completionText.gameObject.SetActive(true);
        completionText.color = completionColor;
        completionText.text = MissionCompletedText;

        yield return new WaitForSeconds(completionMessageDuration);

        completionText.gameObject.SetActive(false);
        completionCoroutine = null;
    }

    private void UpdateProgressUi(Sprite iconSprite = null)
    {
        if (progressContainer == null || progressIcon == null || progressCountText == null)
        {
            return;
        }

        int currentCount = collectedGarbageIds.Count;
        bool shouldShow = currentCount > 0;
        progressContainer.SetActive(shouldShow);

        if (!shouldShow)
        {
            return;
        }

        progressIcon.sprite = iconSprite != null ? iconSprite : GetFallbackSprite();
        progressIcon.color = progressColor;
        progressCountText.text = "x" + currentCount;
    }

    private void HideProgressUi()
    {
        if (progressContainer != null)
        {
            progressContainer.SetActive(false);
        }
    }

    private void TryBuildUi()
    {
        if (missionUiRoot != null)
        {
            return;
        }

        playerCanvas = GetComponentInChildren<Canvas>(true);
        if (playerCanvas == null)
        {
            return;
        }

        Transform existingRoot = playerCanvas.transform.Find("MissionUIRoot");
        if (existingRoot != null)
        {
            missionUiRoot = existingRoot as RectTransform;
        }
        else
        {
            GameObject rootObject = new GameObject("MissionUIRoot", typeof(RectTransform));
            missionUiRoot = rootObject.GetComponent<RectTransform>();
            missionUiRoot.SetParent(playerCanvas.transform, false);
            missionUiRoot.anchorMin = Vector2.zero;
            missionUiRoot.anchorMax = Vector2.one;
            missionUiRoot.offsetMin = Vector2.zero;
            missionUiRoot.offsetMax = Vector2.zero;
        }

        promptText = CreateText("MissionPrompt", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(520f, 64f), 30f, FontStyles.Bold);
        promptText.color = promptColor;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.gameObject.SetActive(false);

        completionText = CreateText("MissionCompleted", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(520f, 72f), 34f, FontStyles.Bold);
        completionText.color = completionColor;
        completionText.alignment = TextAlignmentOptions.Center;
        completionText.gameObject.SetActive(false);

        progressContainer = CreatePanel("MissionProgress", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(170f, 60f), new Color(0f, 0f, 0f, 0.55f));
        progressContainer.SetActive(false);

        progressIcon = CreateImage("MissionIcon", progressContainer.transform as RectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(42f, 42f), progressColor);
        progressIcon.sprite = GetFallbackSprite();

        progressCountText = CreateText("MissionCount", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(95f, 0f), new Vector2(100f, 42f), 28f, FontStyles.Bold, progressContainer.transform as RectTransform);
        progressCountText.alignment = TextAlignmentOptions.Left;
        progressCountText.color = Color.white;
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        float fontSize,
        FontStyles fontStyle,
        RectTransform parentOverride = null)
    {
        RectTransform parent = parentOverride != null ? parentOverride : missionUiRoot;
        Transform existingTransform = parent.Find(objectName);
        GameObject textObject;

        if (existingTransform != null)
        {
            textObject = existingTransform.gameObject;
        }
        else
        {
            textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
        }

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.text = string.Empty;
        text.raycastTarget = false;
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;
        return text;
    }

    private GameObject CreatePanel(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Color backgroundColor)
    {
        Transform existingTransform = missionUiRoot.Find(objectName);
        GameObject panelObject;

        if (existingTransform != null)
        {
            panelObject = existingTransform.gameObject;
        }
        else
        {
            panelObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(missionUiRoot, false);
        }

        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = panelObject.GetComponent<Image>();
        image.color = backgroundColor;
        image.sprite = GetFallbackSprite();
        image.type = Image.Type.Sliced;

        return panelObject;
    }

    private Image CreateImage(string objectName, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        Transform existingTransform = parent.Find(objectName);
        GameObject imageObject;

        if (existingTransform != null)
        {
            imageObject = existingTransform.gameObject;
        }
        else
        {
            imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
        }

        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;

        Image image = imageObject.GetComponent<Image>();
        image.sprite = GetFallbackSprite();
        image.color = color;
        image.preserveAspect = true;
        return image;
    }

    private static Sprite GetFallbackSprite()
    {
        if (fallbackSprite == null)
        {
            fallbackSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }

        return fallbackSprite;
    }
}
