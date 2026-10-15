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
    public float Armor;
    public bool HasHelmet;
    public bool Alive = true;
    public float RespawnAt;

    public int Kills;
    public int Deaths;
    public int Money = Cfg.StartMoney;

    public float CrouchAmount;   // 0 standing .. 1 fully crouched
    public float HeadHeight => U.Lerp(Cfg.StandHeight, Cfg.CrouchHeight, CrouchAmount);
    public float EyeHeight => U.Lerp(Cfg.EyeStand, Cfg.EyeCrouch, CrouchAmount);
    public bool Crouching => CrouchAmount > 0.5f;

    public readonly List<Weapon> Weapons = new();
    public int WeaponIndex;
    public Weapon Current => Weapons.Count == 0 ? FallbackWeapon : Weapons[U.ClampInt(WeaponIndex, 0, Weapons.Count - 1)];

    private static readonly Weapon FallbackWeapon = new(WeaponRegistry.Knife);

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
            float baseSpeed = Current.Def.MoveScale * Cfg.RunSpeed;
            if (Crouching) baseSpeed = MathF.Min(baseSpeed, Cfg.CrouchSpeed);
            return baseSpeed;
        }
    }

    // ---------------------------------------------------------------- weapons

    public void GiveWeapon(WeaponDef def)
    {
        var weapon = new Weapon(def);
        int existing = Weapons.FindIndex(w => w.Def == def);
        if (existing >= 0)
        {
            Weapons[existing] = weapon;
        }
        else
        {
            // Replace any weapon already occupying the same slot (knife is always kept).
            int slotOccupant = Weapons.FindIndex(w => w.Def.Slot == def.Slot);
            if (def.Slot != WeaponSlot.Knife && slotOccupant >= 0) Weapons[slotOccupant] = weapon;
            else Weapons.Add(weapon);
        }

        SelectWeapon(def.Slot);
    }

    public bool SelectWeapon(WeaponSlot slot)
    {
        int idx = Weapons.FindIndex(w => w.Def.Slot == slot);
        if (idx < 0) return false;
        if (idx == WeaponIndex) return false;

        WeaponIndex = idx;
        Weapons[idx].Reloading = false;
        Weapons[idx].ResetRecoil();
        Punch = Vector2.Zero;
        return true;
    }

    public void SelectSlot(int slotNumber)
    {
        if (slotNumber == 0) SelectWeapon(WeaponSlot.Knife);
        else if (slotNumber == 1) SelectWeapon(WeaponSlot.Secondary);
        else if (slotNumber == 2) SelectWeapon(WeaponSlot.Primary);
    }

    // ---------------------------------------------------------------- damage

    /// <summary>Damage already resolved for armour. Returns true when the hit was fatal.</summary>
    public bool ApplyDamage(float amount, HitGroup group, Vector3 point, Actor? attacker)
    {
        if (!Alive) return false;

        float damage = amount * (group == HitGroup.Leg ? 0.75f : 1f) * (group == HitGroup.Stomach ? 1.25f : 1f);

        bool head = group == HitGroup.Head;
        bool helmeted = head && HasHelmet;

        if (Armor > 0f)
        {
            float ratio = head && !helmeted ? 1f : 0.5f;
            float toHealth = damage * ratio;
            float toArmor = (damage - toHealth) * 0.5f;
            if (toArmor > Armor)
            {
                toArmor = Armor;
                toHealth = damage - toArmor * 2f;
                if (toHealth < 0f) toHealth = 0f;
            }
            Armor = MathF.Max(0f, Armor - toArmor);
            Health -= toHealth;
        }
        else
        {
            Health -= damage;
        }

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
    /// resets once the weapon has been idle long enough for the pattern to restart.
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
