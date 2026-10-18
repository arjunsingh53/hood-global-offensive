using System;
using System.Numerics;
using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.Combat
{
    /// <summary>Immutable description of a weapon. Runtime state lives in <see cref="Weapon"/>.</summary>
    public sealed class WeaponDef
    {
        public string Name = "";
        public string Class = "";

        /// <summary>True for the knife: no ammo, no reload, short ray.</summary>
        public bool Melee;

        public float Damage = 30f;
        /// <summary>Uniform damage in the reference: heads take the same damage as the body.</summary>
        public float HeadshotMultiplier = 1f;
        /// <summary>Legacy exponential falloff per 1,000 world units; unused when a linear band is authored.</summary>
        public float Falloff;
        /// <summary>Start of the linear damage-falloff band, in world units; zero disables it.</summary>
        public float FalloffStart;
        /// <summary>End of the linear damage-falloff band, in world units.</summary>
        public float FalloffEnd;
        /// <summary>Damage multiplier reached at <see cref="FalloffEnd"/> and beyond.</summary>
        public float FalloffMinMultiplier = 1f;
        /// <summary>Hard cap for the hit-scan ray.</summary>
        public float Range = 3000f;

        public float CycleTime = 0.1f;
        /// <summary>Seconds a freshly pulled-out copy of this gun takes before it can fire.</summary>
        public float DeployTime;
        public bool Automatic;
        public int Magazine = 30;
        public int Reserve = 999_999;
        /// <summary>Every Redmatch-style gun reloads out of an unlimited pool.</summary>
        public bool InfiniteReserve = true;
        public float ReloadTime = 2.5f;

        // ------------------------------------------------------------ secondary fire
        /// <summary>
        /// A second trigger mode on right-click instead of aim-down-sights. The
        /// Double Barrel's panic blast is the reference's example: huge damage,
        /// huge inaccuracy, and a big self-knockback that doubles as a movement tool.
        /// </summary>
        public bool HasSecondary;
        public float SecondaryDamage;
        public int SecondaryPellets = 1;
        /// <summary>Extra cone half-angle added on top of the normal spread, in degrees.</summary>
        public float SecondarySpreadBonus;
        /// <summary>Knockback applied to the shooter, opposite the aim direction.</summary>
        public float SecondarySelfKnockback;
        /// <summary>Extra upward push for the shooter so aiming down becomes a jump.</summary>
        public float SecondarySelfUp = 120f;
        /// <summary>Knockback applied to victims of the secondary blast.</summary>
        public float SecondaryVictimKnockback;

        // ------------------------------------------------------------ knockback
        /// <summary>Knockback applied to a victim along the shot direction, in units per second.</summary>
        public float KnockbackOnHit;

        /// <summary>Movement speed multiplier while this weapon is in hand.</summary>
        public float MoveScale = 1f;

        public float SpreadIdle = 0.2f;
        public float SpreadMove = 3.5f;
        public float SpreadAir = 12f;
        public float SpreadBloom = 0.28f;
        public float SpreadBloomMax = 6f;
        public float SpreadDecay = 4.5f;
        public float SpreadCrouchScale = 0.62f;
        /// <summary>
        /// Multiplies only the steady and recoil-bloom portions of spread at full
        /// ADS. Movement and air penalties remain explicit.
        /// </summary>
        public float AimSpreadMultiplier = 0.55f;

        /// <summary>View kick per shot, in degrees of upward punch.</summary>
        public float RecoilKick = 1f;
        /// <summary>Maximum random horizontal punch per shot, in degrees.</summary>
        public float RecoilYawKick = 0.3f;
        public float RecoilRecoveryRate = 12f;
        public float RecoilRecoveryDelay = 0.28f;
        public float VisualKick = 8f;

        public int Pellets = 1;
        /// <summary>
        /// First-person aim field of view. A positive value enables hold-to-aim on
        /// secondary fire without drawing a scope mask.
        /// </summary>
        public float AdsFov;
        /// <summary>
        /// True only when the first-person model has a measured rear/front sight
        /// picture aligned to the authoritative centre ray.
        /// </summary>
        public bool HasCalibratedIronSightPicture;
        /// <summary>Magnified optic field of view. A positive value enables the scope path.</summary>
        public float ZoomFov;
        public int PenetrationPower;
        public float PenetrationDamageScale = 0.55f;

        /// <summary>ARGB tracer colour for the view layer; the simulation never stores colours.</summary>
        public uint Tracer = 0xFFFFECA0;
        public string FireSound = "shot";

        public bool HasScope => ZoomFov > 0f;
        public bool HasIronSights => AdsFov > 0f;
        public bool HasCalibratedIronSights => HasIronSights && HasCalibratedIronSightPicture;
        public bool HasZoom => HasScope || HasIronSights;
        public float AimFov => HasScope ? ZoomFov : AdsFov;
        /// <summary>True when the weapon consumes magazine ammo and reloads.</summary>
        public bool UsesAmmo => !Melee;
    }

    /// <summary>Runtime weapon instance carried by an actor in one numbered slot.</summary>
    public sealed class Weapon
    {
        public readonly WeaponDef Def;
        /// <summary>1-based hotkey slot: 1-3 are loadout guns, 4 the knife.</summary>
        public readonly int Slot;
        public int Mag;
        public int Reserve;
        public float NextShotTime;
        /// <summary>Sim time before which the gun cannot fire because it is still being drawn.</summary>
        public float ReadyTime;
        public bool Reloading;
        public float ReloadEndTime;
        public float ReloadStartTime;
        public int ShotIndex;
        public float Bloom;
        /// <summary>Whether the current reload started on an empty magazine.</summary>
        public bool ReloadStartedEmpty;

        public Weapon(WeaponDef def, int slot, int? reserveOverride = null, int? magOverride = null)
        {
            Def = def;
            Slot = slot;
            Mag = magOverride ?? def.Magazine;
            Reserve = reserveOverride ?? (def.InfiniteReserve ? 999_999 : def.Reserve);
        }

        public bool IsEmpty => Def.UsesAmmo && Mag <= 0 && Reserve <= 0;

        public bool CanFire(float now)
            => !Reloading && now >= NextShotTime && now >= ReadyTime && (!Def.UsesAmmo || Mag > 0);

        public bool IsAutomatic => Def.Automatic;

        public float SpreadFor(float horizontalSpeed, bool onGround, bool crouching, float aimBlend = 0f)
        {
            float aimScale = Def.HasZoom
                ? U.Lerp(1f, Def.AimSpreadMultiplier, U.Clamp01(aimBlend))
                : 1f;
            float speedRatio = U.Clamp01(horizontalSpeed / Cfg.RunSpeed);
            float spread = (Def.SpreadIdle + Bloom) * aimScale;

            // A magnified sight picture is pinpoint only while the shooter is
            // planted: ground movement feeds a quadratic stability penalty at
            // full zoom while the airborne case below keeps its explicit air +
            // move terms. Hip-fire is unchanged.
            if (onGround && Def.HasScope && aimBlend > 0f)
                spread += Def.SpreadMove * speedRatio * speedRatio * U.Clamp01(aimBlend);

            if (!onGround) spread += Def.SpreadAir + Def.SpreadMove * speedRatio;
            if (crouching && onGround) spread *= Def.SpreadCrouchScale;
            return spread;
        }

        public void Tick(float now, float dt)
        {
            if (Reloading && now >= ReloadEndTime) FinishReload();
            if (Bloom > 0f) Bloom = MathF.Max(0f, Bloom - Def.SpreadDecay * dt);
            if (Reloading) Bloom = 0f;
        }

        public bool BeginReload(float now)
        {
            if (!Def.UsesAmmo || Reloading || Mag >= Def.Magazine || Reserve <= 0) return false;
            Reloading = true;
            ReloadStartedEmpty = Mag == 0;
            ReloadStartTime = now;
            ReloadEndTime = now + Def.ReloadTime;
            ShotIndex = 0;
            return true;
        }

        public void FinishReload()
        {
            Reloading = false;
            int needed = Def.Magazine - Mag;
            int moved = Math.Min(needed, Reserve);
            Mag += moved;
            Reserve -= moved;
            ReloadStartedEmpty = false;
        }

        /// <summary>
        /// Recoil offset for the next shot, in (yaw, pitch) degrees. The shot lands
        /// on the crosshair and the punch is applied afterwards.
        /// </summary>
        public Vector2 NextRecoil(ref Rng rng)
        {
            int i = ShotIndex++;
            float ramp = MathF.Min(1f, 0.65f + i * 0.07f);
            float pitch = Def.RecoilKick * ramp;
            float yawScale = 0.4f + 0.6f * MathF.Min(1f, i / 5f);
            float yaw = rng.Range(-Def.RecoilYawKick, Def.RecoilYawKick) * yawScale;
            return new Vector2(yaw, pitch);
        }

        public void RegisterShot(float now)
        {
            if (Def.UsesAmmo) Mag = Math.Max(0, Mag - 1);
            NextShotTime = now + Def.CycleTime;
            if (Def.UsesAmmo) Bloom = MathF.Min(Def.SpreadBloomMax, Bloom + Def.SpreadBloom);
        }

        public void ResetRecoil() => ShotIndex = 0;
    }

    /// <summary>The armory. No prices: every gun is free and reloads from a magic pool.</summary>
    public static class WeaponRegistry
    {
        public static readonly WeaponDef Knife = new()
        {
            Name = "Knife", Class = "Melee", Melee = true,
            Damage = 55f, Range = 100f,
            CycleTime = 0.4f, Automatic = false, Magazine = 1, Reserve = 0, ReloadTime = 0.01f,
            MoveScale = 1.08f, SpreadIdle = 0f, SpreadMove = 0f, SpreadAir = 0f, SpreadBloom = 0f,
            SpreadBloomMax = 0f, SpreadDecay = 10f, VisualKick = 3f, RecoilKick = 0.4f, RecoilYawKick = 0.2f,
            Tracer = 0xFFDCDCDC, FireSound = "knife",
        };

        public static readonly WeaponDef Pistol = new()
        {
            Name = "Pistol", Class = "Pistol",
            Damage = 25f, Range = 5000f,
            CycleTime = 0.12f, Automatic = false, Magazine = 12, ReloadTime = 1.2f,
            MoveScale = 1.05f, SpreadIdle = 0.35f, SpreadMove = 4.5f, SpreadAir = 14f,
            SpreadBloom = 0.30f, SpreadBloomMax = 5f, SpreadDecay = 5.5f,
            RecoilKick = 1.3f, RecoilYawKick = 0.35f, RecoilRecoveryRate = 16f, RecoilRecoveryDelay = 0.20f,
            VisualKick = 7f, AdsFov = 67f, AimSpreadMultiplier = 0.5f,
            HasCalibratedIronSightPicture = true,
            Tracer = 0xFFFFF0B4, FireSound = "pistol",
        };

        public static readonly WeaponDef Revolver = new()
        {
            // Reference defaults: 75 damage, a shot every 0.4s, 0.4s pull-out,
            // 6-round cylinder, 2.0s reload, and 100% accuracy at all times.
            Name = "Revolver", Class = "Pistol",
            Damage = 75f, Range = 6000f,
            CycleTime = 0.4f, DeployTime = 0.4f, Automatic = false, Magazine = 6, ReloadTime = 2.0f,
            MoveScale = 1.02f, SpreadIdle = 0f, SpreadMove = 0f, SpreadAir = 0f,
            SpreadBloom = 0f, SpreadBloomMax = 0f, SpreadDecay = 6.5f,
            RecoilKick = 4.6f, RecoilYawKick = 0.5f, RecoilRecoveryRate = 18f, RecoilRecoveryDelay = 0.28f,
            VisualKick = 16f, AdsFov = 65f, AimSpreadMultiplier = 0.42f,
            PenetrationPower = 1, PenetrationDamageScale = 0.75f, KnockbackOnHit = 60f,
            Tracer = 0xFFFFF6C8, FireSound = "deagle",
        };

        public static readonly WeaponDef Smg = new()
        {
            // Spec numbers from the reference's own patch notes: 18.16 shots/s,
            // 40-round magazine, 1.0s reload, 0.4s pull-out, 20 damage after the
            // 1.41.3 buff, spread blooms while held.
            Name = "SMG", Class = "SMG",
            Damage = 20f, Range = 5000f,
            CycleTime = 1f / 18.16f, DeployTime = 0.4f, Automatic = true, Magazine = 40, ReloadTime = 1.0f,
            MoveScale = 0.97f, SpreadIdle = 0.4f, SpreadMove = 3.2f, SpreadAir = 11f,
            SpreadBloom = 0.34f, SpreadBloomMax = 6f, SpreadDecay = 5.5f,
            RecoilKick = 0.7f, RecoilYawKick = 0.28f, RecoilRecoveryRate = 14f, RecoilRecoveryDelay = 0.22f,
            VisualKick = 6f, AdsFov = 64f, AimSpreadMultiplier = 0.6f,
            HasCalibratedIronSightPicture = true,
            Tracer = 0xFFFFECA0, FireSound = "smg",
        };

        public static readonly WeaponDef Rifle = new()
        {
            Name = "Rifle", Class = "Rifle",
            Damage = 30f, Range = 7000f,
            CycleTime = 0.1f, Automatic = true, Magazine = 30, ReloadTime = 1.5f,
            MoveScale = 0.93f, SpreadIdle = 0.28f, SpreadMove = 5.5f, SpreadAir = 15f,
            SpreadBloom = 0.24f, SpreadBloomMax = 6f, SpreadDecay = 4.6f,
            RecoilKick = 1.1f, RecoilYawKick = 0.32f, RecoilRecoveryRate = 12f, RecoilRecoveryDelay = 0.26f,
            VisualKick = 9f, AdsFov = 61f, AimSpreadMultiplier = 0.45f,
            HasCalibratedIronSightPicture = true,
            PenetrationPower = 1, PenetrationDamageScale = 0.6f,
            Tracer = 0xFFFFE896, FireSound = "rifle_ak",
        };

        public static readonly WeaponDef Shotgun = new()
        {
            // Reference patch notes: 9 pellets at 17 damage each (153 on a full
            // connect), with 1x damage until 20m falling to 0.1x at 70m.
            Name = "Shotgun", Class = "Shotgun",
            Damage = 17f, Range = 3000f,
            FalloffStart = 800f, FalloffEnd = 2800f, FalloffMinMultiplier = 0.1f,
            CycleTime = 0.8f, Automatic = false, Magazine = 6, ReloadTime = 2.0f,
            MoveScale = 0.92f, SpreadIdle = 4.6f, SpreadMove = 3f, SpreadAir = 8f,
            SpreadBloom = 1.1f, SpreadBloomMax = 4f, SpreadDecay = 6f,
            RecoilKick = 5.5f, RecoilYawKick = 0.6f, RecoilRecoveryRate = 14f, RecoilRecoveryDelay = 0.32f,
            VisualKick = 18f, Pellets = 9, AdsFov = 66f, AimSpreadMultiplier = 0.72f,
            KnockbackOnHit = 140f,
            Tracer = 0xFFFFDC96, FireSound = "shotgun",
        };

        public static readonly WeaponDef Sniper = new()
        {
            Name = "Sniper", Class = "Sniper",
            Damage = 100f, Range = 9000f,
            CycleTime = 1.2f, Automatic = false, Magazine = 5, ReloadTime = 2.2f,
            MoveScale = 0.85f, SpreadIdle = 5.5f, SpreadMove = 8f, SpreadAir = 22f,
            SpreadBloom = 3.5f, SpreadBloomMax = 9f, SpreadDecay = 8f,
            // The scoped rifle is a precision tool: a planted shot resolves through
            // the reticle (0.11 deg at full zoom, movement and bloom still widen
            // follow-ups) and the punch is a firm nudge, not a screen throw.
            RecoilKick = 2.5f, RecoilYawKick = 0.22f, RecoilRecoveryRate = 30f, RecoilRecoveryDelay = 0.28f,
            VisualKick = 19f, ZoomFov = 18f, AimSpreadMultiplier = 0.02f,
            PenetrationPower = 2, PenetrationDamageScale = 0.85f,
            Tracer = 0xFFFFFADC, FireSound = "awp",
        };

        public static readonly WeaponDef DoubleBarrel = new()
        {
            // Spec numbers from the reference's own patch notes: 2-round magazine,
            // as fast as you can click, 0.4s pull-out, 1.33s reload. Left click is
            // an accurate 90-damage shot with light knockback; right click is a
            // 270-damage blast that launches the shooter. Falloff is 1x to 15m,
            // decreasing linearly to 0.2x at 30m (600 to 1,200 world units at
            // 40 units/m).
            Name = "Double Barrel", Class = "Shotgun",
            Damage = 90f, Range = 3000f,
            FalloffStart = 600f, FalloffEnd = 1200f, FalloffMinMultiplier = 0.2f,
            CycleTime = 0.03f, DeployTime = 0.4f, Automatic = false, Magazine = 2, ReloadTime = 1.33f,
            MoveScale = 0.96f, SpreadIdle = 0.5f, SpreadMove = 4.5f, SpreadAir = 13f,
            SpreadBloom = 0.8f, SpreadBloomMax = 5f, SpreadDecay = 7f,
            RecoilKick = 4.8f, RecoilYawKick = 0.5f, RecoilRecoveryRate = 15f, RecoilRecoveryDelay = 0.3f,
            VisualKick = 20f,
            HasSecondary = true, SecondaryDamage = 270f, SecondaryPellets = 8, SecondarySpreadBonus = 8f,
            SecondarySelfKnockback = 380f, SecondarySelfUp = 130f, SecondaryVictimKnockback = 380f,
            KnockbackOnHit = 100f,
            Tracer = 0xFFFFD68C, FireSound = "double_barrel",
        };

        public static readonly WeaponDef[] All =
            { Knife, Pistol, Revolver, Smg, Rifle, Shotgun, Sniper, DoubleBarrel };

        /// <summary>The seven guns a player can assemble a three-slot loadout from.</summary>
        public static readonly WeaponDef[] LoadoutPool =
            { Pistol, Revolver, Smg, Rifle, Shotgun, Sniper, DoubleBarrel };

        /// <summary>Default loadout slots 1-3.</summary>
        public static readonly string[] DefaultLoadout = { "Rifle", "Shotgun", "Pistol" };

        /// <summary>Weapons a bot may be issued, split by slot.</summary>
        public static readonly WeaponDef[] PrimaryPool = { Smg, Rifle, Shotgun, Sniper, DoubleBarrel };
        public static readonly WeaponDef[] SecondaryPool = { Pistol, Revolver };

        /// <summary>
        /// Looks a weapon up by display name, ignoring case. An unknown name falls back
        /// to the Pistol and says so on stderr.
        /// </summary>
        public static WeaponDef ByName(string name)
        {
            WeaponDef? def = Array.Find(All, w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            if (def is not null && def != Knife) return def;

            Console.Error.WriteLine($"unknown weapon '{name}': using {Pistol.Name}");
            return Pistol;
        }

        /// <summary>Next gun in the loadout pool, used by the menu's loadout picker.</summary>
        public static WeaponDef CycleLoadout(WeaponDef current, int direction)
        {
            int index = Array.IndexOf(LoadoutPool, current);
            if (index < 0) index = 0;
            int next = (index + direction) % LoadoutPool.Length;
            if (next < 0) next += LoadoutPool.Length;
            return LoadoutPool[next];
        }
    }
}
