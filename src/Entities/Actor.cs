using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Entities;

public enum HitGroup
{
    Generic,
    Head,
    Chest,
    Stomach,
    Leg,
}

/// <summary>Shared state and rules for anything that can move, shoot and die.</summary>
public abstract class Actor
{
    public int Id;
    public string Name = "player";
    public bool IsPlayer;
    public int Team;

    /// <summary>Centre of the base of the character. Y is the feet height.</summary>
    public Vector3 Position;
    public Vector3 Velocity;
    public bool OnGround;
    public float Yaw;
    public float Pitch;

    public float Health = Cfg.MaxHealth;
    public bool Alive = true;
    public float RespawnAt;
    /// <summary>Simulation time of the last damage taken; drives health regeneration.</summary>
    public float LastDamageTime = -100f;

    public int Kills;
    public int Deaths;

    /// <summary>
    /// Spent at upgrade cabinets. Points survive death; upgrade levels do not
    /// fully: dying costs the highest upgrade one level.
    /// </summary>
    public int KillPoints;

    // ---------------------------------------------------------------- upgrades
    public int UpgradeHealth;
    public int UpgradeDamage;
    public int UpgradeSpeed;
    public int UpgradeJump;

    public float MaxHealth => Cfg.MaxHealth + UpgradeHealth * Cfg.UpgradeHealthPerLevel;
    public float DamageMultiplier => 1f + UpgradeDamage * Cfg.UpgradeDamagePerLevel;
    public float SpeedMultiplier => 1f + UpgradeSpeed * Cfg.UpgradeSpeedPerLevel;
    public float JumpMultiplier => 1f + UpgradeJump * Cfg.UpgradeJumpPerLevel;

    public float CrouchAmount;   // 0 standing .. 1 fully crouched (machinery retained for hitboxes)
    public float HeadHeight => U.Lerp(Cfg.StandHeight, Cfg.CrouchHeight, CrouchAmount);
    public float EyeHeight => U.Lerp(Cfg.EyeStand, Cfg.EyeCrouch, CrouchAmount);
    public bool Crouching => CrouchAmount > 0.5f;

    public readonly List<Weapon> Weapons = new();
    public int WeaponIndex;
    public Weapon Current => Weapons.Count == 0 ? FallbackWeapon : Weapons[U.ClampInt(WeaponIndex, 0, Weapons.Count - 1)];

    private static readonly Weapon FallbackWeapon = new(WeaponRegistry.Knife, 4);

    /// <summary>Accumulated recoil applied to the view, in (yaw, pitch) degrees.</summary>
    public Vector2 Punch;
    public float LastShotTime = -10f;

    /// <summary>Distance walked since the last footstep, used for footstep audio.</summary>
    public float StepAccumulator;

    public Vector3 EyePosition => new(Position.X, Position.Y + EyeHeight, Position.Z);

    public Aabb Bounds => Aabb.Footprint(Position, Cfg.HalfWidth, HeadHeight);

    /// <summary>Upper hitbox used for headshots.</summary>
    public Aabb HeadBounds => new(
        new Vector3(Position.X - 7f, Position.Y + HeadHeight - 14f, Position.Z - 7f),
        new Vector3(Position.X + 7f, Position.Y + HeadHeight, Position.Z + 7f));

    public Aabb BodyBounds => new(
        new Vector3(Position.X - 12f, Position.Y + 10f, Position.Z - 12f),
        new Vector3(Position.X + 12f, Position.Y + HeadHeight - 14f, Position.Z + 12f));

    public Aabb LegBounds => new(
        new Vector3(Position.X - 11f, Position.Y, Position.Z - 11f),
        new Vector3(Position.X + 11f, Position.Y + 10f, Position.Z + 11f));

    public Vector3 AimDirection => U.DirFromAngles(Yaw + Punch.X, Pitch + Punch.Y);

    public float HorizontalSpeed => U.FlatLength(Velocity);

    public float MaxSpeedNow
    {
        get
        {
            float baseSpeed = Current.Def.MoveScale * Cfg.RunSpeed * SpeedMultiplier;
            if (Crouching) baseSpeed = MathF.Min(baseSpeed, Cfg.CrouchSpeed);
            return baseSpeed;
        }
    }

    // ---------------------------------------------------------------- weapons

    /// <summary>
    /// Places a weapon in a numbered hotkey slot (1-3 loadout guns, 4 knife)
    /// and selects it. Replacing an occupied slot swaps the weapon out.
    /// </summary>
    public void GiveWeapon(WeaponDef def, int slot)
    {
        var weapon = new Weapon(def, slot);
        int existing = Weapons.FindIndex(w => w.Slot == slot);
        if (existing >= 0) Weapons[existing] = weapon;
        else Weapons.Add(weapon);

        SelectWeaponNumber(slot);
    }

    public bool SelectWeaponNumber(int slot)
    {
        int idx = Weapons.FindIndex(w => w.Slot == slot);
        if (idx < 0) return false;
        if (idx == WeaponIndex) return false;

        WeaponIndex = idx;
        Weapons[idx].Reloading = false;
        Weapons[idx].ResetRecoil();
        Punch = Vector2.Zero;
        return true;
    }

    /// <summary>Turns the mouse wheel: steps to the next or previous owned slot, wrapping.</summary>
    public bool CycleWeapon(int direction)
    {
        if (Weapons.Count <= 1) return false;

        var ordered = new List<Weapon>(Weapons);
        ordered.Sort((a, b) => a.Slot.CompareTo(b.Slot));

        int current = ordered.FindIndex(w => ReferenceEquals(w, Current));
        if (current < 0) current = 0;
        int next = (current + direction) % ordered.Count;
        if (next < 0) next += ordered.Count;
        return SelectWeaponNumber(ordered[next].Slot);
    }

    // ---------------------------------------------------------------- damage

    /// <summary>
    /// Applies already-resolved hitscan damage. Damage is uniform across hit
    /// groups, matching the reference's gunplay. Returns true when fatal.
    /// </summary>
    public bool ApplyDamage(float amount, HitGroup group, Vector3 point, Actor? attacker, float now)
    {
        if (!Alive) return false;

        _ = group;
        _ = point;
        Health -= amount;
        LastDamageTime = now;
        _ = attacker;

        if (Health <= 0f)
        {
            Health = 0f;
            Alive = false;
            return true;
        }
        return false;
    }

    /// <summary>Called by the simulation whenever this actor actually discharges a weapon.</summary>
    public virtual void OnShotFired(float now)
    {
        LastShotTime = now;
    }

    /// <summary>
    /// Eases accumulated recoil back toward the commanded view. The spray index
    /// resets once the weapon has been idle long enough.
    /// </summary>
    public void RecoverRecoil(float now, float dt)
    {
        Weapon weapon = Current;
        if (now - LastShotTime < weapon.Def.RecoilRecoveryDelay) return;

        float rate = weapon.Def.RecoilRecoveryRate;
        Punch = new Vector2(
            U.MoveTowards(Punch.X, 0f, rate * dt),
            U.MoveTowards(Punch.Y, 0f, rate * dt));

        if (now - LastShotTime > 0.6f) weapon.ResetRecoil();
    }
}
