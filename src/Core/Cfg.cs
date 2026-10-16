namespace HoodGlobalOffensive.Core;

/// <summary>
/// Central tuning table for the Redmatch-style arena shooter. Distances are in
/// world units (a standing player is 72 units tall), times are seconds and
/// angles are degrees where noted. The movement model is deliberately
/// momentum-first: ground friction eats speed, but jumping and air strafing
/// preserve and can grow it, exactly like the genre expects.
/// </summary>
public static class Cfg
{
    public const string Title = "HOODMATCH  -  early access v0.2.0";

    /// <summary>Shown on the main menu and in the window title.</summary>
    public const string GameName = "HOODMATCH";

    /// <summary>The supplied low-poly industrial zone is the game's playable arena.</summary>
    public const string MapName = "Industrial Zone";

    public const string Version = "0.2.0";

    // ---------------------------------------------------------------- player size
    public const float HalfWidth = 16f;
    public const float StandHeight = 72f;
    public const float CrouchHeight = 54f;
    public const float EyeStand = 64f;
    public const float EyeCrouch = 46f;
    public const float CrouchBlendSpeed = 9f;

    // ---------------------------------------------------------------- movement
    public const float Gravity = 700f;
    public const float MaxFallSpeed = 1100f;
    /// <summary>Base run speed. Everything above this is earned through momentum.</summary>
    public const float RunSpeed = 400f;
    public const float CrouchSpeed = 140f;
    public const float Accelerate = 6.0f;
    /// <summary>
    /// Air acceleration deliberately allows the velocity vector to grow: the
    /// classic strafe-jump / bunny-hop loop. The steering wish speed is capped
    /// low while the acceleration factor stays high, so the player turns their
    /// momentum rather than gaining a second ground-like acceleration route.
    /// </summary>
    public const float AirAccelerate = 2.0f;
    public const float AirMaxWishSpeed = 55f;
    public const float Friction = 5.2f;
    /// <summary>
    /// Touching WASD on the ground kills momentum, exactly like the reference:
    /// friction is multiplied while directional input is held on the ground.
    /// Bhop keeps speed by holding jump and staying off WASD until take-off.
    /// </summary>
    public const float GroundFrictionMoveMultiplier = 2.5f;
    /// <summary>Bhop take-off: holding jump on the ground skips friction and re-jumps instantly.</summary>
    public const float AutoBhopFrictionMultiplier = 0f;
    public const float StopSpeed = 80f;
    public const float JumpVelocity = 330f;
    /// <summary>
    /// Hard safety cap on horizontal player speed. Grapple swings are fast, but
    /// this keeps the swept collision solver and the world bounds authoritative.
    /// </summary>
    public const float MaxHorizontalSpeed = 1800f;
    public const float StepHeight = 20f;
    /// <summary>Vertical snap distance used to keep the mover glued to stairs.</summary>
    public const float GroundSnap = 8f;

    // ---------------------------------------------------------------- grapple
    /// <summary>Maximum ray length of a grapple shot.</summary>
    public const float GrappleRange = 2800f;
    /// <summary>Attached hooks auto-release once the rope gets this short.</summary>
    public const float GrappleMinLength = 90f;
    /// <summary>
    /// The rope pulls like a spring: farther anchors pull harder, which is the
    /// reference's "farther away = more speed in all directions" rule.
    /// Acceleration is distance * this value, capped below.
    /// </summary>
    public const float GrapplePullPerUnit = 3.2f;
    public const float GrapplePullMax = 2400f;
    /// <summary>Total speed the rope physics itself will not exceed, in units per second.</summary>
    public const float GrappleMaxRopeSpeed = 1800f;
    /// <summary>Downward speed is not capped at map scale, but extreme rises are.</summary>
    public const float GrappleMaxRiseSpeed = 1600f;

    // ---------------------------------------------------------------- upgrades
    /// <summary>Owned by the kill-points economy: every kill is worth this many points.</summary>
    public const int KillPointsPerKill = 1;
    /// <summary>Kill points charged per upgrade level at a cabinet.</summary>
    public const int UpgradePointCost = 1;
    /// <summary>Four stats, three levels each: 12 purchases max a single match.</summary>
    public const int UpgradeMaxLevel = 3;
    public const float UpgradeHealthPerLevel = 25f;
    public const float UpgradeDamagePerLevel = 0.12f;
    public const float UpgradeSpeedPerLevel = 0.06f;
    public const float UpgradeJumpPerLevel = 0.07f;
    /// <summary>Death costs one level of the player's highest upgrade, not the points.</summary>
    public const bool LoseOneUpgradeLevelOnDeath = true;
    /// <summary>Flat distance at which the cabinet prompts its upgrade menu.</summary>
    public const float StationInteractRadius = 150f;

    // ---------------------------------------------------------------- health regen
    /// <summary>Seconds without taking damage before health regenerates.</summary>
    public const float RegenDelay = 4f;
    /// <summary>Regeneration rate once it kicks in, in health per second.</summary>
    public const float RegenRate = 20f;

    // ---------------------------------------------------------------- melee
    /// <summary>V is a quick knife swing that never switches weapons.</summary>
    public const float QuickMeleeCooldown = 0.5f;

    // ---------------------------------------------------------------- flashlight
    public const float FlashlightRange = 1600f;
    /// <summary>Half-angle of the fake volumetric cone drawn for the flashlight.</summary>
    public const float FlashlightHalfAngleDeg = 13f;

    // ---------------------------------------------------------------- combat
    public const float MaxHealth = 100f;
    public const float RespawnDelay = 3f;
    public const int KillsToWin = 30;
    public const float MatchTime = 600f;

    // ---------------------------------------------------------- combat feedback
    /// <summary>
    /// Health below this point starts the critical-condition feedback.
    /// It is presentation/audio state only: health, hit-scan and movement
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
    // These are presentation measurements, intentionally separate from
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

    // ---------------------------------------------------------------- character art
    /// <summary>
    /// Preferred full-body actor art.  The renderer measures its actual bounds at
    /// load time so a DCC conversion can never quietly alter the 72-unit actor
    /// collision contract.
    /// </summary>
    public const string SoldierModelPath = "assets/characters/hgo-lowpoly-tactical-operator.glb";
    /// <summary>
    /// Preserve the pre-existing Soldier as a runtime fallback: a bad local
    /// character package must never make bots disappear.
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
