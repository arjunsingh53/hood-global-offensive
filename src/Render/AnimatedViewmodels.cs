using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>
/// First-person viewmodels drawn from the shared animated AK74U rig
/// (tools/build_animated_viewmodels.py builds the GLBs from the user-provided
/// drops). Every weapon in the pack carries the same skeleton, the hoodie arms
/// and six actions - DRAW / IDLE / INSPEC / RELOAD1 / RELOAD2 / SHOOT - so the
/// in-game state machine simply picks an action and a frame and lets raylib
/// pose the skin.
///
/// Axis contract, in the GLB's own glTF space: the muzzle points <c>+Z</c>,
/// up is <c>+Y</c>. The view-model space is -Z forward, so the rig is drawn
/// with a 180-degree yaw. Placement is anchored on the gun's trigger: the
/// measured trigger point (in glTF space) is mapped to a per-weapon view-local
/// target so the rigid mesh sits in the animated hands exactly as placed in
/// Blender - <see cref="Spec.TriggerTarget"/> is the tuning knob.
/// </summary>
public static class AnimatedViewmodels
{
    /// <summary>raylib samples every packed action at 60 fps.</summary>
    private const float AnimationFps = 60f;

    /// <summary>
    /// How much of the box-art reload/deploy flourish still rides on top of the
    /// baked animation. The clips already move the gun; a touch of the old dip
    /// keeps the transition readable without double-swinging.
    /// </summary>
    public const float FlourishScale = 0.25f;

    private sealed class Spec
    {
        public string Path = "";
        /// <summary>
        /// On-screen gun length in view-model units. The scale applies to the
        /// whole rig including the arms, so every weapon keeps
        /// <c>ViewLength ≈ GunLengthRigUnits * 56.7</c> and the hands stay
        /// life-size; the pistol is the one deliberate cheat for readability.
        /// </summary>
        public float ViewLength;
        /// <summary>View-local point the gun's trigger lands on (the tuning knob).</summary>
        public Vector3 TriggerTarget;
        /// <summary>
        /// View-local trigger point at full ADS. The rig lifts the whole gun and
        /// arms so the sight line lands on the centre ray; blended by ZoomBlend.
        /// </summary>
        public Vector3 AdsTarget;
        /// <summary>The gun's trigger point in the mesh's own glTF space.</summary>
        public Vector3 TriggerModel;
        /// <summary>Front-slab centroid - the bore at the muzzle - in glTF space.</summary>
        public Vector3 MuzzleModel;
        /// <summary>Gun length in the rig's own units; the scale is target/real.</summary>
        public float GunLengthRigUnits;
    }

    private static readonly Dictionary<string, Spec> Specs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rifle"] = new()
        {
            Path = "assets/weapons/viewmodel-rifle.glb",
            ViewLength = 45f,
            TriggerTarget = new Vector3(0f, 6f, -30f),
            AdsTarget = new Vector3(-10.5f, 18f, -30f),
            TriggerModel = new Vector3(-0.0674f, 1.4940f, 0.2313f),
            MuzzleModel = new Vector3(-0.0672f, 1.5331f, 0.7222f),
            GunLengthRigUnits = 0.7932f,
        },
        ["Sniper"] = new()
        {
            Path = "assets/weapons/viewmodel-sniper.glb",
            ViewLength = 60f,
            TriggerTarget = new Vector3(0f, 6f, -30f),
            AdsTarget = new Vector3(-12f, 18.4f, -30f),
            TriggerModel = new Vector3(-0.0674f, 1.4920f, 0.2413f),
            MuzzleModel = new Vector3(-0.0715f, 1.5694f, 0.8101f),
            GunLengthRigUnits = 1.0636f,
        },
        ["SMG"] = new()
        {
            Path = "assets/weapons/viewmodel-smg.glb",
            ViewLength = 26.5f,
            TriggerTarget = new Vector3(0f, 6f, -30f),
            AdsTarget = new Vector3(-11.2f, 14f, -30f),
            TriggerModel = new Vector3(-0.0674f, 1.4920f, 0.2233f),
            MuzzleModel = new Vector3(-0.0673f, 1.5135f, 0.4405f),
            GunLengthRigUnits = 0.4656f,
        },
        ["Pistol"] = new()
        {
            Path = "assets/weapons/viewmodel-pistol.glb",
            ViewLength = 11.5f,
            TriggerTarget = new Vector3(0f, 6f, -30f),
            AdsTarget = new Vector3(-10.3f, 7.75f, -30f),
            TriggerModel = new Vector3(-0.0674f, 1.4990f, 0.2213f),
            MuzzleModel = new Vector3(-0.0675f, 1.5407f, 0.3061f),
            GunLengthRigUnits = 0.1677f,
        },
    };

    private sealed class Rig
    {
        public Model Model;
        public unsafe ModelAnimation* Anims;
        public int AnimCount;
        public readonly Dictionary<string, int> ActionIndex = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AnimState
    {
        public float LastTime = float.NaN;
        public float PrevFlash;
        public float ShootTimer;
        public float ShootDuration;
    }

    private static readonly Dictionary<string, Rig?> Rigs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, AnimState> States = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Capture-only override: pins one action frame so tools can verify a clip.</summary>
    private static (string Action, float Time)? Forced;

    /// <summary>Pins one action at a fixed clip time for every subsequent draw.</summary>
    public static void ForceAction(string action, float time) => Forced = (action, time);

    /// <summary>True when this weapon draws from the animated rig pack.</summary>
    public static bool Has(WeaponDef def) => Specs.ContainsKey(def.Name);

    /// <summary>Releases every loaded rig. Call before the window closes.</summary>
    public static unsafe void Shutdown()
    {
        foreach (Rig? rig in Rigs.Values)
        {
            if (rig is null) continue;
            Raylib.UnloadModelAnimations(rig.Anims, rig.AnimCount);
            Raylib.UnloadModel(rig.Model);
        }
        Rigs.Clear();
        States.Clear();
    }

    /// <summary>
    /// Draws the rig weapon into the current view-model space. Returns false
    /// when the weapon has no rig, so the caller can fall back to the static
    /// artwork or the box shapes. The out parameter is the local muzzle point
    /// for the flash, valid only when the method returns true.
    /// </summary>
    public static unsafe bool TryDraw(Simulation sim, WeaponDef def, out Vector3 muzzle)
    {
        muzzle = default;
        if (!Specs.TryGetValue(def.Name, out Spec? spec)) return false;

        Rig? rig = Get(spec.Path);
        if (rig is null || rig.AnimCount == 0) return false;

        float scale = spec.ViewLength / MathF.Max(1e-4f, spec.GunLengthRigUnits);
        AnimState state = StateFor(def.Name);
        Advance(sim, def, rig, state);
        Resolve(sim, def, rig, state, out string action, out float frame);

        Raylib.UpdateModelAnimation(rig.Model, rig.Anims[ActionIndex(rig, action)], frame);

        // Hip pose to iron-sight pose: the whole rig - gun and arms - rides up
        // so the sight line lands on the centre ray as the zoom blend fills.
        float ads = def.HasCalibratedIronSights ? U.Clamp01(sim.Player.ZoomBlend) : 0f;
        Vector3 target = Vector3.Lerp(spec.TriggerTarget, spec.AdsTarget, ads);
        Vector3 pivot = target - Turn(scale * spec.TriggerModel);
        Rlgl.PushMatrix();
        Rlgl.Translatef(pivot.X, pivot.Y, pivot.Z);
        Rlgl.Rotatef(180f, 0f, 1f, 0f);
        Rlgl.Scalef(scale, scale, scale);
        Raylib.DrawModel(rig.Model, Vector3.Zero, 1f, Color.White);
        Rlgl.PopMatrix();

        muzzle = pivot + Turn(scale * spec.MuzzleModel);
        return true;
    }

    /// <summary>glTF forward (+Z) becomes view forward (-Z) through the 180-degree yaw.</summary>
    private static Vector3 Turn(Vector3 v) => new(-v.X, v.Y, -v.Z);

    private static void Advance(Simulation sim, WeaponDef def, Rig rig, AnimState state)
    {
        float dt = float.IsNaN(state.LastTime) ? 0f : U.Clamp(sim.Time - state.LastTime, 0f, 0.25f);
        state.LastTime = sim.Time;

        float flash = sim.Player.MuzzleFlash;
        if (flash > 0f && flash > state.PrevFlash + 1e-4f)
        {
            // One shot restarts the clip. The span never runs longer than the
            // weapon's cycle time, so automatic fire keeps a live flutter
            // instead of freezing on frame zero; the sniper gets the full clip.
            float clip = MathF.Max(1f, Count(rig, "SHOOT") - 1f) / AnimationFps;
            state.ShootDuration = MathF.Max(0.06f, MathF.Min(def.CycleTime, clip));
            state.ShootTimer = state.ShootDuration;
        }
        state.PrevFlash = flash;
        state.ShootTimer = MathF.Max(0f, state.ShootTimer - dt);
    }

    private static void Resolve(Simulation sim, WeaponDef def, Rig rig, AnimState state,
        out string action, out float frame)
    {
        if (Forced is { } forced)
        {
            action = forced.Action;
            frame = Clamped(forced.Time * AnimationFps, Count(rig, action));
            return;
        }

        Weapon weapon = sim.Player.Current;
        float readyIn = weapon.ReadyTime - sim.Time;

        if (weapon.Reloading)
        {
            action = weapon.ReloadStartedEmpty ? "RELOAD2" : "RELOAD1";
            float elapsed = sim.Time - weapon.ReloadStartTime;
            frame = Clamped(elapsed * AnimationFps, Count(rig, action));
            return;
        }

        if (readyIn > 0f && def.DeployTime > 0.0001f)
        {
            action = "DRAW";
            frame = Clamped((def.DeployTime - readyIn) * AnimationFps, Count(rig, action));
            return;
        }

        if (state.ShootTimer > 0f)
        {
            action = "SHOOT";
            frame = Clamped((state.ShootDuration - state.ShootTimer) * AnimationFps, Count(rig, action));
            return;
        }

        action = "IDLE";
        int count = Count(rig, action);
        frame = count > 0 ? (sim.Time * AnimationFps) % count : 0f;
    }

    private static float Clamped(float frame, int count)
        => count > 0 ? MathF.Min(frame, count - 1) : 0f;

    private static AnimState StateFor(string name)
    {
        if (!States.TryGetValue(name, out AnimState? state))
        {
            state = new AnimState();
            States[name] = state;
        }
        return state;
    }

    private static unsafe int ActionIndex(Rig rig, string action)
        => rig.ActionIndex.TryGetValue(action, out int index) ? index : 0;

    private static unsafe int Count(Rig rig, string action)
        => rig.ActionIndex.TryGetValue(action, out int index) ? rig.Anims[index].KeyFrameCount : 0;

    private static Rig? Get(string path)
    {
        if (Rigs.TryGetValue(path, out Rig? cached)) return cached;
        Rig? rig = Load(path);
        Rigs[path] = rig;
        return rig;
    }

    private static unsafe Rig? Load(string path)
    {
        string? file = Resolve(path);
        if (file is null)
        {
            Console.Error.WriteLine($"viewmodel rig not found: {path} "
                + $"(looked next to the executable and in {Directory.GetCurrentDirectory()})");
            return null;
        }

        try
        {
            Model model = Raylib.LoadModel(file);
            if (model.MeshCount <= 0)
            {
                Raylib.UnloadModel(model);
                Console.Error.WriteLine($"viewmodel rig {path} contains no meshes");
                return null;
            }

            int animCount = 0;
            ModelAnimation* anims = Raylib.LoadModelAnimations(file, ref animCount);
            if (animCount <= 0)
            {
                Raylib.UnloadModelAnimations(anims, animCount);
                Raylib.UnloadModel(model);
                Console.Error.WriteLine($"viewmodel rig {path} contains no animations");
                return null;
            }

            var rig = new Rig { Model = model, Anims = anims, AnimCount = animCount };
            var names = new List<string>();
            for (int i = 0; i < animCount; i++)
            {
                string name = anims[i].NameToString();
                rig.ActionIndex[name] = i;
                names.Add($"{name} ({anims[i].KeyFrameCount}f)");
            }

            Console.WriteLine($"viewmodel rig {path}: {model.MeshCount} meshes, "
                + $"{animCount} animations [{string.Join(", ", names)}]");
            return rig;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to load viewmodel rig {path}: {ex.GetType().Name}: {ex.Message}");
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
}
