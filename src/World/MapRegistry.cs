using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.World;

/// <summary>
/// The shipped map catalog. "Industrial" is the original GLB scene; the other
/// four are Redmatch-style box arenas authored directly against the traversal
/// contract, then rendered from their collision brushes.
/// </summary>
public static class MapRegistry
{
    public const string Default = "Industrial";

    public static readonly string[] Names =
    {
        "Industrial", "Arena", "Streets", "Ruins", "Playground",
    };

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Default;
        foreach (string candidate in Names)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        Console.Error.WriteLine($"unknown map '{name}': using {Default}");
        return Default;
    }

    /// <summary>The GLB scene renders its authored asset; the box maps render brushes.</summary>
    public static bool RendersGlb(string name) => Normalize(name) == Default;

    public static void AddCollision(string name, List<Brush> brushes)
    {
        switch (Normalize(name))
        {
            case "Arena": BuildArena(brushes); break;
            case "Streets": BuildStreets(brushes); break;
            case "Ruins": BuildRuins(brushes); break;
            case "Playground": BuildPlayground(brushes); break;
            default: IndustrialZoneLayout.AddCollision(brushes); break;
        }
    }

    public static SpawnPoint[] CreateSpawns(string name) => Normalize(name) switch
    {
        "Arena" => ArenaSpawns(),
        "Streets" => StreetsSpawns(),
        "Ruins" => RuinsSpawns(),
        "Playground" => PlaygroundSpawns(),
        _ => IndustrialZoneLayout.CreateSpawns(),
    };

    // ---------------------------------------------------------------- palette

    private static readonly Color FloorColor = U.Rgb(58, 60, 66);
    private static readonly Color WallColor = U.Rgb(74, 76, 84);
    private static readonly Color CrateColor = U.Rgb(122, 96, 62);
    private static readonly Color MetalColor = U.Rgb(96, 110, 126);
    private static readonly Color StoneColor = U.Rgb(104, 100, 92);
    private static readonly Color PlatformColor = U.Rgb(88, 92, 102);
    private static readonly Color AccentColor = U.Rgb(146, 122, 74);

    /// <summary>Ground slab plus the invisible containment rim every map shares.</summary>
    private static void Ground(List<Brush> brushes, int extent = 1240)
    {
        Add(brushes, -extent, extent, -80f, 0f, -extent, extent, SurfaceKind.Floor, FloorColor);

        const float rim = 32f;
        Add(brushes, -extent, extent, 0f, Cfg.WallHeight, -extent - rim, -extent, SurfaceKind.Wall, WallColor);
        Add(brushes, -extent, extent, 0f, Cfg.WallHeight, extent, extent + rim, SurfaceKind.Wall, WallColor);
        Add(brushes, -extent - rim, -extent, 0f, Cfg.WallHeight, -extent - rim, extent + rim, SurfaceKind.Wall, WallColor);
        Add(brushes, extent, extent + rim, 0f, Cfg.WallHeight, -extent - rim, extent + rim, SurfaceKind.Wall, WallColor);
    }

    private static void Add(List<Brush> brushes, float x0, float x1, float y0, float y1,
        float z0, float z1, SurfaceKind kind, Color color, bool penetrable = false)
    {
        brushes.Add(new Brush
        {
            Box = new Aabb(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)),
            Color = color,
            Kind = kind,
            Penetrable = penetrable,
        });
    }

    // ================================================================ arena

    private static void BuildArena(List<Brush> brushes)
    {
        Ground(brushes);

        // Raised centre dais with two approach steps on each axis.
        Add(brushes, -240f, 240f, 0f, 64f, -240f, 240f, SurfaceKind.Platform, AccentColor);
        Add(brushes, -120f, 120f, 0f, 32f, -330f, -240f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, -120f, 120f, 0f, 32f, 240f, 330f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, -330f, -240f, 0f, 32f, -120f, 120f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, 240f, 330f, 0f, 32f, -120f, 120f, SurfaceKind.Platform, PlatformColor);

        // Corner towers with jumpable lead-in steps; the rest is grapple work.
        foreach ((float sx, float sz) in new[] { (1f, 1f), (1f, -1f), (-1f, -1f), (-1f, 1f) })
        {
            float cx = sx * 840f;
            float cz = sz * 840f;
            Add(brushes, cx - 100f, cx + 100f, 0f, 250f, cz - 100f, cz + 100f, SurfaceKind.Crate, WallColor);
            Add(brushes, cx - 80f, cx + 80f, 0f, 40f, cz - sz * 100f, cz - sz * 220f, SurfaceKind.Crate, CrateColor);
            Add(brushes, cx - 80f, cx + 80f, 0f, 80f, cz - sz * 100f, cz - sz * 190f, SurfaceKind.Crate, CrateColor);
        }

        // Four tall pillars: pure cover and the arena's primary swing anchors.
        foreach ((float sx, float sz) in new[] { (1f, 1f), (1f, -1f), (-1f, -1f), (-1f, 1f) })
        {
            float cx = sx * 430f;
            float cz = sz * 430f;
            Add(brushes, cx - 32f, cx + 32f, 0f, 220f, cz - 32f, cz + 32f, SurfaceKind.Metal, MetalColor);
        }

        // Low walls on the four edges plus scattered double-stacked crates.
        Add(brushes, -260f, 260f, 0f, 90f, -1180f, -1140f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -260f, 260f, 0f, 90f, 1140f, 1180f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -1180f, -1140f, 0f, 90f, -260f, 260f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 1140f, 1180f, 0f, 90f, -260f, 260f, SurfaceKind.Wall, StoneColor);

        foreach ((float sx, float sz) in new[] { (1f, 1f), (1f, -1f), (-1f, -1f), (-1f, 1f) })
        {
            float cx = sx * 640f;
            float cz = sz * 250f;
            Add(brushes, cx - 48f, cx + 48f, 0f, 56f, cz - 48f, cz + 48f, SurfaceKind.Crate, CrateColor);
            Add(brushes, cx - 40f, cx + 40f, 56f, 100f, cz - 40f, cz + 40f, SurfaceKind.Crate, AccentColor);
        }

        Add(brushes, -700f, -604f, 0f, 56f, -640f, -544f, SurfaceKind.Crate, CrateColor);
        Add(brushes, 604f, 700f, 0f, 56f, 544f, 640f, SurfaceKind.Crate, CrateColor);
        Add(brushes, -700f, -604f, 0f, 56f, 544f, 640f, SurfaceKind.Crate, CrateColor);
        Add(brushes, 604f, 700f, 0f, 56f, -640f, -544f, SurfaceKind.Crate, CrateColor);
    }

    private static SpawnPoint[] ArenaSpawns() =>
    [
        S(-880f, -880f, 45f), S(0f, -980f, 90f), S(880f, -880f, 135f),
        S(-980f, 0f, 0f), S(980f, 0f, 180f),
        S(-880f, 880f, -45f), S(0f, 980f, -90f), S(880f, 880f, -135f),
        S(-430f, 430f, 45f), S(430f, -430f, -135f), S(-430f, -430f, 135f),
    ];

    // ================================================================ streets

    private static void BuildStreets(List<Brush> brushes)
    {
        Ground(brushes);

        // Two facing rows of buildings frame the central street canyon.
        Add(brushes, -1080f, -620f, 0f, 150f, -980f, -700f, SurfaceKind.Wall, WallColor);
        Add(brushes, -460f, -20f, 0f, 200f, -980f, -700f, SurfaceKind.Wall, WallColor);
        Add(brushes, 140f, 580f, 0f, 160f, -980f, -700f, SurfaceKind.Wall, WallColor);
        Add(brushes, 700f, 1060f, 0f, 190f, -980f, -700f, SurfaceKind.Wall, WallColor);

        Add(brushes, -1080f, -620f, 0f, 170f, 700f, 980f, SurfaceKind.Wall, WallColor);
        Add(brushes, -460f, -20f, 0f, 150f, 700f, 980f, SurfaceKind.Wall, WallColor);
        Add(brushes, 140f, 580f, 0f, 200f, 700f, 980f, SurfaceKind.Wall, WallColor);
        Add(brushes, 700f, 1060f, 0f, 150f, 700f, 980f, SurfaceKind.Wall, WallColor);

        // Alley gaps double as flanking routes: low connector walls keep the
        // canyon readable without sealing the lanes.
        Add(brushes, -620f, -460f, 0f, 60f, -960f, -740f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -20f, 140f, 0f, 60f, -960f, -740f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 580f, 700f, 0f, 60f, -960f, -740f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -620f, -460f, 0f, 60f, 740f, 960f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -20f, 140f, 0f, 60f, 740f, 960f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 580f, 700f, 0f, 60f, 740f, 960f, SurfaceKind.Wall, StoneColor);

        // Plaza containers: a stackable double-decker in the centre.
        Add(brushes, -200f, 120f, 0f, 68f, -160f, -80f, SurfaceKind.Crate, AccentColor);
        Add(brushes, -200f, 120f, 68f, 136f, -160f, -80f, SurfaceKind.Crate, CrateColor);
        Add(brushes, 40f, 360f, 0f, 68f, 80f, 160f, SurfaceKind.Crate, AccentColor);
        Add(brushes, -360f, -140f, 0f, 68f, 120f, 200f, SurfaceKind.Crate, CrateColor);

        // Street lamps: thin tall covers along both curbs.
        foreach (float x in new[] { -300f, 300f })
        {
            foreach (float z in new[] { -400f, 0f, 400f })
            {
                Add(brushes, x - 20f, x + 20f, 0f, 180f, z - 20f, z + 20f, SurfaceKind.Metal, MetalColor);
            }
        }

        // Centre divider: two low walls with a walkable gap between them.
        Add(brushes, -60f, 60f, 0f, 110f, -420f, -80f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -60f, 60f, 0f, 110f, 80f, 420f, SurfaceKind.Wall, StoneColor);

        // Parked cars along the curbs.
        foreach ((float x, float z) in new[]
                 {
                     (-760f, -520f), (520f, -520f), (-320f, -540f),
                     (-700f, 500f), (660f, 520f), (280f, 540f),
                 })
        {
            Add(brushes, x - 90f, x + 90f, 0f, 62f, z - 40f, z + 40f, SurfaceKind.Crate, U.Rgb(70, 82, 100));
        }
    }

    private static SpawnPoint[] StreetsSpawns() =>
    [
        S(-940f, -820f, 90f), S(-240f, -840f, 90f), S(360f, -840f, 90f), S(920f, -820f, 90f),
        S(-940f, 820f, -90f), S(-240f, 840f, -90f), S(360f, 840f, -90f), S(920f, 820f, -90f),
        S(-1140f, 0f, 0f), S(1140f, 0f, 180f), S(0f, -560f, 90f), S(0f, 560f, -90f),
    ];

    // ================================================================ ruins

    private static void BuildRuins(List<Brush> brushes)
    {
        Ground(brushes);

        // Broken wall fragments in a loose circuit. Heights vary so sightlines
        // open and close as players orbit the arena.
        Add(brushes, -900f, -420f, 0f, 110f, -820f, -776f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -560f, -120f, 0f, 70f, -560f, -516f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 200f, 680f, 0f, 150f, -860f, -816f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 820f, 1160f, 0f, 110f, -420f, -376f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -1160f, -760f, 0f, 90f, -260f, -216f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -300f, 180f, 0f, 70f, -180f, -136f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 420f, 980f, 0f, 130f, -140f, -96f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -980f, -520f, 0f, 150f, 240f, 284f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -140f, 340f, 0f, 110f, 320f, 364f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 620f, 1140f, 0f, 90f, 420f, 464f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -820f, -360f, 0f, 70f, 620f, 664f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 140f, 620f, 0f, 130f, 700f, 744f, SurfaceKind.Wall, StoneColor);
        Add(brushes, -1180f, -880f, 0f, 90f, 940f, 984f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 300f, 760f, 0f, 150f, 960f, 1004f, SurfaceKind.Wall, StoneColor);
        Add(brushes, 860f, 1140f, 0f, 70f, 880f, 924f, SurfaceKind.Wall, StoneColor);

        // Two ragged towers.
        Add(brushes, -320f, -120f, 0f, 280f, -520f, -320f, SurfaceKind.Crate, WallColor);
        Add(brushes, 620f, 820f, 0f, 240f, 520f, 720f, SurfaceKind.Crate, WallColor);

        // A collapsed bridge: two raised spans with a grapple-width gap.
        Add(brushes, -900f, -300f, 120f, 150f, -20f, 60f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, 60f, 660f, 120f, 150f, -20f, 60f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, -880f, -840f, 0f, 120f, -20f, 60f, SurfaceKind.Metal, MetalColor);
        Add(brushes, -360f, -320f, 0f, 120f, -20f, 60f, SurfaceKind.Metal, MetalColor);
        Add(brushes, 80f, 120f, 0f, 120f, -20f, 60f, SurfaceKind.Metal, MetalColor);
        Add(brushes, 620f, 660f, 0f, 120f, -20f, 60f, SurfaceKind.Metal, MetalColor);

        // Rubble mounds.
        foreach ((float x, float z, float h) in new[]
                 {
                     (-620f, -180f, 44f), (760f, -560f, 32f), (-180f, 560f, 40f),
                     (480f, 120f, 36f), (-940f, 480f, 32f), (920f, 180f, 44f),
                     (-60f, 900f, 36f), (180f, -680f, 40f),
                 })
        {
            Add(brushes, x - 70f, x + 70f, 0f, h, z - 70f, z + 70f, SurfaceKind.Crate, U.Rgb(96, 90, 80));
        }
    }

    private static SpawnPoint[] RuinsSpawns() =>
    [
        S(-1040f, -1040f, 45f), S(0f, -1080f, 90f), S(1040f, -1040f, 135f),
        S(-1120f, 0f, 0f), S(1120f, 0f, 180f),
        S(-1040f, 1040f, -45f), S(0f, 1080f, -90f), S(1040f, 1040f, -135f),
        S(-220f, -420f, 90f), S(720f, 620f, -90f),
    ];

    // ================================================================ playground

    private static void BuildPlayground(List<Brush> brushes)
    {
        Ground(brushes);

        // Stepped central tower with a wide viewing deck.
        Add(brushes, -160f, 160f, 0f, 60f, -160f, 160f, SurfaceKind.Platform, AccentColor);
        Add(brushes, -120f, 120f, 60f, 130f, -120f, 120f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, -80f, 80f, 130f, 190f, -80f, 80f, SurfaceKind.Platform, PlatformColor);
        Add(brushes, -200f, 200f, 190f, 206f, -200f, 200f, SurfaceKind.Platform, AccentColor);

        // Floating hops: half on columns, half pure grapple targets.
        Platform(brushes, -820f, -580f, 88f, -820f, -580f, column: true);
        Platform(brushes, 560f, 800f, 168f, -840f, -600f, column: true);
        Platform(brushes, 860f, 1100f, 248f, -240f, 0f, column: false);
        Platform(brushes, -1100f, -860f, 208f, -140f, 100f, column: false);
        Platform(brushes, -760f, -520f, 128f, 520f, 760f, column: true);
        Platform(brushes, 420f, 660f, 88f, 560f, 800f, column: true);
        Platform(brushes, 880f, 1120f, 168f, 380f, 620f, column: false);
        Platform(brushes, -320f, -80f, 248f, 880f, 1120f, column: false);
        Platform(brushes, 240f, 480f, 128f, -360f, -120f, column: false);

        // A six-step staircase in the north-east corner.
        for (int i = 0; i < 6; i++)
        {
            float z0 = -1080f + i * 60f;
            Add(brushes, 700f, 1000f, 0f, 24f * (i + 1), z0, z0 + 60f, SurfaceKind.Platform, PlatformColor);
        }

        // Jump boxes in the south-west: 48, then 96, then 144.
        Add(brushes, -860f, -700f, 0f, 48f, 860f, 1020f, SurfaceKind.Crate, CrateColor);
        Add(brushes, -660f, -500f, 0f, 96f, 860f, 1020f, SurfaceKind.Crate, CrateColor);
        Add(brushes, -460f, -300f, 0f, 144f, 860f, 1020f, SurfaceKind.Crate, CrateColor);

        // The high perch: a grapple-only spire over the south street.
        Add(brushes, -40f, 40f, 0f, 300f, -740f, -660f, SurfaceKind.Metal, MetalColor);
        Add(brushes, -120f, 120f, 300f, 316f, -820f, -580f, SurfaceKind.Platform, AccentColor);
    }

    private static void Platform(List<Brush> brushes, float x0, float x1, float top,
        float z0, float z1, bool column)
    {
        Add(brushes, x0, x1, top, top + 16f, z0, z1, SurfaceKind.Platform, PlatformColor);
        if (column)
        {
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            Add(brushes, cx - 24f, cx + 24f, 0f, top, cz - 24f, cz + 24f, SurfaceKind.Metal, MetalColor);
        }
    }

    private static SpawnPoint[] PlaygroundSpawns() =>
    [
        S(-1000f, -1000f, 45f), S(0f, -900f, 90f), S(1000f, -1000f, 135f),
        S(-1080f, 300f, 0f), S(1080f, 0f, 180f),
        S(-1000f, 1000f, -45f), S(500f, 1000f, -90f), S(1000f, 1000f, -135f),
        S(-380f, 0f, 0f), S(0f, 380f, 180f),
    ];

    private static SpawnPoint S(float x, float z, float yaw) => new(new Vector3(x, 0f, z), yaw);
}
