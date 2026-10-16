using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim;

/// <summary>Which actors fight whom. Team 0 is shared deathmatch.</summary>
public enum MatchMode
{
    Deathmatch,
}

/// <summary>
/// Owns every actor and resolves the whole frame. Rendering and audio only read
/// from here, which is what lets the headless harness run the game without a window.
/// </summary>
public sealed class Simulation
{
    public GameMap Map { get; }
    public NavGraph Nav { get; }
    public readonly List<Actor> Actors = new();
    public readonly Player Player;
    public MatchMode Mode { get; }

    public float Time;
    public float TimeLeft = Cfg.MatchTime;
    public bool MatchOver;
    public string MatchResult = "";
    public int KillsToWin = Cfg.KillsToWin;

    public readonly Rng SeedSource;
    private Rng _rng;
    // Presentation randomness is deliberately isolated from combat/AI randomness.
    // Adding a mist or splat cannot change a future recoil spread, bot choice, or spawn.
    private Rng _vfxRng;

    // ---------------------------------------------------------------- effects
    public readonly List<Tracer> Tracers = new();
    private readonly List<Particle> _particles = new();
    private readonly List<Decal> _decals = new();
    private readonly List<Particle> _bloodMists = new();
    private readonly List<BloodDroplet> _bloodDroplets = new();
    private readonly List<Decal> _bloodSplats = new();
    public IReadOnlyList<Particle> Particles => _particles;
    public IReadOnlyList<Decal> Decals => _decals;
    public IReadOnlyList<Particle> BloodMists => _bloodMists;
    public IReadOnlyList<BloodDroplet> BloodDroplets => _bloodDroplets;
    public IReadOnlyList<Decal> BloodSplats => _bloodSplats;
    public readonly List<FloatingDamage> DamageNumbers = new();
    public readonly List<KillFeedEntry> KillFeed = new();
    public readonly List<SoundEvent> Sounds = new();

    public float HitmarkerUntil = -1f;
    public bool LastHitWasHeadshot;
    public bool LastHitWasKill;
    public float DamageFlash;
    public float DamageFlashDirection;
    public float KilledFlashUntil = -1f;
    public float ScopePulse;

    // ---------------------------------------------------------------- stats
    public readonly Dictionary<int, int> ShotsFired = new();
    public readonly Dictionary<int, int> ShotsHit = new();
    public readonly Dictionary<int, int> Headshots = new();
    public int PlayerHeadshots;
    /// <summary>How many times an actor has been brought back in after dying.</summary>
    public int RespawnCount;

    public Simulation(int botCount, BotSkill skill, ulong seed, int killsToWin = Cfg.KillsToWin)
    {
        _rng = new Rng(seed);
        _vfxRng = new Rng(seed ^ 0xB10D_D00D_91A7_5EEDUL);
        SeedSource = new Rng(seed);
        KillsToWin = killsToWin;
        Mode = MatchMode.Deathmatch;

        Map = new GameMap();
        Nav = NavGraph.Build(Map);

        Player = new Player(0, "You");
        Player.Team = 0;
        Actors.Add(Player);

        var names = new[]
        {
            "Vitaliy", "Nadia", "Kovac", "Ruiz", "Hana", "Dmitri", "Selim", "Priya",
            "Jonas", "Marco", "Yuki", "Anya", "Brook", "Tariq", "Ines", "Oleg",
        };

        for (int i = 0; i < botCount; i++)
        {
            BotSkill s = skill;
            // A little variety so the server is not uniformly deadly.
            if (i % 4 == 1 && skill != BotSkill.Easy) s = skill - 1;
            var bot = new Bot(i + 1, names[i % names.Length], s)
            {
                Team = 0,
            };
            Actors.Add(bot);
        }

        foreach (Actor actor in Actors)
        {
            Respawn(actor, first: true);
        }
    }

    // ---------------------------------------------------------------- random

    public float Rand(float min, float max) => _rng.Range(min, max);
    public int RandInt(int min, int maxExclusive) => _rng.RangeInt(min, maxExclusive);
    public bool Chance(float p) => _rng.Chance(p);
    private float VfxRand(float min, float max) => _vfxRng.Range(min, max);

    public void Emit(string name, Vector3 position, float volume = 1f, float pitch = 1f,
        SoundEventScope scope = SoundEventScope.World)
        => Sounds.Add(new SoundEvent(name, position, volume, pitch, scope));

    // ---------------------------------------------------------------- frame

    public void Update(InputFrame input, float dt)
    {
        if (dt <= 0f) return;
        dt = MathF.Min(dt, 0.05f);

        Sounds.Clear();
        Time += dt;

        if (!MatchOver)
        {
            TimeLeft -= dt;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                EndMatch("TIME LIMIT");
            }
        }

        // ---- player
        if (!MatchOver)
        {
            Player.Update(input, this, dt);
            if (Player.WantsFire && Player.Alive) FireWeapon(Player);
        }
        else
        {
            Player.Velocity = Vector3.Zero;
        }

        // ---- bots
        foreach (Actor actor in Actors)
        {
            if (actor is Bot bot)
            {
                if (!MatchOver)
                {
                    bot.Update(this, dt);
                    if (bot.WantsFire && bot.Alive) FireWeapon(bot);
                }
                else
                {
                    bot.Velocity = Vector3.Zero;
                }
            }
        }

        ActorSeparation();
        HandleRespawns();
        EmitFootsteps(dt);
        TickEffects(dt);

        DamageFlash = U.Approach(DamageFlash, 0f, 3.2f, dt);
        ScopePulse = MathF.Max(0f, ScopePulse - dt);
    }

    private void ActorSeparation()
    {
        // Soft body push so actors do not stand inside each other.
        for (int i = 0; i < Actors.Count; i++)
        {
            Actor a = Actors[i];
            if (!a.Alive) continue;
            for (int j = i + 1; j < Actors.Count; j++)
            {
                Actor b = Actors[j];
                if (!b.Alive) continue;

                float dx = b.Position.X - a.Position.X;
                float dz = b.Position.Z - a.Position.Z;
                float distSq = dx * dx + dz * dz;
                const float minDist = Cfg.HalfWidth * 2f;
                if (distSq > minDist * minDist || distSq < 1e-6f) continue;
                if (MathF.Abs(b.Position.Y - a.Position.Y) > Cfg.StandHeight) continue;

                float dist = MathF.Sqrt(distSq);
                float push = (minDist - dist) * 0.5f;
                float nx = dx / dist, nz = dz / dist;
                a.Position = new Vector3(a.Position.X - nx * push, a.Position.Y, a.Position.Z - nz * push);
                b.Position = new Vector3(b.Position.X + nx * push, b.Position.Y, b.Position.Z + nz * push);
            }
        }
    }

    // ---------------------------------------------------------------- combat

    public void FireWeapon(Actor shooter)
    {
        Weapon weapon = shooter.Current;
        float now = Time;
        if (!weapon.CanFire(now)) return;

        WeaponDef def = weapon.Def;

        float aimBlend = shooter is Player playerShooter ? playerShooter.ZoomBlend : 0f;
        bool sliding = shooter is Player slidingPlayer && slidingPlayer.IsSliding;
        float spread = weapon.SpreadFor(shooter.HorizontalSpeed, shooter.OnGround, shooter.Crouching,
            aimBlend, sliding);
        if (shooter is Bot bot) spread += bot.Profile.AimError * 0.30f;
        if (shooter.IsPlayer) spread = MathF.Max(spread, 0f);

        Vector3 origin = shooter.EyePosition;
        Vector3 baseDir = shooter.AimDirection;

        // Recoil is applied after the shot so the first bullet flies true.
        Vector2 recoil = weapon.NextRecoil();
        shooter.LastShotTime = now;
        weapon.RegisterShot(now);
        shooter.OnShotFired(now);

        ShotsFired.TryGetValue(shooter.Id, out int fired);
        ShotsFired[shooter.Id] = fired + 1;

        Vector3 muzzle = GetPhysicalMuzzleOrigin(shooter);
        int actorHitVfxEvents = 0;

        for (int pellet = 0; pellet < def.Pellets; pellet++)
        {
            Vector3 dir = Hitscan.ApplySpread(baseDir, spread, ref _rng);
            ShotResult result = Hitscan.Trace(Map, Actors, shooter, origin, dir, def, ref _rng);

            // The camera centre chooses the intended target, but the physical
            // barrel still has to clear the world. This reconciles familiar
            // FPS crosshair aiming with fair near-cover behaviour: a player
            // cannot shoot through a low wall that blocks their muzzle merely
            // because their eye is visible above it. Through-cover shots retain
            // their existing penetration path until that system gains an
            // equivalent thickness-aware muzzle continuation.
            if (shooter.IsPlayer && !result.ThroughCover
                && TryReconcileMuzzleShot(muzzle, result, out ShotResult muzzleBlocked))
            {
                result = muzzleBlocked;
            }

            if (result.HitActor && result.Victim is not null)
            {
                // A shotgun may land several pellets at once.  Resolve every
                // pellet's damage/stats as normal, but budget the expensive visual
                // burst to the first actual actor contact in this trigger.
                bool emitImpactVfx = actorHitVfxEvents < Cfg.MaxActorHitVfxEventsPerTrigger;
                ApplyActorHit(shooter, result, def, now, dir, emitImpactVfx);
                if (emitImpactVfx) actorHitVfxEvents++;
            }
            else if (result.HitWorld)
            {
                SpawnWorldImpact(result, def, now);
            }

            Tracers.Add(new Tracer
            {
                From = muzzle,
                To = result.Point,
                Life = 0.055f,
                MaxLife = 0.055f,
                Color = def.Tracer,
                Width = shooter.IsPlayer ? 1.1f : 0.9f,
            });
        }

        // Punch the view now that the bullet has left the barrel.
        shooter.Punch = new Vector2(
            U.Clamp(shooter.Punch.X + recoil.X, -36f, 36f),
            U.Clamp(shooter.Punch.Y + recoil.Y, -36f, 36f));

        Emit(def.FireSound, origin, shooter.IsPlayer ? 0.85f : 0.7f, Rand(0.95f, 1.05f));
    }

    /// <summary>
    /// Returns the presentation-aligned physical muzzle for secondary collision
    /// checks and tracers. This is not the authoritative aim origin—the camera
    /// ray remains the first trace—but it keeps shots honest around low cover.
    /// </summary>
    internal Vector3 GetPhysicalMuzzleOrigin(Actor shooter)
    {
        Vector3 forward = shooter.AimDirection;
        Vector3 right = U.RightFromYaw(shooter.Yaw);

        if (shooter is Player player)
        {
            // Hip fire sits down/right of the eye in the same general region as
            // the first-person mesh. During ADS it rises toward the eye line;
            // it never becomes the eye itself, so a blocked barrel still reads
            // as a blocked barrel.
            Vector3 hip = shooter.EyePosition + forward * 18f + right * 8.5f - Vector3.UnitY * 18f;
            Vector3 ads = shooter.EyePosition + forward * 19f + right * 1.25f - Vector3.UnitY * 6.5f;
            return U.Lerp(hip, ads, player.ZoomBlend);
        }

        // Bots use their readable chest-ready world pose rather than their eye,
        // giving their outgoing tracers a plausible origin without modifying AI
        // targeting, collision, weapon timing, or hitscan accuracy.
        return shooter.Position + Vector3.UnitY * (shooter.HeadHeight * 0.69f)
            + forward * 17f + right * 1.5f;
    }

    /// <summary>
    /// Performs only the second, physical half of a camera-to-muzzle trace. A
    /// completed camera trace supplies the intended result point; if static
    /// geometry lies between the muzzle and that point, the near obstacle wins.
    /// </summary>
    internal bool TryReconcileMuzzleShot(Vector3 muzzle, in ShotResult primary, out ShotResult blocked)
    {
        blocked = default;
        Vector3 toTarget = primary.Point - muzzle;
        float distance = toTarget.Length();
        if (distance <= 0.05f || !float.IsFinite(distance)) return false;

        Vector3 direction = toTarget / distance;
        // Preserve a tiny endpoint tolerance so a world surface selected by the
        // primary camera ray is not spuriously reclassified due to rounding.
        float physicalRange = MathF.Max(0f, distance - 0.15f);
        if (physicalRange <= 0f) return false;
        if (!Map.RayHit(muzzle, direction, physicalRange, out _, out Vector3 point,
            out Vector3 normal, out int brushIndex)) return false;

        blocked = new ShotResult
        {
            HitWorld = true,
            Point = point,
            Normal = normal,
            BrushIndex = brushIndex,
        };
        return true;
    }

    private void ApplyActorHit(Actor shooter, ShotResult result, WeaponDef def, float now,
        Vector3 shotDirection, bool emitImpactVfx)
    {
        Actor victim = result.Victim!;

        ShotsHit.TryGetValue(shooter.Id, out int hits);
        ShotsHit[shooter.Id] = hits + 1;

        float damage = result.Damage;
        bool killed = victim.ApplyDamage(damage, result.Group, result.Point, shooter);

        if (result.Group == HitGroup.Head)
        {
            Headshots.TryGetValue(shooter.Id, out int hs);
            Headshots[shooter.Id] = hs + 1;
        }

        if (emitImpactVfx) SpawnActorHitVfx(result.Point, shotDirection, result.Group);

        if (shooter.IsPlayer)
        {
            HitmarkerUntil = now + 0.12f;
            LastHitWasHeadshot = result.Group == HitGroup.Head;
            LastHitWasKill = killed;
            // Local HUD confirmations are intentionally separate from the physical
            // impact at the target. They remain audible at long range while the
            // world hit still attenuates naturally for every listener.
            Emit("hitmarker", shooter.EyePosition, 0.76f, result.Group == HitGroup.Head ? 1.08f : 1f,
                SoundEventScope.Local);
            if (result.Group == HitGroup.Head)
                Emit("headshot", shooter.EyePosition, 0.82f, killed ? 1f : 1.04f, SoundEventScope.Local);
            DamageNumbers.Add(new FloatingDamage
            {
                Position = result.Point,
                Amount = damage,
                Life = 0.85f,
                MaxLife = 0.85f,
                Headshot = result.Group == HitGroup.Head,
            });
        }

        if (victim.IsPlayer)
        {
            DamageFlash = MathF.Min(1f, DamageFlash + damage / 90f);
            DamageFlashDirection = U.YawFromDirection(U.FlatNormalized(shooter.Position - victim.Position));
            if (victim is Player player)
            {
                // Presentation-only: the simulation and hit result are already
                // resolved. This gives the renderer a short, directional response
                // without ever perturbing the player's aim or shot origin.
                player.TriggerDamageFeedback(damage / 90f, DamageFlashDirection);
            }
        }

        Emit(result.Group == HitGroup.Head ? "hit_head" : "hit_flesh", result.Point, 0.9f, Rand(0.95f, 1.06f));

        if (killed) OnKilled(victim, shooter, def, result.Group == HitGroup.Head);
    }

    private void SpawnWorldImpact(ShotResult result, WeaponDef def, float now)
    {
        bool penetrable = result.BrushIndex >= 0 && Map.Brushes[result.BrushIndex].Penetrable;
        int count = 5;
        for (int i = 0; i < count; i++)
        {
            Vector3 scatter = new(VfxRand(-1f, 1f), VfxRand(0.2f, 1.6f), VfxRand(-1f, 1f));
            float life = VfxRand(0.18f, 0.42f);
            AddImpactParticle(new Particle
            {
                Position = result.Point,
                Velocity = result.Normal * VfxRand(30f, 110f) + scatter * 40f,
                Life = life,
                MaxLife = life,
                Size = VfxRand(1.1f, 2.6f),
                Color = penetrable ? U.Rgb(210, 200, 175) : U.Rgb(226, 220, 198),
                Gravity = true,
                Kind = ParticleKind.Debris,
            });
        }

        AddImpactDecal(new Decal
        {
            Position = result.Point + result.Normal * 0.4f,
            Normal = result.Normal,
            Size = VfxRand(3.2f, 5.4f),
            Life = 12f,
            MaxLife = 12f,
            Color = U.Rgba(38, 34, 30, 205),
            Kind = DecalKind.SurfaceImpact,
        });

        Emit("impact", result.Point, 0.55f, VfxRand(0.9f, 1.15f));
    }

    /// <summary>
    /// Creates the visual-only actor-hit response from the resolved hit point.
    /// The function accepts the real bullet travel vector rather than a render
    /// transform, so entry mist, exit droplets, and backing splats agree with hitscan.
    /// </summary>
    internal void SpawnActorHitVfx(Vector3 point, Vector3 shotDirection, HitGroup group)
    {
        Vector3 travel = U.NormalizeOrZero(shotDirection);
        if (travel == Vector3.Zero) travel = Vector3.UnitZ;

        // The short confirmation mist belongs at the entry side of the hit. Put
        // it just toward the shooter so it is not depth-hidden inside the world
        // model; the physical droplets below remain exit-directed.
        int mistCount = group == HitGroup.Head ? 7 : 5;
        for (int i = 0; i < mistCount; i++)
        {
            Vector3 drift = new(VfxRand(-1f, 1f), VfxRand(-0.45f, 1f), VfxRand(-1f, 1f));
            float life = VfxRand(0.12f, 0.22f);
            Vector3 cloudOffset = drift * VfxRand(0.7f, 2.4f);
            AddBloodMist(new Particle
            {
                Position = point - travel * VfxRand(0.35f, 1.10f) + cloudOffset,
                Velocity = travel * VfxRand(6f, 22f) + drift * VfxRand(8f, 24f),
                Life = life,
                MaxLife = life,
                Size = VfxRand(2.2f, 4.8f),
                Color = U.Rgba(158, 24, 24, 170),
                Gravity = false,
                Kind = ParticleKind.BloodMist,
            });
        }

        int dropletCount = group == HitGroup.Head ? 9 : 6;
        for (int i = 0; i < dropletCount; i++)
        {
            Vector3 spread = new(VfxRand(-1f, 1f), VfxRand(-0.35f, 0.95f), VfxRand(-1f, 1f));
            float life = VfxRand(0.35f, 0.78f);
            AddBloodDroplet(new BloodDroplet
            {
                Position = point + travel * VfxRand(0.8f, 2f),
                Velocity = travel * VfxRand(68f, 174f) + spread * VfxRand(24f, 74f),
                Life = life,
                MaxLife = life,
                Size = VfxRand(1.3f, 2.8f),
                SplatSize = VfxRand(2.2f, 5.6f),
                SplatLife = Cfg.BloodDecalLifetime,
                Color = U.Rgba(128, 18, 18, 205),
            });
        }

        // This short secondary trace provides an immediate readable mark when a
        // surface sits directly behind a target, while later droplets may land on
        // floors, walls, crates, or platforms through the same collision sidecar.
        Vector3 projectionStart = point + travel * 1.0f;
        if (Map.RayHit(projectionStart, travel, Cfg.BloodDecalProjectionRange,
            out _, out Vector3 decalPoint, out Vector3 decalNormal, out _)
            && decalNormal.LengthSquared() > 0.5f)
        {
            AddBloodSplat(new Decal
            {
                Position = decalPoint + decalNormal * Cfg.BloodSplatSurfaceOffset,
                Normal = decalNormal,
                Size = VfxRand(4.4f, 7.8f),
                Life = Cfg.BloodDecalLifetime,
                MaxLife = Cfg.BloodDecalLifetime,
                Color = U.Rgba(112, 15, 16, 190),
                Kind = DecalKind.BloodSplat,
            });
        }
    }

    internal void AddImpactParticle(Particle particle) => AddCapped(_particles, particle, Cfg.MaxImpactParticles);
    internal void AddImpactDecal(Decal decal) => AddCapped(_decals, decal, Cfg.MaxImpactDecals);
    internal void AddBloodMist(Particle particle) => AddCapped(_bloodMists, particle, Cfg.MaxBloodMists);
    internal void AddBloodDroplet(BloodDroplet droplet) => AddCapped(_bloodDroplets, droplet, Cfg.MaxBloodDroplets);
    internal void AddBloodSplat(Decal splat) => AddCapped(_bloodSplats, splat, Cfg.MaxBloodSplats);

    /// <summary>Retains newest presentation effects in deterministic insertion order.</summary>
    internal static void AddCapped<T>(List<T> list, T value, int cap)
    {
        if (cap <= 0) return;
        if (list.Count >= cap) list.RemoveAt(0);
        list.Add(value);
    }

    // ---------------------------------------------------------------- death & scoring

    private void OnKilled(Actor victim, Actor killer, WeaponDef def, bool headshot)
    {
        killer.Kills++;
        victim.Deaths++;
        if (headshot && killer.IsPlayer) PlayerHeadshots++;

        int reward = def.KillAward + (headshot ? Cfg.HeadshotBonus : 0);
        killer.Money = Math.Min(Cfg.MaxMoney, killer.Money + reward);

        KillFeed.Add(new KillFeedEntry
        {
            Killer = killer.Name,
            Victim = victim.Name,
            Weapon = def.Name,
            Headshot = headshot,
            KillerIsPlayer = killer.IsPlayer,
            VictimIsPlayer = victim.IsPlayer,
            SpawnedAt = Time,
        });

        if (KillFeed.Count > 6) KillFeed.RemoveAt(0);

        // The brief reward blip is a private confirmation, not a world sound.
        // Never play it for a bot-versus-bot kill or when the player is the victim.
        if (killer.IsPlayer) Emit("kill", killer.EyePosition, 0.8f, 1f, SoundEventScope.Local);

        if (victim.IsPlayer)
        {
            KilledFlashUntil = Time + 0.9f;
        }
        else if (killer.IsPlayer)
        {
            ScopePulse = 0.25f;
        }

        victim.RespawnAt = Time + Cfg.RespawnDelay;
        victim.Velocity = Vector3.Zero;
        victim.Punch = Vector2.Zero;

        if (killer.Kills >= KillsToWin)
        {
            EndMatch($"{killer.Name.ToUpperInvariant()} REACHED {KillsToWin} KILLS");
        }
    }

    private void HandleRespawns()
    {
        if (MatchOver) return;
        foreach (Actor actor in Actors)
        {
            if (actor.Alive || Time < actor.RespawnAt) continue;
            Respawn(actor, first: false);
        }
    }

    public void Respawn(Actor actor, bool first)
    {
        var occupied = new List<Vector3>();
        foreach (Actor other in Actors)
        {
            if (!ReferenceEquals(other, actor) && other.Alive) occupied.Add(other.Position);
        }

        SpawnPoint spawn = Map.PickSpawn(ref _rng, occupied, 900f);
        actor.Position = spawn.Position;
        actor.Yaw = spawn.Yaw;
        actor.Pitch = 0f;
        actor.Punch = Vector2.Zero;
        actor.Velocity = Vector3.Zero;
        actor.OnGround = false;
        actor.CrouchAmount = 0f;
        actor.Health = Cfg.MaxHealth;
        actor.Armor = Cfg.MaxArmor;
        actor.HasHelmet = true;
        actor.Alive = true;
        actor.RespawnAt = 0f;
        actor.LastShotTime = -10f;

        if (actor is Player player)
        {
            player.ResetMovementState();
            player.ResetPresentationFeedback();
        }

        if (actor.Money < Cfg.RespawnTopUp) actor.Money = Cfg.RespawnTopUp;

        if (actor is Bot bot)
        {
            bot.Weapons.Clear();
            bot.GiveWeapon(WeaponRegistry.Knife);
            WeaponDef secondary = WeaponRegistry.SecondaryPool[_rng.RangeInt(0, WeaponRegistry.SecondaryPool.Length)];
            bot.GiveWeapon(secondary);

            // Tougher bots trend toward rifles; AWP only for the best.
            int roll = _rng.RangeInt(0, 100);
            int skillTier = (int)bot.Profile.TurnRate / 100;
            bool takePrimary = roll < 55 + skillTier * 12;
            if (takePrimary)
            {
                WeaponDef primary = WeaponRegistry.PrimaryPool[_rng.RangeInt(0, WeaponRegistry.PrimaryPool.Length)];
                if (primary == WeaponRegistry.Awp && bot.Difficulty < 0.6f)
                {
                    primary = WeaponRegistry.Ak47;
                }
                bot.GiveWeapon(primary);
            }

            bot.SelectWeapon(bot.Weapons.Count > 2 ? WeaponSlot.Primary : WeaponSlot.Secondary);
            bot.State = BotState.Patrol;
        }
        else
        {
            foreach (Weapon weapon in actor.Weapons)
            {
                weapon.Mag = weapon.Def.Magazine;
                weapon.Reserve = weapon.Def.Reserve;
                weapon.Reloading = false;
                weapon.Bloom = 0f;
                weapon.ResetRecoil();
            }

            if (actor.Weapons.Count == 0) actor.GiveWeapon(WeaponRegistry.Knife);
            actor.SelectWeapon(actor.Weapons.Any(w => w.Def.Slot == WeaponSlot.Primary)
                ? WeaponSlot.Primary
                : WeaponSlot.Secondary);
        }

        if (first && actor.IsPlayer) Emit("spawn", actor.Position, 0.5f, 1f, SoundEventScope.Local);
        else RespawnCount++;
    }

    private void EmitFootsteps(float dt)
    {
        foreach (Actor actor in Actors)
        {
            if (!actor.Alive || !actor.OnGround) continue;
            float speed = actor.HorizontalSpeed;
            if (speed < 40f) continue;

            float distanceToPlayer = (actor.Position - Player.Position).Length();
            if (distanceToPlayer > 1600f) continue;

            actor.StepAccumulator += speed * dt;
            float threshold = actor.Crouching ? 220f : 150f;
            if (actor.StepAccumulator < threshold) continue;
            actor.StepAccumulator = 0f;

            float volume = actor.IsPlayer ? 0.32f : U.Clamp01(1f - distanceToPlayer / 1600f) * 0.5f;
            if (actor.Crouching) volume *= 0.35f;
            // The map sidecar exposes only broad collision materials. Query it
            // rather than inventing texture types from the rendered GLB; custom
            // audio packs fall back to the legacy "step" cue when they do not
            // provide a matching surface derivative.
            string stepCue = "step";
            if (Map.FindFloorSurface(actor.Position + Vector3.UnitY * 0.5f, 1f,
                Cfg.GroundSnap + Cfg.StepHeight + 2f, out _, out SurfaceKind surface))
            {
                stepCue = surface switch
                {
                    SurfaceKind.Floor => "step.floor",
                    SurfaceKind.Crate => "step.crate",
                    SurfaceKind.Metal => "step.metal",
                    SurfaceKind.Platform => "step.platform",
                    _ => "step",
                };
            }
            Emit(stepCue, actor.Position, volume, Rand(0.9f, 1.12f));

            if (actor is Player player)
            {
                float pace = player.SprintState switch
                {
                    PlayerSprintState.TacticalSprinting => 1f,
                    PlayerSprintState.Sprinting => 0.82f,
                    _ => 0.56f,
                };
                if (actor.Crouching) pace *= 0.45f;
                player.TriggerFootstepFeedback(pace);
            }
        }
    }

    private void TickEffects(float dt)
    {
        for (int i = Tracers.Count - 1; i >= 0; i--)
        {
            Tracer t = Tracers[i];
            t.Life -= dt;
            if (t.Life <= 0f) Tracers.RemoveAt(i);
            else Tracers[i] = t;
        }

        TickParticles(_particles, dt);
        TickParticles(_bloodMists, dt);
        TickBloodDroplets(dt);

        for (int i = DamageNumbers.Count - 1; i >= 0; i--)
        {
            FloatingDamage d = DamageNumbers[i];
            d.Life -= dt;
            if (d.Life <= 0f) DamageNumbers.RemoveAt(i);
            else
            {
                d.Position += new Vector3(0f, 34f * dt, 0f);
                DamageNumbers[i] = d;
            }
        }

        TickDecals(_decals, dt);
        TickDecals(_bloodSplats, dt);

        for (int i = KillFeed.Count - 1; i >= 0; i--)
        {
            if (Time - KillFeed[i].SpawnedAt > 6.5f) KillFeed.RemoveAt(i);
        }
    }

    private static void TickParticles(List<Particle> particles, float dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                particles.RemoveAt(i);
                continue;
            }

            if (p.Gravity) p.Velocity -= new Vector3(0f, Cfg.Gravity * 0.55f * dt, 0f);
            p.Position += p.Velocity * dt;
            p.Velocity *= MathF.Exp(-2.4f * dt);
            particles[i] = p;
        }
    }

    /// <summary>
    /// Sweeps each high-velocity droplet across its whole frame step.  This is
    /// intentionally a simulation-side map query so it cannot tunnel through a
    /// thin collision brush or become dependent on render frame rate.
    /// </summary>
    private void TickBloodDroplets(float dt)
    {
        for (int i = _bloodDroplets.Count - 1; i >= 0; i--)
        {
            BloodDroplet d = _bloodDroplets[i];
            d.Life -= dt;
            if (d.Life <= 0f)
            {
                _bloodDroplets.RemoveAt(i);
                continue;
            }

            d.Velocity -= new Vector3(0f, Cfg.Gravity * 0.72f * dt, 0f);
            Vector3 delta = d.Velocity * dt;
            float distance = delta.Length();
            if (distance > 1e-5f && Map.RayHit(d.Position, delta / distance, distance,
                out _, out Vector3 point, out Vector3 normal, out _))
            {
                // A zero normal can occur for a ray whose origin is already
                // embedded in a brush.  Remove that malformed droplet without
                // making a directionless splat.
                if (normal.LengthSquared() > 0.5f)
                {
                    AddBloodSplat(new Decal
                    {
                        Position = point + normal * Cfg.BloodSplatSurfaceOffset,
                        Normal = normal,
                        Size = d.SplatSize,
                        Life = d.SplatLife,
                        MaxLife = d.SplatLife,
                        Color = d.Color,
                        Kind = DecalKind.BloodSplat,
                    });
                }
                _bloodDroplets.RemoveAt(i);
                continue;
            }

            d.Position += delta;
            d.Velocity *= MathF.Exp(-2.4f * dt);
            _bloodDroplets[i] = d;
        }
    }

    private static void TickDecals(List<Decal> decals, float dt)
    {
        for (int i = decals.Count - 1; i >= 0; i--)
        {
            Decal d = decals[i];
            d.Life -= dt;
            if (d.Life <= 0f) decals.RemoveAt(i);
            else decals[i] = d;
        }
    }

    public void EndMatch(string reason)
    {
        if (MatchOver) return;
        MatchOver = true;
        MatchResult = reason;
        Emit("match_end", Player.EyePosition, 0.9f, 1f, SoundEventScope.Local);
    }

    public IEnumerable<Actor> Leaderboard()
        => Actors.OrderByDescending(a => a.Kills).ThenBy(a => a.Deaths);

    public int PlayerRank()
    {
        int rank = 1;
        foreach (Actor a in Actors)
        {
            if (ReferenceEquals(a, Player)) continue;
            if (a.Kills > Player.Kills) rank++;
        }
        return rank;
    }
}
