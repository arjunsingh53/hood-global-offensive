using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.World;

/// <summary>
/// Deterministic collision, spawn and coordinate data for the supplied Low Poly
/// Industrial Zone scene. The visible level remains the unmodified GLB; this
/// sidecar is deliberately invisible and contains only the traversal contract
/// required by HGO's AABB mover, hitscan and bot navigation.
/// </summary>
public static class IndustrialZoneLayout
{
    public const string AssetPath = "assets/maps/low-poly-industrial-zone.glb";

    // The source scene is 165.69 x 142.74 units. At 16x it reads naturally
    // against HGO's 72-unit character while preserving the exported scene's
    // proportions, materials, meshes and placement exactly.
    public const float AssetScale = 16f;
    private static readonly Vector3 AssetOrigin = new(-133.7891f, 0f, 69.5419f);

    private const float RawPadding = 0.45f;

    private readonly record struct Structure(float MinX, float MaxX, float MinZ, float MaxZ, float Height);

    // These footprints were measured from the supplied GLB's upward-facing
    // geometry on a two-source-unit grid, then merged into static buildings,
    // containers and hard cover. They are not generated during play and never
    // add any visual geometry; the GLB remains the only rendered map.
    private static readonly Structure[] Structures =
    [
        new(-50.0f, -36.0f, -62.0f, -44.0f, 7.5f),
        new(70.0f, 76.0f, -62.0f, -58.0f, 4.6f),
        new(48.0f, 50.0f, -60.0f, -58.0f, 18.4f),
        new(-12.0f, 66.0f, -58.0f, -52.0f, 12.2f),
        new(-12.0f, 56.0f, -52.0f, -50.0f, 12.9f),
        new(-12.0f, 42.0f, -50.0f, -44.0f, 14.6f),
        new(64.0f, 76.0f, -46.0f, -44.0f, 4.5f),
        new(-14.0f, 42.0f, -44.0f, -42.0f, 14.7f),
        new(64.0f, 66.0f, -44.0f, -42.0f, 4.5f),
        new(-12.0f, 42.0f, -42.0f, -34.0f, 12.9f),
        new(62.0f, 68.0f, -42.0f, -40.0f, 4.5f),
        new(62.0f, 76.0f, -40.0f, -38.0f, 4.5f),
        new(-50.0f, -42.0f, -38.0f, -26.0f, 11.0f),
        new(64.0f, 78.0f, -38.0f, -36.0f, 4.5f),
        new(64.0f, 76.0f, -36.0f, -34.0f, 4.5f),
        new(62.0f, 76.0f, -34.0f, -32.0f, 4.5f),
        new(62.0f, 70.0f, -32.0f, -30.0f, 4.5f),
        new(-12.0f, 42.0f, -30.0f, -6.0f, 14.5f),
        new(62.0f, 76.0f, -30.0f, -28.0f, 4.5f),
        new(-50.0f, -34.0f, -24.0f, -10.0f, 7.5f),
        new(12.0f, 16.0f, -6.0f, -4.0f, 5.9f),
        new(24.0f, 28.0f, -6.0f, -4.0f, 9.5f),
        new(-6.0f, -2.0f, -4.0f, -2.0f, 8.2f),
        new(2.0f, 16.0f, -4.0f, -2.0f, 8.9f),
        new(22.0f, 30.0f, -4.0f, -2.0f, 7.8f),
        new(72.0f, 78.0f, -4.0f, 2.0f, 4.2f),
        new(-6.0f, 0.0f, -2.0f, 0.0f, 9.5f),
        new(4.0f, 12.0f, -2.0f, 0.0f, 9.8f),
        new(26.0f, 28.0f, -2.0f, 0.0f, 8.0f),
        new(-6.0f, -2.0f, 0.0f, 2.0f, 9.6f),
        new(6.0f, 12.0f, 0.0f, 2.0f, 8.9f),
        new(-50.0f, -40.0f, 2.0f, 20.0f, 8.0f),
        new(-6.0f, -4.0f, 2.0f, 4.0f, 5.8f),
        new(6.0f, 10.0f, 2.0f, 4.0f, 8.9f),
        new(74.0f, 76.0f, 2.0f, 4.0f, 3.5f),
        new(-50.0f, -28.0f, 20.0f, 36.0f, 10.0f),
        new(36.0f, 42.0f, 30.0f, 32.0f, 9.6f),
        new(34.0f, 42.0f, 32.0f, 34.0f, 11.4f),
        new(12.0f, 18.0f, 34.0f, 36.0f, 4.6f),
        new(34.0f, 40.0f, 34.0f, 36.0f, 10.7f),
        new(-48.0f, -30.0f, 36.0f, 44.0f, 7.0f),
        new(-22.0f, -18.0f, 36.0f, 38.0f, 3.6f),
        new(-14.0f, 4.0f, 36.0f, 44.0f, 6.9f),
        new(16.0f, 18.0f, 36.0f, 40.0f, 4.3f),
        new(34.0f, 42.0f, 36.0f, 38.0f, 6.8f),
        new(50.0f, 64.0f, 36.0f, 42.0f, 6.3f),
        new(-24.0f, -18.0f, 38.0f, 42.0f, 3.6f),
        new(34.0f, 36.0f, 38.0f, 40.0f, 6.5f),
        new(38.0f, 42.0f, 38.0f, 40.0f, 6.6f),
        new(-24.0f, -22.0f, 42.0f, 44.0f, 3.1f),
    ];

    public static Vector3 RawToWorld(float x, float z) => AssetOrigin + new Vector3(x * AssetScale, 0f, z * AssetScale);

    public static void AddCollision(List<Brush> brushes)
    {
        // The GLB's island limits after centring and scaling. This single floor
        // lets the existing gravity/step code remain unchanged without drawing a
        // substitute ground plane under the imported scene.
        Add(brushes, -1325.5f, 1325.5f, -96f, 0f, -1142f, 1142f, SurfaceKind.Floor);

        // Invisible outer containment follows the asset bounds so actors cannot
        // leave the island and walk over the sky. It replaces Crossfire's old
        // perimeter walls; nothing here is rendered.
        const float rim = 32f;
        Add(brushes, -1325.5f - rim, -1325.5f, 0f, Cfg.WallHeight, -1142f - rim, 1142f + rim, SurfaceKind.Wall);
        Add(brushes, 1325.5f, 1325.5f + rim, 0f, Cfg.WallHeight, -1142f - rim, 1142f + rim, SurfaceKind.Wall);
        Add(brushes, -1325.5f, 1325.5f, 0f, Cfg.WallHeight, -1142f - rim, -1142f, SurfaceKind.Wall);
        Add(brushes, -1325.5f, 1325.5f, 0f, Cfg.WallHeight, 1142f, 1142f + rim, SurfaceKind.Wall);

        foreach (Structure structure in Structures)
        {
            Vector3 min = RawToWorld(structure.MinX - RawPadding, structure.MinZ - RawPadding);
            Vector3 max = RawToWorld(structure.MaxX + RawPadding, structure.MaxZ + RawPadding);

            // A roof sampled at the GLB's scale becomes the top of a solid
            // traversal blocker. A small cap prevents foot placement from
            // grazing a roof seam due to source/export floating-point noise.
            float top = MathF.Max(Cfg.StandHeight, structure.Height * AssetScale + 4f);
            Add(brushes, min.X, max.X, 0f, top, min.Z, max.Z, SurfaceKind.Crate);
        }
    }

    public static SpawnPoint[] CreateSpawns() =>
    [
        Spawn(-64f, -50f, 90f),
        Spawn(-28f, -50f, 90f),
        Spawn(52f, -20f, 270f),
        Spawn(-66f, -6f, 90f),
        Spawn(50f, -10f, 270f),
        Spawn(-66f, 14f, 90f),
        Spawn(0f, 20f, 180f),
        Spawn(55f, 20f, 270f),
        Spawn(-4f, 50f, 0f),
        Spawn(60f, 52f, 180f),
        Spawn(-60f, 50f, 0f),
        Spawn(22f, 12f, 180f),
    ];

    private static SpawnPoint Spawn(float rawX, float rawZ, float yaw) => new(RawToWorld(rawX, rawZ), yaw);

    private static void Add(List<Brush> brushes, float x0, float x1, float y0, float y1, float z0, float z1, SurfaceKind kind)
    {
        brushes.Add(new Brush
        {
            Box = new Aabb(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)),
            Color = kind == SurfaceKind.Floor ? U.Rgb(45, 45, 45) : new Color(0, 0, 0, 0),
            Kind = kind,
            Penetrable = false,
        });
    }
}
