using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.World;

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
    public Color Color;
    public SurfaceKind Kind;
    /// <summary>Bullets can punch through thin penetrable cover for reduced damage.</summary>
    public bool Penetrable;
}

/// <summary>Where an actor can spawn, plus the direction they face.</summary>
public readonly record struct SpawnPoint(Vector3 Position, float Yaw);

/// <summary>
/// The "Crossfire" arena - the one map Hood Global Offensive currently ships with:
/// a mid building, a long west corridor, an east raised platform with stairs, and
/// cover scattered around a ring route.
/// </summary>
public sealed class GameMap
{
    public string Name => Cfg.MapName;

    public Brush[] Brushes { get; }
    public SpawnPoint[] Spawns { get; }

    public GameMap()
    {
        var brushes = new List<Brush>();
        void Add(float x0, float x1, float y0, float y1, float z0, float z1, SurfaceKind kind, bool penetrable = false)
        {
            brushes.Add(new Brush
            {
                Box = new Aabb(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)),
                Color = ColorFor(kind),
                Kind = kind,
                Penetrable = penetrable,
            });
        }

        const float m = Cfg.MapHalf;
        const float t = 40f;   // wall thickness
        const float h = Cfg.WallHeight;

        // ---------------------------------------------------------------- ground + boundary
        Add(-m - t, m + t, -60f, 0f, -m - t, m + t, SurfaceKind.Floor);
        Add(-m - t, m + t, 0f, h, -m - t, -m, SurfaceKind.Wall);
        Add(-m - t, m + t, 0f, h, m, m + t, SurfaceKind.Wall);
        Add(-m - t, -m, 0f, h, -m, m, SurfaceKind.Wall);
        Add(m, m + t, 0f, h, -m, m, SurfaceKind.Wall);

        // ---------------------------------------------------------------- central building
        Add(-380f, -60f, 0f, 128f, -260f, 40f, SurfaceKind.Crate);
        Add(-60f, 220f, 0f, 72f, -260f, 40f, SurfaceKind.Crate);
        Add(-380f, 220f, 0f, 40f, 40f, 120f, SurfaceKind.Platform);
        Add(-40f, 40f, 128f, 168f, -220f, -140f, SurfaceKind.Metal);

        // ---------------------------------------------------------------- corner pillars
        Add(620f, 700f, 0f, h, 620f, 700f, SurfaceKind.Metal);
        Add(620f, 700f, 0f, h, -700f, -620f, SurfaceKind.Metal);
        Add(-700f, -620f, 0f, h, 620f, 700f, SurfaceKind.Metal);
        Add(-700f, -620f, 0f, h, -700f, -620f, SurfaceKind.Metal);

        // ---------------------------------------------------------------- west corridor
        // Two wall runs with a doorway gap at z in [-200, 400].
        Add(-820f, -780f, 0f, h, -m, -200f, SurfaceKind.Wall);
        Add(-820f, -780f, 0f, h, 400f, m, SurfaceKind.Wall);
        // thin cover inside the corridor is penetrable
        Add(-960f, -940f, 0f, 96f, -900f, -600f, SurfaceKind.Wall, penetrable: true);
        Add(-1150f, -1060f, 0f, 64f, 480f, 570f, SurfaceKind.Crate);
        Add(-960f, -870f, 0f, 64f, -740f, -650f, SurfaceKind.Crate);

        // ---------------------------------------------------------------- east raised platform + stairs
        Add(700f, m, 0f, 64f, -600f, 600f, SurfaceKind.Platform);
        Add(660f, 700f, 0f, 16f, -160f, -60f, SurfaceKind.Platform);
        Add(620f, 660f, 0f, 32f, -160f, -60f, SurfaceKind.Platform);
        Add(580f, 620f, 0f, 48f, -160f, -60f, SurfaceKind.Platform);
        Add(880f, 960f, 64f, 152f, -120f, -40f, SurfaceKind.Crate);
        Add(1000f, 1080f, 64f, 136f, 200f, 280f, SurfaceKind.Crate);
        Add(700f, 740f, 64f, 104f, -600f, -520f, SurfaceKind.Crate);

        // ---------------------------------------------------------------- north divider (long sightline break)
        Add(-300f, -120f, 0f, 140f, -900f, -820f, SurfaceKind.Wall);
        Add(120f, 300f, 0f, 140f, -900f, -820f, SurfaceKind.Wall);
        Add(-200f, -100f, 0f, 64f, -700f, -600f, SurfaceKind.Crate);
        Add(140f, 240f, 0f, 64f, -560f, -460f, SurfaceKind.Crate);

        // ---------------------------------------------------------------- south pit cover
        Add(-500f, -260f, 0f, 96f, 700f, 940f, SurfaceKind.Crate);
        Add(300f, 560f, 0f, 96f, 760f, 1000f, SurfaceKind.Crate);
        Add(-100f, 100f, 0f, 48f, 860f, 900f, SurfaceKind.Platform);
        Add(-560f, -480f, 0f, 64f, 300f, 380f, SurfaceKind.Crate);
        Add(360f, 440f, 0f, 64f, 300f, 380f, SurfaceKind.Crate);
        Add(-460f, -380f, 0f, 64f, -520f, -440f, SurfaceKind.Crate);
        Add(360f, 440f, 0f, 64f, -560f, -480f, SurfaceKind.Crate);

        Brushes = brushes.ToArray();

        Spawns = new[]
        {
            new SpawnPoint(new Vector3(-1080f, 0f, -1000f), 0f),
            new SpawnPoint(new Vector3(-1080f, 0f, -100f), 0f),
            new SpawnPoint(new Vector3(-1080f, 0f, 1000f), 180f),
            new SpawnPoint(new Vector3(-600f, 0f, -1050f), 180f),
            new SpawnPoint(new Vector3(0f, 0f, -1050f), 180f),
            new SpawnPoint(new Vector3(600f, 0f, -1050f), 180f),
            new SpawnPoint(new Vector3(0f, 0f, 1050f), 0f),
            new SpawnPoint(new Vector3(-600f, 0f, 1050f), 0f),
            new SpawnPoint(new Vector3(600f, 0f, 1050f), 0f),
            new SpawnPoint(new Vector3(1080f, 64f, -400f), 135f),
            new SpawnPoint(new Vector3(1080f, 64f, 400f), 45f),
            new SpawnPoint(new Vector3(-300f, 0f, 430f), 90f),
            new SpawnPoint(new Vector3(420f, 0f, 520f), 45f),
        };
    }

    private static Color ColorFor(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Floor => U.Rgb(150, 142, 124),
        SurfaceKind.Wall => U.Rgb(196, 186, 160),
        SurfaceKind.Crate => U.Rgb(146, 104, 60),
        SurfaceKind.Metal => U.Rgb(104, 112, 122),
        SurfaceKind.Platform => U.Rgb(168, 150, 122),
        _ => U.Rgb(180, 180, 180),
    };

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
    {
        floorY = 0f;
        Vector3 origin = new(p.X, p.Y + up, p.Z);
        Vector3 dir = new(0f, -1f, 0f);
        float maxDist = up + down;

        bool found = false;
        float best = float.MaxValue;
        for (int i = 0; i < Brushes.Length; i++)
        {
            if (Brushes[i].Box.Raycast(origin, dir, maxDist, out float t, out Vector3 n) && n.Y > 0.6f && t < best)
            {
                best = t;
                found = true;
            }
        }

        if (found) floorY = origin.Y - best;
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
