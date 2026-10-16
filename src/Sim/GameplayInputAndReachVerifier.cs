using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.UI;
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

        // The B edge that opens the overlay must not immediately close it while
        // the state machine presents the transition frame. Once B is released,
        // a deliberately new B press closes the menu as advertised; Escape is
        // always available immediately.
        var buyCloseLatch = new BuyMenuKeyLatch();
        buyCloseLatch.ArmForOpen();
        Require(!buyCloseLatch.ShouldClose(true, true, false),
            "the B edge that opened the buy menu immediately closed it", failures);
        Require(!buyCloseLatch.ShouldClose(false, false, false),
            "releasing the opening B press closed the buy menu", failures);
        Require(buyCloseLatch.ShouldClose(true, true, false),
            "a new B press did not close the buy menu after release", failures);
        buyCloseLatch.ArmForOpen();
        Require(buyCloseLatch.ShouldClose(true, true, true),
            "Escape could not close the buy menu during B handoff", failures);

        // Protection starts as standard issue in the current deathmatch rules.
        // The Buy Station must show it as equipped instead of accepting $1000
        // for a no-op purchase, while damaged/incomplete gear remains eligible.
        var fullyProtected = new Player(301, "protection probe")
        {
            Armor = Cfg.MaxArmor,
            HasHelmet = true,
        };
        var incompleteProtection = new Player(302, "protection probe")
        {
            Armor = Cfg.MaxArmor - 1f,
            HasHelmet = true,
        };
        Require(Screens.BuyMenu.HasFullProtection(fullyProtected),
            "fully issued protection was not marked equipped", failures);
        Require(!Screens.BuyMenu.HasFullProtection(incompleteProtection),
            "incomplete protection was incorrectly marked equipped", failures);

        fullyProtected.Money = 1_100;
        int fullyProtectedMoney = fullyProtected.Money;
        Require(Screens.BuyMenu.TryPurchaseProtection(fullyProtected, 1_000)
                    == Screens.BuyMenu.ProtectionPurchaseOutcome.AlreadyEquipped,
            "fully issued protection still accepted a paid purchase", failures);
        Require(fullyProtected.Money == fullyProtectedMoney,
            "already-equipped protection deducted money", failures);

        incompleteProtection.Money = 1_200;
        incompleteProtection.HasHelmet = false;
        Require(Screens.BuyMenu.TryPurchaseProtection(incompleteProtection, 1_000)
                    == Screens.BuyMenu.ProtectionPurchaseOutcome.Purchased,
            "damaged or incomplete protection could not be restored", failures);
        Require(incompleteProtection.Armor == Cfg.MaxArmor && incompleteProtection.HasHelmet
                    && incompleteProtection.Money == 200,
            "protection purchase did not restore the loadout and deduct exactly its price", failures);

        var underfundedProtection = new Player(303, "protection probe")
        {
            Armor = 0f,
            HasHelmet = false,
            Money = 999,
        };
        Require(Screens.BuyMenu.TryPurchaseProtection(underfundedProtection, 1_000)
                    == Screens.BuyMenu.ProtectionPurchaseOutcome.InsufficientFunds,
            "underfunded protection purchase was accepted", failures);
        Require(underfundedProtection.Money == 999 && underfundedProtection.Armor == 0f
                    && !underfundedProtection.HasHelmet,
            "underfunded protection purchase changed player state", failures);

        // Industrial Zone spans roughly 2,650 units across, with long diagonal
        // lanes. Every firearm now reaches its intended lane, while
        // falloff still preserves the shotgun/SMG/rifle/sniper hierarchy.
        VerifyReach(WeaponRegistry.Glock, 3_600f, 2_400f, 15f, failures);
        VerifyReach(WeaponRegistry.Deagle, 4_200f, 2_800f, 38f, failures);
        VerifyReach(WeaponRegistry.Mp5, 3_400f, 2_400f, 18f, failures);
        VerifyReach(WeaponRegistry.Ak47, 5_200f, 3_200f, 20f, failures);
        VerifyReach(WeaponRegistry.M4A4, 5_200f, 3_200f, 20f, failures);
        VerifyReach(WeaponRegistry.Nova, 1_500f, 1_000f, 10f, failures);
        VerifyReach(WeaponRegistry.Awp, 7_200f, 3_400f, 85f, failures);
        VerifyGlockCanHitAcrossLongClearLane(failures);
        VerifyCameraToMuzzleReconciliation(failures);
        VerifyAuthoredRifleRangeTiers(failures);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" GAMEPLAY INPUT AND REACH VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine(" checks               : 40");
        Console.WriteLine($" result               : {(failures.Count == 0 ? "PASS" : "FAIL")}");
        Console.WriteLine(" overlays             : Escape pauses; B opens safely; standard-issue armour is never sold twice");
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

    /// <summary>
    /// Uses the real GameMap ray path with a controlled low-cover brush. The
    /// camera must see a target over that cover while the lower physical muzzle
    /// is blocked by it; this isolates the anti-head-glitch rule from unrelated
    /// Industrial Zone buildings while exercising the shipping collision query.
    /// </summary>
    private static void VerifyCameraToMuzzleReconciliation(List<string> failures)
    {
        var sim = new Simulation(0, BotSkill.Normal, 0xBADC0DEu);
        Player player = sim.Player;
        player.OnGround = true;
        player.CrouchAmount = 0f;
        player.Pitch = 0f;

        // Make this test independent of the large art-sidecar layout: it still
        // calls exactly the production GameMap/Aabb ray code, but clears the
        // test simulation's brush array and inserts one 58-unit cover block.
        // An eye at 64 can see over it; the hip muzzle at 46 cannot.
        for (int i = 0; i < sim.Map.Brushes.Length; i++)
        {
            sim.Map.Brushes[i] = new Brush
            {
                Box = new Aabb(new Vector3(20_000f), new Vector3(20_001f)),
                Kind = SurfaceKind.Wall,
            };
        }
        sim.Map.Brushes[0] = new Brush
        {
            Box = new Aabb(new Vector3(-24f, 0f, -10f), new Vector3(24f, 58f, 10f)),
            Kind = SurfaceKind.Crate,
        };

        player.Position = new Vector3(0f, 0f, -80f);
        player.Yaw = 180f;
        Vector3 targetEye = new(0f, Cfg.EyeStand, 80f);
        Vector3 muzzle = sim.GetPhysicalMuzzleOrigin(player);
        bool cameraClear = sim.Map.LineOfSight(player.EyePosition, targetEye);
        bool physicalBlocked = sim.Map.RayHit(muzzle, Vector3.Normalize(targetEye - muzzle),
            Vector3.Distance(muzzle, targetEye), out _, out _, out _, out _);
        var primary = new ShotResult { HitActor = true, Point = targetEye, Normal = -Vector3.UnitZ };
        bool reconciled = sim.TryReconcileMuzzleShot(muzzle, primary, out ShotResult resolved)
            && resolved.HitWorld && !resolved.HitActor;

        Require(cameraClear, "camera eye could not see over controlled low cover", failures);
        Require(physicalBlocked, "controlled low cover did not block the physical muzzle", failures);
        Require(reconciled, "camera-to-muzzle trace did not stop a shot blocked by low cover", failures);
    }

    /// <summary>
    /// Makes the published rifle bands an executable contract.  This prevents a
    /// future visual/weapon refactor from quietly restoring frame-by-frame
    /// exponential falloff and makes the intended close/mid/long values visible
    /// to balancing work.
    /// </summary>
    private static void VerifyAuthoredRifleRangeTiers(List<string> failures)
    {
        WeaponDef ak = WeaponRegistry.Ak47;
        Require(ak.DamageTiers.Length == 4, "AK-47 does not expose four authored range bands", failures);
        Require(Nearly(Hitscan.BaseDamageAtRange(ak, 0f), 34f),
            "AK-47 close-range damage is not 34", failures);
        Require(Nearly(Hitscan.BaseDamageAtRange(ak, 541f), 29f),
            "AK-47 first range drop is not 29", failures);
        Require(Nearly(Hitscan.BaseDamageAtRange(ak, 1_081f), 25f),
            "AK-47 second range drop is not 25", failures);
        Require(Nearly(Hitscan.BaseDamageAtRange(ak, 1_441f), 20f),
            "AK-47 long-range damage is not 20", failures);

        WeaponDef m4 = WeaponRegistry.M4A4;
        Require(m4.DamageTiers.Length == 4 && Nearly(Hitscan.BaseDamageAtRange(m4, 1_153f), 24f),
            "M4A4 range profile is incomplete or its tactical band is wrong", failures);
    }

    private static bool Nearly(float a, float b) => MathF.Abs(a - b) <= 0.001f;

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }
}
