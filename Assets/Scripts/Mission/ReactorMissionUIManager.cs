using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public class ReactorMissionUIManager : MonoBehaviour
{
    private const float CloseAnimationDuration = 0.18f;
    private const float SuccessDisplayDuration = 1.8f;

    public static ReactorMissionUIManager Instance { get; private set; }
    public bool IsOpen { get; private set; }

    private VisualElement terminalPrompt;
    private VisualElement overlay;
    private VisualElement fuelFill;
    private VisualElement syncFill;
    private VisualElement missionComplete;
    private readonly VisualElement[] leverIndicators = new VisualElement[3];
    private readonly Label[] leverStates = new Label[3];
    private Label fuelValue;
    private Label fuelUnits;
    private Label availableCans;
    private Label reactorState;
    private Label reactorDetail;
    private Label syncValue;
    private Button closeButton;

    private FirstPersonController currentFpc;
    private Coroutine closeRoutine;
    private bool completionStarted;

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
        if (Instance == this)
            Instance = null;
    }

    private void InitializeUI(VisualElement root)
    {
        terminalPrompt = root.Q<VisualElement>("reactor-terminal-prompt");
        overlay = root.Q<VisualElement>("reactor-overlay");
        fuelFill = root.Q<VisualElement>("fuel-fill");
        syncFill = root.Q<VisualElement>("sync-fill");
        missionComplete = root.Q<VisualElement>("reactor-mission-complete");
        fuelValue = root.Q<Label>("fuel-value");
        fuelUnits = root.Q<Label>("fuel-units");
        availableCans = root.Q<Label>("available-cans");
        reactorState = root.Q<Label>("reactor-state");
        reactorDetail = root.Q<Label>("reactor-detail");
        syncValue = root.Q<Label>("sync-value");
        closeButton = root.Q<Button>("reactor-close-btn");
        leverIndicators[0] = root.Q<VisualElement>("lever-indicator-1");
        leverIndicators[1] = root.Q<VisualElement>("lever-indicator-2");
        leverIndicators[2] = root.Q<VisualElement>("lever-indicator-3");
        leverStates[0] = root.Q<Label>("lever-state-1");
        leverStates[1] = root.Q<Label>("lever-state-2");
        leverStates[2] = root.Q<Label>("lever-state-3");

        closeButton.clicked += Close;
    }

    public void SetPromptVisible(bool visible)
    {
        if (terminalPrompt == null)
            return;

        terminalPrompt.EnableInClassList("hidden", !visible || IsOpen || completionStarted);
    }

    public void Open(FirstPersonController fpc)
    {
        if (IsOpen || completionStarted)
            return;

        currentFpc = fpc;
        IsOpen = true;
        SetPromptVisible(false);
        missionComplete.AddToClassList("hidden");
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
        RefreshUI();
    }

    private IEnumerator ShowAfterLayout()
    {
        yield return null;
        overlay.AddToClassList("open");
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (Input.GetKeyDown(KeyCode.Escape) && !completionStarted)
        {
            Close();
            return;
        }

        ReactorMissionManager mission = ReactorMissionManager.Instance;
        if (mission == null)
            return;

        if (!completionStarted && mission.IsMissionCompleted.Value)
        {
            completionStarted = true;
            missionComplete.RemoveFromClassList("hidden");
            closeRoutine = StartCoroutine(CloseAfterSuccess());
        }

        RefreshUI();
    }

    private void RefreshUI()
    {
        ReactorMissionManager mission = ReactorMissionManager.Instance;
        if (mission == null)
            return;

        int fuel = mission.FuelPercent.Value;
        fuelValue.text = fuel.ToString("000");
        fuelUnits.text = $"{fuel / 20}/5 FUEL CELLS";
        availableCans.text = mission.AvailableGasCanCount.ToString("00");
        fuelFill.style.height = Length.Percent(fuel);

        for (int i = 0; i < leverIndicators.Length; i++)
        {
            bool engaged = mission.IsLeverPulled(i);
            leverIndicators[i].EnableInClassList("engaged", engaged);
            leverStates[i].text = engaged ? "ENGAGED" : "STANDBY";
            leverStates[i].EnableInClassList("engaged", engaged);
        }

        float syncProgress = mission.LeverSyncProgress.Value;
        syncFill.style.width = Length.Percent(syncProgress * 100f);
        syncValue.text = mission.Phase.Value == ReactorMissionPhase.Synchronizing
            ? $"{Mathf.Max(0f, 1f - syncProgress):0.00} SEC"
            : "STANDBY";

        reactorState.EnableInClassList(
            "critical", mission.Phase.Value == ReactorMissionPhase.InsufficientEnergy);
        reactorState.EnableInClassList(
            "ready", mission.Phase.Value == ReactorMissionPhase.Ready ||
                     mission.Phase.Value == ReactorMissionPhase.Synchronizing ||
                     mission.Phase.Value == ReactorMissionPhase.Completed);

        switch (mission.Phase.Value)
        {
            case ReactorMissionPhase.Fueling:
                reactorState.text = "FUELING REQUIRED";
                reactorDetail.text = "TRANSFER FUEL CELLS TO THE REACTOR CORE";
                break;
            case ReactorMissionPhase.Ready:
                reactorState.text = "IGNITION READY";
                reactorDetail.text = "THREE OPERATORS REQUIRED AT REMOTE LEVERS";
                break;
            case ReactorMissionPhase.Synchronizing:
                reactorState.text = "SYNCHRONIZING";
                reactorDetail.text = "ALL THREE LEVERS MUST ENGAGE WITHIN THE WINDOW";
                break;
            case ReactorMissionPhase.InsufficientEnergy:
                reactorState.text = "INSUFFICIENT ENERGY";
                reactorDetail.text = "FUEL RESERVE LOST // RECOVERY CELLS DEPLOYED";
                break;
            case ReactorMissionPhase.Completed:
                reactorState.text = "REACTOR ONLINE";
                reactorDetail.text = "IGNITION SEQUENCE COMPLETE";
                break;
            default:
                reactorState.text = "SYSTEM OFFLINE";
                reactorDetail.text = "INITIALIZE REACTOR CONTROL";
                break;
        }
    }

    private IEnumerator CloseAfterSuccess()
    {
        yield return new WaitForSeconds(SuccessDisplayDuration);
        BeginClose();
    }

    public void Close()
    {
        if (!IsOpen || completionStarted)
            return;

        BeginClose();
    }

    private void BeginClose()
    {
        if (!IsOpen)
            return;

        overlay.RemoveFromClassList("open");
        if (closeRoutine != null)
            StopCoroutine(closeRoutine);
        closeRoutine = StartCoroutine(FinishClose());
    }

    private IEnumerator FinishClose()
    {
        yield return new WaitForSeconds(CloseAnimationDuration);
        overlay.AddToClassList("hidden");
        missionComplete.AddToClassList("hidden");
        IsOpen = false;
        completionStarted = false;
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
}
