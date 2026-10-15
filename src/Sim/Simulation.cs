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

    // ---------------------------------------------------------------- effects
    public readonly List<Tracer> Tracers = new();
    public readonly List<Particle> Particles = new();
    public readonly List<FloatingDamage> DamageNumbers = new();
    public readonly List<Decal> Decals = new();
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

    public void Emit(string name, Vector3 position, float volume = 1f, float pitch = 1f)
        => Sounds.Add(new SoundEvent(name, position, volume, pitch));

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

        float spread = weapon.SpreadFor(shooter.HorizontalSpeed, shooter.OnGround, shooter.Crouching);
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

        Vector3 muzzle = origin + new Vector3(0f, -4f, 0f);

        for (int pellet = 0; pellet < def.Pellets; pellet++)
        {
            Vector3 dir = Hitscan.ApplySpread(baseDir, spread, ref _rng);
            ShotResult result = Hitscan.Trace(Map, Actors, shooter, origin, dir, def, ref _rng);

            if (result.HitActor && result.Victim is not null)
            {
                ApplyActorHit(shooter, result, def, now);
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

    private void ApplyActorHit(Actor shooter, ShotResult result, WeaponDef def, float now)
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

        SpawnBlood(result.Point, result.Normal, result.Group);

        if (shooter.IsPlayer)
        {
            HitmarkerUntil = now + 0.12f;
            LastHitWasHeadshot = result.Group == HitGroup.Head;
            LastHitWasKill = killed;
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
            Vector3 scatter = new(Rand(-1f, 1f), Rand(0.2f, 1.6f), Rand(-1f, 1f));
            Particles.Add(new Particle
            {
                Position = result.Point,
                Velocity = result.Normal * Rand(30f, 110f) + scatter * 40f,
                Life = Rand(0.18f, 0.42f),
                MaxLife = 0.42f,
                Size = Rand(1.1f, 2.6f),
                Color = penetrable ? U.Rgb(210, 200, 175) : U.Rgb(226, 220, 198),
                Gravity = true,
            });
        }

        Decals.Add(new Decal
        {
            Position = result.Point + result.Normal * 0.4f,
            Normal = result.Normal,
            Size = Rand(3.2f, 5.4f),
            Life = 12f,
            MaxLife = 12f,
            Color = U.Rgba(38, 34, 30, 205),
        });

        Emit("impact", result.Point, 0.55f, Rand(0.9f, 1.15f));
    }

    private void SpawnBlood(Vector3 point, Vector3 normal, HitGroup group)
    {
        int count = group == HitGroup.Head ? 14 : 8;
        for (int i = 0; i < count; i++)
        {
            Particles.Add(new Particle
            {
                Position = point,
                Velocity = normal * Rand(30f, 150f) + new Vector3(Rand(-70f, 70f), Rand(-30f, 90f), Rand(-70f, 70f)),
                Life = Rand(0.2f, 0.55f),
                MaxLife = 0.55f,
                Size = Rand(1.4f, 3.4f),
                Color = U.Rgb(150, 16, 18),
                Gravity = true,
            });
        }
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

        Emit("kill", victim.Position, 0.8f, 1f);

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

        if (first) Emit("spawn", actor.Position, 0.5f, 1f);
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
            Emit("step", actor.Position, volume, Rand(0.9f, 1.12f));
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

        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            Particle p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                Particles.RemoveAt(i);
                continue;
            }
            if (p.Gravity) p.Velocity -= new Vector3(0f, Cfg.Gravity * 0.55f * dt, 0f);
            p.Position += p.Velocity * dt;
            p.Velocity *= MathF.Exp(-2.4f * dt);
            Particles[i] = p;
        }

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

        for (int i = Decals.Count - 1; i >= 0; i--)
        {
            Decal d = Decals[i];
            d.Life -= dt;
            if (d.Life <= 0f) Decals.RemoveAt(i);
            else Decals[i] = d;
        }

        for (int i = KillFeed.Count - 1; i >= 0; i--)
        {
            if (Time - KillFeed[i].SpawnedAt > 6.5f) KillFeed.RemoveAt(i);
        }
    }

    public void EndMatch(string reason)
    {
        if (MatchOver) return;
        MatchOver = true;
        MatchResult = reason;
        Emit("match_end", Vector3.Zero, 0.9f, 1f);
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
