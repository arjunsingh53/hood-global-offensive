using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Render;
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

    // Two independent second-order presentation springs sit between raw input
    // and the rendered weapon.  They are deliberately local to this player and
    // never feed back into yaw, pitch, collision, spread, hit-scan or bots.
    public ViewModelSpring ViewModelOffsetSpring { get; } = new(
        frequencyHz: 9.5f, dampingRatio: 0.82f, maxDisplacement: 8f, maxSpeed: 420f);
    public ViewModelSpring ViewModelRotationSpring { get; } = new(
        frequencyHz: 10.5f, dampingRatio: 0.88f, maxDisplacement: 16f, maxSpeed: 680f);

    // ---------------------------------------------------------------- grapple
    public GrappleHook HookLeft { get; } = new();
    public GrappleHook HookRight { get; } = new();
    public bool AnyHookAttached => HookLeft.Attached || HookRight.Attached;

    // ---------------------------------------------------------------- interactions
    /// <summary>Set for one frame when E is pressed next to an upgrade cabinet; Game consumes it.</summary>
    public bool WantsOpenUpgrade;
    public bool FlashlightOn;
    /// <summary>Consumed by Simulation: the Double Barrel's right-click blast.</summary>
    public bool WantsSecondaryFire;
    /// <summary>Quick knife swing (V): remaining presentation time and which hand swings.</summary>
    public float MeleeSwing;
    public bool MeleeSwingLeftHand;
    private float _meleeNextTime = -10f;

    // Render-only transient layers. They are triggered from real gameplay events,
    // but no collision, aim, spread, recoil, bot or weapon code reads them.
    public float FootstepFeedback { get; private set; }
    public float LandingFeedback { get; private set; }
    public float DamageFeedback { get; private set; }
    public float DamageFeedbackYaw { get; private set; }
    /// <summary>
    /// Sustained critical-health feedback, consumed only by HUD/audio presentation.
    /// </summary>
    public float LowHealthTrauma { get; private set; }
    /// <summary>Short, sharp envelope restarted by each local heartbeat cue.</summary>
    public float TraumaPulse { get; private set; }
    private float _nextLowHealthHeartbeatAt = -10f;

    // Records the weapon that owned the previous frame's aim state. A scoped
    // weapon must never inherit a held ADS flag from a weapon it replaced.
    private Weapon? _lastAimWeapon;

    public Player(int id, string name)
    {
        Id = id;
        Name = name;
        IsPlayer = true;
        GiveWeapon(WeaponRegistry.Knife, 4);
    }

    public void Update(InputFrame input, Simulation sim, float dt)
    {
        float now = sim.Time;
        WantsFire = false;
        WantsSecondaryFire = false;
        WantsOpenUpgrade = false;

        if (!Alive)
        {
            Velocity = Vector3.Zero;
            ZoomHeld = false;
            LandDip = 0f;
            BobAmount = 0f;
            MeleeSwing = 0f;
            ResetViewModelDynamics();
            ResetPresentationFeedback();
            ResetMovementState();
            return;
        }

        TickPresentationFeedback(dt);
        TickLowHealthFeedback(sim, now, dt);

        // Select before applying look sensitivity so a newly equipped optic
        // cannot borrow one frame of the previous weapon's zoom state.
        if (input.SwitchSlot > 0) SelectWeaponNumber(input.SwitchSlot);
        if (input.SwitchCycle != 0) CycleWeapon(input.SwitchCycle);

        Weapon weapon = Current;
        bool weaponChanged = _lastAimWeapon is not null && !ReferenceEquals(_lastAimWeapon, weapon);
        if (weaponChanged)
        {
            ZoomHeld = false;
            ZoomBlend = 0f;
            CurrentFov = Cfg.FovDefault;
            ResetViewModelDynamics();
        }
        _lastAimWeapon = weapon;

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
        weapon.Tick(now, dt);

        // Right click: the Double Barrel fires its panic blast; magnified optics
        // toggle their scope; everything else holds to aim.
        if (weapon.Def.HasSecondary)
        {
            ZoomHeld = false;
            if (input.ZoomPressed)
            {
                WantsSecondaryFire = true;
                // The blast is a bigger event than any single shot: give it a
                // heavier presentation kick whenever the trigger will accept it.
                if (weapon.CanFire(now)) ApplyShotVisuals(sim, weapon, kickScale: 1.8f);
            }
        }
        else if (weapon.Def.HasScope && !weaponChanged && input.ZoomPressed) ZoomHeld = !ZoomHeld;
        else if (weapon.Def.HasIronSights) ZoomHeld = weaponChanged ? false : input.ZoomDown;
        else ZoomHeld = false;
        ZoomBlend = U.Approach(ZoomBlend, ZoomHeld ? 1f : 0f, 15f, dt);

        float targetFov = weapon.Def.HasZoom
            ? U.Lerp(Cfg.FovDefault, weapon.Def.AimFov, ZoomBlend)
            : Cfg.FovDefault;
        CurrentFov = U.Approach(CurrentFov, targetFov, 18f, dt);

        if (input.Reload && weapon.BeginReload(now)) sim.Emit("reload", EyePosition, 0.8f);
        if (input.FireDown && weapon.Def.UsesAmmo && weapon.Mag == 0 && !weapon.Reloading
            && weapon.Reserve > 0 && weapon.BeginReload(now))
        {
            sim.Emit("reload", EyePosition, 0.8f);
        }

        // ------------------------------------------------------------ flashlight
        if (input.FlashlightTogglePressed)
        {
            FlashlightOn = !FlashlightOn;
            sim.Emit("flashlight", EyePosition, 0.3f, FlashlightOn ? 1.1f : 0.9f, SoundEventScope.Local);
        }

        // ------------------------------------------------------------ quick melee (V)
        MeleeSwing = MathF.Max(0f, MeleeSwing - dt / 0.35f);
        if (input.MeleePressed && now >= _meleeNextTime && !weapon.Def.HasScope)
        {
            if (sim.PerformQuickMelee(this))
            {
                _meleeNextTime = now + Cfg.QuickMeleeCooldown;
                MeleeSwing = 1f;
                MeleeSwingLeftHand = !MeleeSwingLeftHand;
                ViewModelRotationSpring.Impulse(new Vector3(0f, MeleeSwingLeftHand ? -70f : 70f, -50f));
            }
        }

        // ------------------------------------------------------------ grapple
        UpgradeStation? station = sim.NearbyStation(this);
        bool useNearbyStation = station is not null && input.UsePressed;
        if (useNearbyStation) WantsOpenUpgrade = true;

        if (input.GrappleLeftPressed) ToggleHook(sim, HookLeft);
        if (input.GrappleRightPressed && !useNearbyStation) ToggleHook(sim, HookRight);
        if (HookLeft.Attached) GrappleSystem.Tick(sim.Map, this, HookLeft, dt);
        if (HookRight.Attached) GrappleSystem.Tick(sim.Map, this, HookRight, dt);

        // ------------------------------------------------------------ movement
        Vector3 forward = U.DirFromAngles(Yaw, 0f);
        Vector3 right = U.RightFromYaw(Yaw);
        Vector3 wishDir = U.FlatNormalized(forward * input.Move.Y + right * input.Move.X);

        float wishSpeed = Cfg.RunSpeed * weapon.Def.MoveScale;
        if (ZoomHeld) wishSpeed *= 0.5f;
        if (weapon.Reloading) wishSpeed *= 0.92f;

        float fallSpeed = Velocity.Y;
        bool wasOnGround = OnGround;

        CharacterMover.Move(sim.Map, this, wishDir, wishSpeed, input.Jump, crouchHeld: false, dt);

        if (!wasOnGround && OnGround && fallSpeed < -160f)
        {
            LandDip = U.Clamp01((-fallSpeed - 160f) / 700f);
            TriggerLandingFeedback(LandDip);
        }
        LandDip = U.Approach(LandDip, 0f, 9f, dt);

        // ------------------------------------------------------------ recoil + firing
        RecoverRecoil(now, dt);

        bool wantShot = weapon.Def.Automatic ? input.FireDown : input.FirePressed;
        if (wantShot && weapon.CanFire(now))
        {
            WantsFire = true;
            ApplyShotVisuals(sim, weapon);
        }

        // ------------------------------------------------------------ view bob
        float speedFactor = U.Clamp01(HorizontalSpeed / Cfg.RunSpeed);
        if (OnGround)
        {
            BobPhase += HorizontalSpeed * dt * Cfg.ViewBobPhasePerUnit;

            float adsScale = U.Lerp(1f, Cfg.ViewBobAdsScale, ZoomBlend);
            float targetBob = speedFactor * adsScale;
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

    /// <summary>
    /// Press-to-fire, press-again-to-release hook control. Firing consumes no
    /// velocity; releasing keeps every unit of swing momentum.
    /// </summary>
    private void ToggleHook(Simulation sim, GrappleHook hook)
    {
        if (hook.Attached)
        {
            hook.Detach();
            sim.Emit("grapple_release", EyePosition, 0.35f, 1.05f, SoundEventScope.Local);
            return;
        }

        if (GrappleSystem.TryAttach(sim.Map, this, hook))
        {
            sim.Emit("grapple_fire", EyePosition, 0.4f, 1.12f, SoundEventScope.Local);
            sim.Emit("grapple_attach", hook.Anchor, 0.5f);
        }
        else
        {
            sim.Emit("grapple_fire", EyePosition, 0.30f, 0.9f, SoundEventScope.Local);
        }
    }

    private void ApplyShotVisuals(Simulation sim, Weapon weapon, float kickScale = 1f)
    {
        float kick = weapon.Def.VisualKick * kickScale;
        ViewKick = MathF.Min(ViewKick + kick, kick * 2.4f);
        KickYaw += sim.Rand(-0.5f, 0.5f) * kick * 0.12f;
        MuzzleFlash = 0.045f;

        ViewModelOffsetSpring.Impulse(new Vector3(0f, -kick * 2.4f, kick * 7.5f));
        ViewModelRotationSpring.Impulse(new Vector3(kick * 15f, KickYaw * 18f, -kick * 2.6f));
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

    /// <summary>Clears transient movement state after death, respawn or a staged capture.</summary>
    public void ResetMovementState()
    {
        HookLeft.Detach();
        HookRight.Detach();
        MeleeSwing = 0f;
        WantsSecondaryFire = false;
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

    /// <summary>
    /// Builds the procedural weapon targets from movement and an idle Lissajous
    /// curve, while mouse drag and firing arrive as impulses.  The values are
    /// only read by Renderer.DrawViewModel; the physical camera stays on the
    /// normal yaw/pitch hierarchy and remains the sole hitscan authority.
    /// </summary>
    private void TickViewModelDynamics(InputFrame input, float time, Vector3 right, float dt)
    {
        float adsScale = U.Lerp(1f, Cfg.ViewModelAdsMotionScale, ZoomBlend);
        float speedScale = U.Clamp01(HorizontalSpeed / (Cfg.RunSpeed * 1.5f));
        float lateralVelocity = Vector3.Dot(Velocity, right);
        float lateral = U.Clamp(lateralVelocity / Cfg.RunSpeed, -1f, 1f);
        float breathing = (1f - speedScale) * adsScale;

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

        // A hanging rope pulls the whole weapon slightly, selling the swing.
        if (HookLeft.Attached || HookRight.Attached)
        {
            positionTarget += new Vector3(0f, -0.3f, -0.5f);
        }

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

        sim.Emit("heartbeat", EyePosition,
            U.Lerp(0.10f, 0.18f, target), U.Lerp(0.92f, 1.04f, target), SoundEventScope.Local);
    }

    private static float Wrap(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}
