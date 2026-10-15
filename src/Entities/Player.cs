using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Entities;

/// <summary>
/// The three press-cycled sprint states used by the referenced Godot controller.
/// Tactical sprint is intentionally a temporary, stamina-limited state.
/// </summary>
public enum PlayerSprintState
{
    None,
    Sprinting,
    TacticalSprinting,
}

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

    // Weapon-readiness presentation state. These values are only consumed by the
    // renderer and HUD; combat/hit-scan continue to read the weapon and aim state.
    public float InspectBlend { get; private set; }
    public float InspectPhase { get; private set; }
    public float SprintWeaponBlend { get; private set; }
    public bool Inspecting => InspectBlend > 0.01f;
    public float InspectPose => InspectBlend * MathF.Sin(InspectPhase * MathF.PI);
    private float _inspectStartTime = -10f;
    private float _inspectEndTime = -10f;

    // Render-only transient layers. They are triggered from real gameplay events,
    // but no collision, aim, spread, recoil, bot or weapon code reads them.
    public float FootstepFeedback { get; private set; }
    public float LandingFeedback { get; private set; }
    public float DamageFeedback { get; private set; }
    public float DamageFeedbackYaw { get; private set; }

    // Mobility is simulation state. Renderer/HUD only read it, which keeps view
    // presentation from influencing collision, aim or the weapon simulation.
    public PlayerSprintState SprintState { get; private set; }
    /// <summary>Remaining tactical-sprint seconds, replenished outside tactical sprint.</summary>
    public float TacticalSprintCharge { get; private set; } = Cfg.TacticalSprintDuration;
    public bool CrouchToggled { get; private set; }
    public bool IsSprinting => SprintState != PlayerSprintState.None;
    public float TacticalSprintStamina => Cfg.TacticalSprintDuration <= 0f
        ? 0f
        : U.Clamp01(TacticalSprintCharge / Cfg.TacticalSprintDuration);

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
            BobAmount = 0f;
            ResetWeaponReadiness();
            ResetPresentationFeedback();
            ResetMovementState();
            return;
        }

        TickPresentationFeedback(dt);

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

        if (input.FireModePressed && weapon.CycleFireMode())
        {
            // Reuse HGO's own UI tick rather than importing source-pack audio.
            sim.Emit("buy", EyePosition, 0.22f, weapon.IsAutomatic ? 1.15f : 0.92f);
        }

        // ------------------------------------------------------------ movement
        Vector3 forward = U.DirFromAngles(Yaw, 0f);
        Vector3 right = U.RightFromYaw(Yaw);
        Vector3 wishDir = U.FlatNormalized(forward * input.Move.Y + right * input.Move.X);

        if (input.CrouchTogglePressed) CrouchToggled = !CrouchToggled;
        bool crouchRequested = input.Crouch || CrouchToggled;

        // The source uses a Shift press (rather than a held modifier) to step
        // through None -> Sprint -> Tactical Sprint -> None. It only permits
        // that cycle while grounded and moving forward. We also cancel when
        // HGO's existing ADS/reload states own the player's movement intent so
        // the first-person gun system remains predictable.
        bool sprintAllowed = input.Move.Y > 0f
            && !crouchRequested
            && !input.Walk
            && !ZoomHeld
            && !weapon.Reloading;
        if (!sprintAllowed || !OnGround) StopSprinting();
        if (input.SprintPressed && sprintAllowed && OnGround) AdvanceSprintState();

        float sprintSpeedMultiplier = SprintState switch
        {
            PlayerSprintState.Sprinting => Cfg.SprintSpeedMultiplier,
            PlayerSprintState.TacticalSprinting => Cfg.TacticalSprintSpeedMultiplier,
            _ => 1f,
        };
        float sprintAccelerationMultiplier = SprintState switch
        {
            PlayerSprintState.Sprinting => Cfg.SprintAccelerationMultiplier,
            PlayerSprintState.TacticalSprinting => Cfg.TacticalSprintAccelerationMultiplier,
            _ => 1f,
        };

        if (SprintState != PlayerSprintState.None)
        {
            // The source evaluates acceleration as (acceleration multiplier *
            // speed multiplier). Preserve that relationship first. HGO's
            // pre-existing friction is calibrated close to its base accel, so
            // clamp only to the minimum that can actually sustain the faster
            // speed cap instead of leaving tactical sprint permanently short.
            float sourceMappedAcceleration = sprintAccelerationMultiplier * sprintSpeedMultiplier;
            float sustainAcceleration = Cfg.Friction / Cfg.Accelerate + Cfg.SprintAccelerationSustainMargin;
            sprintAccelerationMultiplier = MathF.Max(sourceMappedAcceleration, sustainAcceleration);
        }

        float wishSpeed = Cfg.RunSpeed * weapon.Def.MoveScale;
        wishSpeed *= sprintSpeedMultiplier;
        if (ZoomHeld) wishSpeed *= 0.5f;
        if (weapon.Reloading) wishSpeed *= 0.92f;

        float fallSpeed = Velocity.Y;
        bool wasOnGround = OnGround;

        CharacterMover.Move(sim.Map, this, wishDir, wishSpeed, input.Jump, crouchRequested, input.Walk, dt,
            sprintAccelerationMultiplier);

        // The reference clears the mode on take-off while preserving earned
        // horizontal momentum. Keep that rule outside the mover so collision
        // remains shared by the player and the unchanged bot controller.
        if (!OnGround) StopSprinting();
        TickTacticalSprintStamina(dt);

        if (!wasOnGround && OnGround && fallSpeed < -160f)
        {
            LandDip = U.Clamp01((-fallSpeed - 160f) / 700f);
            TriggerLandingFeedback(LandDip);
        }
        LandDip = U.Approach(LandDip, 0f, 9f, dt);

        TickWeaponReadiness(input, weapon, now, dt);

        // ------------------------------------------------------------ recoil + firing
        RecoverRecoil(now, dt);

        bool wantShot = weapon.IsAutomatic ? input.FireDown : input.FirePressed;
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
            BobPhase += HorizontalSpeed * dt * Cfg.ViewBobPhasePerUnit;

            float crouchScale = U.Lerp(1f, Cfg.ViewBobCrouchScale, CrouchAmount);
            float adsScale = U.Lerp(1f, Cfg.ViewBobAdsScale, ZoomBlend);
            float targetBob = speedFactor * crouchScale * adsScale;
            float rate = targetBob > BobAmount ? Cfg.ViewBobInRate : Cfg.ViewBobOutRate;
            BobAmount = U.Approach(BobAmount, targetBob, rate, dt);
            StepDistance += HorizontalSpeed * dt;
            if (StepDistance > 170f) StepDistance -= 170f;
        }
        else
        {
            BobAmount = U.Approach(BobAmount, 0f, Cfg.ViewBobOutRate, dt);
        }

        ViewKick = U.Approach(ViewKick, 0f, 13f, dt);
        KickYaw = U.Approach(KickYaw, 0f, 13f, dt);
        MuzzleFlash = MathF.Max(0f, MuzzleFlash - dt);
    }

    public override void OnShotFired(float now) => LastShotTime = now;

    /// <summary>
    /// Starts a small locomotion pulse consumed only by the renderer. The actual
    /// footstep sound, collision and velocity are already complete at this point.
    /// </summary>
    public void TriggerFootstepFeedback(float intensity)
    {
        FootstepFeedback = MathF.Max(FootstepFeedback, U.Clamp01(intensity));
    }

    /// <summary>Starts a render-only hit response and remembers the attack direction.</summary>
    public void TriggerDamageFeedback(float intensity, float incomingYaw)
    {
        DamageFeedback = MathF.Min(1f, DamageFeedback + U.Clamp01(intensity));
        DamageFeedbackYaw = incomingYaw;
    }

    /// <summary>Clears only transient visual feedback; it does not change player state.</summary>
    public void ResetPresentationFeedback()
    {
        FootstepFeedback = 0f;
        LandingFeedback = 0f;
        DamageFeedback = 0f;
        DamageFeedbackYaw = 0f;
    }

    /// <summary>Clears transient source-style mobility state after death, respawn or a staged capture.</summary>
    public void ResetMovementState()
    {
        SprintState = PlayerSprintState.None;
        TacticalSprintCharge = Cfg.TacticalSprintDuration;
        CrouchToggled = false;
    }

    /// <summary>Clears only the inspect/sprint viewmodel layers, never weapon state.</summary>
    public void ResetWeaponReadiness()
    {
        InspectBlend = 0f;
        InspectPhase = 0f;
        SprintWeaponBlend = 0f;
        _inspectStartTime = -10f;
        _inspectEndTime = -10f;
    }

    private void AdvanceSprintState()
    {
        SprintState = SprintState switch
        {
            PlayerSprintState.None => PlayerSprintState.Sprinting,
            // Avoid the reference's one-frame tactical boost when its stamina
            // is depleted; keep the player in the regular sprint instead.
            PlayerSprintState.Sprinting when TacticalSprintCharge > 1e-4f => PlayerSprintState.TacticalSprinting,
            PlayerSprintState.Sprinting => PlayerSprintState.Sprinting,
            _ => PlayerSprintState.None,
        };
    }

    private void StopSprinting() => SprintState = PlayerSprintState.None;

    private void TickTacticalSprintStamina(float dt)
    {
        if (SprintState == PlayerSprintState.TacticalSprinting)
        {
            TacticalSprintCharge = MathF.Max(0f, TacticalSprintCharge - dt);
            if (TacticalSprintCharge <= 0f) SprintState = PlayerSprintState.Sprinting;
        }
        else
        {
            TacticalSprintCharge = MathF.Min(Cfg.TacticalSprintDuration,
                TacticalSprintCharge + Cfg.TacticalSprintRecoveryRate * dt);
        }
    }

    private void TriggerLandingFeedback(float intensity)
    {
        LandingFeedback = MathF.Max(LandingFeedback, U.Clamp01(intensity));
    }

    private void TickPresentationFeedback(float dt)
    {
        FootstepFeedback = U.Approach(FootstepFeedback, 0f, Cfg.FeedbackFootstepDecayPerSecond, dt);
        LandingFeedback = U.Approach(LandingFeedback, 0f, Cfg.FeedbackLandingDecayPerSecond, dt);
        DamageFeedback = U.Approach(DamageFeedback, 0f, Cfg.FeedbackDamageDecayPerSecond, dt);
    }

    /// <summary>
    /// Independent first-person readiness state. Inspect is cancelled by every
    /// combat-facing action but never changes the weapon, player physics or aim ray.
    /// </summary>
    private void TickWeaponReadiness(InputFrame input, Weapon weapon, float now, float dt)
    {
        bool interrupt = input.FireDown
            || input.FirePressed
            || input.Reload
            || input.ZoomPressed
            || input.SprintPressed
            || input.SwitchSlot >= 0
            || weapon.Reloading
            || ZoomHeld
            || SprintState != PlayerSprintState.None;

        if (input.InspectPressed && !interrupt)
        {
            _inspectStartTime = now;
            _inspectEndTime = now + Cfg.WeaponInspectDuration;
        }

        if (interrupt) _inspectEndTime = now;

        bool active = now < _inspectEndTime;
        if (active)
        {
            InspectPhase = U.Clamp01((now - _inspectStartTime) / Cfg.WeaponInspectDuration);
        }
        else if (InspectBlend <= 0.01f)
        {
            InspectPhase = 0f;
        }

        InspectBlend = U.Approach(InspectBlend, active ? 1f : 0f,
            active ? Cfg.WeaponInspectBlendInRate : Cfg.WeaponInspectBlendOutRate, dt);
        if (!active && InspectBlend < 0.005f)
        {
            InspectBlend = 0f;
            InspectPhase = 0f;
        }
        SprintWeaponBlend = U.Approach(SprintWeaponBlend,
            SprintState == PlayerSprintState.None ? 0f : 1f,
            SprintState == PlayerSprintState.None ? Cfg.SprintWeaponBlendOutRate : Cfg.SprintWeaponBlendInRate,
            dt);
    }

    private static float Wrap(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}
