using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Entities;

/// <summary>The human-controlled actor. Turns mouse deltas into view angles and intent.</summary>
public sealed class Player : Actor
{
    public float Sensitivity = Cfg.Sensitivity;
    public bool ZoomHeld;
    public float ZoomBlend;
    public float CurrentFov = Cfg.FovDefault;
    public bool WantsFire;

    // view-model animation state
    public float BobPhase;
    public float BobAmount;
    public float StepDistance;
    public float LandDip;
    public float ViewKick;
    public float KickYaw;
    public Vector2 Sway;
    public float MuzzleFlash;

    public Player(int id, string name)
    {
        Id = id;
        Name = name;
        IsPlayer = true;
        GiveWeapon(WeaponRegistry.Knife);
        GiveWeapon(WeaponRegistry.Glock);
    }

    public void Update(InputFrame input, Simulation sim, float dt)
    {
        float now = sim.Time;
        WantsFire = false;

        if (!Alive)
        {
            Velocity = Vector3.Zero;
            CrouchAmount = 0f;
            ZoomHeld = false;
            LandDip = 0f;
            return;
        }

        // ------------------------------------------------------------ look
        float zoomScale = U.Lerp(1f, 0.45f, ZoomBlend);
        float scale = Cfg.DegPerCountPerSens * Sensitivity * zoomScale;
        Yaw = Wrap(Yaw - input.Look.X * scale);
        Pitch = U.Clamp(Pitch - input.Look.Y * scale, -Cfg.MaxPitch, Cfg.MaxPitch);

        // view-model sway lags behind the mouse
        Vector2 swayTarget = new(
            U.Clamp(-input.Look.X * 0.012f, -1.4f, 1.4f) + U.Clamp(Punch.X * -0.06f, -0.8f, 0.8f),
            U.Clamp(input.Look.Y * 0.012f, -1.4f, 1.4f));
        Sway = new Vector2(
            U.Approach(Sway.X, swayTarget.X, 11f, dt),
            U.Approach(Sway.Y, swayTarget.Y, 11f, dt));

        // ------------------------------------------------------------ weapon handling
        if (input.SwitchSlot >= 0) SelectSlot(input.SwitchSlot);

        Weapon weapon = Current;
        weapon.Tick(now, dt);

        if (weapon.Def.HasZoom && input.ZoomPressed) ZoomHeld = !ZoomHeld;
        if (!weapon.Def.HasZoom) ZoomHeld = false;
        ZoomBlend = U.Approach(ZoomBlend, ZoomHeld ? 1f : 0f, 15f, dt);

        float targetFov = weapon.Def.HasZoom
            ? U.Lerp(Cfg.FovDefault, weapon.Def.ZoomFov, ZoomBlend)
            : Cfg.FovDefault;
        CurrentFov = U.Approach(CurrentFov, targetFov, 18f, dt);

        if (input.Reload && weapon.BeginReload(now)) sim.Emit("reload", EyePosition, 0.8f);
        if (input.FireDown && weapon.Mag == 0 && !weapon.Reloading && weapon.Reserve > 0
            && weapon.BeginReload(now))
        {
            sim.Emit("reload", EyePosition, 0.8f);
        }

        // ------------------------------------------------------------ movement
        Vector3 forward = U.DirFromAngles(Yaw, 0f);
        Vector3 right = U.RightFromYaw(Yaw);
        Vector3 wishDir = U.FlatNormalized(forward * input.Move.Y + right * input.Move.X);

        float wishSpeed = Cfg.RunSpeed * weapon.Def.MoveScale;
        if (ZoomHeld) wishSpeed *= 0.5f;
        if (weapon.Reloading) wishSpeed *= 0.92f;

        float fallSpeed = Velocity.Y;
        bool wasOnGround = OnGround;

        CharacterMover.Move(sim.Map, this, wishDir, wishSpeed, input.Jump, input.Crouch, input.Walk, dt);

        if (!wasOnGround && OnGround && fallSpeed < -160f)
        {
            LandDip = U.Clamp01((-fallSpeed - 160f) / 700f);
        }
        LandDip = U.Approach(LandDip, 0f, 9f, dt);

        // ------------------------------------------------------------ recoil + firing
        RecoverRecoil(now, dt);

        bool wantShot = weapon.Def.Automatic ? input.FireDown : input.FirePressed;
        if (wantShot && weapon.CanFire(now))
        {
            WantsFire = true;
            float kick = weapon.Def.VisualKick;
            ViewKick = MathF.Min(ViewKick + kick, kick * 2.4f);
            KickYaw += sim.Rand(-0.5f, 0.5f) * kick * 0.12f;
            MuzzleFlash = 0.045f;
        }

        // ------------------------------------------------------------ view bob
        float speedFactor = U.Clamp01(HorizontalSpeed / Cfg.RunSpeed);
        if (OnGround)
        {
            BobPhase += HorizontalSpeed * dt * 0.028f;
            BobAmount = U.Approach(BobAmount, speedFactor * (Crouching ? 0.5f : 1f), 8f, dt);
            StepDistance += HorizontalSpeed * dt;
            if (StepDistance > 170f) StepDistance -= 170f;
        }
        else
        {
            BobAmount = U.Approach(BobAmount, 0.25f, 5f, dt);
        }

        ViewKick = U.Approach(ViewKick, 0f, 13f, dt);
        KickYaw = U.Approach(KickYaw, 0f, 13f, dt);
        MuzzleFlash = MathF.Max(0f, MuzzleFlash - dt);
    }

    public override void OnShotFired(float now) => LastShotTime = now;

    private static float Wrap(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}
