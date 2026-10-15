using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Small deterministic mobility suite for the source-derived sprint layer. It
/// intentionally uses the real Simulation + CharacterMover rather than a
/// parallel physics mock, so input edges, collision, stance clearance and
/// weapon-adjusted movement all exercise their production paths.
/// </summary>
public static class MovementVerifier
{
    private const float Dt = 1f / 60f;

    public static int Run(ulong seed)
    {
        MovementVerificationResult result = Verify(seed);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" MOVEMENT VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine($" seed                 : {seed}");
        Console.WriteLine($" source-style checks  : {result.CheckCount}");
        Console.WriteLine($" deterministic replay : {(result.Deterministic ? "PASS" : "FAIL")}");
        Console.WriteLine($" result               : {(result.Passed ? "PASS" : "FAIL")}");
        foreach (string detail in result.Details) Console.WriteLine($"   {detail}");
        Console.WriteLine("=========================================================");

        return result.Passed ? 0 : 1;
    }

    public static MovementVerificationResult Verify(ulong seed)
    {
        MobilityTrace first = RunTrace(seed);
        MobilityTrace second = RunTrace(seed);
        var failures = new List<string>();

        float targetRun = Cfg.RunSpeed * first.WeaponMoveScale;
        Require(first.ClearOfGeometry, "player entered geometry during the mobility trace", failures);
        Require(first.Finite, "player produced a non-finite transform during the mobility trace", failures);
        Require(first.BaselinePeak >= targetRun * 0.97f,
            $"baseline run peak was {first.BaselinePeak:0.0}, expected at least {targetRun * 0.97f:0.0}", failures);
        Require(first.EnteredSprint, "first forward sprint press did not enter Sprint", failures);
        Require(first.SprintPeak >= targetRun * Cfg.SprintSpeedMultiplier * 0.95f,
            $"sprint peak was {first.SprintPeak:0.0}, expected at least {targetRun * Cfg.SprintSpeedMultiplier * 0.95f:0.0}", failures);
        Require(first.EnteredTacticalSprint, "second forward sprint press did not enter Tactical Sprint", failures);
        Require(first.TacticalPeak >= targetRun * Cfg.TacticalSprintSpeedMultiplier * 0.95f,
            $"tactical sprint peak was {first.TacticalPeak:0.0}, expected at least {targetRun * Cfg.TacticalSprintSpeedMultiplier * 0.95f:0.0}", failures);
        Require(first.TacticalDurationTicks >= (int)MathF.Round(Cfg.TacticalSprintDuration / Dt) - 1
                && first.TacticalDurationTicks <= (int)MathF.Ceiling(Cfg.TacticalSprintDuration / Dt) + 1,
            $"tactical sprint lasted {first.TacticalDurationTicks * Dt:0.000}s instead of {Cfg.TacticalSprintDuration:0.000}s", failures);
        Require(first.TacticalExpiredToSprint, "tactical sprint did not fall back to regular Sprint", failures);
        Require(first.CanceledWithoutForward, "removing forward intent did not cancel sprint", failures);
        Require(first.TacticalChargeRecovered, "tactical-sprint charge did not recover while idle", failures);
        Require(first.CrouchToggleWorked, "C crouch-toggle did not enter and leave crouch in clear space", failures);
        Require(first.CrouchCanceledSprint, "crouch did not cancel sprint", failures);
        Require(first.AirborneCanceledSprint, "take-off did not cancel sprint", failures);
        Require(first.NoDoubleJump, "held jump re-applied upward velocity while airborne", failures);
        Require(first.LandingFeedbackTriggered, "a qualifying landing did not start its presentation feedback", failures);
        Require(first.FeedbackChangesPresentation, "active presentation feedback did not change FOV or the viewmodel state", failures);
        Require(first.FeedbackPreservesAim, "presentation feedback changed the visual aim direction or Player.AimDirection", failures);

        bool deterministic = NearlyEqual(first.FinalPosition, second.FinalPosition)
            && NearlyEqual(first.FinalVelocity, second.FinalVelocity)
            && first.FinalSprintState == second.FinalSprintState
            && MathF.Abs(first.FinalTacticalCharge - second.FinalTacticalCharge) <= 1e-4f
            && MathF.Abs(first.FinalFootstepFeedback - second.FinalFootstepFeedback) <= 1e-4f
            && MathF.Abs(first.FinalLandingFeedback - second.FinalLandingFeedback) <= 1e-4f
            && MathF.Abs(first.FinalDamageFeedback - second.FinalDamageFeedback) <= 1e-4f;
        Require(deterministic, "same seed/input trace produced different final mobility state", failures);

        var details = new List<string>
        {
            $"run {first.BaselinePeak:0.0}  sprint {first.SprintPeak:0.0}  tactical {first.TacticalPeak:0.0} units/s",
            $"tactical duration {first.TacticalDurationTicks * Dt:0.000}s  final charge {first.FinalTacticalCharge:0.000}s",
            $"geometry clear {first.ClearOfGeometry}  finite {first.Finite}  final state {first.FinalSprintState}",
            $"feedback landing {first.LandingFeedbackTriggered}  visual response {first.FeedbackChangesPresentation}  aim preserved {first.FeedbackPreservesAim}",
        };
        foreach (string failure in failures) details.Add($"FAIL: {failure}");

        return new MovementVerificationResult(failures.Count == 0, deterministic, details, 19);
    }

    private static MobilityTrace RunTrace(ulong seed)
    {
        var sim = new Simulation(0, BotSkill.Normal, seed);
        Player player = sim.Player;
        PrepareSandbox(player);
        var trace = new MobilityTrace { WeaponMoveScale = player.Current.Def.MoveScale };

        // Settle onto the known-clear lane, then reach ordinary weapon-adjusted run speed.
        Step(sim, player, InputFrame.Neutral, trace);
        for (int i = 0; i < 100; i++)
        {
            Step(sim, player, Forward(), trace);
            trace.BaselinePeak = MathF.Max(trace.BaselinePeak, player.HorizontalSpeed);
        }

        // Source semantics: a press edge while forward and grounded starts normal sprint.
        Step(sim, player, Forward(sprintPressed: true), trace);
        trace.EnteredSprint = player.SprintState == PlayerSprintState.Sprinting;
        for (int i = 0; i < 120; i++)
        {
            Step(sim, player, Forward(), trace);
            trace.SprintPeak = MathF.Max(trace.SprintPeak, player.HorizontalSpeed);
        }

        // The second press edge enters tactical sprint. Count the initiating tick
        // so the measured charge window is exactly the configured duration.
        Step(sim, player, Forward(sprintPressed: true), trace);
        trace.EnteredTacticalSprint = player.SprintState == PlayerSprintState.TacticalSprinting;
        trace.TacticalDurationTicks = trace.EnteredTacticalSprint ? 1 : 0;
        trace.TacticalPeak = MathF.Max(trace.TacticalPeak, player.HorizontalSpeed);
        while (player.SprintState == PlayerSprintState.TacticalSprinting && trace.TacticalDurationTicks < 240)
        {
            Step(sim, player, Forward(), trace);
            trace.TacticalDurationTicks++;
            trace.TacticalPeak = MathF.Max(trace.TacticalPeak, player.HorizontalSpeed);
        }
        trace.TacticalExpiredToSprint = player.SprintState == PlayerSprintState.Sprinting;

        float chargeAfterTactical = player.TacticalSprintCharge;
        Step(sim, player, InputFrame.Neutral, trace);
        trace.CanceledWithoutForward = player.SprintState == PlayerSprintState.None;
        for (int i = 0; i < 75; i++) Step(sim, player, InputFrame.Neutral, trace);
        trace.TacticalChargeRecovered = player.TacticalSprintCharge > chargeAfterTactical + 0.5f;

        // The mapped C action is a safe toggle extension over HGO's existing
        // Ctrl-held crouch. Verify both toggling and its sprint cancellation.
        Step(sim, player, Forward(crouchTogglePressed: true), trace);
        for (int i = 0; i < 10; i++) Step(sim, player, Forward(), trace);
        bool toggledDown = player.CrouchToggled && player.CrouchAmount > 0.95f;
        Step(sim, player, Forward(crouchTogglePressed: true), trace);
        for (int i = 0; i < 10; i++) Step(sim, player, Forward(), trace);
        trace.CrouchToggleWorked = toggledDown && !player.CrouchToggled && player.CrouchAmount < 0.05f;

        Step(sim, player, Forward(sprintPressed: true), trace);
        bool wasSprinting = player.SprintState == PlayerSprintState.Sprinting;
        Step(sim, player, Forward(crouchHeld: true), trace);
        trace.CrouchCanceledSprint = wasSprinting && player.SprintState == PlayerSprintState.None;
        for (int i = 0; i < 10; i++) Step(sim, player, Forward(), trace);

        Step(sim, player, Forward(sprintPressed: true), trace);
        bool sprintBeforeJump = player.SprintState == PlayerSprintState.Sprinting;
        Step(sim, player, Forward(jump: true), trace);
        float launchVelocity = player.Velocity.Y;
        trace.AirborneCanceledSprint = sprintBeforeJump && !player.OnGround
            && player.SprintState == PlayerSprintState.None && launchVelocity > 0f;
        Step(sim, player, Forward(jump: true), trace);
        trace.NoDoubleJump = player.Velocity.Y < launchVelocity;

        // Let the real mover settle the preceding jump. The state then has to
        // emit its own landing envelope; no renderer or test-only physics path is
        // allowed to manufacture that event.
        for (int i = 0; i < 120 && !player.OnGround; i++) Step(sim, player, InputFrame.Neutral, trace);
        trace.LandingFeedbackTriggered = player.LandingFeedback > 0f;

        // This directly exercises the renderer boundary: feedback may alter FOV
        // and the viewmodel, but its target direction and Player.AimDirection must
        // remain identical to the deterministic combat direction.
        player.ResetPresentationFeedback();
        player.TriggerFootstepFeedback(1f);
        player.TriggerDamageFeedback(0.75f, player.Yaw + 90f);
        Vector3 aimBefore = player.AimDirection;
        var feedbackOff = Renderer.BuildCamera(sim, 0f);
        var feedbackOn = Renderer.BuildCamera(sim, 1f);
        Vector3 offDirection = Vector3.Normalize(feedbackOff.Target - feedbackOff.Position);
        Vector3 onDirection = Vector3.Normalize(feedbackOn.Target - feedbackOn.Position);
        trace.FeedbackChangesPresentation = MathF.Abs(feedbackOff.FovY - feedbackOn.FovY) > 1e-4f;
        trace.FeedbackPreservesAim = Vector3.Dot(onDirection, aimBefore) > 0.99999f
            && Vector3.Dot(offDirection, aimBefore) > 0.99999f
            && NearlyEqual(aimBefore, player.AimDirection);

        trace.FinalPosition = player.Position;
        trace.FinalVelocity = player.Velocity;
        trace.FinalSprintState = player.SprintState;
        trace.FinalTacticalCharge = player.TacticalSprintCharge;
        trace.FinalFootstepFeedback = player.FootstepFeedback;
        trace.FinalLandingFeedback = player.LandingFeedback;
        trace.FinalDamageFeedback = player.DamageFeedback;
        return trace;
    }

    private static void PrepareSandbox(Player player)
    {
        // Industrial Zone's centre service road is a measured clear lane at
        // z=230, x in [-600, 600]. It lets the trace turn around without
        // introducing a test-only collision exception or crossing a GLB
        // structure footprint.
        player.Position = new Vector3(0f, 0f, 230f);
        player.Velocity = Vector3.Zero;
        player.Yaw = 90f;
        player.Pitch = 0f;
        player.OnGround = true;
        player.CrouchAmount = 0f;
        player.Alive = true;
        player.ZoomHeld = false;
        player.ResetPresentationFeedback();
        player.ResetMovementState();
    }

    private static void Step(Simulation sim, Player player, InputFrame input, MobilityTrace trace)
    {
        // Fold the trace back before either map edge so it can observe a full
        // tactical interval without hiding collision failures behind a teleport.
        if (player.Position.X < -560f) player.Yaw = -90f;
        else if (player.Position.X > 560f) player.Yaw = 90f;

        sim.Update(input, Dt);
        trace.ClearOfGeometry &= !sim.Map.BoxBlocked(player.Bounds);
        trace.Finite &= float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Y) && float.IsFinite(player.Position.Z)
            && float.IsFinite(player.Velocity.X) && float.IsFinite(player.Velocity.Y) && float.IsFinite(player.Velocity.Z);
    }

    private static InputFrame Forward(bool sprintPressed = false, bool jump = false, bool crouchHeld = false,
        bool crouchTogglePressed = false)
        => new()
        {
            Move = Vector2.UnitY,
            Jump = jump,
            Crouch = crouchHeld,
            CrouchTogglePressed = crouchTogglePressed,
            SprintPressed = sprintPressed,
            SwitchSlot = -1,
        };

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private static bool NearlyEqual(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) <= 1e-4f;

    private sealed class MobilityTrace
    {
        public bool ClearOfGeometry = true;
        public bool Finite = true;
        public float WeaponMoveScale;
        public float BaselinePeak;
        public bool EnteredSprint;
        public float SprintPeak;
        public bool EnteredTacticalSprint;
        public float TacticalPeak;
        public int TacticalDurationTicks;
        public bool TacticalExpiredToSprint;
        public bool CanceledWithoutForward;
        public bool TacticalChargeRecovered;
        public bool CrouchToggleWorked;
        public bool CrouchCanceledSprint;
        public bool AirborneCanceledSprint;
        public bool NoDoubleJump;
        public bool LandingFeedbackTriggered;
        public bool FeedbackChangesPresentation;
        public bool FeedbackPreservesAim;
        public Vector3 FinalPosition;
        public Vector3 FinalVelocity;
        public PlayerSprintState FinalSprintState;
        public float FinalTacticalCharge;
        public float FinalFootstepFeedback;
        public float FinalLandingFeedback;
        public float FinalDamageFeedback;
    }
}

public readonly record struct MovementVerificationResult(
    bool Passed,
    bool Deterministic,
    IReadOnlyList<string> Details,
    int CheckCount);
