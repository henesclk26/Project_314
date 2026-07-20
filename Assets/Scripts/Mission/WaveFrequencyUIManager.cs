using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public class WaveFrequencyUIManager : MonoBehaviour
{
    private const int StepCount = 5;
    private const int MinimumOptimalMoves = 3;
    private const int MaximumOptimalMoves = 5;
    private const float CloseAnimationDuration = 0.18f;
    private const float SuccessDisplayDuration = 1.8f;

    public static WaveFrequencyUIManager Instance { get; private set; }
    public bool IsOpen { get; private set; }

    private UIDocument uiDocument;
    private VisualElement overlay;
    private VisualElement waveCanvas;
    private VisualElement wavelengthMeter;
    private VisualElement waveWidthMeter;
    private VisualElement matchProgressFill;
    private VisualElement missionComplete;
    private Label wavelengthValue;
    private Label waveWidthValue;
    private Label matchPercent;
    private Button closeButton;

    private FirstPersonController currentFpc;
    private WaveFrequencyTerminalInteractable currentTerminal;
    private Coroutine closeRoutine;
    private bool completionStarted;
    private int targetWavelength;
    private int targetWaveWidth;
    private int currentWavelength;
    private int currentWaveWidth;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null)
            InitializeUI(uiDocument.rootVisualElement);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void InitializeUI(VisualElement root)
    {
        overlay = root.Q<VisualElement>("wave-frequency-overlay");
        waveCanvas = root.Q<VisualElement>("waveform-canvas");
        wavelengthMeter = root.Q<VisualElement>("wavelength-meter");
        waveWidthMeter = root.Q<VisualElement>("wavewidth-meter");
        matchProgressFill = root.Q<VisualElement>("match-progress-fill");
        missionComplete = root.Q<VisualElement>("mission-complete");
        wavelengthValue = root.Q<Label>("wavelength-value");
        waveWidthValue = root.Q<Label>("wavewidth-value");
        matchPercent = root.Q<Label>("match-percent");
        closeButton = root.Q<Button>("close-btn");

        BuildMeter(wavelengthMeter);
        BuildMeter(waveWidthMeter);

        waveCanvas.generateVisualContent += DrawWaveform;
        waveCanvas.RegisterCallback<GeometryChangedEvent>(_ => waveCanvas.MarkDirtyRepaint());
        closeButton.clicked += Close;
    }

    private static void BuildMeter(VisualElement meter)
    {
        meter.Clear();
        for (int i = 0; i < StepCount; i++)
        {
            VisualElement step = new VisualElement();
            step.AddToClassList("meter-step");
            meter.Add(step);
        }
    }

    public void Open(WaveFrequencyTerminalInteractable terminal, FirstPersonController fpc)
    {
        if (IsOpen || IsMissionCompleted())
            return;

        if (closeRoutine != null)
        {
            StopCoroutine(closeRoutine);
            closeRoutine = null;
        }

        currentTerminal = terminal;
        currentFpc = fpc;
        completionStarted = false;
        IsOpen = true;

        CreatePuzzleState();
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
        UpdateVisuals();
    }

    private IEnumerator ShowAfterLayout()
    {
        yield return null;
        overlay.AddToClassList("open");
        waveCanvas.MarkDirtyRepaint();
    }

    private void CreatePuzzleState()
    {
        int desiredMoves = Random.Range(MinimumOptimalMoves, MaximumOptimalMoves + 1);

        for (int attempt = 0; attempt < 64; attempt++)
        {
            currentWavelength = Random.Range(0, StepCount);
            currentWaveWidth = Random.Range(0, StepCount);
            targetWavelength = Random.Range(0, StepCount);
            targetWaveWidth = Random.Range(0, StepCount);

            if (CalculateOptimalMoveCount() == desiredMoves)
                return;
        }

        currentWavelength = 0;
        currentWaveWidth = 0;
        targetWavelength = 4;
        targetWaveWidth = 1;
    }

    private int CalculateOptimalMoveCount()
    {
        return Mathf.Abs(currentWavelength - targetWavelength)
            + Mathf.Abs(currentWaveWidth - targetWaveWidth);
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (!completionStarted && IsMissionCompleted())
        {
            CompleteMission(false);
            return;
        }

        if (completionStarted)
            return;

        if (Input.GetKeyDown(KeyCode.E))
            ChangeWavelength(1);
        else if (Input.GetKeyDown(KeyCode.Q))
            ChangeWavelength(-1);

        if (Input.GetKeyDown(KeyCode.W))
            ChangeWaveWidth(1);
        else if (Input.GetKeyDown(KeyCode.S))
            ChangeWaveWidth(-1);

        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    private void ChangeWavelength(int direction)
    {
        int nextValue = Mathf.Clamp(currentWavelength + direction, 0, StepCount - 1);
        if (nextValue == currentWavelength)
            return;

        currentWavelength = nextValue;
        UpdateVisuals();
        CheckCompletion();
    }

    private void ChangeWaveWidth(int direction)
    {
        int nextValue = Mathf.Clamp(currentWaveWidth + direction, 0, StepCount - 1);
        if (nextValue == currentWaveWidth)
            return;

        currentWaveWidth = nextValue;
        UpdateVisuals();
        CheckCompletion();
    }

    private void CheckCompletion()
    {
        if (currentWavelength == targetWavelength && currentWaveWidth == targetWaveWidth)
            CompleteMission(true);
    }

    private void CompleteMission(bool reportToServer)
    {
        if (completionStarted)
            return;

        completionStarted = true;
        currentTerminal?.MarkCompleted();

        if (reportToServer && MissionManager.Instance != null && MissionManager.Instance.IsSpawned)
            MissionManager.Instance.CompleteWaveFrequencyMissionRpc();

        UpdateVisuals();
        missionComplete.RemoveFromClassList("hidden");
        missionComplete.AddToClassList("success-pulse");
        closeButton.SetEnabled(false);
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
        missionComplete.RemoveFromClassList("success-pulse");
        closeButton.SetEnabled(true);
        IsOpen = false;
        completionStarted = false;
        closeRoutine = null;

        if (currentFpc != null && !currentFpc.isDead.Value &&
            (!GameManager.Instance || !GameManager.Instance.isGameOver))
        {
            currentFpc.playerCanMove = true;
            currentFpc.cameraCanMove = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        currentFpc = null;
        currentTerminal = null;
    }

    private void UpdateVisuals()
    {
        wavelengthValue.text = $"{currentWavelength + 1:00} / {StepCount:00}";
        waveWidthValue.text = $"{currentWaveWidth + 1:00} / {StepCount:00}";
        UpdateMeter(wavelengthMeter, currentWavelength);
        UpdateMeter(waveWidthMeter, currentWaveWidth);

        float wavelengthMatch = 1f - Mathf.Abs(currentWavelength - targetWavelength) / (float)(StepCount - 1);
        float widthMatch = 1f - Mathf.Abs(currentWaveWidth - targetWaveWidth) / (float)(StepCount - 1);
        float match = Mathf.Clamp01((wavelengthMatch + widthMatch) * 0.5f);
        int percent = Mathf.RoundToInt(match * 100f);

        matchPercent.text = $"{percent}%";
        matchProgressFill.style.width = Length.Percent(percent);

        Color feedbackColor = percent == 100
            ? new Color(0f, 1f, 0.5f)
            : new Color(1f, 196f / 255f, 0f);
        matchPercent.style.color = feedbackColor;
        matchProgressFill.style.backgroundColor = feedbackColor;
        waveCanvas.MarkDirtyRepaint();
    }

    private static void UpdateMeter(VisualElement meter, int activeIndex)
    {
        for (int i = 0; i < meter.childCount; i++)
            meter[i].EnableInClassList("active", i <= activeIndex);
    }

    private void DrawWaveform(MeshGenerationContext context)
    {
        Rect rect = waveCanvas.contentRect;
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        Painter2D painter = context.painter2D;
        DrawGrid(painter, rect);
        DrawCenterLine(painter, rect);

        DrawWave(
            painter,
            rect,
            targetWavelength,
            targetWaveWidth,
            new Color(0f, 240f / 255f, 1f, 0.72f),
            3f);

        Color liveColor = completionStarted
            ? new Color(0f, 1f, 0.5f, 1f)
            : new Color(1f, 196f / 255f, 0f, 0.95f);
        DrawWave(painter, rect, currentWavelength, currentWaveWidth, liveColor, 2.5f);
    }

    private static void DrawGrid(Painter2D painter, Rect rect)
    {
        painter.strokeColor = new Color(0f, 240f / 255f, 1f, 0.08f);
        painter.lineWidth = 1f;
        painter.BeginPath();

        const int verticalLines = 12;
        const int horizontalLines = 8;
        for (int i = 1; i < verticalLines; i++)
        {
            float x = rect.width * i / verticalLines;
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, rect.height));
        }

        for (int i = 1; i < horizontalLines; i++)
        {
            float y = rect.height * i / horizontalLines;
            painter.MoveTo(new Vector2(0f, y));
            painter.LineTo(new Vector2(rect.width, y));
        }

        painter.Stroke();
    }

    private static void DrawCenterLine(Painter2D painter, Rect rect)
    {
        painter.strokeColor = new Color(0f, 240f / 255f, 1f, 0.2f);
        painter.lineWidth = 1.5f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(0f, rect.height * 0.5f));
        painter.LineTo(new Vector2(rect.width, rect.height * 0.5f));
        painter.Stroke();
    }

    private static void DrawWave(
        Painter2D painter,
        Rect rect,
        int wavelengthIndex,
        int widthIndex,
        Color color,
        float lineWidth)
    {
        float wavelength = Mathf.Lerp(rect.width * 0.11f, rect.width * 0.29f,
            wavelengthIndex / (float)(StepCount - 1));
        float amplitude = Mathf.Lerp(rect.height * 0.12f, rect.height * 0.39f,
            widthIndex / (float)(StepCount - 1));
        float centerY = rect.height * 0.5f;
        int samples = Mathf.Max(120, Mathf.CeilToInt(rect.width / 4f));

        painter.strokeColor = color;
        painter.lineWidth = lineWidth;
        painter.BeginPath();

        for (int i = 0; i <= samples; i++)
        {
            float x = rect.width * i / samples;
            float y = centerY - Mathf.Sin(x / wavelength * Mathf.PI * 2f) * amplitude;
            Vector2 point = new Vector2(x, y);

            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }

        painter.Stroke();
    }

    private static bool IsMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsWaveFrequencyMissionCompleted.Value;
    }
}
