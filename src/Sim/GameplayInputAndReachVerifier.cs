using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Focused regression check for full-screen input routing and the firearm reach
/// contract. It is window-free and does not change gameplay state.
/// </summary>
public static class GameplayInputAndReachVerifier
{
    public static int Run()
    {
        var failures = new List<string>();

        Require(GameplayOverlayInput.Resolve(true, false, false) == GameplayOverlayAction.Pause,
            "Escape did not route to pause", failures);
        Require(GameplayOverlayInput.Resolve(false, true, false) == GameplayOverlayAction.Buy,
            "B did not route to the buy menu", failures);
        Require(GameplayOverlayInput.Resolve(true, true, false) == GameplayOverlayAction.Pause,
            "Escape did not take priority over B", failures);
        Require(GameplayOverlayInput.Resolve(false, true, true) == GameplayOverlayAction.None,
            "a completed match still opened the buy menu", failures);

        // Industrial Zone spans roughly 2,650 units across, with long diagonal
        // lanes. Every firearm now reaches its intended lane, while
        // falloff still preserves the shotgun/SMG/rifle/sniper hierarchy.
        VerifyReach(WeaponRegistry.Glock, 3_600f, 2_400f, 15f, failures);
        VerifyReach(WeaponRegistry.Deagle, 4_200f, 2_800f, 38f, failures);
        VerifyReach(WeaponRegistry.Mp5, 3_400f, 2_400f, 14f, failures);
        VerifyReach(WeaponRegistry.Ak47, 5_200f, 3_200f, 21f, failures);
        VerifyReach(WeaponRegistry.M4A4, 5_200f, 3_200f, 18f, failures);
        VerifyReach(WeaponRegistry.Nova, 1_500f, 1_000f, 10f, failures);
        VerifyReach(WeaponRegistry.Awp, 7_200f, 3_400f, 85f, failures);
        VerifyGlockCanHitAcrossLongClearLane(failures);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" GAMEPLAY INPUT AND REACH VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine(" checks               : 20");
        Console.WriteLine($" result               : {(failures.Count == 0 ? "PASS" : "FAIL")}");
        Console.WriteLine(" overlays             : Escape pauses, B opens buy before gameplay input");
        Console.WriteLine(" reach                : firearms cover their intended Industrial Zone sightlines");
        foreach (string failure in failures) Console.WriteLine($"   FAIL: {failure}");
        Console.WriteLine("=========================================================");

        return failures.Count == 0 ? 0 : 1;
    }

    private static void VerifyReach(WeaponDef def, float minRange, float distance, float minChestDamage,
        List<string> failures)
    {
        Require(def.Range >= minRange,
            $"{def.Name} stops at {def.Range:0} before its {minRange:0}-unit reach contract", failures);

        float damage = Hitscan.DamageAtRange(def, distance, HitGroup.Chest);
        Require(damage >= minChestDamage,
            $"{def.Name} deals only {damage:0.0} body damage at {distance:0} units", failures);
    }

    private static void VerifyGlockCanHitAcrossLongClearLane(List<string> failures)
    {
        var map = new GameMap();
        SpawnPoint? source = null;
        SpawnPoint? target = null;
        float bestDistance = 0f;

        for (int i = 0; i < map.Spawns.Length; i++)
        {
            for (int j = i + 1; j < map.Spawns.Length; j++)
            {
                Vector3 a = map.Spawns[i].Position + Vector3.UnitY * Cfg.EyeStand;
                Vector3 b = map.Spawns[j].Position + Vector3.UnitY * Cfg.EyeStand;
                float distance = Vector3.Distance(a, b);
                if (distance <= bestDistance || !map.LineOfSight(a, b)) continue;

                source = map.Spawns[i];
                target = map.Spawns[j];
                bestDistance = distance;
            }
        }

        Require(source.HasValue && target.HasValue && bestDistance > 1_500f,
            $"Industrial Zone has no clear test lane longer than the old Glock cap (found {bestDistance:0})", failures);
        if (!source.HasValue || !target.HasValue || bestDistance <= 1_500f) return;

        var shooter = new Player(101, "reach probe") { Position = source.Value.Position };
        var victim = new Player(102, "reach target") { Position = target.Value.Position };
        Vector3 origin = shooter.EyePosition;
        Vector3 direction = Vector3.Normalize(victim.EyePosition - origin);
        var actors = new Actor[] { shooter, victim };
        var rng = new Rng(0xC0FFEEu);
        ShotResult result = Hitscan.Trace(map, actors, shooter, origin, direction, WeaponRegistry.Glock, ref rng);

        Require(result.HitActor && ReferenceEquals(result.Victim, victim),
            $"Glock did not reach a clear {bestDistance:0}-unit Industrial Zone lane", failures);
    }

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }
}
