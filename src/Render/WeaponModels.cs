using System.Numerics;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>
/// Weapons that are drawn from a real mesh instead of from boxes.
///
/// The art meshes are loaded on first use and kept for the life of the process.
/// Every measurement the placement needs is taken from the loaded mesh rather
/// than hard-coded: the bounding box supplies the centre and the length that the
/// caller's target length is scaled against, and the centroid of the front-most
/// slice of the mesh supplies the bore line, so the muzzle flash sits on the
/// actual barrel even if the asset is re-exported at a different size or offset.
///
/// The asset contract is glTF with the mesh's own axes meaning: <c>+X</c> runs
/// along the barrel with the muzzle at <c>-X</c>, <c>+Y</c> is up and <c>+Z</c> is
/// the weapon's side. <see cref="Load"/> warns when a mesh does not look like that.
/// Textures must be PNG: raylib's in-memory image loader cannot decode JPEG (a
/// JPEG embedded in a .glb loads as a 1x1 white texture).
/// </summary>
public static class WeaponModels
{
    private sealed class LoadedMesh
    {
        public Model Model;
        public Vector3 Min;
        public Vector3 Max;
        /// <summary>Centroid of the front 2% of the mesh - the bore at the muzzle.</summary>
        public Vector3 Muzzle;

        public Vector3 Centre => (Min + Max) * 0.5f;
    }

    private static readonly Dictionary<string, LoadedMesh?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Draws an art weapon in the current view-model space and reports where its
    /// muzzle ended up. The mesh is scaled so its barrel is <paramref name="length"/>
    /// game units long and its centre is placed on <paramref name="centre"/>; the
    /// mesh's muzzle (-X) ends up pointing along -Z, which is forward in this space
    /// when <paramref name="yawDegrees"/> is zero. A melee model can deliberately
    /// turn sideways to keep its distinguishing silhouette readable on screen.
    ///
    /// Returns false when the mesh is missing or unloadable, so the caller can fall
    /// back to drawing boxes instead of leaving the player with no weapon.
    /// </summary>
    public static bool TryDraw(string path, float length, Vector3 centre, float yawDegrees, out Vector3 muzzle)
    {
        muzzle = default;

        LoadedMesh? mesh = Get(path);
        if (mesh is null) return false;

        float scale = length / MathF.Max(1e-4f, mesh.Max.X - mesh.Min.X);
        Vector3 pivot = centre - RotateToView(mesh.Centre * scale, yawDegrees);
        muzzle = pivot + RotateToView(mesh.Muzzle * scale, yawDegrees);

        Rlgl.PushMatrix();
        Rlgl.Translatef(pivot.X, pivot.Y, pivot.Z);
        Rlgl.Rotatef(-90f + yawDegrees, 0f, 1f, 0f);
        Rlgl.Scalef(scale, scale, scale);
        Raylib.DrawModel(mesh.Model, Vector3.Zero, 1f, Color.White);
        Rlgl.PopMatrix();

        return true;
    }

    /// <summary>Releases every loaded mesh. Call before the window closes.</summary>
    public static void Shutdown()
    {
        foreach (LoadedMesh? mesh in Cache.Values)
        {
            if (mesh is not null) Raylib.UnloadModel(mesh.Model);
        }
        Cache.Clear();
    }

    /// <summary>Transforms the mesh's own axes into the view with an optional presentation yaw.</summary>
    private static Vector3 RotateToView(Vector3 v, float yawDegrees)
    {
        float radians = (-90f + yawDegrees) * (MathF.PI / 180f);
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new(v.X * cosine + v.Z * sine, v.Y, -v.X * sine + v.Z * cosine);
    }

    private static LoadedMesh? Get(string path)
    {
        if (Cache.TryGetValue(path, out LoadedMesh? cached)) return cached;

        LoadedMesh? mesh = Load(path);
        Cache[path] = mesh;
        return mesh;
    }

    private static LoadedMesh? Load(string path)
    {
        string? file = Resolve(path);
        if (file is null)
        {
            Console.Error.WriteLine($"weapon mesh not found: {path} "
                + $"(looked next to the executable and in {Directory.GetCurrentDirectory()})");
            return null;
        }

        try
        {
            Model model = Raylib.LoadModel(file);
            if (model.MeshCount <= 0)
            {
                Raylib.UnloadModel(model);
                Console.Error.WriteLine($"weapon mesh {path} contains no meshes");
                return null;
            }

            LoadedMesh mesh = Measure(model);
            Vector3 size = mesh.Max - mesh.Min;

            Console.WriteLine($"weapon mesh {path}: {Triangles(model)} triangles, "
                + $"size {size.X:0.000} x {size.Y:0.000} x {size.Z:0.000}, "
                + $"muzzle ({mesh.Muzzle.X:0.000},{mesh.Muzzle.Y:0.000},{mesh.Muzzle.Z:0.000})");

            if (size.X < size.Y)
            {
                Console.Error.WriteLine($"warning: weapon mesh {path} is taller than it is long; the "
                    + "placement assumes the barrel runs along X with the muzzle at -X");
            }

            return mesh;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to load weapon mesh {path}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Next to the executable first, then the working directory (dotnet run).</summary>
    private static string? Resolve(string path)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(beside)) return beside;
        return File.Exists(path) ? path : null;
    }

    private static int Triangles(Model model)
    {
        int total = 0;
        for (int i = 0; i < model.MeshCount; i++)
        {
            unsafe { total += model.Meshes[i].TriangleCount; }
        }
        return total;
    }

    private static unsafe LoadedMesh Measure(Model model)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        for (int i = 0; i < model.MeshCount; i++)
        {
            BoundingBox box = Raylib.GetMeshBoundingBox(model.Meshes[i]);
            min = Vector3.Min(min, box.Min);
            max = Vector3.Max(max, box.Max);
        }

        // The muzzle face is the front-most slab of the mesh. Its centroid is on the
        // bore line - the barrel's centre - which is where the flash belongs; the mesh
        // bounding box centre is not, because the grip hangs below the slide.
        float slab = (max.X - min.X) * 0.02f;
        Vector3 sum = Vector3.Zero;
        int count = 0;

        for (int i = 0; i < model.MeshCount; i++)
        {
            Mesh mesh = model.Meshes[i];
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector3 p = new(mesh.Vertices[v * 3], mesh.Vertices[v * 3 + 1], mesh.Vertices[v * 3 + 2]);
                if (p.X > min.X + slab) continue;
                sum += p;
                count++;
            }
        }

        Vector3 muzzle = count > 0 ? sum / count : new Vector3(min.X, (min.Y + max.Y) * 0.5f, 0f);

        return new LoadedMesh { Model = model, Min = min, Max = max, Muzzle = muzzle };
    }
}
