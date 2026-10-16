using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.UI;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Exercises the original weapon-readiness layer through the production player
/// and simulation path. It proves that per-instance fire selection and local
/// presentation poses cannot mutate the static weapon definitions or aim ray.
/// </summary>
public static class WeaponReadinessVerifier
{
    private const float Dt = 1f / 60f;

    public static int Run(ulong seed)
    {
        WeaponReadinessResult result = Verify(seed);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" WEAPON READINESS VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine($" seed                 : {seed}");
        Console.WriteLine($" checks               : {result.CheckCount}");
        Console.WriteLine($" deterministic replay : {(result.Deterministic ? "PASS" : "FAIL")}");
        Console.WriteLine($" result               : {(result.Passed ? "PASS" : "FAIL")}");
        foreach (string detail in result.Details) Console.WriteLine($"   {detail}");
        Console.WriteLine("=========================================================");

        return result.Passed ? 0 : 1;
    }

    public static WeaponReadinessResult Verify(ulong seed)
    {
        WeaponReadinessTrace first = RunTrace(seed);
        WeaponReadinessTrace second = RunTrace(seed);
        var failures = new List<string>();

        Require(first.DefaultWasAuto, "the rifle did not begin in its original AUTO default", failures);
        Require(first.SelectorReachedSemi, "V did not select SEMI on an opted-in rifle", failures);
        Require(first.SemiHeldShotCount == 1,
            $"held SEMI fire discharged {first.SemiHeldShotCount} shots instead of one", failures);
        Require(first.SelectorReturnedToAuto, "V did not return the rifle to AUTO", failures);
        Require(first.AutoHeldFiredRepeatedly,
            "held AUTO fire did not retain the original repeated-fire behaviour", failures);
        Require(first.DefinitionUnchanged,
            "per-instance selector mutated the shared WeaponRegistry definition", failures);
        Require(first.InspectPresented && first.InspectPreservedAim,
            "inspect did not present locally while preserving the aim ray", failures);
        Require(first.FireCanceledInspect, "fire input did not cancel the inspect pose", failures);
        Require(first.IronSightsHeld && first.IronSightsPreservedAim,
            "AK right-click did not hold ADS locally while preserving the aim ray", failures);
        Require(first.IronSightsReleased, "AK ADS did not return to hip fire on secondary-fire release", failures);
        Require(first.AdsTightensSteadySpread,
            $"AK ADS steady spread {first.AimedSpread:0.000} did not improve on hip {first.HipSpread:0.000}", failures);
        Require(first.MovementAndAirRemainInaccurate,
            "ADS removed movement or airborne handling penalties", failures);
        Require(first.FallbackAimRetainsReticle,
            "a fallback hold-to-aim weapon hid the reticle without a calibrated sight picture", failures);
        Require(first.WeaponSwitchClearedScopedAim,
            "switching from held AK ADS to the AWP carried the old zoom into its toggle scope", failures);
        Require(first.AwpRequiresFreshScopePress,
            "the AWP did not require a fresh secondary-fire press after a weapon replacement", failures);
        Require(first.SprintLoweredWeapon, "sprint did not enter its viewmodel-only lowered pose", failures);
        Require(first.ViewModelResponded,
            "mouse drag did not produce a bounded cosmetic viewmodel response", failures);
        Require(first.ViewModelPreservedAim,
            "presentation-only viewmodel motion changed the authoritative aim ray", failures);
        Require(first.Finite, "weapon readiness trace produced a non-finite transform", failures);

        bool deterministic = first.FinalMode == second.FinalMode
            && first.SemiHeldShotCount == second.SemiHeldShotCount
            && first.AutoHeldShotCount == second.AutoHeldShotCount
            && MathF.Abs(first.FinalInspectBlend - second.FinalInspectBlend) <= 1e-4f
            && MathF.Abs(first.FinalSprintBlend - second.FinalSprintBlend) <= 1e-4f
            && MathF.Abs(first.FinalAdsBlend - second.FinalAdsBlend) <= 1e-4f
            && MathF.Abs(first.HipSpread - second.HipSpread) <= 1e-4f
            && MathF.Abs(first.AimedSpread - second.AimedSpread) <= 1e-4f
            && MathF.Abs(first.MovingAimedSpread - second.MovingAimedSpread) <= 1e-4f
            && MathF.Abs(first.AirAimedSpread - second.AirAimedSpread) <= 1e-4f
            && Vector3.DistanceSquared(first.FinalViewModelOffset, second.FinalViewModelOffset) <= 1e-6f
            && Vector3.DistanceSquared(first.FinalViewModelRotation, second.FinalViewModelRotation) <= 1e-6f
            && Vector3.DistanceSquared(first.FinalAim, second.FinalAim) <= 1e-6f;
        Require(deterministic, "same seed/input trace produced different weapon-readiness state", failures);

        var details = new List<string>
        {
            $"selector AUTO → SEMI → AUTO  semi held {first.SemiHeldShotCount} shot  auto held {first.AutoHeldShotCount} shots",
            $"inspect local {first.InspectPresented}  aim preserved {first.InspectPreservedAim}  fire cancels {first.FireCanceledInspect}",
            $"AK iron sights held {first.IronSightsHeld}  release {first.IronSightsReleased}  aim preserved {first.IronSightsPreservedAim}",
            $"ADS spread hip {first.HipSpread:0.000}  aimed {first.AimedSpread:0.000}  moving {first.MovingAimedSpread:0.000}  air {first.AirAimedSpread:0.000}",
            $"fallback aim keeps reticle {first.FallbackAimRetainsReticle}  weapon switch clears scope {first.WeaponSwitchClearedScopedAim}  fresh AWP press {first.AwpRequiresFreshScopePress}",
            $"sprint-lowered pose {first.SprintLoweredWeapon}  final mode {first.FinalMode}",
            $"viewmodel spring responds {first.ViewModelResponded}  aim remains authoritative {first.ViewModelPreservedAim}",
        };
        foreach (string failure in failures) details.Add($"FAIL: {failure}");

        return new WeaponReadinessResult(failures.Count == 0, deterministic, details, 20);
    }

    private static WeaponReadinessTrace RunTrace(ulong seed)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed);
        Player player = sim.Player;
        player.GiveWeapon(WeaponRegistry.Ak47);
        player.SelectWeapon(WeaponSlot.Primary);
        player.Position = new Vector3(0f, 0f, 600f);
        player.Velocity = Vector3.Zero;
        player.Yaw = 90f;
        player.Pitch = 0f;
        player.OnGround = true;

        Weapon weapon = player.Current;
        var trace = new WeaponReadinessTrace
        {
            DefaultWasAuto = weapon.FireMode == WeaponFireMode.Auto && WeaponRegistry.Ak47.DefaultFireMode == WeaponFireMode.Auto,
        };

        // Selector is runtime-only and starts from the definition's default.
        Step(sim, Input(fireMode: true), trace);
        trace.SelectorReachedSemi = weapon.FireMode == WeaponFireMode.Semi;

        int beforeSemi = Shots(sim);
        Step(sim, Input(fireDown: true, firePressed: true), trace);
        for (int i = 0; i < 18; i++) Step(sim, Input(fireDown: true), trace);
        trace.SemiHeldShotCount = Shots(sim) - beforeSemi;

        Step(sim, Input(fireMode: true), trace);
        trace.SelectorReturnedToAuto = weapon.FireMode == WeaponFireMode.Auto;

        int beforeAuto = Shots(sim);
        for (int i = 0; i < 18; i++) Step(sim, Input(fireDown: true, firePressed: i == 0), trace);
        trace.AutoHeldShotCount = Shots(sim) - beforeAuto;
        trace.AutoHeldFiredRepeatedly = trace.AutoHeldShotCount > 1;
        trace.DefinitionUnchanged = WeaponRegistry.Ak47.DefaultFireMode == WeaponFireMode.Auto
            && WeaponRegistry.Ak47.Automatic
            && WeaponRegistry.Ak47.FireModes.SequenceEqual(new[] { WeaponFireMode.Auto, WeaponFireMode.Semi });

        // The inspect pose must remain visual-only. Let it reach a meaningful
        // local pose before comparing the authoritative aim direction.
        Vector3 aimBeforeInspect = player.AimDirection;
        Step(sim, Input(inspect: true), trace);
        for (int i = 0; i < 12; i++) Step(sim, Input(), trace);
        trace.InspectPresented = player.Inspecting && player.InspectPose > 0.01f;
        trace.InspectPreservedAim = Vector3.Dot(aimBeforeInspect, player.AimDirection) > 0.99999f;

        // The dual-camera weapon drag may visibly lag/bank on input, but it is
        // a local spring only. Verify a real input impulse is observed, then
        // independently kick the presentation springs and prove the normal
        // simulation step leaves the authoritative ray alone.
        // Clear the preceding live-fire recoil first: recoil recovery is an
        // authoritative camera mechanic and must not be mistaken for a local
        // viewmodel-spring mutation in this isolated assertion.
        player.Punch = Vector2.Zero;
        player.ViewKick = 0f;
        player.KickYaw = 0f;
        player.ResetViewModelDynamics();
        Step(sim, Input(look: new Vector2(9f, -5f)), trace);
        trace.ViewModelResponded = player.ViewModelOffsetSpring.Value.LengthSquared() > 0.0001f
            && player.ViewModelRotationSpring.Value.LengthSquared() > 0.0001f;
        Vector3 aimBeforePresentationOnly = player.AimDirection;
        player.ViewModelOffsetSpring.Impulse(new Vector3(1.5f, -0.5f, 0.75f));
        player.ViewModelRotationSpring.Impulse(new Vector3(2f, -1f, 3f));
        for (int i = 0; i < 4; i++) Step(sim, Input(), trace);
        trace.ViewModelPreservedAim = Vector3.Dot(aimBeforePresentationOnly, player.AimDirection) > 0.99999f;

        Step(sim, Input(fireDown: true, firePressed: true), trace);
        for (int i = 0; i < 24; i++) Step(sim, Input(), trace);
        trace.FireCanceledInspect = !player.Inspecting || player.InspectPose < 0.01f;

        // Production AK ADS is hold-to-aim. It may tighten the first-person
        // camera, but its combat ray remains the same authoritative Player aim.
        // Let the preceding fire test's recoil settle before comparing the
        // authoritative aim ray; ADS itself must not alter that ray.
        player.Punch = Vector2.Zero;
        Vector3 aimBeforeAds = player.AimDirection;
        for (int i = 0; i < 20; i++) Step(sim, Input(zoomDown: true), trace);
        trace.IronSightsHeld = player.ZoomHeld && player.ZoomBlend > 0.95f
            && player.CurrentFov < Cfg.FovDefault
            && Vector3.Dot(aimBeforeAds, player.AimDirection) > 0.99999f;
        trace.IronSightsPreservedAim = Vector3.Dot(aimBeforeAds, player.AimDirection) > 0.99999f;
        weapon.Bloom = 0f;
        trace.HipSpread = weapon.SpreadFor(0f, onGround: true, crouching: false);
        trace.AimedSpread = weapon.SpreadFor(0f, onGround: true, crouching: false, aimBlend: player.ZoomBlend);
        trace.MovingAimedSpread = weapon.SpreadFor(Cfg.RunSpeed, onGround: true, crouching: false,
            aimBlend: player.ZoomBlend);
        trace.AirAimedSpread = weapon.SpreadFor(Cfg.RunSpeed, onGround: false, crouching: false,
            aimBlend: player.ZoomBlend);
        trace.AdsTightensSteadySpread = trace.AimedSpread < trace.HipSpread - 1e-4f;
        trace.MovementAndAirRemainInaccurate = trace.MovingAimedSpread > trace.AimedSpread
            && trace.AirAimedSpread > trace.MovingAimedSpread;
        for (int i = 0; i < 20; i++) Step(sim, Input(), trace);
        trace.IronSightsReleased = !player.ZoomHeld && player.ZoomBlend < 0.05f;

        // Fallback view models can still narrow their aim FOV, but their
        // crosshair stays visible until they have a real calibrated sight view.
        player.GiveWeapon(WeaponRegistry.Mp5);
        for (int i = 0; i < 20; i++) Step(sim, Input(zoomDown: true), trace);
        trace.FallbackAimRetainsReticle = player.ZoomHeld && player.ZoomBlend > 0.95f
            && !Hud.IsCrosshairSuppressed(player.Current.Def, player.ZoomBlend);

        // Sprint's lowered pose reads the same real sprint state used by movement;
        // no test-only renderer flag is involved.
        Step(sim, Input(forward: true, sprint: true), trace);
        for (int i = 0; i < 8; i++) Step(sim, Input(forward: true), trace);
        trace.SprintLoweredWeapon = player.SprintState == PlayerSprintState.Sprinting
            && player.SprintWeaponBlend > 0.5f;

        // Recreate the real buy/replace path: an AK is held in ADS, then its
        // primary slot is replaced with the toggle-scoped AWP while RMB remains
        // down. The scope must require a new click instead of inheriting ADS.
        player.GiveWeapon(WeaponRegistry.Ak47);
        for (int i = 0; i < 20; i++) Step(sim, Input(zoomDown: true), trace);
        bool akWasHeld = player.ZoomHeld && player.ZoomBlend > 0.95f;
        player.GiveWeapon(WeaponRegistry.Awp);
        Step(sim, Input(zoomDown: true), trace);
        trace.WeaponSwitchClearedScopedAim = akWasHeld && !player.ZoomHeld
            && player.ZoomBlend < 0.05f
            && MathF.Abs(player.CurrentFov - Cfg.FovDefault) < 1e-4f
            && !Hud.IsCrosshairSuppressed(player.Current.Def, player.ZoomBlend);
        Step(sim, Input(zoomDown: true), trace);
        Step(sim, Input(zoomDown: true, zoomPressed: true), trace);
        for (int i = 0; i < 20; i++) Step(sim, Input(), trace);
        trace.AwpRequiresFreshScopePress = player.ZoomHeld && player.ZoomBlend > 0.95f
            && Hud.IsCrosshairSuppressed(player.Current.Def, player.ZoomBlend);

        trace.FinalMode = weapon.FireMode;
        trace.FinalInspectBlend = player.InspectBlend;
        trace.FinalSprintBlend = player.SprintWeaponBlend;
        trace.FinalAdsBlend = player.ZoomBlend;
        trace.FinalAim = player.AimDirection;
        trace.FinalViewModelOffset = player.ViewModelOffsetSpring.Value;
        trace.FinalViewModelRotation = player.ViewModelRotationSpring.Value;
        return trace;
    }

    private static InputFrame Input(bool forward = false, bool fireDown = false, bool firePressed = false,
        bool inspect = false, bool fireMode = false, bool sprint = false, bool zoomDown = false,
        bool zoomPressed = false, Vector2? look = null)
        => new()
        {
            Move = forward ? Vector2.UnitY : Vector2.Zero,
            Look = look ?? Vector2.Zero,
            FireDown = fireDown,
            FirePressed = firePressed,
            InspectPressed = inspect,
            FireModePressed = fireMode,
            SprintPressed = sprint,
            ZoomDown = zoomDown,
            ZoomPressed = zoomPressed,
            SwitchSlot = -1,
        };

    private static void Step(Simulation sim, InputFrame input, WeaponReadinessTrace trace)
    {
        sim.Update(input, Dt);
        Player p = sim.Player;
        trace.Finite &= float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)
            && float.IsFinite(p.Velocity.X) && float.IsFinite(p.Velocity.Y) && float.IsFinite(p.Velocity.Z)
            && float.IsFinite(p.InspectBlend) && float.IsFinite(p.SprintWeaponBlend)
            && IsFinite(p.ViewModelOffsetSpring.Value) && IsFinite(p.ViewModelOffsetSpring.Velocity)
            && IsFinite(p.ViewModelRotationSpring.Value) && IsFinite(p.ViewModelRotationSpring.Velocity);
    }

    private static bool IsFinite(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static int Shots(Simulation sim) => sim.ShotsFired.TryGetValue(sim.Player.Id, out int shots) ? shots : 0;

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private sealed class WeaponReadinessTrace
    {
        public bool DefaultWasAuto;
        public bool SelectorReachedSemi;
        public int SemiHeldShotCount;
        public bool SelectorReturnedToAuto;
        public int AutoHeldShotCount;
        public bool AutoHeldFiredRepeatedly;
        public bool DefinitionUnchanged;
        public bool InspectPresented;
        public bool InspectPreservedAim;
        public bool FireCanceledInspect;
        public bool IronSightsHeld;
        public bool IronSightsPreservedAim;
        public bool IronSightsReleased;
        public bool AdsTightensSteadySpread;
        public bool MovementAndAirRemainInaccurate;
        public float HipSpread;
        public float AimedSpread;
        public float MovingAimedSpread;
        public float AirAimedSpread;
        public bool FallbackAimRetainsReticle;
        public bool WeaponSwitchClearedScopedAim;
        public bool AwpRequiresFreshScopePress;
        public bool SprintLoweredWeapon;
        public bool ViewModelResponded;
        public bool ViewModelPreservedAim;
        public bool Finite = true;
        public WeaponFireMode FinalMode;
        public float FinalInspectBlend;
        public float FinalSprintBlend;
        public float FinalAdsBlend;
        public Vector3 FinalAim;
        public Vector3 FinalViewModelOffset;
        public Vector3 FinalViewModelRotation;
    }
}

public readonly record struct WeaponReadinessResult(
    bool Passed,
    bool Deterministic,
    IReadOnlyList<string> Details,
    int CheckCount);
