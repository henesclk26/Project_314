using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class ComputerUIManager : MonoBehaviour
{
    public static ComputerUIManager Instance { get; private set; }

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

    public bool IsComputerOpen { get; private set; } = false;
    public bool WasClosedThisFrame { get; private set; } = false;
    private FirstPersonController currentFpc;
    private ComputerData currentData;

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
        {
            InitializeUI(uiDocument.rootVisualElement);
        }
    }

    private void Update()
    {
        WasClosedThisFrame = false;
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

        closeBtn.clicked += CloseComputer;
        submitBtn.clicked += OnPasswordSubmit;
    }

    public void SetPromptVisible(bool visible)
    {
        if (IsComputerOpen) visible = false; // Hide if computer is open

        if (visible)
            promptContainer.RemoveFromClassList("hidden");
        else
            promptContainer.AddToClassList("hidden");
    }

    public void OpenComputer(ComputerData data, FirstPersonController fpc)
    {
        if (IsComputerOpen) return;
        IsComputerOpen = true;
        currentData = data;
        currentFpc = fpc;

        // Hide prompt
        SetPromptVisible(false);

        // Lock player
        if (currentFpc != null)
        {
            currentFpc.playerCanMove = false;
            currentFpc.cameraCanMove = false;
        }
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        // Set header
        headerTitle.text = data.computerName;

        // Configure panels
        emailPanel.AddToClassList("hidden");
        passwordPanel.AddToClassList("hidden");
        statusLabel.AddToClassList("hidden");
        passwordField.value = "";

        if (data.computerType == ComputerType.Email)
        {
            emailPanel.RemoveFromClassList("hidden");
            PopulateEmails(data);
        }
        else if (data.computerType == ComputerType.Password)
        {
            passwordPanel.RemoveFromClassList("hidden");
        }

        // Show overlay with animation class
        overlay.RemoveFromClassList("hidden");
        
        // Slight delay to allow display:flex to apply before opacity transition
        StartCoroutine(AddOpenClassRoutine());
    }

    private IEnumerator AddOpenClassRoutine()
    {
        yield return null;
        overlay.AddToClassList("open");
    }

    public void CloseComputer()
    {
        if (!IsComputerOpen) return;
        
        overlay.RemoveFromClassList("open");
        StartCoroutine(HideOverlayRoutine());
    }

    private IEnumerator HideOverlayRoutine()
    {
        yield return new WaitForSeconds(0.25f); // Match transition duration
        overlay.AddToClassList("hidden");
        IsComputerOpen = false;
        currentData = null;

        // Release player
        if (currentFpc != null)
        {
            currentFpc.playerCanMove = true;
            currentFpc.cameraCanMove = true;
        }
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        currentFpc = null;
    }

    private void PopulateEmails(ComputerData data)
    {
        emailList.Clear();
        detailSender.text = "Sender: ";
        detailSubject.text = "Subject: ";
        detailBody.text = "";

        for (int i = 0; i < data.emails.Count; i++)
        {
            var email = data.emails[i];
            
            // Create Row
            var row = new VisualElement();
            row.AddToClassList("email-row");
            
            var subjectLabel = new Label(email.sender + " - " + email.subject);
            subjectLabel.AddToClassList("email-row-subject");
            
            var timeLabel = new Label("[" + email.time + "]");
            timeLabel.AddToClassList("email-row-time");

            row.Add(subjectLabel);
            row.Add(timeLabel);

            // Click event
            row.RegisterCallback<ClickEvent>(evt => 
            {
                // Remove selected class from all
                foreach (var child in emailList.Children())
                    child.RemoveFromClassList("selected");
                
                row.AddToClassList("selected");
                ShowEmailDetail(email);
            });

            emailList.Add(row);

            // Select first by default
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
        if (currentData == null || currentData.computerType != ComputerType.Password) return;

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

    private IEnumerator CloseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        CloseComputer();
    }
}
