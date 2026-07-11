using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class PuzzleUIManager : MonoBehaviour
{
    public static PuzzleUIManager Instance { get; private set; }
    public bool IsPuzzleOpen { get; private set; } = false;
    public bool WasClosedThisFrame { get; private set; } = false;

    private UIDocument uiDocument;
    private VisualElement overlay;
    private VisualElement gridContainer;
    private Button closeBtn;
    private Label subtitleLabel;

    private List<VisualElement> cells = new List<VisualElement>();
    private List<int> currentPath = new List<int>();
    
    private bool isDrawing = false;
    private int nextExpectedNode = 2;

    // Hardcoded Puzzle Design for a 4x4 Grid
    // 0  1  2  3
    // 4  5  6  7
    // 8  9 10 11
    // 12 13 14 15
    private Dictionary<int, int> nodePositions = new Dictionary<int, int>()
    {
        { 0, 1 },  // Index 0 is Node 1
        { 12, 2 }, // Index 12 is Node 2
        { 10, 3 }, // Index 10 is Node 3
        { 15, 4 }, // Index 15 is Node 4
        { 7, 5 },  // Index 7 is Node 5
        { 6, 6 }   // Index 6 is Node 6
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            InitializeUI(uiDocument.rootVisualElement);
        }
    }

    private void InitializeUI(VisualElement root)
    {
        overlay = root.Q<VisualElement>("puzzle-overlay");
        gridContainer = root.Q<VisualElement>("puzzle-grid");
        closeBtn = root.Q<Button>("close-btn");
        subtitleLabel = root.Q<Label>(className: "header-subtitle");

        closeBtn.clicked += ClosePuzzle;

        GenerateGrid();
    }

    private void GenerateGrid()
    {
        gridContainer.Clear();
        cells.Clear();

        for (int i = 0; i < 16; i++)
        {
            var cell = new VisualElement();
            cell.AddToClassList("puzzle-cell");
            cell.userData = i; // store index

            if (nodePositions.ContainsKey(i))
            {
                var node = new Label(nodePositions[i].ToString());
                node.AddToClassList("puzzle-node");
                cell.Add(node);
            }

            // UI Toolkit Event registrations
            cell.RegisterCallback<PointerDownEvent>(OnCellPointerDown);
            cell.RegisterCallback<PointerEnterEvent>(OnCellPointerEnter);

            gridContainer.Add(cell);
            cells.Add(cell);
        }

        gridContainer.RegisterCallback<PointerUpEvent>(evt => StopDrawing());
        gridContainer.RegisterCallback<PointerLeaveEvent>(evt => {
            // Stop drawing if we leave the grid completely
            if (evt.target == gridContainer) StopDrawing();
        });
    }

    public void OpenPuzzle()
    {
        if (IsPuzzleOpen) return;
        
        IsPuzzleOpen = true;
        overlay.style.display = DisplayStyle.Flex;

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        var fpc = GetOwnerFpc();
        if (fpc != null)
        {
            fpc.playerCanMove = false;
            fpc.cameraCanMove = false;
        }

        ResetPuzzle();
    }

    public void ClosePuzzle()
    {
        if (!IsPuzzleOpen) return;

        IsPuzzleOpen = false;
        overlay.style.display = DisplayStyle.None;

        var fpc = GetOwnerFpc();
        if (fpc != null && !fpc.isDead.Value && (!GameManager.Instance || !GameManager.Instance.isGameOver))
        {
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
            fpc.playerCanMove = true;
            fpc.cameraCanMove = true;
        }
    }

    private void Update()
    {
        WasClosedThisFrame = false;
        if (IsPuzzleOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            ClosePuzzle();
            WasClosedThisFrame = true;
        }
    }

    private void ResetPuzzle()
    {
        currentPath.Clear();
        isDrawing = false;
        nextExpectedNode = 2;
        subtitleLabel.text = "Sayıları sırasıyla birleştir ve tüm alanı doldur.";
        subtitleLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));

        foreach (var cell in cells)
        {
            cell.RemoveFromClassList("path");
            cell.RemoveFromClassList("head");
            var node = cell.Q<Label>(className: "puzzle-node");
            if (node != null) node.RemoveFromClassList("reached");
        }
    }

    private void OnCellPointerDown(PointerDownEvent evt)
    {
        var cell = evt.currentTarget as VisualElement;
        int index = (int)cell.userData;

        // Start drawing only from Node 1
        if (nodePositions.ContainsKey(index) && nodePositions[index] == 1)
        {
            ResetPuzzle();
            isDrawing = true;
            gridContainer.CapturePointer(evt.pointerId);
            AddToPath(index);
        }
    }

    private void OnCellPointerEnter(PointerEnterEvent evt)
    {
        if (!isDrawing) return;

        var cell = evt.currentTarget as VisualElement;
        int index = (int)cell.userData;

        if (currentPath.Contains(index))
        {
            // Allowed to backtrack to previous cell
            if (currentPath.Count > 1 && currentPath[currentPath.Count - 2] == index)
            {
                RemoveLastFromPath();
            }
            return;
        }

        int lastIndex = currentPath[currentPath.Count - 1];
        if (IsAdjacent(lastIndex, index))
        {
            // If it's a node, verify it's the expected next node
            if (nodePositions.ContainsKey(index))
            {
                if (nodePositions[index] == nextExpectedNode)
                {
                    nextExpectedNode++;
                    AddToPath(index);
                }
                else
                {
                    // Hit wrong node, stop drawing
                    StopDrawing();
                }
            }
            else
            {
                AddToPath(index);
            }
        }
    }

    private void AddToPath(int index)
    {
        if (currentPath.Count > 0)
        {
            cells[currentPath[currentPath.Count - 1]].RemoveFromClassList("head");
        }

        currentPath.Add(index);
        cells[index].AddToClassList("path");
        cells[index].AddToClassList("head");

        var node = cells[index].Q<Label>(className: "puzzle-node");
        if (node != null) node.AddToClassList("reached");

        CheckWinCondition();
    }

    private void RemoveLastFromPath()
    {
        int lastIndex = currentPath[currentPath.Count - 1];
        cells[lastIndex].RemoveFromClassList("path");
        cells[lastIndex].RemoveFromClassList("head");
        
        if (nodePositions.ContainsKey(lastIndex))
        {
            nextExpectedNode--;
            var node = cells[lastIndex].Q<Label>(className: "puzzle-node");
            if (node != null) node.RemoveFromClassList("reached");
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        
        if (currentPath.Count > 0)
        {
            cells[currentPath[currentPath.Count - 1]].AddToClassList("head");
        }
    }

    private void StopDrawing()
    {
        isDrawing = false;
        if (currentPath.Count > 0)
        {
            cells[currentPath[currentPath.Count - 1]].RemoveFromClassList("head");
        }
        gridContainer.ReleaseMouse();
    }

    private bool IsAdjacent(int a, int b)
    {
        int r1 = a / 4, c1 = a % 4;
        int r2 = b / 4, c2 = b % 4;
        return Mathf.Abs(r1 - r2) + Mathf.Abs(c1 - c2) == 1;
    }

    private void CheckWinCondition()
    {
        // Require 6 nodes to be reached AND all 16 cells to be filled
        if (nextExpectedNode > 6 && currentPath.Count == 16)
        {
            subtitleLabel.text = "GÜVENLİK AŞILDI! KAPI AÇILDI.";
            subtitleLabel.style.color = new StyleColor(new Color(0.2f, 1f, 0.2f));
            StopDrawing();

            // Notify global manager
            if (MissionManager.Instance != null)
            {
                MissionManager.Instance.UnlockBatteryRoomServerRpc();
            }
            
            // Auto close after 2 seconds
            StartCoroutine(AutoClose());
        }
    }

    private IEnumerator AutoClose()
    {
        yield return new WaitForSeconds(2f);
        ClosePuzzle();
    }

    private FirstPersonController GetOwnerFpc()
    {
        var allFpcs = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (var f in allFpcs) if (f.IsOwner) return f;
        if (allFpcs.Length > 0) return allFpcs[0];
        return null;
    }
}
