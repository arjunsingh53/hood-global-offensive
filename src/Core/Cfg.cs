namespace HoodGlobalOffensive.Core;

/// <summary>
/// Central tuning table. Distances are in classic CS-style world units
/// (a standing player is 72 units tall and runs at 250 units/second),
/// times are seconds and angles are degrees where noted.
/// </summary>
public static class Cfg
{
    public const string Title = "HOOD GLOBAL OFFENSIVE  -  early access v0.1";

    /// <summary>Shown on the main menu and in the window title.</summary>
    public const string GameName = "HOOD GLOBAL OFFENSIVE";

    /// <summary>The playable arena. The game has one map for now; the map is "Crossfire".</summary>
    public const string MapName = "Crossfire";

    public const string Version = "0.1.0";

    // ---------------------------------------------------------------- player size
    public const float HalfWidth = 16f;
    public const float StandHeight = 72f;
    public const float CrouchHeight = 54f;
    public const float EyeStand = 64f;
    public const float EyeCrouch = 46f;
    public const float CrouchBlendSpeed = 9f;

    // ---------------------------------------------------------------- movement
    public const float Gravity = 800f;
    public const float MaxFallSpeed = 1100f;
    public const float RunSpeed = 250f;
    public const float WalkSpeed = 130f;
    public const float CrouchSpeed = 85f;
    public const float Accelerate = 5.5f;
    public const float AirAccelerate = 12f;
    public const float AirSpeedCap = 30f;
    public const float Friction = 5.2f;
    public const float StopSpeed = 80f;
    public const float JumpVelocity = 300f;
    public const float StepHeight = 20f;
    /// <summary>Vertical snap distance used to keep the mover glued to stairs.</summary>
    public const float GroundSnap = 8f;

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

    // ---------------------------------------------------------------- view
    public const float FovDefault = 90f;
    public const float Sensitivity = 2.2f;
    /// <summary>Degrees of rotation per mouse count at sensitivity 1.0.</summary>
    public const float DegPerCountPerSens = 0.04f;
    public const float MaxPitch = 89f;
    public const float ViewLerpSmoothing = 26f;

    // ---------------------------------------------------------------- world
    public const float MapHalf = 1200f;
    public const float WallHeight = 300f;
    public const float SkyDomeSize = 9000f;

    // ---------------------------------------------------------------- bots
    public const int DefaultBotCount = 9;
    public const int MaxBots = 15;
}
