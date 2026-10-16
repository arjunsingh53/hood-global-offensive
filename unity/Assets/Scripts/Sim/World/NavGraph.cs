using System;
using System.Collections.Generic;
using System.Numerics;
using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.World
{
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

            var open = new MinHeap(n);
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

        /// <summary>
        /// Minimum priority queue over (int element, float priority). Unity's
        /// scripting profile has no System.Collections.Generic.PriorityQueue, and
        /// the reference's A* depended on BCL heap ordering, so this reproduces
        /// the BCL 4-ary binary heap exactly; dequeue order for equal priorities
        /// matches the reference build.
        /// </summary>
        private sealed class MinHeap
        {
            private const int Arity = 4;

            private struct Entry
            {
                public int Element;
                public float Priority;
            }

            private Entry[] _nodes;
            private int _size;

            public MinHeap(int capacity)
            {
                _nodes = new Entry[capacity < 4 ? 4 : capacity];
            }

            public void Enqueue(int element, float priority)
            {
                if (_size == _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
                _nodes[_size] = new Entry { Element = element, Priority = priority };
                _size++;
                SiftUp(_size - 1);
            }

            public bool TryDequeue(out int element, out float priority)
            {
                if (_size == 0)
                {
                    element = 0;
                    priority = 0f;
                    return false;
                }

                element = _nodes[0].Element;
                priority = _nodes[0].Priority;

                int last = _size - 1;
                _nodes[0] = _nodes[last];
                _nodes[last] = default;
                _size--;

                if (_size > 0) SiftDown(0);
                return true;
            }

            private void SiftUp(int nodeIndex)
            {
                while (nodeIndex > 0)
                {
                    int parentIndex = (nodeIndex - 1) / Arity;
                    if (_nodes[nodeIndex].Priority >= _nodes[parentIndex].Priority) return;

                    Entry tmp = _nodes[nodeIndex];
                    _nodes[nodeIndex] = _nodes[parentIndex];
                    _nodes[parentIndex] = tmp;
                    nodeIndex = parentIndex;
                }
            }

            private void SiftDown(int nodeIndex)
            {
                while (true)
                {
                    int firstChildIndex = nodeIndex * Arity + 1;
                    if (firstChildIndex >= _size) return;

                    int minChildIndex = firstChildIndex;
                    int maxChildIndex = Math.Min(firstChildIndex + Arity, _size);
                    for (int i = firstChildIndex + 1; i < maxChildIndex; i++)
                    {
                        if (_nodes[i].Priority < _nodes[minChildIndex].Priority) minChildIndex = i;
                    }

                    if (_nodes[minChildIndex].Priority >= _nodes[nodeIndex].Priority) return;

                    Entry tmp = _nodes[nodeIndex];
                    _nodes[nodeIndex] = _nodes[minChildIndex];
                    _nodes[minChildIndex] = tmp;
                    nodeIndex = minChildIndex;
                }
            }
        }
    }
}
