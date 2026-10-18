using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim
{
    /// <summary>Which actors fight whom. Team 0 is shared deathmatch.</summary>
    public enum MatchMode
    {
        Deathmatch,
    }

    /// <summary>One of the four cabinet stats.</summary>
    public enum UpgradeStat
    {
        Health = 0,
        Damage = 1,
        Speed = 2,
        Jump = 3,
    }

    /// <summary>A kill-point upgrade cabinet placed on the map.</summary>
    public sealed class UpgradeStation
    {
        public readonly int Id;
        public readonly Vector3 Position;

        public UpgradeStation(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }
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

        /// <summary>Slot-by-slot gun names the player respawns with (1-3).</summary>
        public readonly string[] Loadout;

        /// <summary>Cabinet positions, always derived from the static nav graph.</summary>
        public readonly List<UpgradeStation> Stations = new();

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

        public Simulation(int botCount, BotSkill skill, ulong seed, int killsToWin = Cfg.KillsToWin,
            string[]? loadout = null, string mapName = MapRegistry.Default)
        {
            _rng = new Rng(seed);
            _vfxRng = new Rng(seed ^ 0xB10D_D00D_91A7_5EEDUL);
            SeedSource = new Rng(seed);
            KillsToWin = killsToWin;
            Mode = MatchMode.Deathmatch;

            Map = new GameMap(mapName);
            Nav = NavGraph.Build(Map);
            BuildStations();

            Loadout = new string[3];
            for (int i = 0; i < 3; i++)
            {
                Loadout[i] = loadout is not null && i < loadout.Length && !string.IsNullOrWhiteSpace(loadout[i])
                    ? loadout[i]
                    : WeaponRegistry.DefaultLoadout[i];
            }

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

        /// <summary>
        /// Places the upgrade cabinets on nodes of the static navigation graph so
        /// they are always standable and reachable, spread across the map.
        /// </summary>
        private void BuildStations()
        {
            Stations.Clear();
            NavGraph.Node[] nodes = Nav.Nodes;
            if (nodes.Length == 0) return;

            int wanted = Math.Min(3, Math.Max(1, nodes.Length / 8));
            const float minSeparation = 900f;

            for (int k = 0; k < wanted; k++)
            {
                int start = (int)((k + 0.5f) / wanted * nodes.Length);
                for (int step = 0; step < nodes.Length; step++)
                {
                    int idx = (start + step * 7) % nodes.Length;
                    Vector3 candidate = nodes[idx].Position;
                    if (Map.FindFloor(candidate + Vector3.UnitY * 10f, 10f, 160f, out float floorY))
                    {
                        candidate = new Vector3(candidate.X, floorY, candidate.Z);
                    }

                    bool clash = false;
                    foreach (UpgradeStation s in Stations)
                    {
                        if (U.FlatLength(s.Position - candidate) < minSeparation)
                        {
                            clash = true;
                            break;
                        }
                    }
                    if (clash) continue;

                    Stations.Add(new UpgradeStation(Stations.Count, candidate));
                    break;
                }
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
                if (Player.WantsSecondaryFire && Player.Alive) FireWeaponSecondary(Player);
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

            TickHealthRegen(dt);
            ActorSeparation();
            HandleRespawns();
            EmitFootsteps(dt);
            TickEffects(dt);

            DamageFlash = U.Approach(DamageFlash, 0f, 3.2f, dt);
            ScopePulse = MathF.Max(0f, ScopePulse - dt);
        }

        /// <summary>
        /// Out-of-combat health regeneration from the reference: a short delay
        /// after the last hit, then a fast heal back to (upgraded) maximum.
        /// </summary>
        private void TickHealthRegen(float dt)
        {
            foreach (Actor actor in Actors)
            {
                if (!actor.Alive) continue;
                if (Time - actor.LastDamageTime < Cfg.RegenDelay) continue;
                if (actor.Health >= actor.MaxHealth) continue;
                actor.Health = MathF.Min(actor.MaxHealth, actor.Health + Cfg.RegenRate * dt);
            }
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
                    Vector3 pressedA = new(a.Position.X - nx * push, a.Position.Y, a.Position.Z - nz * push);
                    Vector3 pressedB = new(b.Position.X + nx * push, b.Position.Y, b.Position.Z + nz * push);

                    // The soft push never re-checks the level, so refuse any shove
                    // that would press an actor into a wall; the next tick's mover
                    // resolves the rest once the pair separates naturally.
                    if (!Map.BoxBlocked(Aabb.Footprint(pressedA, Cfg.HalfWidth, a.HeadHeight)))
                        a.Position = pressedA;
                    if (!Map.BoxBlocked(Aabb.Footprint(pressedB, Cfg.HalfWidth, b.HeadHeight)))
                        b.Position = pressedB;
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
            float spread = weapon.SpreadFor(shooter.HorizontalSpeed, shooter.OnGround, shooter.Crouching, aimBlend);
            if (shooter is Bot bot) spread += bot.Profile.AimError * 0.30f;

            Vector3 origin = shooter.EyePosition;
            Vector3 baseDir = shooter.AimDirection;

            // Recoil is applied after the shot so the first bullet flies true.
            Vector2 recoil = weapon.NextRecoil(ref _rng);
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
        /// The Double Barrel's right-click panic blast: a wide multi-pellet burst
        /// that trades accuracy for huge close-range damage and launches the
        /// shooter opposite their aim - the reference's built-in movement tool.
        /// </summary>
        public void FireWeaponSecondary(Actor shooter)
        {
            Weapon weapon = shooter.Current;
            WeaponDef def = weapon.Def;
            float now = Time;
            if (!def.HasSecondary || !weapon.CanFire(now)) return;

            float aimBlend = shooter is Player playerShooter ? playerShooter.ZoomBlend : 0f;
            float spread = weapon.SpreadFor(shooter.HorizontalSpeed, shooter.OnGround, shooter.Crouching, aimBlend)
                + def.SecondarySpreadBonus;

            Vector3 origin = shooter.EyePosition;
            Vector3 baseDir = shooter.AimDirection;

            Vector2 recoil = weapon.NextRecoil(ref _rng);
            shooter.LastShotTime = now;
            weapon.RegisterShot(now);
            shooter.OnShotFired(now);

            ShotsFired.TryGetValue(shooter.Id, out int fired);
            ShotsFired[shooter.Id] = fired + 1;

            Vector3 muzzle = GetPhysicalMuzzleOrigin(shooter);
            int pellets = Math.Max(1, def.SecondaryPellets);
            float perPellet = def.SecondaryDamage / pellets;
            var knockedVictims = new HashSet<int>();

            for (int pellet = 0; pellet < pellets; pellet++)
            {
                Vector3 dir = Hitscan.ApplySpread(baseDir, spread, ref _rng);
                ShotResult result = Hitscan.Trace(Map, Actors, shooter, origin, dir, def, ref _rng);

                if (result.HitActor && result.Victim is not null)
                {
                    Actor victim = result.Victim;

                    // The blast inherits the gun's authored falloff: divide the
                    // ranged damage by the point-blank damage to get the scale.
                    float distance = (result.Point - origin).Length();
                    float scale = def.Damage > 0.0001f
                        ? Hitscan.BaseDamageAtRange(def, distance) / def.Damage
                        : 1f;
                    float damage = perPellet * scale;

                    bool killed = victim.ApplyDamage(damage, result.Group, result.Point, shooter, now);

                    ShotsHit.TryGetValue(shooter.Id, out int hits);
                    ShotsHit[shooter.Id] = hits + 1;
                    if (result.Group == HitGroup.Head)
                    {
                        Headshots.TryGetValue(shooter.Id, out int hs);
                        Headshots[shooter.Id] = hs + 1;
                    }

                    SpawnActorHitVfx(result.Point, dir, result.Group);

                    // Victims get launched once per blast, not once per pellet.
                    if (def.SecondaryVictimKnockback > 0f && knockedVictims.Add(victim.Id))
                    {
                        Vector3 push = U.FlatNormalized(dir);
                        victim.Velocity += push * def.SecondaryVictimKnockback;
                    }

                    if (shooter.IsPlayer)
                    {
                        HitmarkerUntil = now + 0.12f;
                        LastHitWasHeadshot = result.Group == HitGroup.Head;
                        LastHitWasKill = killed;
                    }

                    if (killed) OnKilled(victim, shooter, def, result.Group == HitGroup.Head);
                }
                else if (result.HitWorld)
                {
                    SpawnWorldImpact(result, def, now);
                }

                Tracers.Add(new Tracer
                {
                    From = muzzle,
                    To = result.Point,
                    Life = 0.075f,
                    MaxLife = 0.075f,
                    Color = def.Tracer,
                    Width = shooter.IsPlayer ? 1.6f : 1.2f,
                });
            }

            // The blast shoves the shooter opposite the aim, plus a lift, so
            // firing while looking down turns the gun into a jump.
            shooter.Velocity += -baseDir * def.SecondarySelfKnockback + Vector3.UnitY * def.SecondarySelfUp;

            shooter.Punch = new Vector2(
                U.Clamp(shooter.Punch.X + recoil.X, -36f, 36f),
                U.Clamp(shooter.Punch.Y + recoil.Y * 1.7f, -40f, 40f));

            Emit(def.FireSound, origin, shooter.IsPlayer ? 0.95f : 0.8f, Rand(0.82f, 0.92f));
        }

        /// <summary>
        /// The V-key quick knife swing: a short melee ray independent of the held
        /// weapon. Returns true when the swing happens so the player can animate.
        /// </summary>
        public bool PerformQuickMelee(Player player)
        {
            WeaponDef def = WeaponRegistry.Knife;
            float now = Time;
            Vector3 origin = player.EyePosition;
            Vector3 dir = player.AimDirection;

            Emit("knife", origin, 0.5f, Rand(0.95f, 1.05f), SoundEventScope.Local);

            ShotResult result = Hitscan.Trace(Map, Actors, player, origin, dir, def, ref _rng);
            if (result.HitActor && result.Victim is not null)
            {
                ShotsFired.TryGetValue(player.Id, out int fired);
                ShotsFired[player.Id] = fired + 1;
                ShotsHit.TryGetValue(player.Id, out int hits);
                ShotsHit[player.Id] = hits + 1;

                bool killed = result.Victim.ApplyDamage(result.Damage, result.Group, result.Point, player, now);
                SpawnActorHitVfx(result.Point, dir, result.Group);

                HitmarkerUntil = now + 0.12f;
                LastHitWasHeadshot = false;
                LastHitWasKill = killed;

                Emit(result.Group == HitGroup.Head ? "hit_head" : "hit_flesh",
                    result.Point, 0.9f, Rand(0.95f, 1.06f));

                if (killed) OnKilled(result.Victim, player, def, false);
            }
            else if (result.HitWorld)
            {
                SpawnWorldImpact(result, def, now);
            }

            return true;
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
            bool killed = victim.ApplyDamage(damage, result.Group, result.Point, shooter, now);

            if (result.Group == HitGroup.Head)
            {
                Headshots.TryGetValue(shooter.Id, out int hs);
                Headshots[shooter.Id] = hs + 1;
            }

            if (emitImpactVfx) SpawnActorHitVfx(result.Point, shotDirection, result.Group);

            // Weapons with authored knockback shove the victim along the shot line.
            if (def.KnockbackOnHit > 0f && victim.Alive)
            {
                Vector3 push = U.FlatNormalized(shotDirection);
                victim.Velocity += push * def.KnockbackOnHit;
            }

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
                    Color = penetrable ? U.Argb(210, 200, 175) : U.Argb(226, 220, 198),
                    Gravity = true,
                    Kind = ParticleKind.Debris,
                });
            }

            // Decals land only on surfaces the level actually draws. The
            // imported Industrial Zone's containment walls are collision-only,
            // so a mark pinned there would hang in the sky for its full life;
            // those surfaces keep the spark and the sound as boundary feedback
            // but never a decal.
            bool visibleSurface = result.BrushIndex < 0 || !Map.Brushes[result.BrushIndex].Invisible;
            if (visibleSurface)
            {
                AddImpactDecal(new Decal
                {
                    Position = result.Point + result.Normal * 0.4f,
                    Normal = result.Normal,
                    Size = VfxRand(3.2f, 5.4f),
                    Life = 12f,
                    MaxLife = 12f,
                    Color = U.Argb(38, 34, 30, 205),
                    Kind = DecalKind.SurfaceImpact,
                });
            }

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
                    Color = U.Argb(158, 24, 24, 170),
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
                    Color = U.Argb(128, 18, 18, 205),
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
                    Color = U.Argb(112, 15, 16, 190),
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

        // ---------------------------------------------------------------- upgrades

        /// <summary>The nearest cabinet inside interact range, or null.</summary>
        public UpgradeStation? NearbyStation(Actor actor)
        {
            UpgradeStation? best = null;
            float bestDistance = Cfg.StationInteractRadius;
            foreach (UpgradeStation station in Stations)
            {
                float distance = U.FlatLength(station.Position - actor.Position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = station;
                }
            }
            return best;
        }

        public static string StatName(UpgradeStat stat) => stat switch
        {
            UpgradeStat.Health => "HEALTH",
            UpgradeStat.Damage => "DAMAGE",
            UpgradeStat.Speed => "SPEED",
            _ => "JUMP",
        };

        public static int UpgradeLevel(Actor actor, UpgradeStat stat) => stat switch
        {
            UpgradeStat.Health => actor.UpgradeHealth,
            UpgradeStat.Damage => actor.UpgradeDamage,
            UpgradeStat.Speed => actor.UpgradeSpeed,
            _ => actor.UpgradeJump,
        };

        /// <summary>True when the actor can pay for the next level of this stat.</summary>
        public static bool CanBuyUpgrade(Actor actor, UpgradeStat stat)
            => actor.KillPoints >= Cfg.UpgradePointCost && UpgradeLevel(actor, stat) < Cfg.UpgradeMaxLevel;

        /// <summary>
        /// Spends kill points on one cabinet level. Points persist through death;
        /// levels do not fully: dying costs the highest stat one level.
        /// </summary>
        public bool TryBuyUpgrade(Actor actor, UpgradeStat stat)
        {
            if (!CanBuyUpgrade(actor, stat))
            {
                if (actor.IsPlayer)
                    Emit("upgrade_deny", actor.EyePosition, 0.6f, 1f, SoundEventScope.Local);
                return false;
            }

            actor.KillPoints -= Cfg.UpgradePointCost;
            switch (stat)
            {
                case UpgradeStat.Health:
                    actor.UpgradeHealth++;
                    actor.Health = MathF.Min(actor.MaxHealth, actor.Health + Cfg.UpgradeHealthPerLevel);
                    break;
                case UpgradeStat.Damage:
                    actor.UpgradeDamage++;
                    break;
                case UpgradeStat.Speed:
                    actor.UpgradeSpeed++;
                    break;
                default:
                    actor.UpgradeJump++;
                    break;
            }

            Emit("upgrade", actor.EyePosition, 0.8f, 1f, SoundEventScope.Local);
            return true;
        }

        /// <summary>
        /// Dying costs one level of the highest upgraded stat, if any. The reference
        /// ruleset always charges this penalty; the Cfg flag records the rule.
        /// </summary>
        private static void ApplyDeathUpgradePenalty(Actor victim)
        {
            UpgradeStat best = UpgradeStat.Health;
            int bestLevel = 0;
            foreach (UpgradeStat stat in new[]
                     { UpgradeStat.Health, UpgradeStat.Damage, UpgradeStat.Speed, UpgradeStat.Jump })
            {
                int level = UpgradeLevel(victim, stat);
                if (level > bestLevel)
                {
                    bestLevel = level;
                    best = stat;
                }
            }

            if (bestLevel <= 0) return;

            switch (best)
            {
                case UpgradeStat.Health: victim.UpgradeHealth--; break;
                case UpgradeStat.Damage: victim.UpgradeDamage--; break;
                case UpgradeStat.Speed: victim.UpgradeSpeed--; break;
                default: victim.UpgradeJump--; break;
            }
        }

        // ---------------------------------------------------------------- death & scoring

        private void OnKilled(Actor victim, Actor killer, WeaponDef def, bool headshot)
        {
            killer.Kills++;
            victim.Deaths++;
            if (headshot && killer.IsPlayer) PlayerHeadshots++;
            killer.KillPoints += Cfg.KillPointsPerKill;

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
                ApplyDeathUpgradePenalty(victim);
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
            actor.Health = actor.MaxHealth;
            actor.Alive = true;
            actor.RespawnAt = 0f;
            actor.LastDamageTime = -100f;
            actor.LastShotTime = -10f;

            if (actor is Player player)
            {
                player.FlashlightOn = false;
                player.ResetMovementState();
                player.ResetPresentationFeedback();

                // Fresh guns every life: full magazines, no inherited reload state.
                player.Weapons.Clear();
                player.GiveWeapon(WeaponRegistry.Knife, 4);
                for (int i = 0; i < Loadout.Length && i < 3; i++)
                {
                    player.GiveWeapon(WeaponRegistry.ByName(Loadout[i]), i + 1);
                }
                player.SelectWeaponNumber(1);
            }
            else if (actor is Bot bot)
            {
                bot.Weapons.Clear();
                bot.GiveWeapon(WeaponRegistry.Knife, 4);

                WeaponDef secondary = WeaponRegistry.SecondaryPool[
                    _rng.RangeInt(0, WeaponRegistry.SecondaryPool.Length)];
                bot.GiveWeapon(secondary, 1);

                // Tougher bots trend toward real primaries; snipers only for the best.
                int roll = _rng.RangeInt(0, 100);
                int skillTier = (int)bot.Profile.TurnRate / 100;
                bool takePrimary = roll < 55 + skillTier * 12;
                if (takePrimary)
                {
                    WeaponDef primary = WeaponRegistry.PrimaryPool[
                        _rng.RangeInt(0, WeaponRegistry.PrimaryPool.Length)];
                    if (primary == WeaponRegistry.Sniper && bot.Difficulty < 0.6f)
                    {
                        primary = WeaponRegistry.Rifle;
                    }
                    bot.GiveWeapon(primary, 2);
                    bot.SelectWeaponNumber(2);
                }
                else
                {
                    bot.SelectWeaponNumber(1);
                }

                bot.State = BotState.Patrol;
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
                const float threshold = 150f;
                if (actor.StepAccumulator < threshold) continue;
                actor.StepAccumulator = 0f;

                float volume = actor.IsPlayer ? 0.32f : U.Clamp01(1f - distanceToPlayer / 1600f) * 0.5f;
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
                    float pace = U.Clamp01(actor.HorizontalSpeed / Cfg.RunSpeed);
                    player.TriggerFootstepFeedback(0.4f + 0.6f * pace);
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
}
