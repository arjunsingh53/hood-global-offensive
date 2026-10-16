using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.World;

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
        AdvancedMobilityTrace advancedFirst = RunAdvancedTrace(seed);
        AdvancedMobilityTrace advancedSecond = RunAdvancedTrace(seed);
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
        Require(first.SlideEntered, "sprint crouch edge did not enter a momentum slide", failures);
        Require(first.SlideEnded, "momentum slide did not resolve back to grounded movement", failures);
        Require(first.SlideNoSpeedBoost,
            $"slide peak {first.SlidePeak:0.0} exceeded entry {first.SlideEntrySpeed:0.0}", failures);
        Require(first.IdleSlideDenied, "idle crouch edge incorrectly entered a slide", failures);
        Require(first.AirSlideDenied, "airborne crouch edge incorrectly entered a slide", failures);
        Require(first.SlidePreservedAim, "slide changed the authoritative aim direction", failures);
        Require(first.AirborneCanceledSprint, "take-off did not cancel sprint", failures);
        Require(first.NoDoubleJump, "held jump re-applied upward velocity while airborne", failures);
        Require(first.AirStrafeCannotGainSpeed,
            $"air strafe peak {first.AirStrafePeak:0.0} exceeded launch {first.AirStrafeLaunch:0.0}", failures);
        Require(first.LandingFeedbackTriggered, "a qualifying landing did not start its presentation feedback", failures);
        Require(first.FeedbackChangesPresentation, "active presentation feedback did not change FOV or the viewmodel state", failures);
        Require(first.FeedbackPreservesAim, "presentation feedback changed the visual aim direction or Player.AimDirection", failures);
        Require(advancedFirst.ProneEntered, "Z prone toggle did not enter the low stance", failures);
        Require(advancedFirst.ProneHitboxReduced, "prone did not reduce authoritative head/eye height", failures);
        Require(advancedFirst.ProneSpeedCapped,
            $"prone movement exceeded {Cfg.ProneSpeed:0.0} units/s", failures);
        Require(advancedFirst.ProneCanceledSprint, "entering prone did not cancel sprint", failures);
        Require(advancedFirst.ProneNoSprintWhileAds,
            "prone + ADS accepted a sprint state or exceeded the prone speed cap", failures);
        Require(advancedFirst.ProneBlockedExit,
            "prone stood into a low ceiling instead of retaining the safe low stance", failures);
        Require(advancedFirst.ProneClearExit, "prone did not leave safely in clear space", failures);
        Require(advancedFirst.MantleStarted, "grounded forward jump did not start a valid low-cover mantle", failures);
        Require(advancedFirst.MantleCompleted, "valid mantle route did not resolve on its landing surface", failures);
        Require(advancedFirst.MantleDurationTicks >= (int)MathF.Floor(Cfg.MantleDuration / Dt)
                && advancedFirst.MantleDurationTicks <= (int)MathF.Ceiling(Cfg.MantleDuration / Dt) + 1,
            $"mantle duration was {advancedFirst.MantleDurationTicks * Dt:0.000}s instead of {Cfg.MantleDuration:0.000}s", failures);
        Require(advancedFirst.MantlePathClear, "mantle path entered test geometry", failures);
        Require(advancedFirst.MantlePreservedAim, "mantle changed the authoritative aim direction", failures);
        Require(advancedFirst.MantleFiringBlocked, "mantle allowed a live weapon discharge", failures);
        Require(advancedFirst.MantleBlockedRouteRejected,
            "mantle accepted a route without head clearance", failures);

        bool deterministic = NearlyEqual(first.FinalPosition, second.FinalPosition)
            && NearlyEqual(first.FinalVelocity, second.FinalVelocity)
            && first.FinalSprintState == second.FinalSprintState
            && MathF.Abs(first.FinalTacticalCharge - second.FinalTacticalCharge) <= 1e-4f
            && MathF.Abs(first.SlideEntrySpeed - second.SlideEntrySpeed) <= 1e-4f
            && MathF.Abs(first.SlidePeak - second.SlidePeak) <= 1e-4f
            && first.SlideTicks == second.SlideTicks
            && MathF.Abs(first.FinalFootstepFeedback - second.FinalFootstepFeedback) <= 1e-4f
            && MathF.Abs(first.FinalLandingFeedback - second.FinalLandingFeedback) <= 1e-4f
            && MathF.Abs(first.FinalDamageFeedback - second.FinalDamageFeedback) <= 1e-4f
            && advancedFirst.Matches(advancedSecond);
        Require(deterministic, "same seed/input trace produced different final mobility state", failures);

        var details = new List<string>
        {
            $"run {first.BaselinePeak:0.0}  sprint {first.SprintPeak:0.0}  tactical {first.TacticalPeak:0.0} units/s",
            $"tactical duration {first.TacticalDurationTicks * Dt:0.000}s  final charge {first.FinalTacticalCharge:0.000}s",
            $"slide entry {first.SlideEntrySpeed:0.0}  peak {first.SlidePeak:0.0}  duration {first.SlideTicks * Dt:0.000}s  aim preserved {first.SlidePreservedAim}",
            $"geometry clear {first.ClearOfGeometry}  finite {first.Finite}  final state {first.FinalSprintState}",
            $"air strafe launch {first.AirStrafeLaunch:0.0}  peak {first.AirStrafePeak:0.0}  no speed gain {first.AirStrafeCannotGainSpeed}",
            $"feedback landing {first.LandingFeedbackTriggered}  visual response {first.FeedbackChangesPresentation}  aim preserved {first.FeedbackPreservesAim}",
            $"prone entered {advancedFirst.ProneEntered}  head {advancedFirst.ProneHeadHeight:0.0}  eye {advancedFirst.ProneEyeHeight:0.0}  peak {advancedFirst.PronePeakSpeed:0.0}",
            $"mantle started {advancedFirst.MantleStarted}  completed {advancedFirst.MantleCompleted}  duration {advancedFirst.MantleDurationTicks * Dt:0.000}s  path clear {advancedFirst.MantlePathClear}",
            $"mantle aim preserved {advancedFirst.MantlePreservedAim}  firing blocked {advancedFirst.MantleFiringBlocked}  blocked route rejected {advancedFirst.MantleBlockedRouteRejected}",
        };
        foreach (string failure in failures) details.Add($"FAIL: {failure}");

        return new MovementVerificationResult(failures.Count == 0, deterministic, details, 40);
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

        // A player-only slide starts only from real sprint momentum. It uses the
        // production mover and collision map, so this detects speed boosts,
        // clipping, and accidental idle/air activation rather than a mock state.
        PrepareSandbox(player);
        // Stay well inside the measured service-road lane; the longer generic
        // sprint trace above intentionally reaches its turn condition, but that
        // would corrupt this slide's entry-speed measurement.
        for (int i = 0; i < 40; i++) Step(sim, player, Forward(), trace);
        Step(sim, player, Forward(sprintPressed: true), trace);
        for (int i = 0; i < 24; i++) Step(sim, player, Forward(), trace);
        Vector3 aimBeforeSlide = player.AimDirection;
        trace.SlideEntrySpeed = player.HorizontalSpeed;
        Step(sim, player, Forward(crouchPressed: true), trace);
        trace.SlideEntered = player.IsSliding;
        trace.SlidePeak = player.HorizontalSpeed;
        trace.SlideTicks = player.IsSliding ? 1 : 0;
        while (player.IsSliding && trace.SlideTicks < 120)
        {
            Step(sim, player, Forward(), trace);
            trace.SlideTicks++;
            trace.SlidePeak = MathF.Max(trace.SlidePeak, player.HorizontalSpeed);
        }
        trace.SlideEnded = !player.IsSliding && player.OnGround;
        trace.SlideNoSpeedBoost = trace.SlidePeak <= trace.SlideEntrySpeed + 0.05f;
        trace.SlidePreservedAim = Vector3.Dot(aimBeforeSlide, player.AimDirection) > 0.99999f;

        PrepareSandbox(player);
        Step(sim, player, Forward(crouchPressed: true), trace);
        trace.IdleSlideDenied = !player.IsSliding;

        PrepareSandbox(player);
        for (int i = 0; i < 40; i++) Step(sim, player, Forward(), trace);
        Step(sim, player, Forward(sprintPressed: true), trace);
        for (int i = 0; i < 24; i++) Step(sim, player, Forward(), trace);
        Step(sim, player, Forward(jump: true), trace);
        Step(sim, player, Forward(crouchPressed: true), trace);
        trace.AirSlideDenied = !player.IsSliding && !player.OnGround;

        PrepareSandbox(player);
        Step(sim, player, Forward(sprintPressed: true), trace);
        bool sprintBeforeJump = player.SprintState == PlayerSprintState.Sprinting;
        Step(sim, player, Forward(jump: true), trace);
        float launchVelocity = player.Velocity.Y;
        trace.AirborneCanceledSprint = sprintBeforeJump && !player.OnGround
            && player.SprintState == PlayerSprintState.None && launchVelocity > 0f;
        Step(sim, player, Forward(jump: true), trace);
        trace.NoDoubleJump = player.Velocity.Y < launchVelocity;

        // A tactical shooter permits limited airborne direction correction, not
        // a Quake-style horizontal acceleration exploit. Start with a real run,
        // jump, then hold a perpendicular strafe and assert it cannot exceed
        // the genuine take-off speed.
        PrepareSandbox(player);
        for (int i = 0; i < 40; i++) Step(sim, player, Forward(), trace);
        Step(sim, player, Forward(jump: true), trace);
        trace.AirStrafeLaunch = player.HorizontalSpeed;
        trace.AirStrafePeak = trace.AirStrafeLaunch;
        var strafe = InputFrame.Neutral;
        strafe.Move = Vector2.UnitX;
        for (int i = 0; i < 24 && !player.OnGround; i++)
        {
            Step(sim, player, strafe, trace);
            trace.AirStrafePeak = MathF.Max(trace.AirStrafePeak, player.HorizontalSpeed);
        }
        trace.AirStrafeCannotGainSpeed = trace.AirStrafePeak <= trace.AirStrafeLaunch + 0.25f;

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

    /// <summary>
    /// Runs stance and mantle checks in deliberately tiny isolated collision
    /// lanes. The production GameMap/AABB mover is still used; only its brush
    /// data is replaced locally so a future Industrial Zone art/collision edit
    /// cannot make the behavioral contract untestable.
    /// </summary>
    private static AdvancedMobilityTrace RunAdvancedTrace(ulong seed)
    {
        var trace = new AdvancedMobilityTrace();

        // ---------------------------------------------------------------- prone
        var proneSim = new Simulation(0, BotSkill.Normal, seed ^ 0xC0FFEEUL);
        ConfigureTestMap(proneSim, includeMantleBlock: false, includeMantleCeiling: false,
            includeProneCeiling: false);
        Player prone = proneSim.Player;
        PrepareTestPlayer(prone, new Vector3(-120f, 0f, -80f), 180f);

        var proneToggle = InputFrame.Neutral;
        proneToggle.ProneTogglePressed = true;
        proneSim.Update(proneToggle, Dt);
        for (int i = 0; i < 30; i++) proneSim.Update(InputFrame.Neutral, Dt);
        trace.ProneEntered = prone.IsProne;
        trace.ProneHeadHeight = prone.HeadHeight;
        trace.ProneEyeHeight = prone.EyeHeight;
        trace.ProneHitboxReduced = prone.HeadHeight <= Cfg.ProneHeight + 0.05f
            && prone.EyeHeight <= Cfg.ProneEyeHeight + 0.05f
            && !proneSim.Map.BoxBlocked(prone.Bounds);

        // Hold both forward sprint intent and ADS while prone. ADS remains a
        // legitimate aiming pose, but it may not restore sprint or let movement
        // escape the prone cap.
        var proneForward = Forward(sprintPressed: true, zoomDown: true);
        for (int i = 0; i < 120; i++)
        {
            proneSim.Update(proneForward, Dt);
            trace.PronePeakSpeed = MathF.Max(trace.PronePeakSpeed, prone.HorizontalSpeed);
        }
        trace.ProneCanceledSprint = prone.SprintState == PlayerSprintState.None;
        trace.ProneSpeedCapped = trace.PronePeakSpeed <= Cfg.ProneSpeed + 0.05f;
        trace.ProneNoSprintWhileAds = prone.ZoomHeld
            && prone.SprintState == PlayerSprintState.None
            && trace.PronePeakSpeed <= Cfg.ProneSpeed + 0.05f;

        // The same Z edge restores standing only if the full collision volume
        // fits. This open lane verifies the regular exit path first.
        proneSim.Update(proneToggle, Dt);
        for (int i = 0; i < 36; i++) proneSim.Update(InputFrame.Neutral, Dt);
        trace.ProneClearExit = !prone.IsProne && prone.CrouchAmount < 0.05f
            && !proneSim.Map.BoxBlocked(prone.Bounds);

        // Now bring a prone player under a 40-unit ceiling. Their 34-unit
        // low stance fits, but the 72-unit standing probe must fail cleanly.
        var ceilingSim = new Simulation(0, BotSkill.Normal, seed ^ 0xC0FFEFUL);
        ConfigureTestMap(ceilingSim, includeMantleBlock: false, includeMantleCeiling: false,
            includeProneCeiling: true);
        Player ceilingProne = ceilingSim.Player;
        // Stand just outside the ceiling's footprint, enter prone, then move
        // only far enough for the low collision hull (not a standing hull) to
        // pass beneath it. This avoids depending on long-distance acceleration
        // timing to establish the clearance test.
        PrepareTestPlayer(ceilingProne, new Vector3(0f, 0f, -40f), 180f);
        ceilingSim.Update(proneToggle, Dt);
        for (int i = 0; i < 30; i++) ceilingSim.Update(InputFrame.Neutral, Dt);
        for (int i = 0; i < 20; i++) ceilingSim.Update(Forward(), Dt);
        ceilingSim.Update(proneToggle, Dt);
        trace.ProneBlockedExit = ceilingProne.IsProne
            && ceilingProne.CrouchAmount > 1.5f
            && !ceilingSim.Map.BoxBlocked(ceilingProne.Bounds);

        // --------------------------------------------------------------- mantle
        var mantleSim = new Simulation(0, BotSkill.Normal, seed ^ 0x5A17EUL);
        ConfigureTestMap(mantleSim, includeMantleBlock: true, includeMantleCeiling: false,
            includeProneCeiling: false);
        Player mantle = mantleSim.Player;
        PrepareTestPlayer(mantle, Vector3.Zero, 180f);
        Vector3 aimBeforeMantle = mantle.AimDirection;
        var mantleStart = Forward(jump: true);
        mantleStart.FireDown = true;
        mantleStart.FirePressed = true;
        mantleSim.Update(mantleStart, Dt);
        trace.MantleStarted = mantle.IsMantling;
        trace.MantlePathClear = !mantleSim.Map.BoxBlocked(mantle.Bounds);
        trace.MantleDurationTicks = mantle.IsMantling ? 1 : 0;
        while (mantle.IsMantling && trace.MantleDurationTicks < 120)
        {
            mantleSim.Update(Forward(), Dt);
            trace.MantleDurationTicks++;
            trace.MantlePathClear &= !mantleSim.Map.BoxBlocked(mantle.Bounds);
        }
        trace.MantleCompleted = trace.MantleStarted
            && !mantle.IsMantling
            && mantle.OnGround
            && mantle.Position.Y >= 47.9f
            && mantle.Position.Z > 30f;
        trace.MantlePreservedAim = Vector3.Dot(aimBeforeMantle, mantle.AimDirection) > 0.99999f;
        trace.MantleFiringBlocked = !mantleSim.ShotsFired.ContainsKey(mantle.Id);

        // A ceiling over the planned cross segment must reject before transit;
        // the player falls back to their ordinary grounded jump and never enters
        // a route that would intersect the ceiling.
        var blockedMantleSim = new Simulation(0, BotSkill.Normal, seed ^ 0x5A17FUL);
        ConfigureTestMap(blockedMantleSim, includeMantleBlock: true, includeMantleCeiling: true,
            includeProneCeiling: false);
        Player blockedMantle = blockedMantleSim.Player;
        PrepareTestPlayer(blockedMantle, Vector3.Zero, 180f);
        blockedMantleSim.Update(Forward(jump: true), Dt);
        trace.MantleBlockedRouteRejected = !blockedMantle.IsMantling
            && !blockedMantleSim.Map.BoxBlocked(blockedMantle.Bounds);

        return trace;
    }

    private static void ConfigureTestMap(Simulation sim, bool includeMantleBlock,
        bool includeMantleCeiling, bool includeProneCeiling)
    {
        Brush[] brushes = sim.Map.Brushes;
        for (int i = 0; i < brushes.Length; i++)
        {
            brushes[i] = new Brush
            {
                Box = new Aabb(new Vector3(10_000f, 10_000f, 10_000f), new Vector3(10_001f, 10_001f, 10_001f)),
                Kind = SurfaceKind.Wall,
                Penetrable = false,
            };
        }

        brushes[0] = new Brush
        {
            Box = new Aabb(new Vector3(-600f, -96f, -600f), new Vector3(600f, 0f, 600f)),
            Kind = SurfaceKind.Floor,
            Penetrable = false,
        };

        int next = 1;
        if (includeMantleBlock)
        {
            // 48 units high, 32 units deep: a concrete local-cover analogue.
            brushes[next++] = new Brush
            {
                Box = new Aabb(new Vector3(-48f, 0f, 30f), new Vector3(48f, 48f, 62f)),
                Kind = SurfaceKind.Crate,
                Penetrable = false,
            };
        }

        if (includeMantleCeiling)
        {
            // It starts just beyond the player's standing footprint, yet occupies
            // the mantle's raised cross segment. A valid planner must reject it.
            brushes[next++] = new Brush
            {
                Box = new Aabb(new Vector3(-48f, 60f, 20f), new Vector3(48f, 150f, 76f)),
                Kind = SurfaceKind.Metal,
                Penetrable = false,
            };
        }

        if (includeProneCeiling)
        {
            brushes[next] = new Brush
            {
                Box = new Aabb(new Vector3(-48f, 40f, -24f), new Vector3(48f, 120f, 48f)),
                Kind = SurfaceKind.Metal,
                Penetrable = false,
            };
        }
    }

    private static void PrepareTestPlayer(Player player, Vector3 position, float yaw)
    {
        player.Position = position;
        player.Velocity = Vector3.Zero;
        player.Yaw = yaw;
        player.Pitch = 0f;
        player.OnGround = true;
        player.CrouchAmount = 0f;
        player.Alive = true;
        player.ZoomHeld = false;
        player.ZoomBlend = 0f;
        player.CurrentFov = Cfg.FovDefault;
        player.ResetMovementState();
        player.ResetPresentationFeedback();
        player.ResetViewModelDynamics();
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
        bool crouchTogglePressed = false, bool crouchPressed = false, bool proneTogglePressed = false,
        bool zoomDown = false)
        => new()
        {
            Move = Vector2.UnitY,
            Jump = jump,
            Crouch = crouchHeld,
            CrouchPressed = crouchPressed,
            CrouchTogglePressed = crouchTogglePressed,
            ProneTogglePressed = proneTogglePressed,
            SprintPressed = sprintPressed,
            ZoomDown = zoomDown,
            SwitchSlot = -1,
        };

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private static bool NearlyEqual(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) <= 1e-4f;

    private sealed class AdvancedMobilityTrace
    {
        public bool ProneEntered;
        public bool ProneHitboxReduced;
        public float ProneHeadHeight;
        public float ProneEyeHeight;
        public float PronePeakSpeed;
        public bool ProneCanceledSprint;
        public bool ProneSpeedCapped;
        public bool ProneNoSprintWhileAds;
        public bool ProneClearExit;
        public bool ProneBlockedExit;
        public bool MantleStarted;
        public bool MantleCompleted;
        public int MantleDurationTicks;
        public bool MantlePathClear = true;
        public bool MantlePreservedAim;
        public bool MantleFiringBlocked;
        public bool MantleBlockedRouteRejected;

        public bool Matches(AdvancedMobilityTrace other)
            => ProneEntered == other.ProneEntered
            && ProneHitboxReduced == other.ProneHitboxReduced
            && MathF.Abs(ProneHeadHeight - other.ProneHeadHeight) <= 1e-4f
            && MathF.Abs(ProneEyeHeight - other.ProneEyeHeight) <= 1e-4f
            && MathF.Abs(PronePeakSpeed - other.PronePeakSpeed) <= 1e-4f
            && ProneCanceledSprint == other.ProneCanceledSprint
            && ProneSpeedCapped == other.ProneSpeedCapped
            && ProneNoSprintWhileAds == other.ProneNoSprintWhileAds
            && ProneClearExit == other.ProneClearExit
            && ProneBlockedExit == other.ProneBlockedExit
            && MantleStarted == other.MantleStarted
            && MantleCompleted == other.MantleCompleted
            && MantleDurationTicks == other.MantleDurationTicks
            && MantlePathClear == other.MantlePathClear
            && MantlePreservedAim == other.MantlePreservedAim
            && MantleFiringBlocked == other.MantleFiringBlocked
            && MantleBlockedRouteRejected == other.MantleBlockedRouteRejected;
    }

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
        public bool SlideEntered;
        public bool SlideEnded;
        public bool SlideNoSpeedBoost;
        public bool IdleSlideDenied;
        public bool AirSlideDenied;
        public bool SlidePreservedAim;
        public float SlideEntrySpeed;
        public float SlidePeak;
        public int SlideTicks;
        public bool AirborneCanceledSprint;
        public bool NoDoubleJump;
        public bool AirStrafeCannotGainSpeed;
        public float AirStrafeLaunch;
        public float AirStrafePeak;
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
