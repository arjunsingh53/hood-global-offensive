using System;
using System.Collections.Generic;
using System.Numerics;
using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.World
{
    public enum SurfaceKind
    {
        Floor,
        Wall,
        Crate,
        Metal,
        Platform,
    }

    /// <summary>A solid block of level geometry.</summary>
    public struct Brush
    {
        public Aabb Box;
        /// <summary>ARGB colour for the view layer and radar; the simulation never shades.</summary>
        public uint Color;
        public SurfaceKind Kind;
        /// <summary>Bullets can punch through thin penetrable cover for reduced damage.</summary>
        public bool Penetrable;
        /// <summary>
        /// Collision-only surface: the level never draws it, so impacts on it
        /// must not leave visible marks. Used by the imported Industrial Zone's
        /// containment rim, whose decals would otherwise hang in the sky.
        /// </summary>
        public bool Invisible;
    }

    /// <summary>Where an actor can spawn, plus the direction they face.</summary>
    public readonly struct SpawnPoint
    {
        public readonly Vector3 Position;
        public readonly float Yaw;

        public SpawnPoint(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// One playable arena. "Industrial" is the supplied Low Poly Industrial Zone
    /// GLB with a matching deterministic collision sidecar; the other catalog maps
    /// are box-built arenas where collision brushes are also what the renderer draws.
    /// </summary>
    public sealed class GameMap
    {
        public string Name { get; }

        /// <summary>True when the authored GLB is the visible level (Industrial).</summary>
        public bool RendersGlb => MapRegistry.RendersGlb(Name);

        public Brush[] Brushes { get; }
        public SpawnPoint[] Spawns { get; }

        public GameMap(string name = MapRegistry.Default)
        {
            Name = MapRegistry.Normalize(name);

            var brushes = new List<Brush>();
            MapRegistry.AddCollision(Name, brushes);
            Brushes = brushes.ToArray();
            Spawns = BuildSpawns();
        }

        /// <summary>
        /// Validates the catalog's authored spawn list against the collision it just
        /// built. A spawn that lands inside solid geometry relocates to the nearest
        /// clear floor spot; if the list ever thins below a playable count, a
        /// deterministic grid top-up fills it back in. Every spawn faces the map
        /// centre so a relocated player never starts nose-first into a wall.
        /// </summary>
        private SpawnPoint[] BuildSpawns()
        {
            SpawnPoint[] authored = MapRegistry.CreateSpawns(Name);
            var result = new List<SpawnPoint>(authored.Length);
            foreach (SpawnPoint spawn in authored)
            {
                if (TryClearSpawn(spawn, out SpawnPoint adjusted)) result.Add(adjusted);
            }

            if (result.Count >= 6) return result.ToArray();

            for (float x = -1080f; x <= 1080f && result.Count < 12; x += 220f)
            {
                for (float z = -1080f; z <= 1080f && result.Count < 12; z += 220f)
                {
                    var candidate = new Vector3(x, 0f, z);
                    bool tooClose = false;
                    foreach (SpawnPoint existing in result)
                    {
                        if (U.FlatLength(existing.Position - candidate) < 500f)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;

                    float yaw = U.YawFromDirection(new Vector3(-x, 0f, -z));
                    if (TryClearSpawn(new SpawnPoint(candidate, yaw), out SpawnPoint found)) result.Add(found);
                }
            }

            return result.ToArray();
        }

        private bool TryClearSpawn(SpawnPoint spawn, out SpawnPoint result)
        {
            for (int ring = 0; ring <= 8; ring++)
            {
                int steps = ring == 0 ? 1 : 8;
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * MathF.Tau / steps;
                    var candidate = new Vector3(
                        spawn.Position.X + (ring == 0 ? 0f : MathF.Cos(angle) * ring * 90f),
                        spawn.Position.Y,
                        spawn.Position.Z + (ring == 0 ? 0f : MathF.Sin(angle) * ring * 90f));

                    if (!FindFloor(candidate, 60f, 400f, out float floorY)) continue;
                    if (floorY > spawn.Position.Y + 60f) continue;

                    var feet = new Vector3(candidate.X, floorY, candidate.Z);
                    if (BoxBlocked(Aabb.Footprint(feet, Cfg.HalfWidth, Cfg.StandHeight))) continue;

                    result = new SpawnPoint(feet, spawn.Yaw);
                    return true;
                }
            }

            result = default;
            return false;
        }

        // ---------------------------------------------------------------- queries

        /// <summary>True when the box overlaps any solid brush.</summary>
        public bool BoxBlocked(in Aabb box)
        {
            for (int i = 0; i < Brushes.Length; i++)
            {
                if (Brushes[i].Box.Intersects(box)) return true;
            }
            return false;
        }

        /// <summary>Index of the first brush blocking a segment, or -1.</summary>
        public int SegmentBlocker(Vector3 a, Vector3 b)
        {
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < Brushes.Length; i++)
            {
                Vector3 d = b - a;
                float len = d.Length();
                if (len < 1e-5f) continue;
                if (Brushes[i].Box.Raycast(a, d / len, len, out float t, out _) && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Line-of-sight check between two points.</summary>
        public bool LineOfSight(Vector3 a, Vector3 b) => SegmentBlocker(a, b) < 0;

        /// <summary>Closest brush hit along a ray.</summary>
        public bool RayHit(Vector3 origin, Vector3 dir, float maxDist,
            out float distance, out Vector3 point, out Vector3 normal, out int brushIndex)
        {
            distance = maxDist;
            point = origin + dir * maxDist;
            normal = -dir;
            brushIndex = -1;
            bool hit = false;

            for (int i = 0; i < Brushes.Length; i++)
            {
                if (Brushes[i].Box.Raycast(origin, dir, maxDist, out float t, out Vector3 n) && t < distance)
                {
                    distance = t;
                    point = origin + dir * t;
                    normal = n;
                    brushIndex = i;
                    hit = true;
                }
            }

            return hit;
        }

        /// <summary>Highest walkable surface under a point, searched within a vertical window.</summary>
        public bool FindFloor(Vector3 p, float up, float down, out float floorY)
            => FindFloorSurface(p, up, down, out floorY, out _);

        /// <summary>
        /// Finds the same highest walkable surface as <see cref="FindFloor"/>, plus
        /// its authored collision material family. Consumers such as footsteps use
        /// this only as sidecar metadata; it never changes collision, spawns, or
        /// the supplied GLB's rendered geometry.
        /// </summary>
        public bool FindFloorSurface(Vector3 p, float up, float down, out float floorY, out SurfaceKind surface)
        {
            floorY = 0f;
            surface = SurfaceKind.Floor;
            Vector3 origin = new(p.X, p.Y + up, p.Z);
            Vector3 dir = new(0f, -1f, 0f);
            float maxDist = up + down;

            bool found = false;
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < Brushes.Length; i++)
            {
                if (Brushes[i].Box.Raycast(origin, dir, maxDist, out float t, out Vector3 n) && n.Y > 0.6f && t < best)
                {
                    best = t;
                    found = true;
                    bestIndex = i;
                }
            }

            if (found)
            {
                floorY = origin.Y - best;
                surface = Brushes[bestIndex].Kind;
            }
            return found;
        }

        /// <summary>Index of the brush a point is inside, or -1.</summary>
        public int BrushAt(Vector3 p)
        {
            for (int i = 0; i < Brushes.Length; i++)
            {
                if (Brushes[i].Box.Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Choose a spawn far away from a set of busy positions. Takes the RNG by
        /// reference so consecutive picks advance the same deterministic stream.
        /// </summary>
        public SpawnPoint PickSpawn(ref Rng rng, IReadOnlyList<Vector3> avoid, float minDistance)
        {
            // Try a handful of random candidates and keep the most isolated one.
            SpawnPoint best = Spawns[0];
            float bestScore = -1f;

            int attempts = Math.Min(Spawns.Length * 3, 40);
            for (int a = 0; a < attempts; a++)
            {
                SpawnPoint s = Spawns[rng.RangeInt(0, Spawns.Length)];
                float nearest = float.MaxValue;
                for (int i = 0; i < avoid.Count; i++)
                {
                    float d = (avoid[i] - s.Position).Length();
                    if (d < nearest) nearest = d;
                }

                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = s;
                    if (nearest >= minDistance) break;
                }
            }

            return best;
        }
    }
}
