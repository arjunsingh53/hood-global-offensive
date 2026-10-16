namespace HoodGlobalOffensive.Core;

/// <summary>
/// Central tuning table. Distances are in classic CS-style world units
/// (a standing player is 72 units tall and runs at 250 units/second),
/// times are seconds and angles are degrees where noted.
/// </summary>
public static class Cfg
{
    public const string Title = "HOOD GLOBAL OFFENSIVE  -  early access v0.1.1";

    /// <summary>Shown on the main menu and in the window title.</summary>
    public const string GameName = "HOOD GLOBAL OFFENSIVE";

    /// <summary>The supplied low-poly industrial zone is the game's playable arena.</summary>
    public const string MapName = "Industrial Zone";

    public const string Version = "0.1.1";

    // ---------------------------------------------------------------- player size
    public const float HalfWidth = 16f;
    public const float StandHeight = 72f;
    public const float CrouchHeight = 54f;
    public const float EyeStand = 64f;
    public const float EyeCrouch = 46f;
    public const float CrouchBlendSpeed = 9f;
    /// <summary>
    /// Low-prone uses the existing continuous stance/collision interpolation so
    /// hit boxes, eye position and world collision stay in one authoritative
    /// shape.  The compatibility amount is intentionally derived rather than
    /// hardcoded: HeadHeight and EyeHeight both resolve to these values.
    /// </summary>
    public const float ProneHeight = 34f;
    public const float ProneEyeHeight = 26f;
    public const float ProneStanceAmount = (StandHeight - ProneHeight) / (StandHeight - CrouchHeight);
    public const float ProneSpeed = 48f;
    public const float ProneBlendSpeed = 6.5f;

    // ---------------------------------------------------------------- movement
    public const float Gravity = 800f;
    public const float MaxFallSpeed = 1100f;
    public const float RunSpeed = 250f;
    public const float WalkSpeed = 130f;
    public const float CrouchSpeed = 85f;
    public const float Accelerate = 5.5f;
    /// <summary>
    /// Air input provides modest steering but never a second acceleration route.
    /// These values are intentionally separate from the grounded controller so
    /// a player cannot create speed through repeated jump/strafe inputs.
    /// </summary>
    public const float AirAccelerate = 12f;
    public const float AirControlSpeed = 110f;
    public const float Friction = 5.2f;
    public const float StopSpeed = 80f;
    public const float JumpVelocity = 300f;
    /// <summary>Regular and tactical sprint follow the supplied Godot reference's 1.4x / 1.7x targets.</summary>
    public const float SprintSpeedMultiplier = 1.4f;
    public const float TacticalSprintSpeedMultiplier = 1.7f;
    /// <summary>Source-derived acceleration factors, mapped with the speed multiplier in Player.</summary>
    public const float SprintAccelerationMultiplier = 0.7f;
    public const float TacticalSprintAccelerationMultiplier = 0.5f;
    /// <summary>
    /// HGO's base friction/acceleration ratio is much tighter than the Godot
    /// reference. This small headroom keeps a higher sprint target attainable
    /// after mapping its relative acceleration multipliers into HGO units.
    /// </summary>
    public const float SprintAccelerationSustainMargin = 0.02f;
    public const float TacticalSprintDuration = 3f;
    /// <summary>Seconds of tactical-sprint charge restored per real second while not tactical sprinting.</summary>
    public const float TacticalSprintRecoveryRate = 1f;
    /// <summary>
    /// A slide may only begin from earned sprint momentum. It never supplies a
    /// speed boost of its own: entry velocity is preserved, then decelerated
    /// through the normal swept collision mover.
    /// </summary>
    public const float SlideMinimumEntrySpeed = 280f;
    public const float SlideDuration = 0.72f;
    public const float SlideExitSpeed = 95f;
    /// <summary>Low ground friction lets a slide carry momentum without becoming a glide.</summary>
    public const float SlideFrictionMultiplier = 0.30f;
    /// <summary>How quickly deliberate movement can bend an active slide, in inverse seconds.</summary>
    public const float SlideSteerResponsiveness = 3.5f;
    /// <summary>Extra movement spread while sliding; crouch accuracy never applies mid-slide.</summary>
    public const float SlideSpreadPenalty = 1.35f;
    public const float StepHeight = 20f;
    /// <summary>Vertical snap distance used to keep the mover glued to stairs.</summary>
    public const float GroundSnap = 8f;

    // --------------------------------------------------------------- mantling
    /// <summary>
    /// A player-only mantle is a pre-validated three-segment route: lift,
    /// cross a low surface, then settle on it.  The values are in HGO world
    /// units (72 units is the standing collision height), corresponding to
    /// roughly 0.5--1.8 m of climbable static cover.
    /// </summary>
    public const float MantleMinHeight = 22f;
    public const float MantleMaxHeight = 78f;
    public const float MantleForwardReach = 58f;
    /// <summary>Reject broad building roofs; mantle is for local cover, not map bypasses.</summary>
    public const float MantleMaxSurfaceDepth = 64f;
    public const float MantleLandingInset = 14f;
    public const float MantleSurfaceClearance = 0.35f;
    public const float MantleCollisionSubstep = 3.5f;
    public const float MantleDuration = 0.60f;

    // ---------------------------------------------------------------- combat
    public const float MaxHealth = 100f;
    public const float MaxArmor = 100f;
    public const float RespawnDelay = 3f;
    public const int KillsToWin = 30;
    public const float MatchTime = 600f;
    public const int StartMoney = 800;
    public const int MaxMoney = 16000;
    public const int KillReward = 300;
    public const int HeadshotBonus = 100;
    /// <summary>Respawn top-up so a player is never stuck without a usable buy.</summary>
    public const int RespawnTopUp = 1100;

    // ---------------------------------------------------------- combat feedback
    /// <summary>
    /// Health below this point starts HGO's original critical-condition feedback.
    /// It is presentation/audio state only: health, armour, hit-scan and movement
    /// remain authoritative and unchanged.
    /// </summary>
    public const float LowHealthTraumaThreshold = 35f;
    /// <summary>How quickly sustained critical-health feedback rises and clears.</summary>
    public const float LowHealthTraumaRiseRate = 6.5f;
    public const float LowHealthTraumaFallRate = 3.0f;
    /// <summary>Sharp local pulse decay after each critical-health heartbeat.</summary>
    public const float LowHealthPulseDecayRate = 5.6f;
    /// <summary>Heartbeat cadence at the edge and bottom of the critical range.</summary>
    public const float LowHealthHeartbeatCalmInterval = 1.02f;
    public const float LowHealthHeartbeatUrgentInterval = 0.56f;
    /// <summary>Lowest retained world-sound gain during critical-health focus.</summary>
    public const float LowHealthWorldSoundFloor = 0.76f;
    /// <summary>Largest neutral wash alpha used by the low-health HUD overlay.</summary>
    public const int LowHealthNeutralWashAlpha = 54;

    /// <summary>
    /// Presentation effects retain the newest entries when these caps are reached.
    /// The values bound combat feedback during sustained automatic fire without
    /// changing damage, collision, scoring or simulation timing.
    /// </summary>
    public const int MaxImpactParticles = 240;
    public const int MaxImpactDecals = 112;
    public const int MaxBloodMists = 72;
    public const int MaxBloodDroplets = 96;
    public const int MaxBloodSplats = 80;
    /// <summary>Only the first actor contact in a multi-pellet trigger emits the expensive burst.</summary>
    public const int MaxActorHitVfxEventsPerTrigger = 1;
    public const float BloodDecalProjectionRange = 132f;
    public const float BloodSplatSurfaceOffset = 0.20f;
    public const float BloodDecalLifetime = 10f;

    // ---------------------------------------------------------------- view
    public const float FovDefault = 90f;
    public const float Sensitivity = 2.2f;
    /// <summary>Degrees of rotation per mouse count at sensitivity 1.0.</summary>
    public const float DegPerCountPerSens = 0.04f;
    public const float MaxPitch = 89f;
    public const float ViewLerpSmoothing = 26f;
    /// <summary>View bob is distance-driven so its cadence follows actual movement speed.</summary>
    public const float ViewBobPhasePerUnit = 0.028f;
    public const float ViewBobSide = 1.05f;
    public const float ViewBobVertical = 0.80f;
    public const float ViewBobRollDeg = 0.35f;
    public const float ViewBobInRate = 9f;
    public const float ViewBobOutRate = 13f;
    /// <summary>ADS calms camera motion without changing the aim or hit-scan ray.</summary>
    public const float ViewBobAdsScale = 0.18f;
    public const float ViewBobCrouchScale = 0.55f;
    public const float ViewLandDipDistance = 9f;
    public const float ViewModelBobSide = 1.55f;
    public const float ViewModelBobVertical = 1.15f;
    /// <summary>
    /// The first-person weapon is rendered through its own, fixed focal-length
    /// camera.  World ADS still changes the world camera FOV; keeping this pass
    /// at a stable 59 degrees prevents a wide player FOV from stretching the
    /// weapon or changing the authored iron-sight picture.
    /// </summary>
    public const float ViewModelFov = 59f;
    public const float ViewModelNearClip = 0.05f;
    public const float ViewModelFarClip = 600f;
    /// <summary>ADS calms cosmetic weapon inertia without touching aim or spread.</summary>
    public const float ViewModelAdsMotionScale = 0.20f;

    // ---------------------------------------------------------------- camera feedback
    // These are HGO-local presentation measurements, intentionally separate from
    // the physical player controller. They create a restrained, layered response
    // to footsteps, landings and incoming damage without moving the aim ray.
    public const float FeedbackFootstepSideDistance = 0.28f;
    public const float FeedbackFootstepDropDistance = 0.42f;
    public const float FeedbackLandingDropDistance = 1.15f;
    public const float FeedbackDamageSideDistance = 0.78f;
    public const float FeedbackDamageDropDistance = 0.34f;
    public const float FeedbackDamagePushDistance = 0.48f;
    public const float FeedbackFootstepDecayPerSecond = 1.8f;
    public const float FeedbackLandingDecayPerSecond = 2.7f;
    public const float FeedbackDamageDecayPerSecond = 2.5f;
    public const float FeedbackFootstepFovDegrees = 0.08f;
    public const float FeedbackLandingFovDegrees = 0.32f;
    public const float FeedbackDamageFovDegrees = 0.18f;
    public const float FeedbackAdsScale = 0.25f;
    public const float FeedbackViewModelScale = 1.30f;

    // --------------------------------------------------------- weapon readiness
    // These presentation timings belong to HGO's own first-person handling layer.
    // They do not change weapon cadence, ballistic state, collision or hit-scan.
    public const float WeaponInspectDuration = 1.55f;
    public const float WeaponInspectBlendInRate = 13f;
    public const float WeaponInspectBlendOutRate = 18f;
    public const float SprintWeaponBlendInRate = 10f;
    public const float SprintWeaponBlendOutRate = 14f;

    // ---------------------------------------------------------------- character art
    /// <summary>
    /// Preferred full-body actor art.  The renderer measures its actual bounds at
    /// load time so a DCC conversion can never quietly alter HGO's 72-unit actor
    /// collision contract.
    /// </summary>
    public const string SoldierModelPath = "assets/characters/hgo-lowpoly-tactical-operator.glb";
    /// <summary>
    /// Preserve the pre-existing Soldier as a runtime fallback: a bad local
    /// character package must never make bots disappear or affect Gun Game.
    /// </summary>
    public const string SoldierFallbackModelPath = "assets/characters/soldier.glb";
    public const float CharacterCullDistance = 5200f;
    public const float CharacterShadowDiameter = 30f;
    public const float CharacterAnimationFrameRate = 30f;
    public const float CharacterIdleSpeed = 12f;
    public const float CharacterRunSpeed = 155f;
    public const float CharacterWalkRate = 0.85f;
    public const float CharacterRunRate = 1.0f;
    /// <summary>Offsets the shared clip so a group of bots do not animate in lockstep.</summary>
    public const float CharacterAnimationPhasePerId = 5.25f;

    // ---------------------------------------------------------------- world
    /// <summary>Half-width of the centred Industrial Zone island and its containment collision.</summary>
    public const float MapHalf = 1340f;
    public const float WallHeight = 300f;
    public const float SkyDomeSize = 9000f;

    // ---------------------------------------------------------------- bots
    public const int DefaultBotCount = 9;
    public const int MaxBots = 15;
}
