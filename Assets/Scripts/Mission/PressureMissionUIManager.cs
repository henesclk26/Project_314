using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public class PressureMissionUIManager : MonoBehaviour
{
    private const float CloseAnimationDuration = 0.18f;
    private const float SuccessDisplayDuration = 1.8f;
    private const float GaugeMinimumAngle = -130f;
    private const float GaugeMaximumAngle = 130f;

    public static PressureMissionUIManager Instance { get; private set; }
    public bool IsOpen { get; private set; }

    private VisualElement overlay;
    private VisualElement terminalPrompt;
    private VisualElement gauge;
    private VisualElement needle;
    private VisualElement needleShadow;
    private VisualElement valveInstructions;
    private VisualElement stabilizeProgress;
    private VisualElement missionComplete;
    private Label pressureState;
    private Label pressureDetail;
    private Label pressureReadout;
    private Button closeButton;
    private Button stabilizeButton;

    private FirstPersonController currentFpc;
    private PressureValveInteractable hintSource;
    private Coroutine closeRoutine;
    private float displayedPressure;
    private float stabilizeTimer;
    private bool isHoldingStabilize;
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

    private void Start()
    {
        GameObject terminal = GameObject.Find("PressureTerminal");
        if (terminal != null && terminal.GetComponent<PressureTerminalInteractable>() == null)
            terminal.AddComponent<PressureTerminalInteractable>();
    }

    private void InitializeUI(VisualElement root)
    {
        overlay = root.Q<VisualElement>("pressure-overlay");
        terminalPrompt = root.Q<VisualElement>("terminal-prompt");
        gauge = root.Q<VisualElement>("pressure-gauge");
        needle = root.Q<VisualElement>("pressure-needle");
        needleShadow = root.Q<VisualElement>(className: "needle-shadow");
        valveInstructions = root.Q<VisualElement>("valve-instructions");
        stabilizeProgress = root.Q<VisualElement>("stabilize-progress");
        missionComplete = root.Q<VisualElement>("mission-complete");
        pressureState = root.Q<Label>("pressure-state");
        pressureDetail = root.Q<Label>("pressure-detail");
        pressureReadout = root.Q<Label>("pressure-readout");
        closeButton = root.Q<Button>("close-btn");
        stabilizeButton = root.Q<Button>("stabilize-btn");

        closeButton.clicked += Close;
        stabilizeButton.RegisterCallback<PointerDownEvent>(OnStabilizePointerDown, TrickleDown.TrickleDown);
        stabilizeButton.RegisterCallback<PointerUpEvent>(OnStabilizePointerUp, TrickleDown.TrickleDown);
        stabilizeButton.RegisterCallback<PointerCancelEvent>(_ => StopStabilizing(), TrickleDown.TrickleDown);
        stabilizeButton.RegisterCallback<PointerCaptureOutEvent>(_ => StopStabilizing());
        stabilizeButton.RegisterCallback<DetachFromPanelEvent>(_ => StopStabilizing());
        gauge.generateVisualContent += DrawGauge;
        gauge.RegisterCallback<GeometryChangedEvent>(_ => gauge.MarkDirtyRepaint());
    }

    public void Open(FirstPersonController fpc)
    {
        if (IsOpen || completionStarted)
            return;

        currentFpc = fpc;
        IsOpen = true;
        SetPromptVisible(false);
        displayedPressure = MissionManager.Instance != null ? MissionManager.Instance.CurrentPressure.Value : 0f;
        overlay.RemoveFromClassList("hidden");
        overlay.RemoveFromClassList("open");
        missionComplete.AddToClassList("hidden");
        StartCoroutine(ShowAfterLayout());

        if (currentFpc != null)
        {
            currentFpc.playerCanMove = false;
            currentFpc.cameraCanMove = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RefreshTerminal();
    }

    public void SetPromptVisible(bool visible)
    {
        if (terminalPrompt == null)
            return;

        terminalPrompt.EnableInClassList("hidden", !visible || IsOpen || completionStarted);
    }

    public void SetValveHint(PressureValveInteractable source, bool visible)
    {
        if (valveInstructions == null)
            return;

        if (visible)
        {
            hintSource = source;
            valveInstructions.RemoveFromClassList("hidden");
        }
        else if (hintSource == source)
        {
            hintSource = null;
            valveInstructions.AddToClassList("hidden");
        }
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

        if (MissionManager.Instance == null)
            return;

        if (!completionStarted && MissionManager.Instance.IsPressureMissionCompleted.Value)
        {
            CompleteMission();
            return;
        }

        RefreshTerminal();
        UpdateStabilization();
    }

    private void RefreshTerminal()
    {
        MissionManager mission = MissionManager.Instance;
        if (mission == null)
            return;

        displayedPressure = Mathf.MoveTowards(
            displayedPressure,
            mission.CurrentPressure.Value,
            36f * Time.deltaTime);
        float normalized = Mathf.InverseLerp(0f, 100f, displayedPressure);
        float needleAngle = Mathf.Lerp(GaugeMinimumAngle, GaugeMaximumAngle, normalized);
        needle.style.rotate = new Rotate(Angle.Degrees(needleAngle));
        needleShadow.style.rotate = new Rotate(Angle.Degrees(needleAngle));
        pressureReadout.text = (displayedPressure / 10f).ToString("0.0");

        bool active = mission.IsPressureMissionActive.Value;
        bool optimal = IsPressureOptimal(mission);
        bool overpressure = mission.CurrentPressure.Value >= 93f;
        stabilizeButton.SetEnabled(active && optimal && !completionStarted);

        pressureState.EnableInClassList("optimal", optimal);
        pressureState.EnableInClassList("critical", overpressure);
        if (!active)
        {
            pressureState.text = "AWAITING CALIBRATION";
            pressureDetail.text = "INITIALIZE TERMINAL TO ENABLE VALVES";
        }
        else if (overpressure)
        {
            pressureState.text = "OVERPRESSURE";
            pressureDetail.text = "REVERSE A VALVE TO RETURN TO THE SAFE RANGE";
        }
        else if (optimal)
        {
            pressureState.text = "OPTIMAL RANGE";
            pressureDetail.text = "HOLD STABILIZE WHILE REMOTE VALVES REMAIN STILL";
        }
        else if (mission.CurrentPressure.Value < mission.PressureTargetMin.Value)
        {
            pressureState.text = "PRESSURE LOW";
            pressureDetail.text = "REQUEST A FORWARD VALVE ADJUSTMENT";
        }
        else
        {
            pressureState.text = "PRESSURE HIGH";
            pressureDetail.text = "REQUEST A REVERSE VALVE ADJUSTMENT";
        }

        gauge.MarkDirtyRepaint();
    }

    private void OnStabilizePointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !stabilizeButton.enabledSelf || completionStarted)
            return;

        isHoldingStabilize = true;
        MissionManager.Instance?.BeginPressureStabilizationRpc();
    }

    private void OnStabilizePointerUp(PointerUpEvent evt)
    {
        if (evt.button == 0)
            StopStabilizing();
    }

    private void StopStabilizing()
    {
        if (!isHoldingStabilize)
            return;

        isHoldingStabilize = false;
        MissionManager.Instance?.CancelPressureStabilizationRpc();
    }

    private void UpdateStabilization()
    {
        MissionManager mission = MissionManager.Instance;
        bool canStabilize = mission != null && mission.IsPressureMissionActive.Value && IsPressureOptimal(mission);
        if (!canStabilize)
        {
            isHoldingStabilize = false;
            stabilizeTimer = 0f;
        }
        else
        {
            stabilizeTimer = mission.PressureStabilizeProgress.Value;
        }

        stabilizeProgress.style.width = Length.Percent(Mathf.Clamp01(stabilizeTimer) * 100f);
    }

    private void CompleteMission()
    {
        if (completionStarted)
            return;

        completionStarted = true;
        isHoldingStabilize = false;
        stabilizeButton.SetEnabled(false);
        missionComplete.RemoveFromClassList("hidden");
        closeRoutine = StartCoroutine(CloseAfterSuccess());
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

        StopStabilizing();
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
        stabilizeTimer = 0f;
        stabilizeProgress.style.width = Length.Percent(0f);
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

    private static bool IsPressureOptimal(MissionManager mission)
    {
        return mission.IsPressureMissionActive.Value &&
            mission.CurrentPressure.Value >= mission.PressureTargetMin.Value &&
            mission.CurrentPressure.Value <= mission.PressureTargetMax.Value;
    }

    private void DrawGauge(MeshGenerationContext context)
    {
        if (gauge.contentRect.width < 2f || gauge.contentRect.height < 2f)
            return;

        Rect rect = gauge.contentRect;
        Vector2 center = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
        float radius = Mathf.Min(rect.width, rect.height) * 0.43f;
        Painter2D painter = context.painter2D;

        DrawArc(painter, center, radius + 7f, GaugeMinimumAngle, GaugeMaximumAngle, new Color(0f, 232f / 255f, 245f / 255f, 0.12f), 16f);
        DrawArc(painter, center, radius, GaugeMinimumAngle, GaugeMaximumAngle, new Color(0f, 232f / 255f, 245f / 255f, 0.72f), 6f);
        DrawArc(painter, center, radius, PressureToAngle(78f), PressureToAngle(92f), new Color(1f, 194f / 255f, 52f / 255f, 0.95f), 8f);
        DrawArc(painter, center, radius, PressureToAngle(92f), GaugeMaximumAngle, new Color(1f, 78f / 255f, 86f / 255f, 1f), 9f);
        DrawArc(painter, center, radius - 35f, GaugeMinimumAngle, GaugeMaximumAngle, new Color(0f, 232f / 255f, 245f / 255f, 0.1f), 1f);

        MissionManager mission = MissionManager.Instance;
        if (mission != null && mission.IsPressureMissionActive.Value)
        {
            float from = PressureToAngle(mission.PressureTargetMin.Value);
            float to = PressureToAngle(mission.PressureTargetMax.Value);
            DrawArc(painter, center, radius, from, to, new Color(0f, 1f, 136f / 255f, 1f), 12f);
            DrawArc(painter, center, radius - 13f, from, to, new Color(0f, 1f, 136f / 255f, 0.32f), 5f);
        }

        for (int i = 0; i <= 50; i++)
        {
            float normalized = i / 50f;
            float angle = Mathf.Lerp(GaugeMinimumAngle, GaugeMaximumAngle, normalized) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            float tickLength = i % 5 == 0 ? 19f : 10f;
            Color tickColor = normalized >= 0.92f
                ? new Color(1f, 78f / 255f, 86f / 255f, i % 5 == 0 ? 1f : 0.65f)
                : normalized >= 0.78f
                    ? new Color(1f, 194f / 255f, 52f / 255f, i % 5 == 0 ? 1f : 0.65f)
                    : new Color(210f / 255f, 238f / 255f, 245f / 255f, i % 5 == 0 ? 0.95f : 0.45f);
            painter.strokeColor = tickColor;
            painter.lineWidth = i % 5 == 0 ? 2f : 1f;
            painter.BeginPath();
            painter.MoveTo(center + direction * (radius - tickLength - 4f));
            painter.LineTo(center + direction * (radius - 4f));
            painter.Stroke();
        }

        painter.strokeColor = new Color(0f, 232f / 255f, 245f / 255f, 0.08f);
        painter.lineWidth = 1f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - radius * 0.62f, center.y));
        painter.LineTo(new Vector2(center.x + radius * 0.62f, center.y));
        painter.Stroke();
    }

    private static float PressureToAngle(float pressure)
    {
        return Mathf.Lerp(GaugeMinimumAngle, GaugeMaximumAngle, Mathf.Clamp01(pressure / 100f));
    }

    private static void DrawArc(Painter2D painter, Vector2 center, float radius, float fromAngle, float toAngle, Color color, float width)
    {
        painter.strokeColor = color;
        painter.lineWidth = width;
        painter.BeginPath();

        const int segments = 72;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(fromAngle, toAngle, i / (float)segments) * Mathf.Deg2Rad;
            Vector2 point = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }

        painter.Stroke();
    }
}
