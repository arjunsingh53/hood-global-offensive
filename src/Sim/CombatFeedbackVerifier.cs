using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Deterministic, window-free contracts for the presentation-only critical-health
/// and actor-impact systems.  These assertions intentionally drive the production
/// simulation; they never substitute a render- or gameplay-only test path.
/// </summary>
public static class CombatFeedbackVerifier
{
    private const float Dt = 1f / 60f;

    public static int Run(ulong seed)
    {
        CombatFeedbackVerificationResult result = Verify(seed);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" COMBAT FEEDBACK VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine($" seed                 : {seed}");
        Console.WriteLine($" checks               : {result.CheckCount}");
        Console.WriteLine($" deterministic replay : {(result.Deterministic ? "PASS" : "FAIL")}");
        Console.WriteLine($" result               : {(result.Passed ? "PASS" : "FAIL")}");
        foreach (string detail in result.Details) Console.WriteLine($"   {detail}");
        Console.WriteLine("=========================================================");

        return result.Passed ? 0 : 1;
    }

    public static CombatFeedbackVerificationResult Verify(ulong seed)
    {
        CombatFeedbackTrace first = RunTrace(seed);
        CombatFeedbackTrace second = RunTrace(seed);
        var failures = new List<string>();

        Require(first.HealthyInactive, "healthy player entered critical-health presentation", failures);
        Require(first.ThresholdInactive,
            $"health at the explicit < {Cfg.LowHealthTraumaThreshold:0} threshold activated trauma", failures);
        Require(first.LowHealthActivated, "low health did not create sustained trauma and a first heartbeat", failures);
        Require(first.HeartbeatCount >= 2,
            $"critical health emitted only {first.HeartbeatCount} heartbeat cues over the scripted interval", failures);
        Require(first.MoreCriticalIsStronger,
            "lower health did not increase the sustained critical-health intensity", failures);
        Require(first.AimPreserved, "critical-health presentation changed the authoritative aim ray", failures);
        Require(first.RespawnClearedTrauma,
            "respawn did not restore health and clear critical-health presentation immediately", failures);
        Require(first.MultiPelletActorHit,
            "controlled multi-pellet actor hit did not preserve per-pellet damage/stat resolution", failures);
        Require(first.MultiPelletVfxBudget,
            "multi-pellet hit emitted more than one bounded actor-impact visual burst", failures);
        Require(first.Finite, "combat feedback produced a non-finite particle, droplet, or splat field", failures);
        Require(first.CapsRetainNewest, "presentation-effect queues exceeded a cap or did not retain newest entries", failures);
        Require(first.SweptDropletLanded,
            "high-velocity blood droplet tunneled through collision instead of creating a surface splat", failures);
        Require(first.SplatFacesFloor,
            "swept droplet splat was not offset outward from the collided surface", failures);
        Require(first.ExpiryDrainsEffects, "short-lived presentation effects did not expire cleanly", failures);
        Require(first.VfxDoesNotAdvanceGameplayRng,
            "actor-impact cosmetics advanced the gameplay RNG stream", failures);

        bool deterministic = first.HeartbeatCount == second.HeartbeatCount
            && NearlyEqual(first.TraumaAtLowHealth, second.TraumaAtLowHealth)
            && NearlyEqual(first.TraumaAtCriticalHealth, second.TraumaAtCriticalHealth)
            && first.ActorHitCount == second.ActorHitCount
            && first.VfxFingerprint == second.VfxFingerprint
            && first.CapsFingerprint == second.CapsFingerprint
            && first.SweptSplatFingerprint == second.SweptSplatFingerprint;
        Require(deterministic, "same seed/input trace produced different combat-feedback state", failures);

        var details = new List<string>
        {
            $"critical threshold < {Cfg.LowHealthTraumaThreshold:0}; heartbeats {first.HeartbeatCount}; trauma {first.TraumaAtLowHealth:0.00} → {first.TraumaAtCriticalHealth:0.00}",
            $"aim preserved {first.AimPreserved}; respawn clears trauma {first.RespawnClearedTrauma}; gameplay RNG isolated {first.VfxDoesNotAdvanceGameplayRng}",
            $"actor pellets {first.ActorHitCount}; bounded burst mist {first.ActorMistCount}; droplets {first.ActorDropletCount}",
            $"caps retain newest {first.CapsRetainNewest}; swept splat {first.SweptDropletLanded}; effects expire {first.ExpiryDrainsEffects}",
        };
        foreach (string failure in failures) details.Add($"FAIL: {failure}");

        return new CombatFeedbackVerificationResult(failures.Count == 0, deterministic, details, 16);
    }

    private static CombatFeedbackTrace RunTrace(ulong seed)
    {
        var trace = new CombatFeedbackTrace();
        var sim = new Simulation(0, BotSkill.Normal, seed);
        Player player = sim.Player;
        PreparePlayer(player);

        Vector3 aimBefore = player.AimDirection;
        Step(sim);
        trace.HealthyInactive = player.LowHealthTrauma <= 1e-5f
            && player.TraumaPulse <= 1e-5f
            && CountHeartbeat(sim) == 0;

        player.Health = Cfg.LowHealthTraumaThreshold;
        Step(sim);
        trace.ThresholdInactive = player.LowHealthTrauma <= 1e-5f
            && player.TraumaPulse <= 1e-5f
            && CountHeartbeat(sim) == 0;

        player.Health = Cfg.LowHealthTraumaThreshold - 10f;
        Step(sim);
        int heartbeats = CountHeartbeat(sim);
        trace.LowHealthActivated = player.LowHealthTrauma > 0f
            && player.TraumaPulse > 0f
            && heartbeats == 1;

        // This interval is intentionally longer than two calm critical beats,
        // but uses the normal fixed simulation tick rather than a timer mock.
        for (int i = 0; i < 150; i++)
        {
            Step(sim);
            heartbeats += CountHeartbeat(sim);
        }
        trace.HeartbeatCount = heartbeats;
        trace.TraumaAtLowHealth = player.LowHealthTrauma;

        player.Health = 8f;
        for (int i = 0; i < 18; i++) Step(sim);
        trace.TraumaAtCriticalHealth = player.LowHealthTrauma;
        trace.MoreCriticalIsStronger = trace.TraumaAtCriticalHealth > trace.TraumaAtLowHealth + 0.05f;
        trace.AimPreserved = Vector3.Dot(aimBefore, player.AimDirection) > 0.99999f;

        sim.Respawn(player, first: false);
        trace.RespawnClearedTrauma = player.Alive
            && NearlyEqual(player.Health, Cfg.MaxHealth)
            && player.LowHealthTrauma <= 1e-5f
            && player.TraumaPulse <= 1e-5f;

        VerifyControlledActorImpact(seed, trace);
        VerifyEffectCaps(seed, trace);
        VerifySweptDroplet(seed, trace);
        VerifyExpiry(seed, trace);
        trace.VfxDoesNotAdvanceGameplayRng = VerifyGameplayRngIsolation(seed);
        trace.Finite &= float.IsFinite(player.LowHealthTrauma) && float.IsFinite(player.TraumaPulse);
        return trace;
    }

    private static void VerifyControlledActorImpact(ulong seed, CombatFeedbackTrace trace)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed ^ 0xA771_C7E5UL);
        if (!TryFindClearLane(sim.Map, out SpawnPoint source, out SpawnPoint targetSpawn)) return;

        Player shooter = sim.Player;
        PreparePlayer(shooter);
        shooter.Position = source.Position;
        shooter.GiveWeapon(FeedbackProbeWeapon);
        shooter.SelectWeapon(WeaponSlot.Primary);

        var victim = new Bot(77, "impact probe", BotSkill.Easy)
        {
            Team = 0,
            Position = targetSpawn.Position,
            Velocity = Vector3.Zero,
            OnGround = true,
            Health = Cfg.MaxHealth,
            Armor = 0f,
            HasHelmet = false,
            Alive = true,
        };
        sim.Actors.Add(victim);

        Vector3 bodyPoint = victim.Position + new Vector3(0f, Cfg.StandHeight * 0.50f, 0f);
        Vector3 direction = Vector3.Normalize(bodyPoint - shooter.EyePosition);
        shooter.Yaw = U.YawFromDirection(direction);
        shooter.Pitch = U.PitchFromDirection(direction);
        shooter.Punch = Vector2.Zero;

        int beforeHits = Hits(sim, shooter.Id);
        sim.FireWeapon(shooter);
        trace.ActorHitCount = Hits(sim, shooter.Id) - beforeHits;
        trace.ActorMistCount = sim.BloodMists.Count;
        trace.ActorDropletCount = sim.BloodDroplets.Count;
        trace.MultiPelletActorHit = trace.ActorHitCount == FeedbackProbeWeapon.Pellets
            && victim.Health < Cfg.MaxHealth;
        trace.MultiPelletVfxBudget = trace.ActorMistCount is > 0 and <= 7
            && trace.ActorDropletCount is > 0 and <= 9;
        trace.Finite &= AllFinite(sim);
        trace.VfxFingerprint = Fingerprint(sim);
    }

    private static void VerifyEffectCaps(ulong seed, CombatFeedbackTrace trace)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed ^ 0xC0FF_EE11UL);
        const int overflow = 3;
        for (int i = 0; i < Cfg.MaxImpactParticles + overflow; i++)
        {
            sim.AddImpactParticle(TestParticle(i));
        }
        for (int i = 0; i < Cfg.MaxImpactDecals + overflow; i++)
        {
            sim.AddImpactDecal(TestDecal(i, DecalKind.SurfaceImpact));
        }
        for (int i = 0; i < Cfg.MaxBloodMists + overflow; i++)
        {
            Particle mist = TestParticle(i);
            mist.Kind = ParticleKind.BloodMist;
            sim.AddBloodMist(mist);
        }
        for (int i = 0; i < Cfg.MaxBloodDroplets + overflow; i++)
        {
            sim.AddBloodDroplet(TestDroplet(i));
        }
        for (int i = 0; i < Cfg.MaxBloodSplats + overflow; i++)
        {
            sim.AddBloodSplat(TestDecal(i, DecalKind.BloodSplat));
        }

        trace.CapsRetainNewest = sim.Particles.Count == Cfg.MaxImpactParticles
            && sim.Decals.Count == Cfg.MaxImpactDecals
            && sim.BloodMists.Count == Cfg.MaxBloodMists
            && sim.BloodDroplets.Count == Cfg.MaxBloodDroplets
            && sim.BloodSplats.Count == Cfg.MaxBloodSplats
            && NearlyEqual(sim.Particles[0].Position.X, overflow)
            && NearlyEqual(sim.Decals[0].Position.X, overflow)
            && NearlyEqual(sim.BloodMists[0].Position.X, overflow)
            && NearlyEqual(sim.BloodDroplets[0].Position.X, overflow)
            && NearlyEqual(sim.BloodSplats[0].Position.X, overflow);
        trace.Finite &= AllFinite(sim);
        trace.CapsFingerprint = Fingerprint(sim);
    }

    private static void VerifySweptDroplet(ulong seed, CombatFeedbackTrace trace)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed ^ 0xD20F_1E7UL);
        sim.AddBloodDroplet(new BloodDroplet
        {
            // The measured central service road has a clear floor collision at
            // y = 0. This 100-unit-per-tick sweep would tunnel through it if
            // droplets were integrated only at their end position.
            Position = new Vector3(0f, 50f, 230f),
            Velocity = new Vector3(0f, -6_000f, 0f),
            Life = 0.5f,
            MaxLife = 0.5f,
            Size = 2f,
            SplatSize = 4f,
            SplatLife = Cfg.BloodDecalLifetime,
            Color = U.Rgba(120, 20, 20, 200),
        });

        Step(sim);
        trace.SweptDropletLanded = sim.BloodDroplets.Count == 0 && sim.BloodSplats.Count == 1;
        if (trace.SweptDropletLanded)
        {
            Decal splat = sim.BloodSplats[0];
            trace.SplatFacesFloor = splat.Normal.Y > 0.5f
                && splat.Position.Y >= Cfg.BloodSplatSurfaceOffset - 1e-4f;
        }
        trace.Finite &= AllFinite(sim);
        trace.SweptSplatFingerprint = Fingerprint(sim);
    }

    private static void VerifyExpiry(ulong seed, CombatFeedbackTrace trace)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed ^ 0xE9F1_7E5UL);
        const float shortLife = Dt * 0.5f;
        Particle debris = TestParticle(0);
        debris.Life = debris.MaxLife = shortLife;
        sim.AddImpactParticle(debris);

        Particle mist = TestParticle(1);
        mist.Life = mist.MaxLife = shortLife;
        mist.Kind = ParticleKind.BloodMist;
        sim.AddBloodMist(mist);

        BloodDroplet droplet = TestDroplet(2);
        droplet.Life = droplet.MaxLife = shortLife;
        sim.AddBloodDroplet(droplet);

        Decal mark = TestDecal(3, DecalKind.BloodSplat);
        mark.Life = mark.MaxLife = shortLife;
        sim.AddBloodSplat(mark);

        Step(sim);
        trace.ExpiryDrainsEffects = sim.Particles.Count == 0
            && sim.BloodMists.Count == 0
            && sim.BloodDroplets.Count == 0
            && sim.BloodSplats.Count == 0;
    }

    private static bool VerifyGameplayRngIsolation(ulong seed)
    {
        var baseline = new Simulation(0, BotSkill.Normal, seed ^ 0xD155_0A7EUL);
        var withVfx = new Simulation(0, BotSkill.Normal, seed ^ 0xD155_0A7EUL);
        withVfx.SpawnActorHitVfx(new Vector3(0f, 36f, 230f), Vector3.UnitX, HitGroup.Chest);

        for (int i = 0; i < 8; i++)
        {
            if (!NearlyEqual(baseline.Rand(-100f, 100f), withVfx.Rand(-100f, 100f))) return false;
        }
        return true;
    }

    private static bool TryFindClearLane(GameMap map, out SpawnPoint source, out SpawnPoint target)
    {
        source = default;
        target = default;
        float bestDistance = 0f;
        for (int i = 0; i < map.Spawns.Length; i++)
        {
            for (int j = i + 1; j < map.Spawns.Length; j++)
            {
                Vector3 from = map.Spawns[i].Position + Vector3.UnitY * Cfg.EyeStand;
                Vector3 to = map.Spawns[j].Position + Vector3.UnitY * (Cfg.StandHeight * 0.50f);
                float distance = Vector3.Distance(from, to);
                if (distance <= bestDistance || !map.LineOfSight(from, to)) continue;
                source = map.Spawns[i];
                target = map.Spawns[j];
                bestDistance = distance;
            }
        }
        return bestDistance > 0f;
    }

    private static readonly WeaponDef FeedbackProbeWeapon = new()
    {
        Name = "feedback probe",
        Class = "Verifier",
        Slot = WeaponSlot.Primary,
        Damage = 2f,
        ArmorPenetration = 1f,
        HeadshotMultiplier = 1f,
        Range = 6_000f,
        CycleTime = 0.1f,
        Automatic = false,
        Magazine = 9,
        Reserve = 0,
        SpreadIdle = 0f,
        SpreadMove = 0f,
        SpreadAir = 0f,
        SpreadBloom = 0f,
        SpreadBloomMax = 0f,
        SpreadDecay = 1f,
        Pellets = 3,
        Pattern = Array.Empty<Vector2>(),
        FireSound = "rifle_ak",
    };

    private static void PreparePlayer(Player player)
    {
        player.Position = new Vector3(0f, 0f, 230f);
        player.Velocity = Vector3.Zero;
        player.Yaw = 90f;
        player.Pitch = 0f;
        player.Punch = Vector2.Zero;
        player.CrouchAmount = 0f;
        player.OnGround = true;
        player.Health = Cfg.MaxHealth;
        player.Armor = 0f;
        player.HasHelmet = false;
        player.Alive = true;
        player.ResetMovementState();
        player.ResetPresentationFeedback();
    }

    private static void Step(Simulation sim) => sim.Update(InputFrame.Neutral, Dt);

    private static int CountHeartbeat(Simulation sim)
    {
        int count = 0;
        foreach (SoundEvent cue in sim.Sounds)
        {
            if (cue.Name == "heartbeat" && cue.Scope == SoundEventScope.Local) count++;
        }
        return count;
    }

    private static int Hits(Simulation sim, int actorId)
        => sim.ShotsHit.TryGetValue(actorId, out int hits) ? hits : 0;

    private static Particle TestParticle(int id) => new()
    {
        Position = new Vector3(id, 1f, 2f),
        Velocity = new Vector3(1f, 2f, 3f),
        Life = 1f,
        MaxLife = 1f,
        Size = 1f,
        Color = U.Rgba(120, 20, 20, 180),
        Gravity = false,
        Kind = ParticleKind.Debris,
    };

    private static BloodDroplet TestDroplet(int id) => new()
    {
        Position = new Vector3(id, 10f, 230f),
        Velocity = new Vector3(1f, 2f, 3f),
        Life = 1f,
        MaxLife = 1f,
        Size = 1f,
        SplatSize = 2f,
        SplatLife = 1f,
        Color = U.Rgba(120, 20, 20, 180),
    };

    private static Decal TestDecal(int id, DecalKind kind) => new()
    {
        Position = new Vector3(id, 1f, 2f),
        Normal = Vector3.UnitY,
        Size = 1f,
        Life = 1f,
        MaxLife = 1f,
        Color = U.Rgba(120, 20, 20, 180),
        Kind = kind,
    };

    private static bool AllFinite(Simulation sim)
    {
        foreach (Particle p in sim.Particles)
        {
            if (!Finite(p.Position) || !Finite(p.Velocity) || !Finite(p.Life) || !Finite(p.MaxLife) || !Finite(p.Size)) return false;
        }
        foreach (Particle mist in sim.BloodMists)
        {
            if (!Finite(mist.Position) || !Finite(mist.Velocity) || !Finite(mist.Life) || !Finite(mist.MaxLife) || !Finite(mist.Size)) return false;
        }
        foreach (BloodDroplet droplet in sim.BloodDroplets)
        {
            if (!Finite(droplet.Position) || !Finite(droplet.Velocity) || !Finite(droplet.Life)
                || !Finite(droplet.MaxLife) || !Finite(droplet.Size) || !Finite(droplet.SplatSize) || !Finite(droplet.SplatLife)) return false;
        }
        foreach (Decal splat in sim.BloodSplats)
        {
            if (!Finite(splat.Position) || !Finite(splat.Normal) || !Finite(splat.Life) || !Finite(splat.MaxLife) || !Finite(splat.Size)) return false;
        }
        return true;
    }

    private static ulong Fingerprint(Simulation sim)
    {
        ulong hash = 14_695_981_039_346_656_037UL;
        foreach (Particle p in sim.Particles) hash = Fold(hash, p.Position, p.Velocity, p.Life, p.MaxLife, p.Size);
        foreach (Particle mist in sim.BloodMists) hash = Fold(hash, mist.Position, mist.Velocity, mist.Life, mist.MaxLife, mist.Size);
        foreach (BloodDroplet d in sim.BloodDroplets)
        {
            hash = Fold(hash, d.Position, d.Velocity, d.Life, d.MaxLife, d.Size);
            hash = Fold(hash, d.SplatSize);
            hash = Fold(hash, d.SplatLife);
        }
        foreach (Decal d in sim.BloodSplats) hash = Fold(hash, d.Position, d.Normal, d.Life, d.MaxLife, d.Size);
        return hash;
    }

    private static ulong Fold(ulong hash, Vector3 a, Vector3 b, float c, float d, float e)
    {
        hash = Fold(hash, a.X);
        hash = Fold(hash, a.Y);
        hash = Fold(hash, a.Z);
        hash = Fold(hash, b.X);
        hash = Fold(hash, b.Y);
        hash = Fold(hash, b.Z);
        hash = Fold(hash, c);
        hash = Fold(hash, d);
        return Fold(hash, e);
    }

    private static ulong Fold(ulong hash, float value)
        => (hash ^ BitConverter.SingleToUInt32Bits(value)) * 1_099_511_628_211UL;

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(float value) => float.IsFinite(value);
    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 1e-4f;

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private sealed class CombatFeedbackTrace
    {
        public bool HealthyInactive;
        public bool ThresholdInactive;
        public bool LowHealthActivated;
        public int HeartbeatCount;
        public float TraumaAtLowHealth;
        public float TraumaAtCriticalHealth;
        public bool MoreCriticalIsStronger;
        public bool AimPreserved;
        public bool RespawnClearedTrauma;
        public int ActorHitCount;
        public int ActorMistCount;
        public int ActorDropletCount;
        public bool MultiPelletActorHit;
        public bool MultiPelletVfxBudget;
        public bool Finite = true;
        public bool CapsRetainNewest;
        public bool SweptDropletLanded;
        public bool SplatFacesFloor;
        public bool ExpiryDrainsEffects;
        public bool VfxDoesNotAdvanceGameplayRng;
        public ulong VfxFingerprint;
        public ulong CapsFingerprint;
        public ulong SweptSplatFingerprint;
    }
}

public readonly record struct CombatFeedbackVerificationResult(
    bool Passed,
    bool Deterministic,
    IReadOnlyList<string> Details,
    int CheckCount);
