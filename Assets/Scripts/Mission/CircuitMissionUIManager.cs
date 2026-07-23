using System;
using System.Collections;
using System.Collections.Generic;
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

    private VisualElement overlay;
    private VisualElement circuitGrid;
    private VisualElement powerProgressFill;
    private VisualElement missionComplete;
    private Label actualVoltage;
    private Label routeProgress;
    private Label systemStatus;
    private Label templateLabel;
    private Label selectedNodeLabel;
    private Button closeButton;
    private Button resetButton;

    private readonly CircuitTileState[] tiles = new CircuitTileState[GridSize * GridSize];
    private readonly CircuitTileElement[] tileElements = new CircuitTileElement[GridSize * GridSize];

    private FirstPersonController currentFpc;
    private CircuitMissionInteractable currentTerminal;
    private Coroutine closeRoutine;
    private int[] activeRouteIndices;
    private int selectedIndex = -1;
    private bool completionStarted;
    private bool puzzleReady;

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
        circuitGrid = root.Q<VisualElement>("circuit-grid");
        powerProgressFill = root.Q<VisualElement>("power-progress-fill");
        missionComplete = root.Q<VisualElement>("mission-complete");
        actualVoltage = root.Q<Label>("actual-voltage");
        routeProgress = root.Q<Label>("route-progress");
        systemStatus = root.Q<Label>("system-status");
        templateLabel = root.Q<Label>("template-label");
        selectedNodeLabel = root.Q<Label>("selected-node");
        closeButton = root.Q<Button>("close-btn");
        resetButton = root.Q<Button>("reset-btn");

        closeButton.clicked += Close;
        resetButton.clicked += ResetCircuit;
    }

    public void Open(CircuitMissionInteractable terminal, FirstPersonController fpc)
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

        if (!puzzleReady)
            BuildPuzzle();
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
    }

    private IEnumerator ShowAfterLayout()
    {
        yield return null;
        overlay.AddToClassList("open");
        RepaintTiles();
    }

    private void BuildPuzzle()
    {
        puzzleReady = false;
        circuitGrid.Clear();
        int templateIndex = new System.Random(System.Environment.TickCount ^ GetInstanceID())
            .Next(CircuitRoutes.Length);
        Vector2Int[] route = CircuitRoutes[templateIndex];
        activeRouteIndices = new int[route.Length];

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int index = ToIndex(x, y);
                tiles[index] = new CircuitTileState
                {
                    Coordinate = new Vector2Int(x, y),
                    IsLocked = ((x * 3 + y * 5 + templateIndex * 2) % 9) == 0
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

            CircuitTileState tile = tiles[index];
            tile.IsPath = true;
            tile.IsLocked = false;
            tile.IsSource = i == 0;
            tile.IsSink = i == route.Length - 1;
            tile.PathOrder = i;
            tile.RequiredMask = requiredMask;
            tile.IsRotatable = !tile.IsSource && !tile.IsSink;

            int turns = 0;
            if (tile.IsRotatable)
                turns = IsStraight(requiredMask) ? 1 : UnityEngine.Random.Range(1, 4);

            tile.CurrentMask = RotateMask(requiredMask, turns);
            tile.InitialMask = tile.CurrentMask;
            activeRouteIndices[i] = index;
        }

        for (int i = 0; i < tiles.Length; i++)
        {
            CircuitTileElement element = new CircuitTileElement(i, tiles[i], HandleTilePointer);
            element.style.width = Length.Percent(100f / GridSize);
            element.style.height = Length.Percent(100f / GridSize);
            circuitGrid.Add(element);
            tileElements[i] = element;
        }

        templateLabel.text = $"ROUTE PATTERN // {templateIndex + 1:00}";
        selectedIndex = activeRouteIndices.Length > 1 ? activeRouteIndices[1] : -1;
        puzzleReady = true;
        UpdatePowerFlow(false);
        RefreshSelection();
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

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

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

    private void HandleTilePointer(int index, int button)
    {
        if (!IsOpen || completionStarted || !tiles[index].IsRotatable)
            return;

        selectedIndex = index;
        RefreshSelection();
        RotateSelected(button == 1 ? -1 : 1);
    }

    private void MoveSelection(int deltaX, int deltaY)
    {
        if (selectedIndex < 0)
            return;

        Vector2Int coordinate = tiles[selectedIndex].Coordinate;
        for (int step = 1; step < GridSize; step++)
        {
            int x = coordinate.x + deltaX * step;
            int y = coordinate.y + deltaY * step;
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
                break;

            int candidate = ToIndex(x, y);
            if (tiles[candidate].IsRotatable)
            {
                selectedIndex = candidate;
                RefreshSelection();
                return;
            }
        }
    }

    private void RotateSelected(int direction)
    {
        if (selectedIndex < 0 || !tiles[selectedIndex].IsRotatable)
            return;

        tiles[selectedIndex].CurrentMask = RotateMask(tiles[selectedIndex].CurrentMask, direction);
        UpdatePowerFlow(true);
        RefreshSelection();
    }

    private void ResetCircuit()
    {
        if (!IsOpen || completionStarted || !puzzleReady)
            return;

        foreach (int index in activeRouteIndices)
        {
            if (tiles[index].IsRotatable)
                tiles[index].CurrentMask = tiles[index].InitialMask;
        }

        selectedIndex = activeRouteIndices.Length > 1 ? activeRouteIndices[1] : -1;
        UpdatePowerFlow(false);
        RefreshSelection();
    }

    private void UpdatePowerFlow(bool allowCompletion)
    {
        foreach (CircuitTileState tile in tiles)
            tile.IsEnergized = false;

        int energizedCount = 0;
        if (activeRouteIndices.Length > 0)
        {
            tiles[activeRouteIndices[0]].IsEnergized = true;
            energizedCount = 1;
        }

        for (int i = 0; i < activeRouteIndices.Length - 1; i++)
        {
            CircuitTileState current = tiles[activeRouteIndices[i]];
            CircuitTileState next = tiles[activeRouteIndices[i + 1]];
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
                             tiles[activeRouteIndices[^1]].IsEnergized;
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
            CompleteMission(true);
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < tileElements.Length; i++)
        {
            if (tileElements[i] != null)
                tileElements[i].Refresh(i == selectedIndex);
        }

        if (selectedIndex >= 0)
        {
            CircuitTileState selected = tiles[selectedIndex];
            selectedNodeLabel.text =
                $"NODE {selected.PathOrder + 1:00} // X{selected.Coordinate.x + 1} Y{selected.Coordinate.y + 1}";
        }
        else
        {
            selectedNodeLabel.text = "NODE --";
        }
    }

    private void RepaintTiles()
    {
        foreach (CircuitTileElement element in tileElements)
            element?.Refresh(element.Index == selectedIndex);
    }

    private void CompleteMission(bool reportToServer)
    {
        if (completionStarted)
            return;

        completionStarted = true;
        currentTerminal?.MarkCompleted();

        if (reportToServer && MissionManager.Instance != null && MissionManager.Instance.IsSpawned)
            MissionManager.Instance.CompleteCircuitMissionRpc();

        actualVoltage.text = "1";
        systemStatus.text = "OUTPUT STABLE";
        missionComplete.RemoveFromClassList("hidden");
        missionComplete.AddToClassList("success-pulse");
        closeButton.SetEnabled(false);
        resetButton.SetEnabled(false);
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

    private static bool IsMissionCompleted()
    {
        return MissionManager.Instance != null &&
               MissionManager.Instance.IsCircuitMissionCompleted.Value;
    }

    private sealed class CircuitTileState
    {
        public Vector2Int Coordinate;
        public int PathOrder = -1;
        public int RequiredMask;
        public int CurrentMask;
        public int InitialMask;
        public bool IsPath;
        public bool IsSource;
        public bool IsSink;
        public bool IsRotatable;
        public bool IsLocked;
        public bool IsEnergized;
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

            if (state.IsPath)
                AddToClassList("path-cell");
            if (state.IsLocked)
                AddToClassList("locked-cell");
            if (state.IsSource)
                AddToClassList("source-cell");
            if (state.IsSink)
                AddToClassList("sink-cell");
            if (state.IsRotatable)
                AddToClassList("rotatable-cell");

            node = new VisualElement();
            node.AddToClassList("circuit-node");
            Add(node);

            if (state.IsSource || state.IsSink)
            {
                Label label = new Label(state.IsSource ? "IN" : "OUT");
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
            if (state.IsEnergized)
                lineColor = new Color(0f, 1f, 0.5f, 1f);
            else if (state.IsSink)
                lineColor = new Color(1f, 0.77f, 0f, 0.9f);
            else
                lineColor = new Color(0f, 0.94f, 1f, 0.72f);

            Painter2D painter = context.painter2D;
            DrawMask(painter, rect, state.CurrentMask, new Color(lineColor.r, lineColor.g, lineColor.b, 0.14f), 9f);
            DrawMask(painter, rect, state.CurrentMask, lineColor, state.IsEnergized ? 3.5f : 2.5f);
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
