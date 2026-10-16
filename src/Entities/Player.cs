using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Render;
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

    // Two independent second-order presentation springs sit between raw input
    // and the rendered weapon.  They are deliberately local to this player and
    // never feed back into yaw, pitch, collision, spread, hit-scan or bots.
    public ViewModelSpring ViewModelOffsetSpring { get; } = new(
        frequencyHz: 9.5f, dampingRatio: 0.82f, maxDisplacement: 8f, maxSpeed: 420f);
    public ViewModelSpring ViewModelRotationSpring { get; } = new(
        frequencyHz: 10.5f, dampingRatio: 0.88f, maxDisplacement: 16f, maxSpeed: 680f);

    // Weapon-readiness presentation state. These values are only consumed by the
    // renderer and HUD; combat/hit-scan continue to read the weapon and aim state.
    public float InspectBlend { get; private set; }
    public float InspectPhase { get; private set; }
    public float SprintWeaponBlend { get; private set; }
    public bool Inspecting => InspectBlend > 0.01f;
    public float InspectPose => InspectBlend * MathF.Sin(InspectPhase * MathF.PI);
    private float _inspectStartTime = -10f;
    private float _inspectEndTime = -10f;
    // Records the weapon that owned the previous frame's aim state. A scoped
    // weapon must never inherit a held ADS flag from a weapon it replaced.
    private Weapon? _lastAimWeapon;

    // Render-only transient layers. They are triggered from real gameplay events,
    // but no collision, aim, spread, recoil, bot or weapon code reads them.
    public float FootstepFeedback { get; private set; }
    public float LandingFeedback { get; private set; }
    public float DamageFeedback { get; private set; }
    public float DamageFeedbackYaw { get; private set; }
    /// <summary>
    /// Sustained critical-health feedback, consumed only by HUD/audio presentation.
    /// It never changes collision, aim, weapon timing, damage, or armour.
    /// </summary>
    public float LowHealthTrauma { get; private set; }
    /// <summary>Short, sharp envelope restarted by each local heartbeat cue.</summary>
    public float TraumaPulse { get; private set; }
    private float _nextLowHealthHeartbeatAt = -10f;

    // Mobility is simulation state. Renderer/HUD only read it, which keeps view
    // presentation from influencing collision, aim or the weapon simulation.
    public PlayerSprintState SprintState { get; private set; }
    /// <summary>Remaining tactical-sprint seconds, replenished outside tactical sprint.</summary>
    public float TacticalSprintCharge { get; private set; } = Cfg.TacticalSprintDuration;
    /// <summary>
    /// Player-only momentum slide. It is resolved by the same swept mover as all
    /// other locomotion, so this presentation-facing addition cannot bypass world
    /// collision or alter bot navigation.
    /// </summary>
    public bool IsSliding { get; private set; }
    public float SlideTimeRemaining { get; private set; }
    public float SlideProgress => Cfg.SlideDuration <= 0f
        ? 0f
        : U.Clamp01(1f - SlideTimeRemaining / Cfg.SlideDuration);
    /// <summary>
    /// Low-prone is a player-only stance.  It reuses the shared continuous
    /// collision interpolation so its eye, hit boxes and clearance checks cannot
    /// drift apart from the authoritative actor state.
    /// </summary>
    public bool IsProne { get; private set; }
    /// <summary>
    /// Player-only collision-validated traversal of a low static surface. Bots
    /// never enter this state and retain their existing navigation/mover path.
    /// </summary>
    public bool IsMantling { get; private set; }
    public float MantleTimeRemaining { get; private set; }
    public float MantleProgress => Cfg.MantleDuration <= 0f
        ? 0f
        : U.Clamp01(1f - MantleTimeRemaining / Cfg.MantleDuration);
    private MantlePlan _mantlePlan;
    private float _mantleElapsed;
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
            ResetViewModelDynamics();
            ResetWeaponReadiness();
            ResetPresentationFeedback();
            ResetMovementState();
            return;
        }

        TickPresentationFeedback(dt);
        TickLowHealthFeedback(sim, now, dt);

        // Select before applying look sensitivity so a newly equipped optic
        // cannot borrow one frame of the previous weapon's zoom state.
        if (input.SwitchSlot >= 0) SelectSlot(input.SwitchSlot);

        Weapon weapon = Current;
        bool weaponChanged = _lastAimWeapon is not null && !ReferenceEquals(_lastAimWeapon, weapon);
        if (weaponChanged)
        {
            // A switch or buy replacement is an explicit aim boundary. In
            // particular, do not let a held right mouse button turn the newly
            // equipped AWP scope on without a fresh press.
            ZoomHeld = false;
            ZoomBlend = 0f;
            CurrentFov = Cfg.FovDefault;
            ResetViewModelDynamics();
        }
        _lastAimWeapon = weapon;

        // ------------------------------------------------------------ look
        float zoomScale = U.Lerp(1f, 0.45f, ZoomBlend);
        float scale = Cfg.DegPerCountPerSens * Sensitivity * zoomScale;
        float lookYaw = -input.Look.X * scale;
        float lookPitch = -input.Look.Y * scale;
        Yaw = Wrap(Yaw + lookYaw);
        Pitch = U.Clamp(Pitch + lookPitch, -Cfg.MaxPitch, Cfg.MaxPitch);

        // view-model sway lags behind the mouse
        Vector2 swayTarget = new(
            U.Clamp(-input.Look.X * 0.012f, -1.4f, 1.4f) + U.Clamp(Punch.X * -0.06f, -0.8f, 0.8f),
            U.Clamp(input.Look.Y * 0.012f, -1.4f, 1.4f));
        Sway = new Vector2(
            U.Approach(Sway.X, swayTarget.X, 11f, dt),
            U.Approach(Sway.Y, swayTarget.Y, 11f, dt));

        // ------------------------------------------------------------ weapon handling
        weapon.Tick(now, dt);

        // Magnified optics retain the original click-to-toggle scope control.
        // Hold-to-aim firearms use the expected COD-style path instead, so
        // releasing secondary fire returns cleanly to hip fire. A new weapon
        // waits for a fresh secondary-fire input instead of inheriting it.
        if (IsMantling) ZoomHeld = false;
        else if (weapon.Def.HasScope && !weaponChanged && input.ZoomPressed) ZoomHeld = !ZoomHeld;
        else if (weapon.Def.HasIronSights) ZoomHeld = weaponChanged ? false : input.ZoomDown;
        else if (!weapon.Def.HasScope) ZoomHeld = false;
        ZoomBlend = U.Approach(ZoomBlend, ZoomHeld ? 1f : 0f, 15f, dt);

        float targetFov = weapon.Def.HasZoom
            ? U.Lerp(Cfg.FovDefault, weapon.Def.AimFov, ZoomBlend)
            : Cfg.FovDefault;
        CurrentFov = U.Approach(CurrentFov, targetFov, 18f, dt);

        if (!IsMantling && input.Reload && weapon.BeginReload(now)) sim.Emit("reload", EyePosition, 0.8f);
        if (!IsMantling && input.FireDown && weapon.Mag == 0 && !weapon.Reloading && weapon.Reserve > 0
            && weapon.BeginReload(now))
        {
            sim.Emit("reload", EyePosition, 0.8f);
        }

        if (input.FireModePressed && weapon.CycleFireMode())
        {
            // Reuse HGO's own UI tick rather than importing source-pack audio.
            sim.Emit("buy", EyePosition, 0.22f, weapon.IsAutomatic ? 1.15f : 0.92f, SoundEventScope.Local);
        }

        // ------------------------------------------------------------ movement
        Vector3 forward = U.DirFromAngles(Yaw, 0f);
        Vector3 right = U.RightFromYaw(Yaw);
        Vector3 wishDir = U.FlatNormalized(forward * input.Move.Y + right * input.Move.X);

        if (input.ProneTogglePressed && !IsMantling) ToggleProne(sim.Map);

        bool slideInput = input.CrouchPressed || input.CrouchTogglePressed;
        bool crouchRequested = IsProne || input.Crouch || CrouchToggled;

        // The source uses a Shift press (rather than a held modifier) to step
        // through None -> Sprint -> Tactical Sprint -> None. It only permits
        // that cycle while grounded and moving forward. We also cancel when
        // HGO's existing ADS/reload states own the player's movement intent so
        // the first-person gun system remains predictable.
        // A crouch edge may become a slide before it cancels the sprint. Holding
        // crouch without that edge keeps its ordinary non-sprint behaviour.
        bool sprintAllowed = input.Move.Y > 0f
            && (!crouchRequested || slideInput)
            && !input.Walk
            && !ZoomHeld
            && !weapon.Reloading
            && !IsProne
            && !IsMantling;
        if (!IsSliding && (!sprintAllowed || !OnGround)) StopSprinting();
        if (!IsSliding && input.SprintPressed && sprintAllowed && OnGround) AdvanceSprintState();

        bool canStartSlide = !IsSliding
            && !IsProne
            && !IsMantling
            && slideInput
            && OnGround
            && SprintState != PlayerSprintState.None
            && HorizontalSpeed >= Cfg.SlideMinimumEntrySpeed
            && !ZoomHeld
            && !weapon.Reloading
            && !input.Walk;
        if (canStartSlide)
        {
            StartSlide(wishDir, forward);
            // A tap starts and completes one slide instead of becoming a sticky
            // crouch toggle. Holding Ctrl still keeps the player crouched after it.
            CrouchToggled = false;
            crouchRequested = true;
        }
        else if (input.CrouchTogglePressed && !IsSliding && !IsProne && !IsMantling)
        {
            CrouchToggled = !CrouchToggled;
            crouchRequested = input.Crouch || CrouchToggled;
        }

        if (IsSliding || IsProne) crouchRequested = true;

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

        float groundAccelerationMultiplier = sprintAccelerationMultiplier;
        float groundFrictionMultiplier = 1f;
        if (IsSliding)
        {
            // Keep the actual entry velocity; player input may steer it gradually
            // but cannot accelerate a slide or turn it into a strafe-jump boost.
            Vector3 carryDirection = U.FlatNormalized(Velocity);
            if (carryDirection == Vector3.Zero) carryDirection = forward;
            if (wishDir != Vector3.Zero)
            {
                float steer = 1f - MathF.Exp(-Cfg.SlideSteerResponsiveness * dt);
                carryDirection = U.FlatNormalized(U.Lerp(carryDirection, wishDir, steer));
            }

            float carriedSpeed = HorizontalSpeed;
            Velocity = new Vector3(carryDirection.X * carriedSpeed, Velocity.Y, carryDirection.Z * carriedSpeed);
            wishDir = Vector3.Zero;
            wishSpeed = 0f;
            groundAccelerationMultiplier = 0f;
            groundFrictionMultiplier = Cfg.SlideFrictionMultiplier;
        }

        float fallSpeed = Velocity.Y;
        bool wasOnGround = OnGround;

        // Jumping deliberately exits the slide first, keeping jump timing and
        // collision semantics identical to the existing mover rather than adding
        // a second velocity path.  Prone never instant-stands into a jump.
        bool jumpRequested = input.Jump && !IsProne && !IsMantling;
        if (IsSliding && jumpRequested) EndSlide();

        // A mantle may only begin from a standing, grounded jump into local
        // static cover. It is deliberately unavailable while ADS/reloading so
        // it cannot be used as a sprint, firing, or scope-state bypass.
        bool wantsMantle = !IsMantling
            && jumpRequested
            && OnGround
            && !IsSliding
            && CrouchAmount < 0.05f
            && input.Move.Y > 0.25f
            && wishDir != Vector3.Zero
            && !ZoomHeld
            && !weapon.Reloading;
        if (wantsMantle && CharacterMover.TryBuildMantlePlan(sim.Map, this, forward, out MantlePlan plan))
        {
            BeginMantle(plan);
        }

        float stanceTarget = IsProne
            ? Cfg.ProneStanceAmount
            : crouchRequested ? 1f : 0f;
        if (IsMantling)
        {
            TickMantle(sim.Map, dt);
        }
        else
        {
            CharacterMover.Move(sim.Map, this, wishDir, wishSpeed, jumpRequested, crouchRequested, input.Walk, dt,
                groundAccelerationMultiplier, groundFrictionMultiplier, stanceTarget);
        }

        // The reference clears the mode on take-off while preserving earned
        // horizontal momentum. Keep that rule outside the mover so collision
        // remains shared by the player and the unchanged bot controller.
        if (!OnGround)
        {
            StopSprinting();
            EndSlide();
        }
        else if (IsSliding)
        {
            TickSlide(dt);
        }
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

        // Mantle is a controlled traversal state, not a free firing pose.  This
        // keeps its collision route and weapon state separate from gunplay.
        bool wantShot = !IsMantling && (weapon.IsAutomatic ? input.FireDown : input.FirePressed);
        if (wantShot && weapon.CanFire(now))
        {
            WantsFire = true;
            float kick = weapon.Def.VisualKick;
            ViewKick = MathF.Min(ViewKick + kick, kick * 2.4f);
            KickYaw += sim.Rand(-0.5f, 0.5f) * kick * 0.12f;
            MuzzleFlash = 0.045f;

            // Visual kick has its own spring impulse.  The existing recoil
            // pattern continues to move the authoritative camera aim after the
            // shot; this layer only gives the mesh a weighted physical recovery.
            ViewModelOffsetSpring.Impulse(new Vector3(0f, -kick * 2.4f, kick * 7.5f));
            ViewModelRotationSpring.Impulse(new Vector3(kick * 15f, KickYaw * 18f, -kick * 2.6f));
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

        TickViewModelDynamics(input, sim.Time, right, dt);
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
        LowHealthTrauma = 0f;
        TraumaPulse = 0f;
        _nextLowHealthHeartbeatAt = -10f;
    }

    /// <summary>Clears transient source-style mobility state after death, respawn or a staged capture.</summary>
    public void ResetMovementState()
    {
        SprintState = PlayerSprintState.None;
        TacticalSprintCharge = Cfg.TacticalSprintDuration;
        CrouchToggled = false;
        IsSliding = false;
        SlideTimeRemaining = 0f;
        IsProne = false;
        IsMantling = false;
        MantleTimeRemaining = 0f;
        _mantleElapsed = 0f;
        _mantlePlan = default;
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

    /// <summary>
    /// Resets only local viewmodel inertia.  It is called at presentation
    /// boundaries (death and weapon replacement), never as a gameplay rule.
    /// </summary>
    public void ResetViewModelDynamics()
    {
        ViewModelOffsetSpring.Reset();
        ViewModelRotationSpring.Reset();
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

    private void StartSlide(Vector3 wishDir, Vector3 forward)
    {
        Vector3 entryDirection = U.FlatNormalized(Velocity);
        if (entryDirection == Vector3.Zero) entryDirection = wishDir;
        if (entryDirection == Vector3.Zero) entryDirection = forward;

        // SlideMinimumEntrySpeed is only an eligibility check above; keeping the
        // measured velocity here makes a slide incapable of granting free speed.
        float entrySpeed = HorizontalSpeed;
        Velocity = new Vector3(entryDirection.X * entrySpeed, Velocity.Y, entryDirection.Z * entrySpeed);
        IsSliding = true;
        SlideTimeRemaining = Cfg.SlideDuration;
        StopSprinting();
    }

    private void TickSlide(float dt)
    {
        SlideTimeRemaining = MathF.Max(0f, SlideTimeRemaining - dt);
        if (SlideTimeRemaining <= 0f || HorizontalSpeed <= Cfg.SlideExitSpeed) EndSlide();
    }

    private void EndSlide()
    {
        IsSliding = false;
        SlideTimeRemaining = 0f;
    }

    /// <summary>
    /// Z toggles a grounded low-prone stance. Leaving prone first probes the
    /// full standing volume; a ceiling simply keeps the player prone instead of
    /// forcing them through it or invoking the mover rescue path.
    /// </summary>
    private void ToggleProne(GameMap map)
    {
        if (IsProne)
        {
            if (!CharacterMover.CanFitStance(map, this, 0f)) return;
            IsProne = false;
            CrouchToggled = false;
            return;
        }

        if (!OnGround || IsSliding || IsMantling) return;
        IsProne = true;
        CrouchToggled = false;
        StopSprinting();
    }

    private void BeginMantle(MantlePlan plan)
    {
        _mantlePlan = plan;
        _mantleElapsed = 0f;
        MantleTimeRemaining = Cfg.MantleDuration;
        IsMantling = true;
        IsProne = false;
        CrouchToggled = false;
        StopSprinting();
        EndSlide();
        Velocity = Vector3.Zero;
        // The plan is only built from a fully standing actor. Keeping the
        // controlled route in that same collision volume makes every preflight
        // clearance sample valid throughout the 0.6s transit.
        CrouchAmount = 0f;
        OnGround = true;
    }

    private void TickMantle(GameMap map, float dt)
    {
        if (!IsMantling) return;

        float duration = MathF.Max(0.001f, Cfg.MantleDuration);
        float nextElapsed = MathF.Min(duration, _mantleElapsed + MathF.Max(0f, dt));
        Vector3 target = _mantlePlan.PositionAt(nextElapsed / duration);
        if (!CharacterMover.TryTraverseMantle(map, this, target))
        {
            // Do not resolve a failed route through a wall. Stay at the last
            // known safe point and let ordinary gravity resume if it is airborne.
            IsMantling = false;
            MantleTimeRemaining = 0f;
            _mantleElapsed = 0f;
            Velocity = Vector3.Zero;
            OnGround = false;
            if (map.FindFloor(Position + Vector3.UnitY * 0.5f, 0.5f, Cfg.GroundSnap, out float floorY)
                && Position.Y - floorY <= Cfg.GroundSnap)
            {
                Position = new Vector3(Position.X, floorY, Position.Z);
                OnGround = true;
            }
            return;
        }

        _mantleElapsed = nextElapsed;
        MantleTimeRemaining = MathF.Max(0f, duration - _mantleElapsed);
        Velocity = Vector3.Zero;
        OnGround = true;

        if (_mantleElapsed < duration) return;

        IsMantling = false;
        MantleTimeRemaining = 0f;
        _mantleElapsed = 0f;
        // The final point was collision checked above and is a verified top
        // surface from TryBuildMantlePlan, so it is safe to resume the regular
        // grounded mover next frame.
        Position = _mantlePlan.Landing;
        Velocity = Vector3.Zero;
        OnGround = true;
    }

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

    /// <summary>
    /// Builds the procedural weapon targets from movement and an idle Lissajous
    /// curve, while mouse drag and firing arrive as impulses.  The values are
    /// only read by Renderer.DrawViewModel; the physical camera stays on the
    /// normal yaw/pitch hierarchy and remains the sole hitscan authority.
    /// </summary>
    private void TickViewModelDynamics(InputFrame input, float time, Vector3 right, float dt)
    {
        float adsScale = U.Lerp(1f, Cfg.ViewModelAdsMotionScale, ZoomBlend);
        float speedScale = U.Clamp01(HorizontalSpeed / (Cfg.RunSpeed * Cfg.TacticalSprintSpeedMultiplier));
        float lateralVelocity = Vector3.Dot(Velocity, right);
        float lateral = U.Clamp(lateralVelocity / Cfg.RunSpeed, -1f, 1f);
        float breathing = (1f - speedScale) * (1f - SprintWeaponBlend) * adsScale;

        // Small out-of-phase waves avoid the metronomic look of a single sine.
        Vector3 positionTarget = new(
            (MathF.Sin(time * 1.20f) * 0.42f + MathF.Sin(time * 0.80f) * 0.24f) * breathing
                - lateral * 0.46f * adsScale,
            MathF.Cos(time) * 0.42f * breathing,
            -LandDip * 0.42f);
        Vector3 rotationTarget = new(
            -LandDip * 2.3f,
            0f,
            -lateral * 3.6f * adsScale);

        // Mouse drag is velocity-driven rather than a permanent pose target:
        // large turns create a brief lag/bank that the spring catches up with.
        if (input.Look != Vector2.Zero)
        {
            ViewModelOffsetSpring.Impulse(new Vector3(
                -input.Look.X * 1.35f * adsScale,
                input.Look.Y * 0.72f * adsScale,
                0f));
            ViewModelRotationSpring.Impulse(new Vector3(
                -input.Look.Y * 4.6f * adsScale,
                input.Look.X * 2.1f * adsScale,
                -input.Look.X * 5.4f * adsScale));
        }

        ViewModelOffsetSpring.Tick(positionTarget, dt);
        ViewModelRotationSpring.Tick(rotationTarget, dt);
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
        TraumaPulse = U.Approach(TraumaPulse, 0f, Cfg.LowHealthPulseDecayRate, dt);
    }

    /// <summary>
    /// Owns only the local critical-condition presentation.  The explicit timer
    /// emits one short cue per beat instead of letting the renderer create audio
    /// every frame, which keeps it deterministic and valid in headless tests.
    /// </summary>
    private void TickLowHealthFeedback(Simulation sim, float now, float dt)
    {
        float target = Health < Cfg.LowHealthTraumaThreshold
            ? U.Clamp01((Cfg.LowHealthTraumaThreshold - Health) / Cfg.LowHealthTraumaThreshold)
            : 0f;
        float rate = target > LowHealthTrauma
            ? Cfg.LowHealthTraumaRiseRate
            : Cfg.LowHealthTraumaFallRate;
        LowHealthTrauma = U.Approach(LowHealthTrauma, target, rate, dt);

        if (target <= 0.0001f)
        {
            _nextLowHealthHeartbeatAt = -10f;
            return;
        }

        if (now < _nextLowHealthHeartbeatAt) return;

        TraumaPulse = 1f;
        float interval = U.Lerp(Cfg.LowHealthHeartbeatCalmInterval,
            Cfg.LowHealthHeartbeatUrgentInterval, target);
        _nextLowHealthHeartbeatAt = now + interval;

        // This is a private perceptual cue; it must not attenuate or pan like a
        // world emitter.  The generated bank sound is deliberately unbranded.
        sim.Emit("heartbeat", EyePosition,
            U.Lerp(0.10f, 0.18f, target), U.Lerp(0.92f, 1.04f, target), SoundEventScope.Local);
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
            || SprintState != PlayerSprintState.None
            || IsSliding
            || IsMantling;

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
        bool loweredMobilityPose = SprintState != PlayerSprintState.None || IsSliding || IsMantling;
        SprintWeaponBlend = U.Approach(SprintWeaponBlend,
            loweredMobilityPose ? 1f : 0f,
            loweredMobilityPose ? Cfg.SprintWeaponBlendInRate : Cfg.SprintWeaponBlendOutRate,
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
