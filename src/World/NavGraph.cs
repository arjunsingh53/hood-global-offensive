using System.Numerics;
using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.World;

/// <summary>
/// Navigation graph built by sampling a grid over the map. Nodes exist where a
/// character-sized box fits and a walkable floor is present; edges connect
/// neighbours that can be traversed without clipping geometry.
/// </summary>
public sealed class NavGraph
{
    public readonly struct Node
    {
        public readonly Vector3 Position;
        public readonly int[] Neighbours;
        public Node(Vector3 position, int[] neighbours)
        {
            Position = position;
            Neighbours = neighbours;
        }
    }

    public Node[] Nodes { get; }

    private const float Cell = 80f;
    private const float MaxStep = 36f;
    /// <summary>Extra clearance beyond the raw actor box so bots hug corners less tightly.</summary>
    private const float Padding = 3f;

    private NavGraph(Node[] nodes) => Nodes = nodes;

    public static NavGraph Build(GameMap map)
    {
        float half = Cfg.MapHalf - 40f;
        int side = (int)MathF.Floor((half * 2f) / Cell) + 1;

        var positions = new Vector3[side * side];
        var valid = new bool[side * side];
        var index = new int[side * side];
        Array.Fill(index, -1);

        Vector3 boxSize = new(Cfg.HalfWidth * 2f + Padding, Cfg.StandHeight + Padding, Cfg.HalfWidth * 2f + Padding);

        int count = 0;
        for (int gz = 0; gz < side; gz++)
        {
            for (int gx = 0; gx < side; gx++)
            {
                float x = -half + gx * Cell;
                float z = -half + gz * Cell;
                int slot = gz * side + gx;

                var probe = new Vector3(x, 200f, z);
                if (!map.FindFloor(probe, 0f, 400f, out float floorY)) continue;

                var centre = new Vector3(x, floorY + boxSize.Y * 0.5f, z);
                var box = Aabb.FromCenterSize(centre, boxSize);
                if (map.BoxBlocked(box)) continue;

                positions[slot] = new Vector3(x, floorY, z);
                valid[slot] = true;
                index[slot] = count++;
            }
        }

        var finalPositions = new List<Vector3>(count);
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i]) finalPositions.Add(positions[i]);
        }

        // Re-map so a grid slot points at its node id.
        var nodeId = new int[side * side];
        Array.Fill(nodeId, -1);
        int next = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i]) nodeId[i] = next++;
        }

        var neighbourLists = new List<int>[count];
        for (int i = 0; i < count; i++) neighbourLists[i] = new List<int>(8);

        for (int gz = 0; gz < side; gz++)
        {
            for (int gx = 0; gx < side; gx++)
            {
                int slot = gz * side + gx;
                if (!valid[slot]) continue;
                int a = nodeId[slot];

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = gx + dx, nz = gz + dz;
                        if (nx < 0 || nz < 0 || nx >= side || nz >= side) continue;
                        int nslot = nz * side + nx;
                        if (!valid[nslot]) continue;
                        int b = nodeId[nslot];
                        if (b <= a) continue;

                        if (CanTraverse(map, positions[slot], positions[nslot], boxSize))
                        {
                            neighbourLists[a].Add(b);
                            neighbourLists[b].Add(a);
                        }
                    }
                }
            }
        }

        var nodes = new Node[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = new Node(finalPositions[i], neighbourLists[i].ToArray());
        }

        return new NavGraph(nodes);
    }

    /// <summary>Walkability test: sample along the segment and require floor + clearance at each step.</summary>
    private static bool CanTraverse(GameMap map, Vector3 a, Vector3 b, Vector3 boxSize)
    {
        const int steps = 4;
        for (int s = 1; s < steps; s++)
        {
            float t = s / (float)steps;
            Vector3 p = Vector3.Lerp(a, b, t);
            float expectY = U.Lerp(a.Y, b.Y, t);

            if (!map.FindFloor(new Vector3(p.X, expectY, p.Z), MaxStep, MaxStep, out float floorY)) return false;
            if (MathF.Abs(floorY - expectY) > MaxStep) return false;

            var box = Aabb.FromCenterSize(new Vector3(p.X, floorY + boxSize.Y * 0.5f, p.Z), boxSize);
            if (map.BoxBlocked(box)) return false;
        }

        return true;
    }

    public int NearestNode(Vector3 p)
    {
        int best = -1;
        float bestSq = float.MaxValue;
        for (int i = 0; i < Nodes.Length; i++)
        {
            Vector3 d = Nodes[i].Position - p;
            float sq = d.X * d.X + d.Z * d.Z + d.Y * d.Y * 0.25f;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }
        return best;
    }

    private readonly record struct Scored(int Node, float F);

    /// <summary>A* over the waypoint graph. Returns node ids from start to goal, or an empty list.</summary>
    public List<int> FindPath(Vector3 from, Vector3 to)
    {
        var result = new List<int>();
        if (Nodes.Length == 0) return result;

        int start = NearestNode(from);
        int goal = NearestNode(to);
        if (start < 0 || goal < 0) return result;
        if (start == goal)
        {
            result.Add(goal);
            return result;
        }

        int n = Nodes.Length;
        var gScore = new float[n];
        var cameFrom = new int[n];
        var closed = new bool[n];
        Array.Fill(gScore, float.MaxValue);
        Array.Fill(cameFrom, -1);

        var open = new PriorityQueue<int, float>();
        gScore[start] = 0f;
        open.Enqueue(start, Heuristic(start, goal));

        while (open.TryDequeue(out int current, out _))
        {
            if (current == goal) break;
            if (closed[current]) continue;
            closed[current] = true;

            foreach (int nb in Nodes[current].Neighbours)
            {
                if (closed[nb]) continue;
                float tentative = gScore[current] + Heuristic(current, nb);
                if (tentative >= gScore[nb]) continue;
                gScore[nb] = tentative;
                cameFrom[nb] = current;
                open.Enqueue(nb, tentative + Heuristic(nb, goal));
            }
        }

        if (cameFrom[goal] < 0 && start != goal) return result;

        for (int cur = goal; cur >= 0; cur = cameFrom[cur])
        {
            result.Add(cur);
            if (cur == start) break;
        }

        result.Reverse();
        return result;
    }

    private float Heuristic(int a, int b)
    {
        Vector3 d = Nodes[a].Position - Nodes[b].Position;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z + d.Y * d.Y);
    }
}
