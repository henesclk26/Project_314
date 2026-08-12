using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public class CircuitMissionUIManager : MonoBehaviour
{
    private const int GridSize = 7;
    private const int Up = 1;
    private const int Right = 2;
    private const int Down = 4;
    private const int Left = 8;
    private const float CloseAnimationDuration = 0.18f;
    private const float SuccessDisplayDuration = 1.8f;

    private static readonly Vector2Int[][] CircuitRoutes =
    {
        new[]
        {
            new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, 2),
            new Vector2Int(1, 3), new Vector2Int(2, 3), new Vector2Int(3, 3),
            new Vector2Int(3, 2), new Vector2Int(3, 1), new Vector2Int(4, 1),
            new Vector2Int(5, 1), new Vector2Int(5, 2), new Vector2Int(5, 3),
            new Vector2Int(4, 3), new Vector2Int(4, 4), new Vector2Int(4, 5),
            new Vector2Int(5, 5), new Vector2Int(6, 5)
        },
        new[]
        {
            new Vector2Int(0, 5), new Vector2Int(1, 5), new Vector2Int(2, 5),
            new Vector2Int(2, 4), new Vector2Int(2, 3), new Vector2Int(1, 3),
            new Vector2Int(1, 2), new Vector2Int(1, 1), new Vector2Int(2, 1),
            new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(3, 3),
            new Vector2Int(4, 3), new Vector2Int(5, 3), new Vector2Int(5, 2),
            new Vector2Int(5, 1), new Vector2Int(6, 1)
        },
        new[]
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1),
            new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(3, 3),
            new Vector2Int(2, 3), new Vector2Int(1, 3), new Vector2Int(1, 4),
            new Vector2Int(1, 5), new Vector2Int(2, 5), new Vector2Int(3, 5),
            new Vector2Int(4, 5), new Vector2Int(5, 5), new Vector2Int(5, 6)
        }
    };

    public static CircuitMissionUIManager Instance { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsSabotageMode => isSabotageMode;

    private VisualElement overlay;
    private VisualElement panel;
    private VisualElement circuitGrid;
    private VisualElement powerProgressFill;
    private VisualElement missionComplete;
    private Label headerTitle;
    private Label headerKicker;
    private Label connectionLabel;
    private Label actualCaption;
    private Label targetCaption;
    private Label actualVoltage;
    private Label targetVoltage;
    private Label routeProgress;
    private Label systemStatus;
    private Label templateLabel;
    private Label selectedNodeLabel;
    private Label systemNoteText;
    private Label completeSubtitle;
    private Button closeButton;
    private Button resetButton;

    private readonly CircuitTileState[] normalTiles = new CircuitTileState[GridSize * GridSize];
    private readonly CircuitTileState[] sabotageTiles = new CircuitTileState[GridSize * GridSize];
    private readonly CircuitTileElement[] tileElements = new CircuitTileElement[GridSize * GridSize];
    private readonly bool[] sabotageEnergized = new bool[GridSize * GridSize];

    private FirstPersonController currentFpc;
    private CircuitMissionInteractable currentTerminal;
    private Coroutine closeRoutine;
    private int[] activeRouteIndices;
    private int selectedIndex = -1;
    private int normalSelectedIndex = -1;
    private int sabotageSelectedIndex = -1;
    private int normalTemplateIndex;
    private int lastSabotageTemplate = -1;
    private int lastSabotageRevision = -1;
    private bool completionStarted;
    private bool normalPuzzleReady;
    private int normalTaskRevision = -1;
    private bool isSabotageMode;
    private bool normalCompletedWhenOpened;
    private bool sabotageCompletedWhenOpened;

    private CircuitTileState[] ActiveTiles => isSabotageMode ? sabotageTiles : normalTiles;

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
        overlay = root.Q<VisualElement>("circuit-overlay");
        panel = root.Q<VisualElement>("circuit-panel");
        circuitGrid = root.Q<VisualElement>("circuit-grid");
        powerProgressFill = root.Q<VisualElement>("power-progress-fill");
        missionComplete = root.Q<VisualElement>("mission-complete");
        headerTitle = root.Q<Label>("header-title");
        headerKicker = root.Q<Label>("header-kicker");
        connectionLabel = root.Q<Label>("connection-label");
        actualCaption = root.Q<Label>("actual-caption");
        targetCaption = root.Q<Label>("target-caption");
        actualVoltage = root.Q<Label>("actual-voltage");
        targetVoltage = root.Q<Label>("target-voltage");
        routeProgress = root.Q<Label>("route-progress");
        systemStatus = root.Q<Label>("system-status");
        templateLabel = root.Q<Label>("template-label");
        selectedNodeLabel = root.Q<Label>("selected-node");
        systemNoteText = root.Q<Label>("system-note-text");
        completeSubtitle = root.Q<Label>("complete-subtitle");
        closeButton = root.Q<Button>("close-btn");
        resetButton = root.Q<Button>("reset-btn");

        closeButton.clicked += Close;
        resetButton.clicked += ResetCircuit;
    }

    public void Open(CircuitMissionInteractable terminal, FirstPersonController fpc)
    {
        if (IsOpen)
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
        // Repeatable TaskRuns own completion. MissionManager flags are reset by
        // TaskManager when the current normal/rogue run starts.
        normalCompletedWhenOpened = false;
        sabotageCompletedWhenOpened = false;
        IsOpen = true;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        if (CanOpenRogueMode())
        {
            isSabotageMode = true;
            MissionManager.Instance?.RequestInitializeCircuitSabotage();
        }
#endif

        if (!normalPuzzleReady ||
            (MissionManager.Instance != null &&
             normalTaskRevision != MissionManager.Instance.CircuitMissionRevision.Value))
            BuildNormalPuzzle();
        ApplyModePresentation();

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
        RepaintTiles();
    }

    private void BuildNormalPuzzle()
    {
        normalPuzzleReady = false;
        normalTemplateIndex = new System.Random(Environment.TickCount ^ GetInstanceID())
            .Next(CircuitRoutes.Length);
        Vector2Int[] route = CircuitRoutes[normalTemplateIndex];
        activeRouteIndices = new int[route.Length];

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int index = ToIndex(x, y);
                normalTiles[index] = new CircuitTileState
                {
                    Coordinate = new Vector2Int(x, y),
                    IsLocked = ((x * 3 + y * 5 + normalTemplateIndex * 2) % 9) == 0
                };
            }
        }

        for (int i = 0; i < route.Length; i++)
        {
            int index = ToIndex(route[i].x, route[i].y);
            int requiredMask = 0;
            if (i > 0)
                requiredMask |= DirectionBetween(route[i], route[i - 1]);
            if (i < route.Length - 1)
                requiredMask |= DirectionBetween(route[i], route[i + 1]);

            CircuitTileState tile = normalTiles[index];
            tile.IsPath = true;
            tile.IsLocked = false;
            tile.IsSource = i == 0;
            tile.IsSink = i == route.Length - 1;
            tile.PathOrder = i;
            tile.RequiredMask = requiredMask;
            tile.IsRotatable = !tile.IsSource && !tile.IsSink;

            int turns = tile.IsRotatable
                ? (IsStraight(requiredMask) ? 1 : UnityEngine.Random.Range(1, 4))
                : 0;
            tile.CurrentMask = RotateMask(requiredMask, turns);
            tile.InitialMask = tile.CurrentMask;
            activeRouteIndices[i] = index;
        }

        normalSelectedIndex = activeRouteIndices.Length > 1 ? activeRouteIndices[1] : -1;
        normalPuzzleReady = true;
        normalTaskRevision = MissionManager.Instance != null
            ? MissionManager.Instance.CircuitMissionRevision.Value
            : 0;
        UpdateNormalPowerFlow(false);
    }

    private void Update()
    {
        if (!IsOpen)
            return;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        if (!isSabotageMode && CanOpenRogueMode())
        {
            isSabotageMode = true;
            MissionManager.Instance?.RequestInitializeCircuitSabotage();
            ApplyModePresentation();
        }
#endif

        if (isSabotageMode)
        {
            SyncSabotageBoard();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleSabotageMode();
            return;
        }
#endif

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        bool activeMissionCompleted = isSabotageMode
            ? IsSabotageMissionCompleted()
            : IsNormalMissionCompleted();
        if (activeMissionCompleted)
            return;

        if (Input.GetKeyDown(KeyCode.R))
            ResetCircuit();

        if (Input.GetKeyDown(KeyCode.Q))
            RotateSelected(-1);
        else if (Input.GetKeyDown(KeyCode.E))
            RotateSelected(1);

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            MoveSelection(0, -1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            MoveSelection(0, 1);
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            MoveSelection(-1, 0);
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            MoveSelection(1, 0);
    }

    private void ToggleSabotageMode()
    {
        if (completionStarted)
            return;

        if (isSabotageMode)
        {
            sabotageSelectedIndex = selectedIndex;
            isSabotageMode = false;
            selectedIndex = normalSelectedIndex;
        }
        else
        {
            if (!CanOpenRogueMode() && !IsDevelopmentSabotagePreviewEnabled())
            {
                return;
            }

            normalSelectedIndex = selectedIndex;
            isSabotageMode = true;
            MissionManager.Instance?.RequestInitializeCircuitSabotage();
            SyncSabotageBoard(true);
            selectedIndex = sabotageSelectedIndex;
        }

        ApplyModePresentation();
    }

    private bool CanOpenRogueMode()
    {
        return currentFpc != null &&
               TaskManager.Instance != null &&
               TaskManager.Instance.CanUseRogueTask(
                   currentFpc.OwnerClientId,
                   "CircuitMission");
    }

    private static bool IsDevelopmentSabotagePreviewEnabled()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    private void ApplyModePresentation()
    {
        panel.EnableInClassList("sabotage-mode", isSabotageMode);
        missionComplete.EnableInClassList("sabotage-success", isSabotageMode);
        missionComplete.AddToClassList("hidden");
        missionComplete.RemoveFromClassList("success-pulse");
        closeButton.SetEnabled(true);
        resetButton.SetEnabled(true);

        if (isSabotageMode)
        {
            headerTitle.text = "POWER DIVERSION";
            headerKicker.text = "UNAUTHORIZED ROUTING // SECONDARY BUS";
            connectionLabel.text = "DIVERSION LINK ACTIVE";
            actualCaption.text = "PRIMARY OUTPUT";
            targetCaption.text = "SECONDARY OUTPUT";
            targetVoltage.text = "0";
            systemNoteText.text = "CUT PRIMARY OUTPUT AND ROUTE POWER TO THE SECONDARY BUS.";
            completeSubtitle.text = "SECONDARY POWER ROUTE ESTABLISHED";
            SyncSabotageBoard(true);
            if (IsSabotageMissionCompleted())
                ShowCompletedState(true);
        }
        else
        {
            headerTitle.text = "POWER ROUTING";
            headerKicker.text = "ENERGY TRANSFER // AUXILIARY GRID";
            connectionLabel.text = "SOURCE ONLINE";
            actualCaption.text = "ACTUAL VOLTAGE";
            targetCaption.text = "TARGET VOLTAGE";
            targetVoltage.text = "1";
            systemNoteText.text = "ALIGN ALL ACTIVE CONDUITS TO ESTABLISH A STABLE POWER ROUTE.";
            completeSubtitle.text = "POWER ROUTE ESTABLISHED";
            templateLabel.text = $"ROUTE PATTERN // {normalTemplateIndex + 1:00}";
            selectedIndex = normalSelectedIndex;
            RenderBoard(normalTiles);
            UpdateNormalPowerFlow(false);
            if (IsNormalMissionCompleted())
                ShowCompletedState(false);
        }
    }

    private void SyncSabotageBoard(bool force = false)
    {
        if (!isSabotageMode || MissionManager.Instance == null)
            return;

        MissionManager mission = MissionManager.Instance;
        if (!mission.IsCircuitSabotageInitialized.Value)
        {
            circuitGrid.Clear();
            Array.Clear(tileElements, 0, tileElements.Length);
            actualVoltage.text = "1";
            targetVoltage.text = "0";
            routeProgress.text = "SYNCING";
            systemStatus.text = "INITIALIZING DIVERSION BUS";
            powerProgressFill.style.width = Length.Percent(0f);
            return;
        }

        int templateIndex = mission.CircuitSabotageTemplateIndex.Value;
        int revision = mission.CircuitSabotageRevision.Value;
        if (!force &&
            templateIndex == lastSabotageTemplate &&
            revision == lastSabotageRevision)
        {
            return;
        }
        if (templateIndex < 0 || templateIndex >= CircuitSabotageTemplates.All.Length)
            return;

        lastSabotageTemplate = templateIndex;
        lastSabotageRevision = revision;
        CircuitSabotageTemplates.Template template =
            CircuitSabotageTemplates.All[templateIndex];
        ulong packedState = mission.CircuitSabotagePackedState.Value;

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int index = ToIndex(x, y);
                sabotageTiles[index] = new CircuitTileState
                {
                    Coordinate = new Vector2Int(x, y),
                    IsLocked = ((x * 5 + y * 3 + templateIndex) % 8) == 0,
                    IsSabotage = true
                };
            }
        }

        for (int i = 0; i < template.Nodes.Length; i++)
        {
            CircuitSabotageTemplates.Node node = template.Nodes[i];
            int index = ToIndex(node.Coordinate.x, node.Coordinate.y);
            CircuitTileState tile = sabotageTiles[index];
            tile.IsPath = true;
            tile.IsLocked = false;
            tile.IsSource = node.IsSource;
            tile.IsSink = node.IsPrimarySink || node.IsSecondarySink;
            tile.IsPrimarySink = node.IsPrimarySink;
            tile.IsSecondarySink = node.IsSecondarySink;
            tile.IsRotatable = node.Slot >= 0;
            tile.SabotageSlot = node.Slot;
            tile.PathOrder = i;
            tile.CurrentMask = CircuitSabotageTemplates.GetMask(template, node, packedState);
            tile.InitialMask = node.InitialMask;
            tile.RequiredMask = node.TargetMask;
        }

        bool success = CircuitSabotageTemplates.Evaluate(
            template,
            packedState,
            out bool primaryPowered,
            out bool secondaryPowered,
            sabotageEnergized);
        int energizedCount = 0;
        for (int i = 0; i < sabotageTiles.Length; i++)
        {
            sabotageTiles[i].IsEnergized = sabotageEnergized[i];
            if (sabotageEnergized[i])
                energizedCount++;
        }

        if (sabotageSelectedIndex < 0 ||
            sabotageSelectedIndex >= sabotageTiles.Length ||
            !sabotageTiles[sabotageSelectedIndex].IsRotatable)
        {
            sabotageSelectedIndex = FindFirstRotatable(sabotageTiles);
        }
        selectedIndex = sabotageSelectedIndex;
        templateLabel.text = $"DIVERSION PATTERN // {templateIndex + 1:00}";
        actualVoltage.text = primaryPowered ? "1" : "0";
        targetVoltage.text = secondaryPowered ? "1" : "0";
        routeProgress.text = $"{energizedCount:00} / {template.Nodes.Length:00}";
        powerProgressFill.style.width = Length.Percent(
            template.Nodes.Length == 0 ? 0f : energizedCount * 100f / template.Nodes.Length);
        systemStatus.text = success
            ? "SECONDARY BUS ACTIVE"
            : secondaryPowered
                ? "PRIMARY BUS STILL CONNECTED"
                : primaryPowered
                    ? "PRIMARY BUS ACTIVE"
                    : "DIVERTING POWER";
        systemStatus.EnableInClassList("powered", success);
        RenderBoard(sabotageTiles);
        RefreshSelection();
    }

    private void RenderBoard(CircuitTileState[] states)
    {
        circuitGrid.Clear();
        for (int i = 0; i < states.Length; i++)
        {
            CircuitTileElement element = new CircuitTileElement(i, states[i], HandleTilePointer);
            element.style.width = Length.Percent(100f / GridSize);
            element.style.height = Length.Percent(100f / GridSize);
            circuitGrid.Add(element);
            tileElements[i] = element;
        }
        RefreshSelection();
    }

    private void HandleTilePointer(int index, int button)
    {
        CircuitTileState[] states = ActiveTiles;
        if (!IsOpen ||
            completionStarted ||
            index < 0 ||
            index >= states.Length ||
            !states[index].IsRotatable)
        {
            return;
        }

        selectedIndex = index;
        if (isSabotageMode)
            sabotageSelectedIndex = index;
        else
            normalSelectedIndex = index;
        RefreshSelection();
        RotateSelected(button == 1 ? -1 : 1);
    }

    private void MoveSelection(int deltaX, int deltaY)
    {
        CircuitTileState[] states = ActiveTiles;
        if (selectedIndex < 0 || selectedIndex >= states.Length)
            return;

        Vector2Int coordinate = states[selectedIndex].Coordinate;
        for (int step = 1; step < GridSize; step++)
        {
            int x = coordinate.x + deltaX * step;
            int y = coordinate.y + deltaY * step;
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
                break;

            int candidate = ToIndex(x, y);
            if (!states[candidate].IsRotatable)
                continue;

            selectedIndex = candidate;
            if (isSabotageMode)
                sabotageSelectedIndex = candidate;
            else
                normalSelectedIndex = candidate;
            RefreshSelection();
            return;
        }
    }

    private void RotateSelected(int direction)
    {
        CircuitTileState[] states = ActiveTiles;
        if (selectedIndex < 0 ||
            selectedIndex >= states.Length ||
            !states[selectedIndex].IsRotatable)
        {
            return;
        }

        if (isSabotageMode)
        {
            MissionManager.Instance?.RequestRotateCircuitSabotageNode(
                states[selectedIndex].SabotageSlot,
                direction);
            SyncSabotageBoard(true);
        }
        else
        {
            states[selectedIndex].CurrentMask =
                RotateMask(states[selectedIndex].CurrentMask, direction);
            UpdateNormalPowerFlow(true);
            RefreshSelection();
        }
    }

    private void ResetCircuit()
    {
        if (!IsOpen || completionStarted)
            return;

        if (isSabotageMode)
        {
            MissionManager.Instance?.RequestResetCircuitSabotage();
            SyncSabotageBoard(true);
            return;
        }

        if (!normalPuzzleReady || IsNormalMissionCompleted())
            return;

        foreach (int index in activeRouteIndices)
        {
            if (normalTiles[index].IsRotatable)
                normalTiles[index].CurrentMask = normalTiles[index].InitialMask;
        }

        normalSelectedIndex = activeRouteIndices.Length > 1 ? activeRouteIndices[1] : -1;
        selectedIndex = normalSelectedIndex;
        UpdateNormalPowerFlow(false);
        RefreshSelection();
    }

    private void UpdateNormalPowerFlow(bool allowCompletion)
    {
        foreach (CircuitTileState tile in normalTiles)
            tile.IsEnergized = false;

        int energizedCount = 0;
        if (activeRouteIndices.Length > 0)
        {
            normalTiles[activeRouteIndices[0]].IsEnergized = true;
            energizedCount = 1;
        }

        for (int i = 0; i < activeRouteIndices.Length - 1; i++)
        {
            CircuitTileState current = normalTiles[activeRouteIndices[i]];
            CircuitTileState next = normalTiles[activeRouteIndices[i + 1]];
            if (!current.IsEnergized)
                break;

            int direction = DirectionBetween(current.Coordinate, next.Coordinate);
            if (!HasConnection(current.CurrentMask, direction) ||
                !HasConnection(next.CurrentMask, Opposite(direction)))
            {
                break;
            }

            next.IsEnergized = true;
            energizedCount++;
        }

        bool outputPowered = activeRouteIndices.Length > 0 &&
                             normalTiles[activeRouteIndices[^1]].IsEnergized;
        float progress = activeRouteIndices.Length == 0
            ? 0f
            : energizedCount / (float)activeRouteIndices.Length;

        actualVoltage.text = outputPowered ? "1" : "0";
        routeProgress.text = $"{energizedCount:00} / {activeRouteIndices.Length:00}";
        powerProgressFill.style.width = Length.Percent(progress * 100f);
        systemStatus.text = outputPowered ? "OUTPUT STABLE" : "ROUTING POWER";
        systemStatus.EnableInClassList("powered", outputPowered);
        RepaintTiles();

        if (allowCompletion && outputPowered)
            CompleteNormalMission(true);
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < tileElements.Length; i++)
            tileElements[i]?.Refresh(i == selectedIndex);

        CircuitTileState[] states = ActiveTiles;
        if (selectedIndex >= 0 &&
            selectedIndex < states.Length &&
            states[selectedIndex] != null)
        {
            CircuitTileState selected = states[selectedIndex];
            string prefix = isSabotageMode ? "JUNCTION" : "NODE";
            selectedNodeLabel.text =
                $"{prefix} {selected.PathOrder + 1:00} // X{selected.Coordinate.x + 1} Y{selected.Coordinate.y + 1}";
        }
        else
        {
            selectedNodeLabel.text = isSabotageMode ? "JUNCTION --" : "NODE --";
        }
    }

    private void RepaintTiles()
    {
        foreach (CircuitTileElement element in tileElements)
            element?.Refresh(element.Index == selectedIndex);
    }

    private void CompleteNormalMission(bool reportToServer)
    {
        if (completionStarted)
            return;

        completionStarted = true;
        currentTerminal?.MarkCompleted();
        if (reportToServer)
        {
            if (currentFpc != null &&
                TaskManager.Instance != null &&
                TaskManager.Instance.IsSpawned)
            {
                TaskManager.Instance.ReportTaskCompletedRpc("CircuitMission");
            }
            else
            {
                Debug.LogWarning(
                    "[CircuitMissionUI] Could not report CircuitMission completion: " +
                    "local player or spawned TaskManager is missing.");
            }

            // In an online match TaskManager marks the mission state only
            // after validating this player's TaskRun. Keep the direct path
            // for Quick Test, where there is no spawned TaskManager.
            if ((TaskManager.Instance == null || !TaskManager.Instance.IsSpawned) &&
                MissionManager.Instance != null)
            {
                MissionManager.Instance.CompleteNormalTaskServer("CircuitMission");
            }
        }

        ShowCompletedState(false);
        closeButton.SetEnabled(false);
        resetButton.SetEnabled(false);
        closeRoutine = StartCoroutine(CloseAfterSuccess());
    }

    private void CompleteSabotage()
    {
        if (completionStarted)
            return;

        completionStarted = true;
        if (currentFpc != null &&
            TaskManager.Instance != null &&
            TaskManager.Instance.IsSpawned)
        {
            TaskManager.Instance.ReportTaskCompletedRpc("CircuitMission");
        }
        ShowCompletedState(true);
        closeButton.SetEnabled(false);
        resetButton.SetEnabled(false);
        closeRoutine = StartCoroutine(CloseAfterSuccess());
    }

    private void ShowCompletedState(bool sabotage)
    {
        actualVoltage.text = sabotage ? "0" : "1";
        if (sabotage)
            targetVoltage.text = "1";
        systemStatus.text = sabotage ? "SECONDARY BUS ACTIVE" : "OUTPUT STABLE";
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
        resetButton.SetEnabled(true);
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

    private static int FindFirstRotatable(CircuitTileState[] states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] != null && states[i].IsRotatable)
                return i;
        }
        return -1;
    }

    private static int ToIndex(int x, int y)
    {
        return y * GridSize + x;
    }

    private static int DirectionBetween(Vector2Int from, Vector2Int to)
    {
        Vector2Int difference = to - from;
        if (difference == Vector2Int.up)
            return Down;
        if (difference == Vector2Int.down)
            return Up;
        if (difference == Vector2Int.right)
            return Right;
        return Left;
    }

    private static int Opposite(int direction)
    {
        return direction switch
        {
            Up => Down,
            Right => Left,
            Down => Up,
            _ => Right
        };
    }

    private static bool HasConnection(int mask, int direction)
    {
        return (mask & direction) != 0;
    }

    private static bool IsStraight(int mask)
    {
        return mask == (Up | Down) || mask == (Left | Right);
    }

    private static int RotateMask(int mask, int turns)
    {
        turns = (turns % 4 + 4) % 4;
        for (int i = 0; i < turns; i++)
            mask = ((mask << 1) & 0xF) | ((mask >> 3) & 1);
        return mask;
    }

    private static bool IsNormalMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsCircuitMissionCompleted.Value;
    }

    private static bool IsSabotageMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsCircuitSabotageCompleted.Value;
    }

    private static bool AreBothMissionsCompleted()
    {
        return IsNormalMissionCompleted() && IsSabotageMissionCompleted();
    }

    private sealed class CircuitTileState
    {
        public Vector2Int Coordinate;
        public int PathOrder = -1;
        public int RequiredMask;
        public int CurrentMask;
        public int InitialMask;
        public int SabotageSlot = -1;
        public bool IsPath;
        public bool IsSource;
        public bool IsSink;
        public bool IsPrimarySink;
        public bool IsSecondarySink;
        public bool IsRotatable;
        public bool IsLocked;
        public bool IsEnergized;
        public bool IsSabotage;
    }

    private sealed class CircuitTileElement : VisualElement
    {
        private readonly CircuitTileState state;
        private readonly VisualElement node;

        public int Index { get; }

        public CircuitTileElement(
            int index,
            CircuitTileState tileState,
            Action<int, int> pointerHandler)
        {
            Index = index;
            state = tileState;
            AddToClassList("circuit-cell");

            if (state.IsPath) AddToClassList("path-cell");
            if (state.IsLocked) AddToClassList("locked-cell");
            if (state.IsSource) AddToClassList("source-cell");
            if (state.IsSink) AddToClassList("sink-cell");
            if (state.IsPrimarySink) AddToClassList("primary-sink-cell");
            if (state.IsSecondarySink) AddToClassList("secondary-sink-cell");
            if (state.IsRotatable) AddToClassList("rotatable-cell");
            if (state.IsSabotage) AddToClassList("sabotage-cell");

            node = new VisualElement();
            node.AddToClassList("circuit-node");
            Add(node);

            if (state.IsSource || state.IsSink)
            {
                string endpointText = state.IsSource
                    ? "IN"
                    : state.IsPrimarySink
                        ? "OUT-A"
                        : state.IsSecondarySink
                            ? "OUT-B"
                            : "OUT";
                Label label = new Label(endpointText);
                label.AddToClassList("endpoint-label");
                Add(label);
            }

            generateVisualContent += DrawConnections;
            RegisterCallback<PointerDownEvent>(evt =>
            {
                pointerHandler?.Invoke(Index, evt.button);
                evt.StopPropagation();
            });
        }

        public void Refresh(bool selected)
        {
            EnableInClassList("selected", selected);
            EnableInClassList("energized", state.IsEnergized);
            node.EnableInClassList("energized", state.IsEnergized);
            MarkDirtyRepaint();
        }

        private void DrawConnections(MeshGenerationContext context)
        {
            if (!state.IsPath)
                return;

            Rect rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            Color lineColor;
            if (state.IsSabotage)
            {
                lineColor = state.IsEnergized
                    ? new Color(1f, 0.19f, 0.17f, 1f)
                    : state.IsSecondarySink
                        ? new Color(1f, 0.55f, 0.25f, 0.92f)
                        : new Color(0.8f, 0.32f, 0.28f, 0.68f);
            }
            else if (state.IsEnergized)
            {
                lineColor = new Color(0f, 1f, 0.5f, 1f);
            }
            else if (state.IsSink)
            {
                lineColor = new Color(1f, 0.77f, 0f, 0.9f);
            }
            else
            {
                lineColor = new Color(0f, 0.94f, 1f, 0.72f);
            }

            Painter2D painter = context.painter2D;
            DrawMask(painter, rect, state.CurrentMask,
                new Color(lineColor.r, lineColor.g, lineColor.b, 0.14f), 9f);
            DrawMask(painter, rect, state.CurrentMask, lineColor,
                state.IsEnergized ? 3.5f : 2.5f);
        }

        private static void DrawMask(Painter2D painter, Rect rect, int mask, Color color, float width)
        {
            Vector2 center = rect.center;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();

            if (HasConnection(mask, Up))
            {
                painter.MoveTo(center);
                painter.LineTo(new Vector2(center.x, 0f));
            }
            if (HasConnection(mask, Right))
            {
                painter.MoveTo(center);
                painter.LineTo(new Vector2(rect.width, center.y));
            }
            if (HasConnection(mask, Down))
            {
                painter.MoveTo(center);
                painter.LineTo(new Vector2(center.x, rect.height));
            }
            if (HasConnection(mask, Left))
            {
                painter.MoveTo(center);
                painter.LineTo(new Vector2(0f, center.y));
            }

            painter.Stroke();
        }
    }
}
