using System;
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
    public bool IsSabotageMode => isSabotageMode;

    private VisualElement overlay;
    private VisualElement panel;
    private VisualElement normalBody;
    private VisualElement sabotageBody;
    private VisualElement waveCanvas;
    private VisualElement wavelengthMeter;
    private VisualElement waveWidthMeter;
    private VisualElement matchProgressFill;
    private VisualElement missionComplete;
    private VisualElement satelliteCanvas;
    private VisualElement satelliteOrbitStage;
    private VisualElement computerPortRow;
    private VisualElement computerIconHost;
    private VisualElement targetSequenceList;
    private Label headerTitle;
    private Label headerKicker;
    private Label connectionLabel;
    private Label wavelengthValue;
    private Label waveWidthValue;
    private Label matchPercent;
    private Label satelliteLinkCount;
    private Label satelliteStatus;
    private Label satelliteSelectionStatus;
    private Label completeSubtitle;
    private Button closeButton;
    private Button computerConnectButton;
    private Button unlinkButton;

    private readonly VisualElement[] satelliteCards =
        new VisualElement[WaveSatelliteSabotageLayout.SatelliteCount];
    private readonly VisualElement[] satelliteUplinkPorts =
        new VisualElement[WaveSatelliteSabotageLayout.SatelliteCount];
    private readonly VisualElement[] computerPorts =
        new VisualElement[WaveSatelliteSabotageLayout.SatelliteCount];
    private readonly SatelliteCableElement[] cableElements =
        new SatelliteCableElement[WaveSatelliteSabotageLayout.SatelliteCount];

    private FirstPersonController currentFpc;
    private WaveFrequencyTerminalInteractable currentTerminal;
    private Coroutine closeRoutine;
    private WaveSatelliteSabotageLayout.Layout sabotageLayout;
    private int targetWavelength;
    private int targetWaveWidth;
    private int currentWavelength;
    private int currentWaveWidth;
    private int selectedSatellite = -1;
    private int selectedCableSatellite = -1;
    private int lastSabotageSeed;
    private int lastSabotageRevision = -1;
    private bool completionStarted;
    private bool isSabotageMode;
    private bool normalCompletedWhenOpened;
    private bool sabotageCompletedWhenOpened;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        UIDocument uiDocument = GetComponent<UIDocument>();
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
        panel = root.Q<VisualElement>("wave-frequency-panel");
        normalBody = root.Q<VisualElement>("normal-wave-body");
        sabotageBody = root.Q<VisualElement>("satellite-sabotage-body");
        waveCanvas = root.Q<VisualElement>("waveform-canvas");
        wavelengthMeter = root.Q<VisualElement>("wavelength-meter");
        waveWidthMeter = root.Q<VisualElement>("wavewidth-meter");
        matchProgressFill = root.Q<VisualElement>("match-progress-fill");
        missionComplete = root.Q<VisualElement>("mission-complete");
        satelliteCanvas = root.Q<VisualElement>("satellite-routing-canvas");
        satelliteOrbitStage = root.Q<VisualElement>("satellite-orbit-stage");
        computerPortRow = root.Q<VisualElement>("computer-port-row");
        computerIconHost = root.Q<VisualElement>("computer-icon-host");
        targetSequenceList = root.Q<VisualElement>("target-sequence-list");
        headerTitle = root.Q<Label>("header-title");
        headerKicker = root.Q<Label>("header-kicker");
        connectionLabel = root.Q<Label>("connection-label");
        wavelengthValue = root.Q<Label>("wavelength-value");
        waveWidthValue = root.Q<Label>("wavewidth-value");
        matchPercent = root.Q<Label>("match-percent");
        satelliteLinkCount = root.Q<Label>("satellite-link-count");
        satelliteStatus = root.Q<Label>("satellite-status");
        satelliteSelectionStatus = root.Q<Label>("satellite-selection-status");
        completeSubtitle = root.Q<Label>("complete-subtitle");
        closeButton = root.Q<Button>("close-btn");
        computerConnectButton = root.Q<Button>("computer-connect-btn");
        unlinkButton = root.Q<Button>("unlink-btn");

        BuildMeter(wavelengthMeter);
        BuildMeter(waveWidthMeter);
        waveCanvas.generateVisualContent += DrawWaveform;
        waveCanvas.RegisterCallback<GeometryChangedEvent>(
            _ => waveCanvas.MarkDirtyRepaint());
        satelliteCanvas.RegisterCallback<GeometryChangedEvent>(
            _ => RefreshCableGeometry());
        closeButton.clicked += Close;
        computerConnectButton.clicked += CommitSelectedSatellite;
        unlinkButton.clicked += UnlinkSelectedCable;
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

    public void Open(
        WaveFrequencyTerminalInteractable terminal,
        FirstPersonController fpc)
    {
        if (IsOpen || AreBothMissionsCompleted())
            return;

        if (closeRoutine != null)
        {
            StopCoroutine(closeRoutine);
            closeRoutine = null;
        }

        currentTerminal = terminal;
        currentFpc = fpc;
        completionStarted = false;
        isSabotageMode = false;
        normalCompletedWhenOpened = IsNormalMissionCompleted();
        sabotageCompletedWhenOpened = IsSabotageMissionCompleted();
        selectedSatellite = -1;
        selectedCableSatellite = -1;
        IsOpen = true;

        if (!normalCompletedWhenOpened)
            CreatePuzzleState();

        overlay.RemoveFromClassList("hidden");
        overlay.RemoveFromClassList("open");
        ApplyModePresentation();
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
        waveCanvas.MarkDirtyRepaint();
        RefreshCableGeometry();
    }

    private void CreatePuzzleState()
    {
        int desiredMoves = UnityEngine.Random.Range(
            MinimumOptimalMoves,
            MaximumOptimalMoves + 1);

        for (int attempt = 0; attempt < 64; attempt++)
        {
            currentWavelength = UnityEngine.Random.Range(0, StepCount);
            currentWaveWidth = UnityEngine.Random.Range(0, StepCount);
            targetWavelength = UnityEngine.Random.Range(0, StepCount);
            targetWaveWidth = UnityEngine.Random.Range(0, StepCount);

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
        return Mathf.Abs(currentWavelength - targetWavelength) +
               Mathf.Abs(currentWaveWidth - targetWaveWidth);
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (isSabotageMode)
        {
            SyncSabotageState();
            if (!completionStarted &&
                !sabotageCompletedWhenOpened &&
                IsSabotageMissionCompleted())
            {
                CompleteSabotage();
                return;
            }
        }
        else if (!completionStarted &&
                 !normalCompletedWhenOpened &&
                 IsNormalMissionCompleted())
        {
            CompleteNormalMission(false);
            return;
        }

        if (completionStarted)
            return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            SetSabotageMode(!isSabotageMode);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (isSabotageMode || IsNormalMissionCompleted())
            return;

        if (Input.GetKeyDown(KeyCode.E))
            ChangeWavelength(1);
        else if (Input.GetKeyDown(KeyCode.Q))
            ChangeWavelength(-1);

        if (Input.GetKeyDown(KeyCode.W))
            ChangeWaveWidth(1);
        else if (Input.GetKeyDown(KeyCode.S))
            ChangeWaveWidth(-1);
    }

    private void SetSabotageMode(bool sabotage)
    {
        if (completionStarted || isSabotageMode == sabotage)
            return;

        isSabotageMode = sabotage;
        selectedSatellite = -1;
        selectedCableSatellite = -1;
        if (isSabotageMode)
            MissionManager.Instance?.RequestInitializeWaveSatelliteSabotage();
        ApplyModePresentation();
    }

    private void ApplyModePresentation()
    {
        panel.EnableInClassList("sabotage-mode", isSabotageMode);
        normalBody.EnableInClassList("hidden", isSabotageMode);
        sabotageBody.EnableInClassList("hidden", !isSabotageMode);
        missionComplete.EnableInClassList("sabotage-success", isSabotageMode);
        missionComplete.AddToClassList("hidden");
        missionComplete.RemoveFromClassList("success-pulse");
        closeButton.SetEnabled(true);

        if (isSabotageMode)
        {
            headerTitle.text = "UPLINK OVERRIDE";
            headerKicker.text = "UNAUTHORIZED SATELLITE ROUTING // CHANNEL 04";
            connectionLabel.text = "OVERRIDE LINK ACTIVE";
            completeSubtitle.text = "SATELLITE ROUTING TABLE REPLACED";
            SyncSabotageState(true);
            if (IsSabotageMissionCompleted())
                ShowCompletedState(true);
        }
        else
        {
            headerTitle.text = "SIGNAL CALIBRATION";
            headerKicker.text = "WAVE FREQUENCY TERMINAL // CHANNEL 04";
            connectionLabel.text = "LINK ACTIVE";
            completeSubtitle.text = "SIGNAL LOCK CONFIRMED";
            UpdateVisuals();
            if (IsNormalMissionCompleted())
                ShowCompletedState(false);
        }
    }

    private void SyncSabotageState(bool force = false)
    {
        if (!isSabotageMode || MissionManager.Instance == null)
            return;

        MissionManager mission = MissionManager.Instance;
        if (!mission.IsWaveSatelliteSabotageInitialized.Value)
        {
            satelliteStatus.text = "INITIALIZING UPLINK ARRAY";
            satelliteLinkCount.text = "00 / 06 LINKED";
            satelliteSelectionStatus.text = "AWAITING SERVER ROUTING TABLE";
            computerConnectButton.SetEnabled(false);
            unlinkButton.SetEnabled(false);
            return;
        }

        int seed = mission.WaveSatelliteSabotageSeed.Value;
        if (sabotageLayout == null || seed != lastSabotageSeed)
        {
            lastSabotageSeed = seed;
            sabotageLayout = WaveSatelliteSabotageLayout.Create(seed);
            BuildSabotageInterface();
            force = true;
        }

        int revision = mission.WaveSatelliteSabotageRevision.Value;
        if (!force && revision == lastSabotageRevision)
            return;

        lastSabotageRevision = revision;
        ulong packed = mission.WaveSatelliteSabotagePackedConnections.Value;
        int connectedCount =
            WaveSatelliteSabotageLayout.GetConnectedCount(packed);

        for (int satellite = 0;
             satellite < WaveSatelliteSabotageLayout.SatelliteCount;
             satellite++)
        {
            int port =
                WaveSatelliteSabotageLayout.FindSatellitePort(packed, satellite);
            bool connected = port >= 0;
            satelliteCards[satellite].EnableInClassList(
                "connected",
                connected);
            satelliteCards[satellite].EnableInClassList(
                "selected",
                selectedSatellite == satellite);
            satelliteCards[satellite].EnableInClassList(
                "cable-selected",
                selectedCableSatellite == satellite);

            SatelliteCableElement cable = cableElements[satellite];
            cable.style.display = connected
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            cable.SetPort(port);
            cable.SetSelected(selectedCableSatellite == satellite);
        }

        for (int port = 0;
             port < WaveSatelliteSabotageLayout.SatelliteCount;
             port++)
        {
            int satellite =
                WaveSatelliteSabotageLayout.GetSatelliteAtPort(packed, port);
            bool occupied =
                satellite != WaveSatelliteSabotageLayout.EmptyPort;
            computerPorts[port].EnableInClassList("occupied", occupied);
        }

        bool complete = mission.IsWaveSatelliteSabotageCompleted.Value;
        satelliteLinkCount.text = $"{connectedCount:00} / 06 LINKED";
        if (complete)
        {
            satelliteStatus.text = "SEQUENCE ACCEPTED";
        }
        else if (connectedCount ==
                 WaveSatelliteSabotageLayout.SatelliteCount)
        {
            satelliteStatus.text = "SEQUENCE MISMATCH";
        }
        else
        {
            satelliteStatus.text = $"UPLINK {connectedCount:00} / 06";
        }

        RefreshSabotageSelection();
        satelliteCanvas.schedule.Execute(RefreshCableGeometry).StartingIn(1);
    }

    private void BuildSabotageInterface()
    {
        satelliteOrbitStage.Clear();
        computerPortRow.Clear();
        computerIconHost.Clear();
        targetSequenceList.Clear();

        for (int satellite = 0;
             satellite < WaveSatelliteSabotageLayout.SatelliteCount;
             satellite++)
        {
            int capturedSatellite = satellite;
            VisualElement card = new VisualElement();
            card.AddToClassList("satellite-node");
            card.AddToClassList($"satellite-position-{satellite}");

            VisualElement dish = new VisualElement();
            dish.AddToClassList("satellite-dish-image");
            if (satellite >= WaveSatelliteSabotageLayout.SatelliteCount / 2)
                dish.AddToClassList("mirrored");
            card.Add(dish);

            Label nameLabel = new Label($"SAT-{satellite + 1:00}");
            nameLabel.AddToClassList("satellite-name");
            card.Add(nameLabel);

            Label codeLabel = new Label(sabotageLayout.Codes[satellite]);
            codeLabel.AddToClassList("satellite-code");
            card.Add(codeLabel);

            VisualElement uplinkPort = new VisualElement();
            uplinkPort.AddToClassList("satellite-uplink-port");
            card.Add(uplinkPort);
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                HandleSatellitePointer(capturedSatellite);
                evt.StopPropagation();
            });

            satelliteOrbitStage.Add(card);
            satelliteCards[satellite] = card;
            satelliteUplinkPorts[satellite] = uplinkPort;
        }

        for (int port = 0;
             port < WaveSatelliteSabotageLayout.SatelliteCount;
             port++)
        {
            VisualElement portElement = new VisualElement();
            portElement.AddToClassList("computer-port");
            Label portLabel = new Label($"{port + 1:00}");
            portLabel.AddToClassList("computer-port-number");
            portElement.Add(portLabel);
            computerPortRow.Add(portElement);
            computerPorts[port] = portElement;
        }

        computerIconHost.Add(new ComputerTerminalGraphic());

        for (int order = 0;
             order < WaveSatelliteSabotageLayout.SatelliteCount;
             order++)
        {
            int satellite = sabotageLayout.TargetOrder[order];
            VisualElement row = new VisualElement();
            row.AddToClassList("target-sequence-row");

            Label number = new Label($"{order + 1:00}");
            number.AddToClassList("target-sequence-number");
            row.Add(number);

            VisualElement divider = new VisualElement();
            divider.AddToClassList("target-sequence-line");
            row.Add(divider);

            Label code = new Label(sabotageLayout.Codes[satellite]);
            code.AddToClassList("target-sequence-code");
            row.Add(code);
            targetSequenceList.Add(row);
        }

        for (int satellite = 0;
             satellite < WaveSatelliteSabotageLayout.SatelliteCount;
             satellite++)
        {
            cableElements[satellite]?.RemoveFromHierarchy();
            int capturedSatellite = satellite;
            SatelliteCableElement cable =
                new SatelliteCableElement(satellite);
            cable.RegisterCallback<PointerDownEvent>(evt =>
            {
                HandleCablePointer(capturedSatellite);
                evt.StopPropagation();
            });
            satelliteCanvas.Insert(0, cable);
            cableElements[satellite] = cable;
        }

        RefreshSabotageSelection();
        satelliteCanvas.schedule.Execute(RefreshCableGeometry).StartingIn(1);
    }

    private void HandleSatellitePointer(int satelliteIndex)
    {
        if (!CanEditSabotage())
            return;

        ulong packed =
            MissionManager.Instance.WaveSatelliteSabotagePackedConnections.Value;
        bool connected =
            WaveSatelliteSabotageLayout.FindSatellitePort(
                packed,
                satelliteIndex) >= 0;
        if (connected)
        {
            selectedSatellite = -1;
            selectedCableSatellite = satelliteIndex;
        }
        else
        {
            selectedSatellite = satelliteIndex;
            selectedCableSatellite = -1;
        }

        RefreshSabotageSelection();
    }

    private void HandleCablePointer(int satelliteIndex)
    {
        if (!CanEditSabotage())
            return;

        if (selectedCableSatellite == satelliteIndex)
        {
            RequestCableDisconnect(satelliteIndex);
            return;
        }

        selectedSatellite = -1;
        selectedCableSatellite = satelliteIndex;
        RefreshSabotageSelection();
    }

    private void CommitSelectedSatellite()
    {
        if (!CanEditSabotage() || selectedSatellite < 0)
            return;

        MissionManager.Instance.RequestConnectWaveSatellite(selectedSatellite);
        selectedSatellite = -1;
        SyncSabotageState(true);
    }

    private void UnlinkSelectedCable()
    {
        if (!CanEditSabotage() || selectedCableSatellite < 0)
            return;

        RequestCableDisconnect(selectedCableSatellite);
    }

    private void RequestCableDisconnect(int satelliteIndex)
    {
        MissionManager.Instance.RequestDisconnectWaveSatellite(satelliteIndex);
        selectedCableSatellite = -1;
        SyncSabotageState(true);
    }

    private bool CanEditSabotage()
    {
        return IsOpen &&
               isSabotageMode &&
               !completionStarted &&
               MissionManager.Instance != null &&
               MissionManager.Instance.IsWaveSatelliteSabotageInitialized.Value &&
               !MissionManager.Instance.IsWaveSatelliteSabotageCompleted.Value;
    }

    private void RefreshSabotageSelection()
    {
        if (IsSatelliteLayoutUnavailable())
            return;

        for (int satellite = 0;
             satellite < WaveSatelliteSabotageLayout.SatelliteCount;
             satellite++)
        {
            satelliteCards[satellite].EnableInClassList(
                "selected",
                selectedSatellite == satellite);
            satelliteCards[satellite].EnableInClassList(
                "cable-selected",
                selectedCableSatellite == satellite);
            cableElements[satellite]?.SetSelected(
                selectedCableSatellite == satellite);
        }

        if (selectedSatellite >= 0)
        {
            satelliteSelectionStatus.text =
                $"SELECTED // {sabotageLayout.Codes[selectedSatellite]}";
        }
        else if (selectedCableSatellite >= 0)
        {
            satelliteSelectionStatus.text =
                $"CABLE SELECTED // {sabotageLayout.Codes[selectedCableSatellite]}";
        }
        else
        {
            satelliteSelectionStatus.text = "NO UPLINK SELECTED";
        }

        bool editable = CanEditSabotage();
        computerConnectButton.SetEnabled(
            editable && selectedSatellite >= 0);
        unlinkButton.SetEnabled(
            editable && selectedCableSatellite >= 0);
    }

    private bool IsSatelliteLayoutUnavailable()
    {
        return sabotageLayout == null || satelliteCards[0] == null;
    }

    private void RefreshCableGeometry()
    {
        if (IsSatelliteLayoutUnavailable() ||
            satelliteCanvas.contentRect.width <= 1f ||
            satelliteCanvas.contentRect.height <= 1f)
        {
            return;
        }

        for (int satellite = 0;
             satellite < WaveSatelliteSabotageLayout.SatelliteCount;
             satellite++)
        {
            SatelliteCableElement cable = cableElements[satellite];
            int port = cable?.PortIndex ?? -1;
            if (cable == null || port < 0 || port >= computerPorts.Length)
                continue;

            VisualElement uplinkPort = satelliteUplinkPorts[satellite];
            VisualElement portElement = computerPorts[port];
            if (uplinkPort == null)
                continue;

            Vector2 start = uplinkPort.ChangeCoordinatesTo(
                satelliteCanvas,
                uplinkPort.contentRect.center);
            Vector2 end = portElement.ChangeCoordinatesTo(
                satelliteCanvas,
                portElement.contentRect.center);
            cable.SetEndpoints(start, end);
        }
    }

    private void ChangeWavelength(int direction)
    {
        int nextValue = Mathf.Clamp(
            currentWavelength + direction,
            0,
            StepCount - 1);
        if (nextValue == currentWavelength)
            return;

        currentWavelength = nextValue;
        UpdateVisuals();
        CheckNormalCompletion();
    }

    private void ChangeWaveWidth(int direction)
    {
        int nextValue = Mathf.Clamp(
            currentWaveWidth + direction,
            0,
            StepCount - 1);
        if (nextValue == currentWaveWidth)
            return;

        currentWaveWidth = nextValue;
        UpdateVisuals();
        CheckNormalCompletion();
    }

    private void CheckNormalCompletion()
    {
        if (currentWavelength == targetWavelength &&
            currentWaveWidth == targetWaveWidth)
        {
            CompleteNormalMission(true);
        }
    }

    private void CompleteNormalMission(bool reportToServer)
    {
        if (completionStarted)
            return;

        completionStarted = true;
        currentTerminal?.MarkCompleted();
        if (reportToServer &&
            MissionManager.Instance != null &&
            MissionManager.Instance.IsSpawned)
        {
            MissionManager.Instance.CompleteWaveFrequencyMissionRpc();
        }

        UpdateVisuals();
        ShowCompletedState(false);
        closeButton.SetEnabled(false);
        closeRoutine = StartCoroutine(CloseAfterSuccess());
    }

    private void CompleteSabotage()
    {
        if (completionStarted)
            return;

        completionStarted = true;
        ShowCompletedState(true);
        closeButton.SetEnabled(false);
        computerConnectButton.SetEnabled(false);
        unlinkButton.SetEnabled(false);
        closeRoutine = StartCoroutine(CloseAfterSuccess());
    }

    private void ShowCompletedState(bool sabotage)
    {
        completeSubtitle.text = sabotage
            ? "SATELLITE ROUTING TABLE REPLACED"
            : "SIGNAL LOCK CONFIRMED";
        missionComplete.EnableInClassList("sabotage-success", sabotage);
        missionComplete.RemoveFromClassList("hidden");
        missionComplete.AddToClassList("success-pulse");
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

        if (currentFpc != null &&
            !currentFpc.isDead.Value &&
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
        wavelengthValue.text =
            $"{currentWavelength + 1:00} / {StepCount:00}";
        waveWidthValue.text =
            $"{currentWaveWidth + 1:00} / {StepCount:00}";
        UpdateMeter(wavelengthMeter, currentWavelength);
        UpdateMeter(waveWidthMeter, currentWaveWidth);

        float wavelengthMatch =
            1f - Mathf.Abs(currentWavelength - targetWavelength) /
            (float)(StepCount - 1);
        float widthMatch =
            1f - Mathf.Abs(currentWaveWidth - targetWaveWidth) /
            (float)(StepCount - 1);
        float match = Mathf.Clamp01(
            (wavelengthMatch + widthMatch) * 0.5f);
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
        DrawWave(
            painter,
            rect,
            currentWavelength,
            currentWaveWidth,
            liveColor,
            2.5f);
    }

    private static void DrawGrid(Painter2D painter, Rect rect)
    {
        painter.strokeColor =
            new Color(0f, 240f / 255f, 1f, 0.08f);
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
        painter.strokeColor =
            new Color(0f, 240f / 255f, 1f, 0.2f);
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
        float wavelength = Mathf.Lerp(
            rect.width * 0.11f,
            rect.width * 0.29f,
            wavelengthIndex / (float)(StepCount - 1));
        float amplitude = Mathf.Lerp(
            rect.height * 0.12f,
            rect.height * 0.39f,
            widthIndex / (float)(StepCount - 1));
        float centerY = rect.height * 0.5f;
        int samples = Mathf.Max(120, Mathf.CeilToInt(rect.width / 4f));

        painter.strokeColor = color;
        painter.lineWidth = lineWidth;
        painter.BeginPath();

        for (int i = 0; i <= samples; i++)
        {
            float x = rect.width * i / samples;
            float y = centerY -
                Mathf.Sin(x / wavelength * Mathf.PI * 2f) * amplitude;
            Vector2 point = new Vector2(x, y);
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }

        painter.Stroke();
    }

    private static bool IsNormalMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsWaveFrequencyMissionCompleted.Value;
    }

    private static bool IsSabotageMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsWaveSatelliteSabotageCompleted.Value;
    }

    private static bool AreBothMissionsCompleted()
    {
        return IsNormalMissionCompleted() && IsSabotageMissionCompleted();
    }

    private sealed class ComputerTerminalGraphic : VisualElement
    {
        public ComputerTerminalGraphic()
        {
            AddToClassList("computer-terminal-graphic");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            Painter2D painter = context.painter2D;
            Color line = new Color(1f, 0.34f, 0.3f, 0.95f);
            Color core = new Color(1f, 0.68f, 0.25f, 0.85f);
            float left = rect.width * 0.2f;
            float right = rect.width * 0.8f;
            float top = rect.height * 0.15f;
            float bottom = rect.height * 0.67f;

            painter.strokeColor = line;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(left, top));
            painter.LineTo(new Vector2(right, top));
            painter.LineTo(new Vector2(right, bottom));
            painter.LineTo(new Vector2(left, bottom));
            painter.LineTo(new Vector2(left, top));
            painter.MoveTo(new Vector2(rect.width * 0.5f, bottom));
            painter.LineTo(new Vector2(rect.width * 0.5f, rect.height * 0.82f));
            painter.MoveTo(new Vector2(rect.width * 0.32f, rect.height * 0.82f));
            painter.LineTo(new Vector2(rect.width * 0.68f, rect.height * 0.82f));
            painter.Stroke();

            painter.strokeColor = core;
            painter.lineWidth = 3f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.width * 0.38f, rect.height * 0.4f));
            painter.LineTo(new Vector2(rect.width * 0.62f, rect.height * 0.4f));
            painter.Stroke();
        }
    }

    private sealed class SatelliteCableElement : VisualElement
    {
        private const float HitTolerance = 12f;
        private Vector2 start;
        private Vector2 end;
        private Vector2 controlA;
        private Vector2 controlB;
        private Vector2 routeA;
        private Vector2 routeB;
        private Vector2 curveStart;
        private bool hasGeometry;
        private bool hasRoute;
        private bool selected;

        public int SatelliteIndex { get; }
        public int PortIndex { get; private set; } = -1;

        public SatelliteCableElement(int satelliteIndex)
        {
            SatelliteIndex = satelliteIndex;
            AddToClassList("satellite-cable");
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += Draw;
        }

        public void SetPort(int portIndex)
        {
            PortIndex = portIndex;
        }

        public void SetSelected(bool isSelected)
        {
            selected = isSelected;
            MarkDirtyRepaint();
        }

        public void SetEndpoints(Vector2 startPoint, Vector2 endPoint)
        {
            start = startPoint;
            end = endPoint;
            curveStart = start;
            hasRoute = false;
            float verticalDistance = Mathf.Max(
                32f,
                Mathf.Abs(end.y - start.y) * 0.42f);
            controlA = curveStart + Vector2.down * verticalDistance;
            controlB = end + Vector2.up * verticalDistance;
            hasGeometry = true;
            MarkDirtyRepaint();
        }

        public void SetRoutedEndpoints(
            Vector2 startPoint,
            Vector2 firstRoutePoint,
            Vector2 secondRoutePoint,
            Vector2 endPoint)
        {
            start = startPoint;
            routeA = firstRoutePoint;
            routeB = secondRoutePoint;
            curveStart = routeB;
            end = endPoint;
            hasRoute = true;
            float verticalDistance = Mathf.Max(
                24f,
                Mathf.Abs(end.y - curveStart.y) * 0.38f);
            controlA = curveStart + Vector2.down * verticalDistance;
            controlB = end + Vector2.up * verticalDistance;
            hasGeometry = true;
            MarkDirtyRepaint();
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (!hasGeometry || resolvedStyle.display == DisplayStyle.None)
                return false;

            Vector2 previous = curveStart;
            if (hasRoute &&
                (DistanceToSegment(localPoint, start, routeA) <=
                    HitTolerance ||
                 DistanceToSegment(localPoint, routeA, routeB) <=
                    HitTolerance))
            {
                return true;
            }

            const int segments = 28;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 current = EvaluateBezier(t);
                if (DistanceToSegment(localPoint, previous, current) <=
                    HitTolerance)
                {
                    return true;
                }
                previous = current;
            }

            return false;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!hasGeometry)
                return;

            Painter2D painter = context.painter2D;
            Color color = selected
                ? new Color(1f, 0.67f, 0.2f, 1f)
                : new Color(1f, 0.22f, 0.19f, 0.96f);

            DrawCurve(
                painter,
                new Color(color.r, color.g, color.b, 0.16f),
                selected ? 12f : 9f);
            DrawCurve(painter, color, selected ? 4f : 3f);
        }

        private void DrawCurve(Painter2D painter, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(start);
            if (hasRoute)
            {
                painter.LineTo(routeA);
                painter.LineTo(routeB);
            }
            painter.BezierCurveTo(controlA, controlB, end);
            painter.Stroke();
        }

        private Vector2 EvaluateBezier(float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * inverse * curveStart +
                   3f * inverse * inverse * t * controlA +
                   3f * inverse * t * t * controlB +
                   t * t * t * end;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, segmentStart);

            float t = Mathf.Clamp01(
                Vector2.Dot(point - segmentStart, segment) /
                lengthSquared);
            return Vector2.Distance(
                point,
                segmentStart + segment * t);
        }
    }
}
