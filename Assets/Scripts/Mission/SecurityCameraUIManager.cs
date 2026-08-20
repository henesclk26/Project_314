using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public class SecurityCameraUIManager : MonoBehaviour
{
    private const int CameraSlotCount = 6;
    private const float CloseAnimationDuration = 0.18f;

    public static SecurityCameraUIManager Instance { get; private set; }

    public bool IsOpen { get; private set; }
    public bool WasClosedThisFrame { get; private set; }

    private VisualElement terminalPrompt;
    private VisualElement overlay;
    private VisualElement gridView;
    private VisualElement detailView;
    private Image detailImage;
    private Label detailName;
    private Label detailIndex;
    private Button closeButton;
    private Button backButton;
    private Button previousButton;
    private Button nextButton;

    private readonly Button[] feedCards = new Button[CameraSlotCount];
    private readonly Image[] feedImages = new Image[CameraSlotCount];
    private readonly Label[] feedNames = new Label[CameraSlotCount];
    private readonly Label[] feedStatuses = new Label[CameraSlotCount];

    private SecurityCameraFeed[] feeds = Array.Empty<SecurityCameraFeed>();
    private FirstPersonController currentFpc;
    private Coroutine closeRoutine;
    private int selectedIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        UIDocument document = GetComponent<UIDocument>();
        if (document != null)
            InitializeUI(document.rootVisualElement);
    }

    private void OnDestroy()
    {
        StopAllStreams();
        if (Instance == this)
            Instance = null;
    }

    private void InitializeUI(VisualElement root)
    {
        terminalPrompt = root.Q<VisualElement>("security-terminal-prompt");
        overlay = root.Q<VisualElement>("security-camera-overlay");
        gridView = root.Q<VisualElement>("camera-grid-view");
        detailView = root.Q<VisualElement>("camera-detail-view");
        detailImage = root.Q<Image>("detail-image");
        detailName = root.Q<Label>("detail-camera-name");
        detailIndex = root.Q<Label>("detail-camera-index");
        closeButton = root.Q<Button>("security-close-btn");
        backButton = root.Q<Button>("camera-back-btn");
        previousButton = root.Q<Button>("camera-prev-btn");
        nextButton = root.Q<Button>("camera-next-btn");

        closeButton.clicked += Close;
        backButton.clicked += ShowGrid;
        previousButton.clicked += ShowPrevious;
        nextButton.clicked += ShowNext;

        for (int i = 0; i < CameraSlotCount; i++)
        {
            int capturedIndex = i;
            feedCards[i] = root.Q<Button>("feed-card-" + i);
            feedImages[i] = root.Q<Image>("feed-image-" + i);
            feedNames[i] = root.Q<Label>("feed-name-" + i);
            feedStatuses[i] = root.Q<Label>("feed-status-" + i);
            if (feedCards[i] != null)
                feedCards[i].clicked += () => ShowDetail(capturedIndex);
        }

        ShowGridImmediate();
    }

    private void Update()
    {
        WasClosedThisFrame = false;
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            WasClosedThisFrame = true;
        }
    }

    public void SetPromptVisible(bool visible)
    {
        if (terminalPrompt == null)
            return;

        terminalPrompt.EnableInClassList("hidden", !visible || IsOpen);
    }

    public void Open(FirstPersonController fpc)
    {
        if (IsOpen || overlay == null)
            return;

        DiscoverFeeds();
        currentFpc = fpc;
        IsOpen = true;
        SetPromptVisible(false);
        PopulateGrid();
        ShowGridImmediate();

        overlay.RemoveFromClassList("hidden");
        overlay.RemoveFromClassList("open");
        StartCoroutine(ShowAfterLayout());

        if (currentFpc != null)
        {
            currentFpc.playerCanMove = false;
            currentFpc.cameraCanMove = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private IEnumerator ShowAfterLayout()
    {
        yield return null;
        overlay.AddToClassList("open");
    }

    private void DiscoverFeeds()
    {
        feeds = FindObjectsByType<SecurityCameraFeed>(FindObjectsSortMode.None)
            .OrderBy(feed => feed.DisplayOrder)
            .ThenBy(feed => feed.FeedId)
            .Take(CameraSlotCount)
            .ToArray();
    }

    private void PopulateGrid()
    {
        for (int i = 0; i < CameraSlotCount; i++)
        {
            bool available = i < feeds.Length && feeds[i] != null;
            if (feedCards[i] != null)
                feedCards[i].SetEnabled(available);

            if (!available)
            {
                if (feedImages[i] != null)
                    feedImages[i].image = null;
                if (feedNames[i] != null)
                    feedNames[i].text = "CAMERA " + (i + 1).ToString("00");
                if (feedStatuses[i] != null)
                    feedStatuses[i].text = "SIGNAL LOST";
                continue;
            }

            RenderTexture texture = feeds[i].PrepareStream();
            feeds[i].SetStreaming(true);
            if (feedImages[i] != null)
            {
                feedImages[i].image = texture;
                feedImages[i].scaleMode = ScaleMode.ScaleAndCrop;
            }
            if (feedNames[i] != null)
                feedNames[i].text = feeds[i].DisplayName;
            if (feedStatuses[i] != null)
                feedStatuses[i].text = "LIVE";
        }
    }

    private void ShowDetail(int index)
    {
        if (index < 0 || index >= feeds.Length || feeds[index] == null)
            return;

        selectedIndex = index;
        gridView.AddToClassList("hidden");
        detailView.RemoveFromClassList("hidden");
        SetDetailFeed();
    }

private void SetDetailFeed()
    {
        if (feeds.Length == 0)
            return;

        selectedIndex = (selectedIndex % feeds.Length + feeds.Length) % feeds.Length;
        for (int i = 0; i < feeds.Length; i++)
            feeds[i].SetStreaming(i == selectedIndex);

        SecurityCameraFeed selected = feeds[selectedIndex];
        detailImage.image = selected.PrepareStream();
        detailImage.scaleMode = ScaleMode.ScaleAndCrop;
        detailName.text = "CAM " + (selectedIndex + 1).ToString("00");
        detailIndex.text = (selectedIndex + 1).ToString("00") + " / " + feeds.Length.ToString("00");
    }

    private void ShowPrevious()
    {
        if (feeds.Length == 0)
            return;

        selectedIndex--;
        SetDetailFeed();
    }

    private void ShowNext()
    {
        if (feeds.Length == 0)
            return;

        selectedIndex++;
        SetDetailFeed();
    }

    private void ShowGrid()
    {
        PopulateGrid();
        ShowGridImmediate();
    }

    private void ShowGridImmediate()
    {
        if (gridView != null)
            gridView.RemoveFromClassList("hidden");
        if (detailView != null)
            detailView.AddToClassList("hidden");
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        // Stop the extra scene renders immediately. The close animation can
        // still show the last captured frame, while the player's main camera
        // is no longer competing with six live feed cameras during the exit.
        StopAllStreams();
        overlay.RemoveFromClassList("open");
        if (closeRoutine != null)
            StopCoroutine(closeRoutine);
        closeRoutine = StartCoroutine(FinishClose());
    }

    private IEnumerator FinishClose()
    {
        yield return new WaitForSeconds(CloseAnimationDuration);
        overlay.AddToClassList("hidden");
        IsOpen = false;
        closeRoutine = null;

        if (currentFpc != null && !currentFpc.isDead.Value &&
            (GameManager.Instance == null || !GameManager.Instance.isGameOver))
        {
            currentFpc.playerCanMove = true;
            currentFpc.cameraCanMove = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        currentFpc = null;
    }

    private void StopAllStreams()
    {
        foreach (SecurityCameraFeed feed in feeds)
        {
            if (feed != null)
                feed.SetStreaming(false);
        }
    }
}
