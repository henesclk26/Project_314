using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class ComputerUIManager : MonoBehaviour
{
    public static ComputerUIManager Instance { get; private set; }

    private static readonly string[] SabotageFolderNames =
    {
        "PERSONNEL",
        "NAVIGATION",
        "REACTOR",
        "SECURITY",
        "ARCHIVE"
    };

    private UIDocument uiDocument;
    private VisualElement promptContainer;
    private VisualElement overlay;
    private VisualElement screen;
    private Label headerTitle;
    private Button closeBtn;

    private VisualElement emailPanel;
    private ScrollView emailList;
    private Label detailSender;
    private Label detailSubject;
    private Label detailBody;

    private VisualElement passwordPanel;
    private TextField passwordField;
    private Button submitBtn;
    private Label statusLabel;

    private VisualElement sabotageDesktop;
    private Label sabotageStatus;
    private VisualElement sabotageExecutable;
    private readonly VisualElement[] sabotageFolders =
        new VisualElement[MissionManager.FileSabotageFolderCount];
    private VisualElement folderContextMenu;
    private Button contextOpenButton;
    private Button contextDeleteButton;
    private Button contextDetailsButton;
    private VisualElement sabotageProgressOverlay;
    private Label sabotageProgressTitle;
    private VisualElement sabotageProgressFill;
    private Label sabotageProgressPercent;
    private VisualElement accessDeniedOverlay;
    private VisualElement sabotageSuccessOverlay;

    public bool IsComputerOpen { get; private set; }
    public bool WasClosedThisFrame { get; private set; }

    private FirstPersonController currentFpc;
    private ComputerData currentData;
    private bool sabotageAvailable;
    private bool isSabotageMode;
    private bool completionCloseScheduled;
    private int selectedFolderIndex = -1;
    private Coroutine accessDeniedRoutine;
    private FileSabotagePhase lastSabotagePhase =
        FileSabotagePhase.AwaitingExecutable;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null && uiDocument.rootVisualElement != null)
            InitializeUI(uiDocument.rootVisualElement);
    }

    private void Update()
    {
        WasClosedThisFrame = false;

        if (IsComputerOpen &&
            sabotageAvailable &&
            Input.GetKeyDown(KeyCode.F1))
        {
            SetSabotageMode(!isSabotageMode);
        }

        RefreshSabotageUI();

        if (IsComputerOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseComputer();
            WasClosedThisFrame = true;
        }
    }

    private void InitializeUI(VisualElement root)
    {
        promptContainer = root.Q<VisualElement>("prompt-container");
        overlay = root.Q<VisualElement>("computer-overlay");
        screen = root.Q<VisualElement>("computer-screen");
        headerTitle = root.Q<Label>("header-title");
        closeBtn = root.Q<Button>("close-btn");

        emailPanel = root.Q<VisualElement>("email-panel");
        emailList = root.Q<ScrollView>("email-list");
        detailSender = root.Q<Label>("detail-sender");
        detailSubject = root.Q<Label>("detail-subject");
        detailBody = root.Q<Label>("detail-body");

        passwordPanel = root.Q<VisualElement>("password-panel");
        passwordField = root.Q<TextField>("password-field");
        submitBtn = root.Q<Button>("submit-btn");
        statusLabel = root.Q<Label>("status-label");

        sabotageDesktop = root.Q<VisualElement>("sabotage-desktop");
        sabotageStatus = root.Q<Label>("sabotage-status");
        sabotageExecutable = root.Q<VisualElement>("sabotage-executable");
        folderContextMenu = root.Q<VisualElement>("folder-context-menu");
        contextOpenButton = root.Q<Button>("context-open");
        contextDeleteButton = root.Q<Button>("context-delete");
        contextDetailsButton = root.Q<Button>("context-details");
        sabotageProgressOverlay =
            root.Q<VisualElement>("sabotage-progress-overlay");
        sabotageProgressTitle =
            root.Q<Label>("sabotage-progress-title");
        sabotageProgressFill =
            root.Q<VisualElement>("sabotage-progress-fill");
        sabotageProgressPercent =
            root.Q<Label>("sabotage-progress-percent");
        accessDeniedOverlay =
            root.Q<VisualElement>("access-denied-overlay");
        sabotageSuccessOverlay =
            root.Q<VisualElement>("sabotage-success-overlay");

        for (int i = 0; i < sabotageFolders.Length; i++)
        {
            int folderIndex = i;
            sabotageFolders[i] =
                root.Q<VisualElement>($"folder-{folderIndex}");
            sabotageFolders[i].RegisterCallback<PointerDownEvent>(
                evt => OnFolderPointerDown(evt, folderIndex));
        }

        closeBtn.clicked += CloseComputer;
        submitBtn.clicked += OnPasswordSubmit;
        sabotageExecutable.RegisterCallback<ClickEvent>(OnExecutableClicked);
        contextOpenButton.clicked += ShowAccessDenied;
        contextDeleteButton.clicked += DeleteSelectedFolder;
        contextDetailsButton.clicked += ShowAccessDenied;
    }

    public void SetPromptVisible(bool visible)
    {
        if (IsComputerOpen)
            visible = false;

        if (visible)
            promptContainer.RemoveFromClassList("hidden");
        else
            promptContainer.AddToClassList("hidden");
    }

    public void OpenComputer(
        ComputerData data,
        FirstPersonController fpc,
        ComputerInteractable source = null)
    {
        if (IsComputerOpen)
            return;

        IsComputerOpen = true;
        currentData = data;
        currentFpc = fpc;
        sabotageAvailable =
            source != null &&
            source.gameObject.name == "MissionComputer" &&
            data != null &&
            data.computerType == ComputerType.Password;
        completionCloseScheduled =
            MissionManager.Instance != null &&
            MissionManager.Instance.FileSabotageState.Value ==
                FileSabotagePhase.Completed;
        lastSabotagePhase = MissionManager.Instance != null
            ? MissionManager.Instance.FileSabotageState.Value
            : FileSabotagePhase.AwaitingExecutable;

        SetPromptVisible(false);
        LockPlayer();

        passwordField.value = "";
        statusLabel.AddToClassList("hidden");
        SetSabotageMode(false);
        overlay.RemoveFromClassList("hidden");
        StartCoroutine(AddOpenClassRoutine());
    }

    private void LockPlayer()
    {
        if (currentFpc != null)
        {
            currentFpc.playerCanMove = false;
            currentFpc.cameraCanMove = false;
        }

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private IEnumerator AddOpenClassRoutine()
    {
        yield return null;
        overlay.AddToClassList("open");
    }

    public void CloseComputer()
    {
        if (!IsComputerOpen)
            return;

        HideContextMenu();
        overlay.RemoveFromClassList("open");
        StartCoroutine(HideOverlayRoutine());
    }

    private IEnumerator HideOverlayRoutine()
    {
        yield return new WaitForSecondsRealtime(0.25f);
        overlay.AddToClassList("hidden");
        IsComputerOpen = false;
        currentData = null;
        sabotageAvailable = false;
        isSabotageMode = false;
        screen.RemoveFromClassList("sabotage-mode");

        if (currentFpc != null)
        {
            currentFpc.playerCanMove = true;
            currentFpc.cameraCanMove = true;
        }

        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        currentFpc = null;
    }

    private void SetSabotageMode(bool enabled)
    {
        if (enabled && !sabotageAvailable)
            return;

        isSabotageMode = enabled;
        emailPanel.AddToClassList("hidden");
        passwordPanel.AddToClassList("hidden");
        sabotageDesktop.AddToClassList("hidden");
        HideContextMenu();
        HideAccessDeniedImmediate();

        if (enabled)
        {
            screen.AddToClassList("sabotage-mode");
            headerTitle.text = "SECURE FILE SYSTEM";
            sabotageDesktop.RemoveFromClassList("hidden");
            RefreshSabotageUI();
            return;
        }

        screen.RemoveFromClassList("sabotage-mode");
        if (currentData == null)
            return;

        headerTitle.text = currentData.computerName;
        if (currentData.computerType == ComputerType.Email)
        {
            emailPanel.RemoveFromClassList("hidden");
            PopulateEmails(currentData);
        }
        else if (currentData.computerType == ComputerType.Password)
        {
            passwordPanel.RemoveFromClassList("hidden");
        }
    }

    private void PopulateEmails(ComputerData data)
    {
        emailList.Clear();
        detailSender.text = "Sender: ";
        detailSubject.text = "Subject: ";
        detailBody.text = "";

        for (int i = 0; i < data.emails.Count; i++)
        {
            EmailData email = data.emails[i];
            VisualElement row = new VisualElement();
            row.AddToClassList("email-row");

            Label subjectLabel =
                new Label(email.sender + " - " + email.subject);
            subjectLabel.AddToClassList("email-row-subject");

            Label timeLabel = new Label("[" + email.time + "]");
            timeLabel.AddToClassList("email-row-time");
            row.Add(subjectLabel);
            row.Add(timeLabel);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                foreach (VisualElement child in emailList.Children())
                    child.RemoveFromClassList("selected");

                row.AddToClassList("selected");
                ShowEmailDetail(email);
            });

            emailList.Add(row);
            if (i == 0)
            {
                row.AddToClassList("selected");
                ShowEmailDetail(email);
            }
        }
    }

    private void ShowEmailDetail(EmailData email)
    {
        detailSender.text = "Sender: " + email.sender;
        detailSubject.text = "Subject: " + email.subject;
        detailBody.text = "Text: " + email.body;
    }

    private void OnPasswordSubmit()
    {
        if (currentData == null ||
            currentData.computerType != ComputerType.Password)
        {
            return;
        }

        string entered = passwordField.value;
        statusLabel.RemoveFromClassList("hidden");
        statusLabel.RemoveFromClassList("status-granted");
        statusLabel.RemoveFromClassList("status-denied");

        if (entered == currentData.correctPassword)
        {
            statusLabel.text = currentData.successMessage;
            statusLabel.AddToClassList("status-granted");
            StartCoroutine(CloseAfterDelay(1.5f));
        }
        else
        {
            statusLabel.text = "ACCESS DENIED";
            statusLabel.AddToClassList("status-denied");
        }
    }

    private void OnExecutableClicked(ClickEvent evt)
    {
        sabotageExecutable.AddToClassList("selected");
        if (evt.clickCount < 2 ||
            MissionManager.Instance == null ||
            MissionManager.Instance.FileSabotageState.Value !=
                FileSabotagePhase.AwaitingExecutable)
        {
            return;
        }

        MissionManager.Instance.RequestStartFileCopy();
    }

    private void OnFolderPointerDown(
        PointerDownEvent evt,
        int folderIndex)
    {
        if (evt.button != 1 ||
            MissionManager.Instance == null ||
            MissionManager.Instance.FileSabotageState.Value !=
                FileSabotagePhase.ReadyToDelete ||
            MissionManager.Instance.IsFileSabotageFolderDeleted(folderIndex))
        {
            return;
        }

        selectedFolderIndex = folderIndex;
        for (int i = 0; i < sabotageFolders.Length; i++)
            sabotageFolders[i].RemoveFromClassList("selected");
        sabotageFolders[folderIndex].AddToClassList("selected");

        Vector2 localPosition = sabotageDesktop.WorldToLocal(
            new Vector2(evt.position.x, evt.position.y));
        float maxX = Mathf.Max(
            0f,
            sabotageDesktop.resolvedStyle.width - 220f);
        float maxY = Mathf.Max(
            0f,
            sabotageDesktop.resolvedStyle.height - 140f);
        folderContextMenu.style.left =
            Mathf.Clamp(localPosition.x, 0f, maxX);
        folderContextMenu.style.top =
            Mathf.Clamp(localPosition.y, 44f, maxY);
        folderContextMenu.RemoveFromClassList("hidden");
        folderContextMenu.BringToFront();
        evt.StopPropagation();
    }

    private void DeleteSelectedFolder()
    {
        if (selectedFolderIndex < 0 ||
            MissionManager.Instance == null ||
            MissionManager.Instance.FileSabotageState.Value !=
                FileSabotagePhase.ReadyToDelete)
        {
            return;
        }

        MissionManager.Instance.RequestDeleteFolder(selectedFolderIndex);
        HideContextMenu();
    }

    private void ShowAccessDenied()
    {
        HideContextMenu();
        if (accessDeniedRoutine != null)
            StopCoroutine(accessDeniedRoutine);
        accessDeniedRoutine = StartCoroutine(ShowAccessDeniedRoutine());
    }

    private IEnumerator ShowAccessDeniedRoutine()
    {
        accessDeniedOverlay.RemoveFromClassList("hidden");
        accessDeniedOverlay.BringToFront();
        yield return new WaitForSecondsRealtime(1.25f);
        accessDeniedOverlay.AddToClassList("hidden");
        accessDeniedRoutine = null;
    }

    private void HideAccessDeniedImmediate()
    {
        if (accessDeniedRoutine != null)
        {
            StopCoroutine(accessDeniedRoutine);
            accessDeniedRoutine = null;
        }

        accessDeniedOverlay.AddToClassList("hidden");
    }

    private void HideContextMenu()
    {
        selectedFolderIndex = -1;
        folderContextMenu.AddToClassList("hidden");
        for (int i = 0; i < sabotageFolders.Length; i++)
            sabotageFolders[i].RemoveFromClassList("selected");
    }

    private void RefreshSabotageUI()
    {
        MissionManager manager = MissionManager.Instance;
        if (manager == null)
            return;

        FileSabotagePhase phase = manager.FileSabotageState.Value;
        bool canDelete = phase == FileSabotagePhase.ReadyToDelete;
        int deletedCount = 0;

        for (int i = 0; i < sabotageFolders.Length; i++)
        {
            bool deleted = manager.IsFileSabotageFolderDeleted(i);
            sabotageFolders[i].style.display =
                deleted ? DisplayStyle.None : DisplayStyle.Flex;
            sabotageFolders[i].EnableInClassList(
                "folder-locked",
                !canDelete);
            if (deleted)
                deletedCount++;
        }

        sabotageExecutable.EnableInClassList(
            "executed",
            phase != FileSabotagePhase.AwaitingExecutable);

        if (phase != FileSabotagePhase.ReadyToDelete)
            HideContextMenu();

        bool operationActive =
            phase == FileSabotagePhase.Copying ||
            phase == FileSabotagePhase.Deleting;
        sabotageProgressOverlay.EnableInClassList(
            "hidden",
            !operationActive);
        sabotageSuccessOverlay.EnableInClassList(
            "hidden",
            phase != FileSabotagePhase.Completed);

        if (operationActive)
        {
            float progress = manager.GetFileSabotageOperationProgress();
            sabotageProgressFill.style.width =
                Length.Percent(progress * 100f);
            sabotageProgressPercent.text =
                Mathf.RoundToInt(progress * 100f) + "%";
            sabotageProgressTitle.text =
                phase == FileSabotagePhase.Copying
                    ? "DOSYALAR KOPYALANIYOR"
                    : "DOSYA SİLİNİYOR";
            sabotageProgressOverlay.BringToFront();
        }

        switch (phase)
        {
            case FileSabotagePhase.AwaitingExecutable:
                sabotageStatus.text = "EXECUTABLE REQUIRED";
                break;
            case FileSabotagePhase.Copying:
                sabotageStatus.text = "COPY IN PROGRESS";
                break;
            case FileSabotagePhase.ReadyToDelete:
                sabotageStatus.text =
                    (MissionManager.FileSabotageFolderCount - deletedCount) +
                    " OBJECTS REMAIN";
                break;
            case FileSabotagePhase.Deleting:
                int activeIndex =
                    manager.FileSabotageActiveFolderIndex.Value;
                sabotageStatus.text =
                    activeIndex >= 0 &&
                    activeIndex < SabotageFolderNames.Length
                        ? "PURGING " + SabotageFolderNames[activeIndex]
                        : "PURGE IN PROGRESS";
                break;
            case FileSabotagePhase.Completed:
                sabotageStatus.text = "TRANSFER COMPLETE";
                sabotageSuccessOverlay.BringToFront();
                break;
        }

        if (phase == FileSabotagePhase.Completed &&
            lastSabotagePhase != FileSabotagePhase.Completed &&
            IsComputerOpen &&
            !completionCloseScheduled)
        {
            completionCloseScheduled = true;
            SetSabotageMode(true);
            StartCoroutine(CloseAfterDelay(1.5f));
        }

        lastSabotagePhase = phase;
    }

    private IEnumerator CloseAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        CloseComputer();
    }
}
