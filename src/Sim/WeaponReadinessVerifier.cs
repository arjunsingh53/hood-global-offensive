using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;

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
        Require(first.SprintLoweredWeapon, "sprint did not enter its viewmodel-only lowered pose", failures);
        Require(first.Finite, "weapon readiness trace produced a non-finite transform", failures);

        bool deterministic = first.FinalMode == second.FinalMode
            && first.SemiHeldShotCount == second.SemiHeldShotCount
            && first.AutoHeldShotCount == second.AutoHeldShotCount
            && MathF.Abs(first.FinalInspectBlend - second.FinalInspectBlend) <= 1e-4f
            && MathF.Abs(first.FinalSprintBlend - second.FinalSprintBlend) <= 1e-4f
            && Vector3.DistanceSquared(first.FinalAim, second.FinalAim) <= 1e-6f;
        Require(deterministic, "same seed/input trace produced different weapon-readiness state", failures);

        var details = new List<string>
        {
            $"selector AUTO → SEMI → AUTO  semi held {first.SemiHeldShotCount} shot  auto held {first.AutoHeldShotCount} shots",
            $"inspect local {first.InspectPresented}  aim preserved {first.InspectPreservedAim}  fire cancels {first.FireCanceledInspect}",
            $"sprint-lowered pose {first.SprintLoweredWeapon}  final mode {first.FinalMode}",
        };
        foreach (string failure in failures) details.Add($"FAIL: {failure}");

        return new WeaponReadinessResult(failures.Count == 0, deterministic, details, 11);
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

        Step(sim, Input(fireDown: true, firePressed: true), trace);
        for (int i = 0; i < 24; i++) Step(sim, Input(), trace);
        trace.FireCanceledInspect = !player.Inspecting || player.InspectPose < 0.01f;

        // Sprint's lowered pose reads the same real sprint state used by movement;
        // no test-only renderer flag is involved.
        Step(sim, Input(forward: true, sprint: true), trace);
        for (int i = 0; i < 8; i++) Step(sim, Input(forward: true), trace);
        trace.SprintLoweredWeapon = player.SprintState == PlayerSprintState.Sprinting
            && player.SprintWeaponBlend > 0.5f;

        trace.FinalMode = weapon.FireMode;
        trace.FinalInspectBlend = player.InspectBlend;
        trace.FinalSprintBlend = player.SprintWeaponBlend;
        trace.FinalAim = player.AimDirection;
        return trace;
    }

    private static InputFrame Input(bool forward = false, bool fireDown = false, bool firePressed = false,
        bool inspect = false, bool fireMode = false, bool sprint = false)
        => new()
        {
            Move = forward ? Vector2.UnitY : Vector2.Zero,
            FireDown = fireDown,
            FirePressed = firePressed,
            InspectPressed = inspect,
            FireModePressed = fireMode,
            SprintPressed = sprint,
            SwitchSlot = -1,
        };

    private static void Step(Simulation sim, InputFrame input, WeaponReadinessTrace trace)
    {
        sim.Update(input, Dt);
        Player p = sim.Player;
        trace.Finite &= float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)
            && float.IsFinite(p.Velocity.X) && float.IsFinite(p.Velocity.Y) && float.IsFinite(p.Velocity.Z)
            && float.IsFinite(p.InspectBlend) && float.IsFinite(p.SprintWeaponBlend);
    }

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
        public bool SprintLoweredWeapon;
        public bool Finite = true;
        public WeaponFireMode FinalMode;
        public float FinalInspectBlend;
        public float FinalSprintBlend;
        public Vector3 FinalAim;
    }
}

public readonly record struct WeaponReadinessResult(
    bool Passed,
    bool Deterministic,
    IReadOnlyList<string> Details,
    int CheckCount);
