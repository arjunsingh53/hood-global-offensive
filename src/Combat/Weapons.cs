using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.Combat;

public enum WeaponSlot
{
    Knife = 0,
    Secondary = 1,
    Primary = 2,
}

/// <summary>
/// Per-instance fire-control state. Definitions retain their original default
/// behaviour; only a runtime weapon can change its selected mode.
/// </summary>
public enum WeaponFireMode
{
    Semi,
    Auto,
}

/// <summary>
/// An authored damage band for a hitscan weapon.  Distances use HGO's existing
/// world-unit contract rather than assuming that an imported asset happens to
/// be one metre wide.  The final band should normally use
/// <see cref="float.PositiveInfinity"/> so a firearm can remain useful across
/// a long clear lane without reverting to an accidental exponential curve.
/// </summary>
public readonly record struct DamageRangeTier(float MaxDistance, float Damage);

/// <summary>Immutable description of a weapon. Runtime state lives in <see cref="Weapon"/>.</summary>
public sealed class WeaponDef
{
    public string Name = "";
    public string Class = "";
    public WeaponSlot Slot = WeaponSlot.Primary;
    public int Price;

    public float Damage = 30f;
    /// <summary>Fraction of incoming damage that reaches health through kevlar (CS "armor ratio").</summary>
    public float ArmorPenetration = 0.75f;
    public float HeadshotMultiplier = 4f;
    public float LegMultiplier = 0.75f;
    /// <summary>Fraction of damage lost for each 1,000 world units travelled.</summary>
    public float Falloff;
    /// <summary>
    /// Optional authored body-damage bands, ordered nearest to farthest.  When
    /// present they deliberately replace <see cref="Falloff"/> so weapon
    /// ranges are readable, tuneable and covered by a deterministic test.
    /// Older definitions remain on their existing falloff contract until they
    /// receive an explicit balance profile.
    /// </summary>
    public DamageRangeTier[] DamageTiers = Array.Empty<DamageRangeTier>();
    /// <summary>Hard cap for the hit-scan ray, independent of damage falloff.</summary>
    public float Range = 3000f;

    public float CycleTime = 0.1f;
    public bool Automatic;
    /// <summary>
    /// Supported fire controls in default-first order. Empty means to inherit the
    /// legacy <see cref="Automatic"/> value, preserving all existing weapons.
    /// </summary>
    public WeaponFireMode[] FireModes = Array.Empty<WeaponFireMode>();
    public int Magazine = 30;
    public int Reserve = 90;
    public float ReloadTime = 2.5f;

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
    /// ADS. Movement and air penalties remain explicit, so aiming is useful but
    /// never turns a sprint or jump into laser accuracy.
    /// </summary>
    public float AimSpreadMultiplier = 0.55f;

    public Vector2[] Pattern = Array.Empty<Vector2>();
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
    /// picture aligned to the authoritative centre ray. Other hold-to-aim
    /// weapons retain their reticle rather than pretending that a box model has
    /// usable iron sights.
    /// </summary>
    public bool HasCalibratedIronSightPicture;
    /// <summary>Magnified optic field of view. A positive value enables the AWP-style scope path.</summary>
    public float ZoomFov;
    public int PenetrationPower;
    public float PenetrationDamageScale = 0.55f;

    public Color Tracer = U.Rgb(255, 236, 160);
    public string FireSound = "shot";
    public int KillAward = Cfg.KillReward;

    public bool HasScope => ZoomFov > 0f;
    public bool HasIronSights => AdsFov > 0f;
    public bool HasCalibratedIronSights => HasIronSights && HasCalibratedIronSightPicture;
    public bool HasZoom => HasScope || HasIronSights;
    public float AimFov => HasScope ? ZoomFov : AdsFov;

    public WeaponFireMode DefaultFireMode => FireModes.Length > 0
        ? FireModes[0]
        : Automatic ? WeaponFireMode.Auto : WeaponFireMode.Semi;
}

/// <summary>Runtime weapon instance carried by an actor.</summary>
public sealed class Weapon
{
    public readonly WeaponDef Def;
    public int Mag;
    public int Reserve;
    public float NextShotTime;
    public bool Reloading;
    public float ReloadEndTime;
    public float ReloadStartTime;
    public int ShotIndex;
    public float Bloom;
    /// <summary>Whether the current reload started on an empty magazine.</summary>
    public bool ReloadStartedEmpty;
    /// <summary>Player-controlled fire control. Bots retain their definition default.</summary>
    public WeaponFireMode FireMode;

    public Weapon(WeaponDef def, int? reserveOverride = null, int? magOverride = null)
    {
        Def = def;
        FireMode = def.DefaultFireMode;
        Mag = magOverride ?? def.Magazine;
        Reserve = reserveOverride ?? def.Reserve;
    }

    public bool IsEmpty => Mag <= 0 && Reserve <= 0;

    public bool CanFire(float now) => !Reloading && Mag > 0 && now >= NextShotTime;

    public bool IsAutomatic => FireMode == WeaponFireMode.Auto;
    public bool HasSelectableFireMode => Def.FireModes.Length > 1;
    public string FireModeLabel => IsAutomatic ? "AUTO" : "SEMI";

    /// <summary>
    /// Advances this weapon's own selector. The static registry definition is never
    /// mutated, so other actors and later spawns keep their original defaults.
    /// </summary>
    public bool CycleFireMode()
    {
        if (!HasSelectableFireMode || Reloading) return false;

        int current = Array.IndexOf(Def.FireModes, FireMode);
        if (current < 0) current = 0;
        FireMode = Def.FireModes[(current + 1) % Def.FireModes.Length];
        return true;
    }

    public float SpreadFor(float horizontalSpeed, bool onGround, bool crouching, float aimBlend = 0f,
        bool sliding = false)
    {
        float aimScale = Def.HasZoom
            ? U.Lerp(1f, Def.AimSpreadMultiplier, U.Clamp01(aimBlend))
            : 1f;
        float spread = (Def.SpreadIdle + Bloom) * aimScale;
        float movement = Def.SpreadMove * U.Clamp01(horizontalSpeed / Cfg.RunSpeed);
        if (sliding) movement += Def.SpreadMove * Cfg.SlideSpreadPenalty;
        spread += movement;
        if (!onGround) spread += Def.SpreadAir;
        if (crouching && onGround && !sliding) spread *= Def.SpreadCrouchScale;
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
        if (Reloading || Mag >= Def.Magazine || Reserve <= 0) return false;
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

    /// <summary>Recoil offset for the next shot, in (yaw, pitch) degrees.</summary>
    public Vector2 NextRecoil()
    {
        if (Def.Pattern.Length == 0) return Vector2.Zero;

        int i = Math.Min(ShotIndex, Def.Pattern.Length - 1);
        Vector2 baseOffset = Def.Pattern[i];

        if (ShotIndex >= Def.Pattern.Length)
        {
            // Past the authored pattern, keep going with the tail drifting sideways.
            float extra = (ShotIndex - Def.Pattern.Length + 1) * 0.4f;
            baseOffset += new Vector2(MathF.Sin(extra * 0.7f) * 0.5f, 0f);
        }

        ShotIndex++;
        return baseOffset;
    }

    public void RegisterShot(float now)
    {
        Mag = Math.Max(0, Mag - 1);
        NextShotTime = now + Def.CycleTime;
        Bloom = MathF.Min(Def.SpreadBloomMax, Bloom + Def.SpreadBloom);
    }

    public void ResetRecoil() => ShotIndex = 0;
}

/// <summary>The armory.</summary>
public static class WeaponRegistry
{
    public static readonly WeaponDef Knife = new()
    {
        Name = "Knife", Class = "Melee", Slot = WeaponSlot.Knife, Price = 0,
        Damage = 55f, ArmorPenetration = 0.8f, HeadshotMultiplier = 3f, Falloff = 0f, Range = 90f,
        CycleTime = 0.4f, Automatic = false, Magazine = 1, Reserve = 0, ReloadTime = 0.01f,
        MoveScale = 1f, SpreadIdle = 0f, SpreadMove = 0f, SpreadAir = 0f, SpreadBloom = 0f,
        SpreadBloomMax = 0f, SpreadDecay = 10f, VisualKick = 3f,
        Pattern = new[] { new Vector2(0f, 0.4f) },
        Tracer = U.Rgb(220, 220, 220), FireSound = "knife", KillAward = 1500,
    };

    public static readonly WeaponDef Glock = new()
    {
        Name = "Glock 17", Class = "Pistol", Slot = WeaponSlot.Secondary, Price = 200,
        Damage = 28f, ArmorPenetration = 0.47f, HeadshotMultiplier = 4f, Falloff = 0.22f, Range = 3600f,
        CycleTime = 0.15f, Automatic = false, Magazine = 20, Reserve = 120, ReloadTime = 2.2f,
        MoveScale = 1f, SpreadIdle = 0.35f, SpreadMove = 4.5f, SpreadAir = 14f,
        SpreadBloom = 0.32f, SpreadBloomMax = 5f, SpreadDecay = 5.5f,
        Pattern = Pattern4(new Vector2(0f, 1.0f), new Vector2(0f, 1.5f), new Vector2(-0.4f, 1.6f)),
        RecoilRecoveryRate = 16f, RecoilRecoveryDelay = 0.22f, VisualKick = 7f,
        AdsFov = 67f,
        AimSpreadMultiplier = 0.55f,
        Tracer = U.Rgb(255, 240, 180), FireSound = "pistol",
    };

    public static readonly WeaponDef Deagle = new()
    {
        Name = "Desert Eagle", Class = "Pistol", Slot = WeaponSlot.Secondary, Price = 700,
        Damage = 63f, ArmorPenetration = 0.93f, HeadshotMultiplier = 4f, Falloff = 0.16f, Range = 4200f,
        CycleTime = 0.225f, Automatic = false, Magazine = 7, Reserve = 35, ReloadTime = 2.2f,
        MoveScale = 1f, SpreadIdle = 0.4f, SpreadMove = 6.5f, SpreadAir = 18f,
        SpreadBloom = 1.9f, SpreadBloomMax = 6f, SpreadDecay = 6.5f,
        Pattern = Pattern4(new Vector2(0f, 4.2f), new Vector2(0.3f, 4.6f)),
        RecoilRecoveryRate = 18f, RecoilRecoveryDelay = 0.25f, VisualKick = 16f,
        AdsFov = 65f,
        AimSpreadMultiplier = 0.45f,
        PenetrationPower = 1, PenetrationDamageScale = 0.75f,
        Tracer = U.Rgb(255, 246, 200), FireSound = "deagle",
    };

    public static readonly WeaponDef Mp5 = new()
    {
        Name = "MP5-SD", Class = "SMG", Slot = WeaponSlot.Primary, Price = 1500,
        Damage = 27f, ArmorPenetration = 0.62f, HeadshotMultiplier = 4f, Falloff = 0.22f, Range = 3400f,
        // HGO's standing actor is 72 units tall.  These bands are explicit
        // gameplay distances rather than a rescaled copy of any reference game.
        DamageTiers = new[]
        {
            new DamageRangeTier(432f, 30f),
            new DamageRangeTier(864f, 25f),
            new DamageRangeTier(1_152f, 21f),
            new DamageRangeTier(float.PositiveInfinity, 18f),
        },
        CycleTime = 0.08f, Automatic = true, Magazine = 30, Reserve = 120, ReloadTime = 2.6f,
        MoveScale = 0.96f, SpreadIdle = 0.4f, SpreadMove = 3.2f, SpreadAir = 11f,
        SpreadBloom = 0.20f, SpreadBloomMax = 5f, SpreadDecay = 5.5f,
        Pattern = Smg(0.45f, 0.8f, 30), RecoilRecoveryRate = 14f, RecoilRecoveryDelay = 0.25f, VisualKick = 6f,
        AdsFov = 64f,
        AimSpreadMultiplier = 0.62f,
        Tracer = U.Rgb(255, 236, 160), FireSound = "smg",
    };

    public static readonly WeaponDef Ak47 = new()
    {
        Name = "AK-47", Class = "Rifle", Slot = WeaponSlot.Primary, Price = 2700,
        Damage = 34f, ArmorPenetration = 0.775f, HeadshotMultiplier = 4f, Falloff = 0.15f, Range = 5200f,
        // Four explicit bands give the rifle a repeatable close/mid/long
        // identity.  Close body TTK is 200 ms at 600 RPM (three hits); the
        // first drop moves it to four hits instead of silently decaying every
        // frame through an exponential formula.
        DamageTiers = new[]
        {
            new DamageRangeTier(540f, 34f),
            new DamageRangeTier(1_080f, 29f),
            new DamageRangeTier(1_440f, 25f),
            new DamageRangeTier(float.PositiveInfinity, 20f),
        },
        CycleTime = 0.1f, Automatic = true, Magazine = 30, Reserve = 90, ReloadTime = 2.5f,
        FireModes = new[] { WeaponFireMode.Auto, WeaponFireMode.Semi },
        MoveScale = 0.9f, SpreadIdle = 0.28f, SpreadMove = 6.5f, SpreadAir = 16f,
        SpreadBloom = 0.30f, SpreadBloomMax = 6f, SpreadDecay = 4.6f,
        Pattern = new[]
        {
            new Vector2(0f, 1.10f), new Vector2(0f, 1.75f), new Vector2(0f, 2.05f), new Vector2(-0.25f, 2.20f),
            new Vector2(0.20f, 2.25f), new Vector2(-0.35f, 2.10f), new Vector2(0.45f, 1.95f), new Vector2(0.95f, 1.85f),
            new Vector2(1.45f, 1.70f), new Vector2(1.75f, 1.60f), new Vector2(1.60f, 1.50f), new Vector2(1.05f, 1.45f),
            new Vector2(0.30f, 1.40f), new Vector2(-0.55f, 1.35f), new Vector2(-1.30f, 1.30f), new Vector2(-1.85f, 1.25f),
            new Vector2(-2.10f, 1.22f), new Vector2(-1.95f, 1.20f), new Vector2(-1.35f, 1.18f), new Vector2(-0.45f, 1.16f),
            new Vector2(0.55f, 1.15f), new Vector2(1.45f, 1.15f), new Vector2(2.05f, 1.15f), new Vector2(2.15f, 1.15f),
            new Vector2(1.70f, 1.15f), new Vector2(0.85f, 1.15f), new Vector2(-0.20f, 1.15f), new Vector2(-1.15f, 1.15f),
            new Vector2(-1.85f, 1.15f), new Vector2(-2.20f, 1.15f),
        },
        RecoilRecoveryRate = 11f, RecoilRecoveryDelay = 0.3f, VisualKick = 10f,
        AdsFov = 61f,
        AimSpreadMultiplier = 0.44f,
        HasCalibratedIronSightPicture = true,
        PenetrationPower = 1, PenetrationDamageScale = 0.6f,
        Tracer = U.Rgb(255, 232, 150), FireSound = "rifle_ak",
    };

    public static readonly WeaponDef M4A4 = new()
    {
        Name = "M4A4", Class = "Rifle", Slot = WeaponSlot.Primary, Price = 3100,
        Damage = 32f, ArmorPenetration = 0.70f, HeadshotMultiplier = 4f, Falloff = 0.16f, Range = 5200f,
        DamageTiers = new[]
        {
            new DamageRangeTier(576f, 32f),
            new DamageRangeTier(1_152f, 28f),
            new DamageRangeTier(1_584f, 24f),
            new DamageRangeTier(float.PositiveInfinity, 20f),
        },
        CycleTime = 0.09f, Automatic = true, Magazine = 30, Reserve = 90, ReloadTime = 3.1f,
        FireModes = new[] { WeaponFireMode.Auto, WeaponFireMode.Semi },
        MoveScale = 0.9f, SpreadIdle = 0.24f, SpreadMove = 6f, SpreadAir = 15f,
        SpreadBloom = 0.26f, SpreadBloomMax = 5.5f, SpreadDecay = 4.8f,
        Pattern = new[]
        {
            new Vector2(0f, 0.80f), new Vector2(0f, 1.20f), new Vector2(0.15f, 1.45f), new Vector2(-0.45f, 1.55f),
            new Vector2(0.30f, 1.50f), new Vector2(-0.20f, 1.40f), new Vector2(0.50f, 1.32f), new Vector2(-0.60f, 1.26f),
            new Vector2(0.90f, 1.20f), new Vector2(-0.30f, 1.16f), new Vector2(0.70f, 1.12f), new Vector2(1.20f, 1.08f),
            new Vector2(0.60f, 1.05f), new Vector2(-0.40f, 1.02f), new Vector2(-1.10f, 1.00f), new Vector2(-0.30f, 0.98f),
            new Vector2(1.10f, 0.96f), new Vector2(1.60f, 0.95f), new Vector2(0.70f, 0.94f), new Vector2(-0.50f, 0.93f),
            new Vector2(-1.50f, 0.92f), new Vector2(-1.00f, 0.91f), new Vector2(0.60f, 0.90f), new Vector2(1.70f, 0.90f),
            new Vector2(1.20f, 0.90f), new Vector2(-0.30f, 0.90f), new Vector2(-1.60f, 0.90f), new Vector2(-1.10f, 0.90f),
            new Vector2(0.50f, 0.90f), new Vector2(1.40f, 0.90f),
        },
        RecoilRecoveryRate = 12f, RecoilRecoveryDelay = 0.28f, VisualKick = 9f,
        AdsFov = 60f,
        AimSpreadMultiplier = 0.43f,
        PenetrationPower = 1, PenetrationDamageScale = 0.6f,
        Tracer = U.Rgb(255, 236, 160), FireSound = "rifle_m4",
    };

    public static readonly WeaponDef Nova = new()
    {
        Name = "Nova", Class = "Shotgun", Slot = WeaponSlot.Primary, Price = 1050,
        Damage = 24f, ArmorPenetration = 0.5f, HeadshotMultiplier = 3f, Falloff = 0.55f, Range = 1500f,
        CycleTime = 0.88f, Automatic = false, Magazine = 8, Reserve = 32, ReloadTime = 3.4f,
        MoveScale = 0.95f, SpreadIdle = 4.4f, SpreadMove = 3f, SpreadAir = 8f,
        SpreadBloom = 1.2f, SpreadBloomMax = 4f, SpreadDecay = 6f,
        Pattern = Pattern4(new Vector2(0f, 2.6f), new Vector2(0.4f, 3.0f)),
        Pellets = 9, RecoilRecoveryRate = 14f, RecoilRecoveryDelay = 0.3f, VisualKick = 18f,
        AdsFov = 66f,
        AimSpreadMultiplier = 0.70f,
        Tracer = U.Rgb(255, 220, 150), FireSound = "shotgun", KillAward = 900,
    };

    public static readonly WeaponDef Awp = new()
    {
        Name = "AWP", Class = "Sniper", Slot = WeaponSlot.Primary, Price = 4750,
        Damage = 115f, ArmorPenetration = 0.975f, HeadshotMultiplier = 1.5f, Falloff = 0.08f, Range = 7200f,
        CycleTime = 1.46f, Automatic = false, Magazine = 10, Reserve = 30, ReloadTime = 3.7f,
        MoveScale = 0.83f, SpreadIdle = 5.5f, SpreadMove = 8f, SpreadAir = 22f,
        SpreadBloom = 3.5f, SpreadBloomMax = 9f, SpreadDecay = 8f,
        Pattern = Pattern4(new Vector2(0f, 7.5f)),
        ZoomFov = 20f, RecoilRecoveryRate = 20f, RecoilRecoveryDelay = 0.4f, VisualKick = 26f,
        AimSpreadMultiplier = 0.25f,
        PenetrationPower = 2, PenetrationDamageScale = 0.85f,
        Tracer = U.Rgb(255, 250, 220), FireSound = "awp", KillAward = 100,
    };

    public static readonly WeaponDef[] All = { Knife, Glock, Deagle, Mp5, Ak47, M4A4, Nova, Awp };

    /// <summary>Weapons a bot may be issued, split by slot.</summary>
    public static readonly WeaponDef[] PrimaryPool = { Mp5, Ak47, M4A4, Nova, Awp };
    public static readonly WeaponDef[] SecondaryPool = { Glock, Deagle };

    /// <summary>
    /// Looks a weapon up by display name, ignoring case. An unknown name falls back
    /// to the Glock and says so on stderr, because quietly handing back the fallback
    /// made `--weapon Deagle` look like it had worked.
    /// </summary>
    public static WeaponDef ByName(string name)
    {
        WeaponDef? def = Array.Find(All, w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def is not null) return def;

        Console.Error.WriteLine($"unknown weapon '{name}': using {Glock.Name}");
        return Glock;
    }

    private static Vector2[] Pattern4(params Vector2[] head)
    {
        var list = new List<Vector2>(head);
        float x = 0f;
        for (int i = list.Count; i < 30; i++)
        {
            x += MathF.Sin(i * 0.9f) * 0.5f;
            x = U.Clamp(x, -2.4f, 2.4f);
            float y = MathF.Max(0.4f, head[^1].Y * 0.55f);
            list.Add(new Vector2(x, y));
        }
        return list.ToArray();
    }

    private static Vector2[] Smg(float vertical, float horizontal, int count)
    {
        var list = new List<Vector2>(count);
        float x = 0f;
        for (int i = 0; i < count; i++)
        {
            float ramp = MathF.Min(1f, i / 6f);
            x += MathF.Sin(i * 1.35f) * horizontal * 0.55f;
            x = U.Clamp(x, -horizontal * 2.2f, horizontal * 2.2f);
            list.Add(new Vector2(x, vertical * (0.55f + 0.45f * ramp)));
        }
        return list.ToArray();
    }
}
