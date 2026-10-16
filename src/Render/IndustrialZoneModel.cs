using System.Numerics;
using HoodGlobalOffensive.World;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>Loads and draws the supplied industrial-zone GLB as one static map.</summary>
public static class IndustrialZoneModel
{
    private static Model _model;
    private static bool _attempted;
    private static bool _loaded;

    /// <summary>
    /// Draws the original self-contained GLB exactly once per frame. The model
    /// carries all authored buildings, terrain, vegetation and materials; this
    /// class deliberately does not add generated props, boxes or floor grids.
    /// </summary>
    public static bool TryDraw()
    {
        EnsureLoaded();
        if (!_loaded) return false;

        // The GLB's authored ground plane is not at raw Y=0.  Draw it at the
        // layout's measured render origin so it meets the deterministic floor,
        // actor feet, collision brushes and spawn plane instead of covering them.
        Raylib.DrawModel(_model, IndustrialZoneLayout.AssetDrawPosition, IndustrialZoneLayout.AssetScale, Color.White);
        return true;
    }

    public static void Shutdown()
    {
        if (_loaded) Raylib.UnloadModel(_model);
        _model = default;
        _loaded = false;
        _attempted = false;
    }

    private static void EnsureLoaded()
    {
        if (_attempted) return;
        _attempted = true;

        string path = IndustrialZoneLayout.AssetPath;
        string? file = Resolve(path);
        if (file is null)
        {
            Console.Error.WriteLine($"Industrial Zone map asset is missing: {path} "
                + $"(looked next to the executable and in {Directory.GetCurrentDirectory()})");
            return;
        }

        try
        {
            Model model = Raylib.LoadModel(file);
            if (!Raylib.IsModelValid(model))
            {
                Console.Error.WriteLine($"Industrial Zone map asset could not be loaded: {path}");
                Raylib.UnloadModel(model);
                return;
            }

            _model = model;
            _loaded = true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Industrial Zone map asset load failed: {exception.Message}");
        }
    }

    private static string? Resolve(string path)
    {
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(besideExecutable)) return besideExecutable;

        string workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), path);
        return File.Exists(workingDirectory) ? workingDirectory : null;
    }
}
