using System;
using System.Collections.Generic;
using UnityEngine;

public static class CircuitSabotageTemplates
{
    public const int GridSize = 7;
    public const int Up = 1;
    public const int Right = 2;
    public const int Down = 4;
    public const int Left = 8;

    public sealed class Node
    {
        public Vector2Int Coordinate;
        public int InitialMask;
        public int TargetMask;
        public int Slot = -1;
        public bool IsSource;
        public bool IsPrimarySink;
        public bool IsSecondarySink;
    }

    public sealed class Template
    {
        public Node[] Nodes;
        public int[] NodeByGridIndex;
        public ulong InitialPackedState;
        public ulong TargetPackedState;
        public int RotatableCount;
        public int OptimalMoveCount;
    }

    public static readonly Template[] All =
    {
        Build(
            new[] { P(0, 3), P(1, 3), P(2, 3), P(3, 3) },
            new[] { P(3, 2), P(3, 1), P(4, 1), P(5, 1), P(6, 1) },
            new[] { P(3, 4), P(3, 5), P(4, 5), P(5, 5), P(6, 5) }),
        Build(
            new[] { P(3, 0), P(3, 1), P(3, 2), P(3, 3) },
            new[] { P(2, 3), P(1, 3), P(1, 4), P(1, 5), P(0, 5) },
            new[] { P(4, 3), P(5, 3), P(5, 4), P(5, 5), P(6, 5) }),
        Build(
            new[] { P(6, 3), P(5, 3), P(4, 3), P(3, 3) },
            new[] { P(3, 4), P(3, 5), P(2, 5), P(1, 5), P(0, 5) },
            new[] { P(3, 2), P(3, 1), P(2, 1), P(1, 1), P(0, 1) })
    };

    public static int GetMask(Template template, Node node, ulong packedState)
    {
        if (node.Slot < 0)
        {
            return node.InitialMask;
        }

        return (int)((packedState >> (node.Slot * 4)) & 0xFUL);
    }

    public static ulong Rotate(Template template, ulong packedState, int slot, int direction)
    {
        if (slot < 0 || slot >= template.RotatableCount)
        {
            return packedState;
        }

        int shift = slot * 4;
        int mask = (int)((packedState >> shift) & 0xFUL);
        int turns = direction < 0 ? 3 : 1;
        for (int i = 0; i < turns; i++)
        {
            mask = RotateClockwise(mask);
        }

        ulong clear = ~(0xFUL << shift);
        return (packedState & clear) | ((ulong)mask << shift);
    }

    public static bool Evaluate(
        Template template,
        ulong packedState,
        out bool primaryPowered,
        out bool secondaryPowered,
        bool[] energizedGrid = null)
    {
        if (energizedGrid != null)
        {
            Array.Clear(energizedGrid, 0, energizedGrid.Length);
        }

        primaryPowered = false;
        secondaryPowered = false;
        int sourceNode = -1;
        for (int i = 0; i < template.Nodes.Length; i++)
        {
            if (template.Nodes[i].IsSource)
            {
                sourceNode = i;
                break;
            }
        }

        if (sourceNode < 0)
        {
            return false;
        }

        bool[] visited = new bool[template.Nodes.Length];
        Queue<int> queue = new Queue<int>();
        visited[sourceNode] = true;
        queue.Enqueue(sourceNode);

        while (queue.Count > 0)
        {
            int nodeIndex = queue.Dequeue();
            Node node = template.Nodes[nodeIndex];
            if (energizedGrid != null)
            {
                energizedGrid[ToGridIndex(node.Coordinate)] = true;
            }

            primaryPowered |= node.IsPrimarySink;
            secondaryPowered |= node.IsSecondarySink;
            int mask = GetMask(template, node, packedState);

            VisitNeighbour(template, packedState, node, mask, node.Coordinate + Vector2Int.down, Up, Down, visited, queue);
            VisitNeighbour(template, packedState, node, mask, node.Coordinate + Vector2Int.right, Right, Left, visited, queue);
            VisitNeighbour(template, packedState, node, mask, node.Coordinate + Vector2Int.up, Down, Up, visited, queue);
            VisitNeighbour(template, packedState, node, mask, node.Coordinate + Vector2Int.left, Left, Right, visited, queue);
        }

        return secondaryPowered && !primaryPowered;
    }

    private static Template Build(Vector2Int[] common, Vector2Int[] primaryTail, Vector2Int[] secondaryTail)
    {
        Vector2Int[] primaryRoute = Concat(common, primaryTail);
        Vector2Int[] secondaryRoute = Concat(common, secondaryTail);
        Dictionary<Vector2Int, Node> nodes = new Dictionary<Vector2Int, Node>();

        AddRoute(nodes, primaryRoute, false);
        AddRoute(nodes, secondaryRoute, true);

        nodes[common[0]].IsSource = true;
        nodes[primaryTail[^1]].IsPrimarySink = true;
        nodes[secondaryTail[^1]].IsSecondarySink = true;
        nodes[secondaryTail[^1]].InitialMask = nodes[secondaryTail[^1]].TargetMask;

        HashSet<Vector2Int> rotatable = new HashSet<Vector2Int>();
        for (int i = 1; i < common.Length; i++)
        {
            rotatable.Add(common[i]);
        }
        for (int i = 0; i < secondaryTail.Length - 1; i++)
        {
            rotatable.Add(secondaryTail[i]);
        }

        Node junction = nodes[common[^1]];
        junction.InitialMask = MaskBetween(common[^1], common[^2]) | MaskBetween(common[^1], primaryTail[0]);
        junction.TargetMask = MaskBetween(common[^1], common[^2]) | MaskBetween(common[^1], secondaryTail[0]);

        int slot = 0;
        ulong initial = 0;
        ulong target = 0;
        int optimalMoves = 0;
        foreach (Node node in nodes.Values)
        {
            if (!rotatable.Contains(node.Coordinate) || node.IsSource || node.IsPrimarySink || node.IsSecondarySink)
            {
                continue;
            }

            node.Slot = slot;
            if (IsOnRoute(node.Coordinate, secondaryTail) && node != junction)
            {
                node.InitialMask = RotateClockwise(node.TargetMask);
            }

            initial |= (ulong)node.InitialMask << (slot * 4);
            target |= (ulong)node.TargetMask << (slot * 4);
            optimalMoves += QuarterTurnDistance(node.InitialMask, node.TargetMask);
            slot++;
        }

        Template template = new Template
        {
            Nodes = new Node[nodes.Count],
            NodeByGridIndex = CreateLookup(),
            InitialPackedState = initial,
            TargetPackedState = target,
            RotatableCount = slot,
            OptimalMoveCount = optimalMoves
        };
        nodes.Values.CopyTo(template.Nodes, 0);
        for (int i = 0; i < template.Nodes.Length; i++)
        {
            template.NodeByGridIndex[ToGridIndex(template.Nodes[i].Coordinate)] = i;
        }

        Validate(template);
        return template;
    }

    private static void AddRoute(Dictionary<Vector2Int, Node> nodes, Vector2Int[] route, bool targetOnly)
    {
        for (int i = 0; i < route.Length; i++)
        {
            if (!nodes.TryGetValue(route[i], out Node node))
            {
                node = new Node { Coordinate = route[i] };
                nodes.Add(route[i], node);
            }

            int mask = 0;
            if (i > 0)
            {
                mask |= MaskBetween(route[i], route[i - 1]);
            }
            if (i < route.Length - 1)
            {
                mask |= MaskBetween(route[i], route[i + 1]);
            }

            node.TargetMask |= mask;
            if (!targetOnly)
            {
                node.InitialMask |= mask;
            }
        }
    }

    private static void VisitNeighbour(
        Template template,
        ulong packedState,
        Node node,
        int nodeMask,
        Vector2Int coordinate,
        int direction,
        int opposite,
        bool[] visited,
        Queue<int> queue)
    {
        if ((nodeMask & direction) == 0 || coordinate.x < 0 || coordinate.y < 0 ||
            coordinate.x >= GridSize || coordinate.y >= GridSize)
        {
            return;
        }

        int neighbourIndex = template.NodeByGridIndex[ToGridIndex(coordinate)];
        if (neighbourIndex < 0 || visited[neighbourIndex])
        {
            return;
        }

        Node neighbour = template.Nodes[neighbourIndex];
        if ((GetMask(template, neighbour, packedState) & opposite) == 0)
        {
            return;
        }

        visited[neighbourIndex] = true;
        queue.Enqueue(neighbourIndex);
    }

    private static void Validate(Template template)
    {
        if (template.RotatableCount > 16)
        {
            throw new InvalidOperationException("Circuit sabotage templates support at most 16 rotatable nodes.");
        }
        if (template.OptimalMoveCount < 4 || template.OptimalMoveCount > 6)
        {
            throw new InvalidOperationException($"Circuit sabotage optimal solution must be 4-6 moves, got {template.OptimalMoveCount}.");
        }

        Evaluate(template, template.InitialPackedState, out bool initialPrimary, out bool initialSecondary);
        Evaluate(template, template.TargetPackedState, out bool targetPrimary, out bool targetSecondary);
        if (!initialPrimary || initialSecondary || targetPrimary || !targetSecondary)
        {
            throw new InvalidOperationException("Circuit sabotage template output states are invalid.");
        }
    }

    private static Vector2Int[] Concat(Vector2Int[] first, Vector2Int[] second)
    {
        Vector2Int[] result = new Vector2Int[first.Length + second.Length];
        Array.Copy(first, result, first.Length);
        Array.Copy(second, 0, result, first.Length, second.Length);
        return result;
    }

    private static int[] CreateLookup()
    {
        int[] lookup = new int[GridSize * GridSize];
        for (int i = 0; i < lookup.Length; i++)
        {
            lookup[i] = -1;
        }
        return lookup;
    }

    private static bool IsOnRoute(Vector2Int coordinate, Vector2Int[] route)
    {
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] == coordinate)
            {
                return true;
            }
        }
        return false;
    }

    private static int QuarterTurnDistance(int from, int to)
    {
        int current = from;
        for (int turns = 0; turns < 4; turns++)
        {
            if (current == to)
            {
                return Math.Min(turns, 4 - turns);
            }
            current = RotateClockwise(current);
        }
        return 4;
    }

    private static int RotateClockwise(int mask)
    {
        int rotated = 0;
        if ((mask & Up) != 0) rotated |= Right;
        if ((mask & Right) != 0) rotated |= Down;
        if ((mask & Down) != 0) rotated |= Left;
        if ((mask & Left) != 0) rotated |= Up;
        return rotated;
    }

    private static int MaskBetween(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = to - from;
        if (delta == Vector2Int.up) return Down;
        if (delta == Vector2Int.right) return Right;
        if (delta == Vector2Int.down) return Up;
        if (delta == Vector2Int.left) return Left;
        throw new InvalidOperationException("Circuit route nodes must be orthogonally adjacent.");
    }

    private static int ToGridIndex(Vector2Int coordinate)
    {
        return coordinate.y * GridSize + coordinate.x;
    }

    private static Vector2Int P(int x, int y)
    {
        return new Vector2Int(x, y);
    }
}
