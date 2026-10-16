using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;

namespace HoodGlobalOffensive.World;

/// <summary>
/// Fully collision-validated route for a controlled player mantle.  The route
/// never represents a ballistic jump: it lifts vertically, crosses above the
/// selected low surface, then settles on its top.
/// </summary>
public readonly record struct MantlePlan(Vector3 Start, Vector3 Lift, Vector3 Cross, Vector3 Landing)
{
    /// <summary>Samples the three route segments with a deliberately even 0.6s cadence.</summary>
    public Vector3 PositionAt(float progress)
    {
        progress = U.Clamp01(progress);
        if (progress < 0.34f) return U.Lerp(Start, Lift, progress / 0.34f);
        if (progress < 0.72f) return U.Lerp(Lift, Cross, (progress - 0.34f) / 0.38f);
        return U.Lerp(Cross, Landing, (progress - 0.72f) / 0.28f);
    }
}

/// <summary>
/// Grounded tactical character physics: controlled acceleration/deceleration,
/// bounded air steering, gravity, jumping and swept axis-by-axis collision with
/// automatic step-up onto low ledges.  Air steering deliberately preserves, but
/// never creates, horizontal speed so the player motor cannot acquire a
/// Quake-style bunny-hop/strafe-jump exploit.
/// </summary>
public static class CharacterMover
{
    private const float SubStep = 6f;

    public static void Move(GameMap map, Actor actor, Vector3 wishDir, float wishSpeed,
        bool jump, bool crouchHeld, bool walk, float dt, float groundAccelerationMultiplier = 1f,
        float groundFrictionMultiplier = 1f, float stanceTarget = -1f)
    {
        Vector3 vel = actor.Velocity;

        // ------------------------------------------------------------ crouch blend
        float targetCrouch = stanceTarget >= 0f ? stanceTarget : crouchHeld ? 1f : 0f;
        targetCrouch = MathF.Max(0f, targetCrouch);
        float previousCrouch = actor.CrouchAmount;

        if (targetCrouch < actor.CrouchAmount)
        {
            // Only stand up if there is room above.
            float standHeight = U.Lerp(Cfg.StandHeight, Cfg.CrouchHeight, targetCrouch);
            float probeHeight = U.Lerp(actor.CrouchAmount, targetCrouch, MathF.Min(1f, Cfg.CrouchBlendSpeed * dt * 3f));
            var probe = Aabb.Footprint(actor.Position, Cfg.HalfWidth, U.Lerp(Cfg.StandHeight, Cfg.CrouchHeight, probeHeight));
            if (map.BoxBlocked(probe))
            {
                targetCrouch = actor.CrouchAmount;
                _ = standHeight;
            }
        }

        float stanceBlendSpeed = targetCrouch > 1f || actor.CrouchAmount > 1f
            ? Cfg.ProneBlendSpeed
            : Cfg.CrouchBlendSpeed;
        actor.CrouchAmount = U.MoveTowards(actor.CrouchAmount, targetCrouch, stanceBlendSpeed * dt);
        _ = previousCrouch;

        float speedCap = wishSpeed;
        if (actor is Player { IsProne: true }) speedCap = MathF.Min(speedCap, Cfg.ProneSpeed);
        else if (actor.CrouchAmount > 0.5f) speedCap = MathF.Min(speedCap, Cfg.CrouchSpeed);
        if (walk) speedCap = MathF.Min(speedCap, Cfg.WalkSpeed);

        // ------------------------------------------------------------ ground / air acceleration
        if (actor.OnGround)
        {
            ApplyFriction(ref vel, dt, groundFrictionMultiplier, speedCap);
            float groundAcceleration = Cfg.Accelerate * MathF.Max(0f, groundAccelerationMultiplier);
            Accelerate(ref vel, wishDir, speedCap, groundAcceleration, dt);
        }
        else
        {
            AirAccelerate(ref vel, wishDir, speedCap, Cfg.AirAccelerate, dt);
        }

        // ------------------------------------------------------------ jump
        if (jump && actor.OnGround)
        {
            vel.Y = Cfg.JumpVelocity;
            actor.OnGround = false;
        }

        // ------------------------------------------------------------ gravity
        vel.Y -= Cfg.Gravity * dt;
        if (vel.Y < -Cfg.MaxFallSpeed) vel.Y = -Cfg.MaxFallSpeed;

        actor.Velocity = vel;

        // ------------------------------------------------------------ integrate with collision
        bool wasOnGround = actor.OnGround;
        MoveAxis(map, actor, 0, vel.X * dt, out bool hitX);
        MoveAxis(map, actor, 2, vel.Z * dt, out bool hitZ);

        if (hitX) actor.Velocity = new Vector3(0f, actor.Velocity.Y, actor.Velocity.Z);
        if (hitZ) actor.Velocity = new Vector3(actor.Velocity.X, actor.Velocity.Y, 0f);

        actor.OnGround = false;
        MoveAxis(map, actor, 1, actor.Velocity.Y * dt, out bool hitY);
        if (hitY)
        {
            actor.OnGround = actor.Velocity.Y <= 0f;
            actor.Velocity = new Vector3(actor.Velocity.X, 0f, actor.Velocity.Z);
        }

        // Walking down a step should not launch the character into the air.
        if (wasOnGround && !hitY && actor.Velocity.Y <= 0f)
        {
            SnapToGround(map, actor);
        }

        Unstick(map, actor);
    }

    /// <summary>
    /// Returns whether an actor can occupy a requested continuous stance at its
    /// current feet position.  It is used before leaving prone so a low ceiling
    /// cannot force a stand-up clip or an automatic displacement.
    /// </summary>
    public static bool CanFitStance(GameMap map, Actor actor, float stanceAmount)
    {
        float height = U.Lerp(Cfg.StandHeight, Cfg.CrouchHeight, MathF.Max(0f, stanceAmount));
        return !map.BoxBlocked(Aabb.Footprint(actor.Position, Cfg.HalfWidth, height));
    }

    /// <summary>
    /// Finds one local, ground-attached piece of low static geometry directly in
    /// front of the actor and constructs a three-segment mantle route.  Every
    /// segment is sampled against the production collision map before the route
    /// can be started, so a mantle cannot be used to move through a wall or
    /// bypass a broad building roof.
    /// </summary>
    public static bool TryBuildMantlePlan(GameMap map, Actor actor, Vector3 forward, out MantlePlan plan)
    {
        plan = default;
        forward = U.FlatNormalized(forward);
        if (!actor.OnGround || forward == Vector3.Zero || actor.CrouchAmount > 0.05f) return false;
        if (map.BoxBlocked(Aabb.Footprint(actor.Position, Cfg.HalfWidth, Cfg.StandHeight))) return false;

        Vector3 probe = actor.Position + Vector3.UnitY * 6f;
        float bestDistance = float.MaxValue;
        Brush? candidate = null;
        float candidateEntry = 0f;

        foreach (Brush brush in map.Brushes)
        {
            // Floors are support, not a mantle target. The invisible rim wall is
            // deliberately excluded so this system cannot create an OOB bypass.
            if (brush.Kind is SurfaceKind.Floor or SurfaceKind.Wall) continue;

            float rise = brush.Box.Max.Y - actor.Position.Y;
            if (rise < Cfg.MantleMinHeight || rise > Cfg.MantleMaxHeight) continue;
            if (brush.Box.Min.Y > actor.Position.Y + 1f) continue;
            if (!brush.Box.Raycast(probe, forward, Cfg.MantleForwardReach, out float entry, out Vector3 normal)) continue;
            if (entry < Cfg.HalfWidth - 1f || entry >= bestDistance) continue;
            if (Vector3.Dot(normal, forward) > -0.35f) continue;

            if (!TryMeasureSurfaceDepth(brush.Box, probe, forward, entry, out float depth)) continue;
            if (depth > Cfg.MantleMaxSurfaceDepth) continue;

            bestDistance = entry;
            candidate = brush;
            candidateEntry = entry;
        }

        if (candidate is null) return false;

        Aabb box = candidate.Value.Box;
        if (!TryMeasureSurfaceDepth(box, probe, forward, candidateEntry, out float surfaceDepth)) return false;

        // Place the actor securely on the target's top rather than beyond it:
        // this makes the destination a real floor and prevents a vault through
        // the far face of narrow collision brushes.
        float inset = MathF.Min(Cfg.MantleLandingInset, MathF.Max(5f, surfaceDepth * 0.5f));
        float landingDistance = candidateEntry + inset;
        Vector3 landing = actor.Position + forward * landingDistance;
        landing.Y = box.Max.Y;

        if (!map.FindFloor(landing + Vector3.UnitY, 2f, 4f, out float floorY)) return false;
        if (MathF.Abs(floorY - box.Max.Y) > 1.5f) return false;
        landing.Y = floorY;

        float safeY = floorY + Cfg.MantleSurfaceClearance;
        Vector3 lift = new(actor.Position.X, safeY, actor.Position.Z);
        Vector3 cross = new(landing.X, safeY, landing.Z);
        var candidatePlan = new MantlePlan(actor.Position, lift, cross, landing);

        if (!RouteClear(map, candidatePlan.Start, candidatePlan.Lift)) return false;
        if (!RouteClear(map, candidatePlan.Lift, candidatePlan.Cross)) return false;
        if (!RouteClear(map, candidatePlan.Cross, candidatePlan.Landing)) return false;

        plan = candidatePlan;
        return true;
    }

    /// <summary>
    /// Advances one already validated mantle route. The collision sampling is
    /// repeated during transit as a final guard against an accidental invalid
    /// route or a future dynamic map implementation. No partial blocked move is
    /// applied, which is safer than resolving it through a solid face.
    /// </summary>
    public static bool TryTraverseMantle(GameMap map, Actor actor, Vector3 target)
    {
        Vector3 start = actor.Position;
        float distance = Vector3.Distance(start, target);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / Cfg.MantleCollisionSubstep));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = U.Lerp(start, target, i / (float)steps);
            if (map.BoxBlocked(Aabb.Footprint(sample, Cfg.HalfWidth, Cfg.StandHeight))) return false;
        }

        actor.Position = target;
        actor.Velocity = Vector3.Zero;
        return true;
    }

    private static bool TryMeasureSurfaceDepth(in Aabb box, Vector3 probe, Vector3 forward, float entry,
        out float depth)
    {
        depth = 0f;
        bool seenSurface = false;
        const float step = 2f;
        for (float distance = entry + 0.05f; distance <= entry + Cfg.MantleMaxSurfaceDepth + step; distance += step)
        {
            Vector3 point = probe + forward * distance;
            bool inside = point.X >= box.Min.X && point.X <= box.Max.X
                && point.Z >= box.Min.Z && point.Z <= box.Max.Z;
            if (inside)
            {
                seenSurface = true;
                depth = distance - entry;
                continue;
            }

            if (seenSurface) return true;
        }

        // No far edge within the local-cover budget means it is a broad wall or
        // building, not a safely mantleable piece of cover.
        return false;
    }

    private static bool RouteClear(GameMap map, Vector3 from, Vector3 to)
    {
        float distance = Vector3.Distance(from, to);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / Cfg.MantleCollisionSubstep));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 sample = U.Lerp(from, to, i / (float)steps);
            if (map.BoxBlocked(Aabb.Footprint(sample, Cfg.HalfWidth, Cfg.StandHeight))) return false;
        }

        return true;
    }

    private static void ApplyFriction(ref Vector3 vel, float dt, float multiplier, float speedCap)
    {
        float speed = U.FlatLength(vel);
        if (speed < 1e-4f)
        {
            vel.X = 0f;
            vel.Z = 0f;
            return;
        }

        // A low stance may intentionally cap below the legacy StopSpeed.  Using
        // that larger floor would erase all of its per-tick acceleration before
        // it can build up, leaving prone to stutter at a few units per second.
        // Keep the established StopSpeed for normal/slide movement while using
        // the lower requested cap as the friction floor for a true low stance.
        float lowSpeedControl = speedCap > 1e-4f
            ? MathF.Min(Cfg.StopSpeed, speedCap)
            : Cfg.StopSpeed;
        float control = speed < lowSpeedControl ? lowSpeedControl : speed;
        float drop = control * Cfg.Friction * MathF.Max(0f, multiplier) * dt;
        float newSpeed = MathF.Max(0f, speed - drop);
        float scale = newSpeed / speed;
        vel.X *= scale;
        vel.Z *= scale;
    }

    private static void Accelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishSpeed <= 0f) return;
        float currentSpeed = Vector3.Dot(vel, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0f) return;

        float accelSpeed = accel * dt * wishSpeed;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        vel += wishDir * accelSpeed;
    }

    /// <summary>
    /// Bounded tactical air steering. A jump retains earned ground momentum and
    /// can bend toward a direction, but the flat speed is clamped to the larger
    /// of its take-off speed and the small standstill air-control allowance.
    /// This makes a stationary jump responsive without making repeated strafes
    /// an alternate acceleration system.
    /// </summary>
    private static void AirAccelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishSpeed <= 0f || wishDir == Vector3.Zero) return;

        float startingSpeed = U.FlatLength(vel);
        float steerTarget = MathF.Min(wishSpeed, Cfg.AirControlSpeed);
        float currentSpeed = Vector3.Dot(vel, wishDir);
        float addSpeed = steerTarget - currentSpeed;
        if (addSpeed <= 0f) return;

        float accelSpeed = accel * steerTarget * dt;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        vel += wishDir * accelSpeed;

        float maxAirSpeed = MathF.Max(startingSpeed, steerTarget);
        float resultingSpeed = U.FlatLength(vel);
        if (resultingSpeed > maxAirSpeed && resultingSpeed > 1e-4f)
        {
            float scale = maxAirSpeed / resultingSpeed;
            vel.X *= scale;
            vel.Z *= scale;
        }
    }

    /// <summary>
    /// Move along one axis, resolving penetration with a bisection so fast movers
    /// cannot tunnel. A blocked horizontal move retries from a raised position so
    /// the character climbs low ledges and stairs.
    /// </summary>
    private static void MoveAxis(GameMap map, Actor actor, int axis, float amount, out bool hit)
    {
        hit = false;
        if (MathF.Abs(amount) < 1e-5f) return;

        Vector3 dir = axis switch
        {
            0 => new Vector3(1f, 0f, 0f),
            1 => new Vector3(0f, 1f, 0f),
            _ => new Vector3(0f, 0f, 1f),
        };

        float remaining = amount;
        int guard = 0;
        while (MathF.Abs(remaining) > 1e-4f && guard++ < 32)
        {
            float step = U.Clamp(remaining, -SubStep, SubStep);
            Vector3 candidate = actor.Position + dir * step;
            Aabb box = Aabb.Footprint(candidate, Cfg.HalfWidth, actor.HeadHeight);

            if (!map.BoxBlocked(box))
            {
                actor.Position = candidate;
                remaining -= step;
                continue;
            }

            // Find the furthest safe offset along this step.
            float lo = 0f, hi = step;
            for (int i = 0; i < 7; i++)
            {
                float mid = (lo + hi) * 0.5f;
                Aabb probe = Aabb.Footprint(actor.Position + dir * mid, Cfg.HalfWidth, actor.HeadHeight);
                if (map.BoxBlocked(probe)) hi = mid;
                else lo = mid;
            }

            Vector3 resolved = actor.Position + dir * lo;
            Vector3 steppedPos = resolved;
            bool stepped = axis != 1 && TryStepUp(map, actor, dir, step, resolved, out steppedPos);
            actor.Position = stepped ? steppedPos : resolved;

            hit = !stepped;
            remaining = 0f;
        }
    }

    /// <summary>Attempts to climb a ledge by moving from a raised position and settling back down.</summary>
    private static bool TryStepUp(GameMap map, Actor actor, Vector3 dir, float step, Vector3 resolved, out Vector3 result)
    {
        result = resolved;

        Vector3 raised = actor.Position + new Vector3(0f, Cfg.StepHeight, 0f);
        if (map.BoxBlocked(Aabb.Footprint(raised, Cfg.HalfWidth, actor.HeadHeight))) return false;

        Vector3 forward = raised + dir * step;
        if (map.BoxBlocked(Aabb.Footprint(forward, Cfg.HalfWidth, actor.HeadHeight))) return false;

        if (!map.FindFloor(forward + new Vector3(0f, 0.5f, 0f), 1f, Cfg.StepHeight + 2f, out float floorY)) return false;
        if (floorY <= actor.Position.Y + 0.35f) return false;
        if (floorY > actor.Position.Y + Cfg.StepHeight) return false;

        Vector3 landing = new(forward.X, floorY, forward.Z);
        if (map.BoxBlocked(Aabb.Footprint(landing, Cfg.HalfWidth, actor.HeadHeight))) return false;

        result = landing;
        return true;
    }

    private static void SnapToGround(GameMap map, Actor actor)
    {
        if (!map.FindFloor(actor.Position + new Vector3(0f, 0.5f, 0f), 0.5f, Cfg.GroundSnap, out float floorY)) return;
        if (actor.Position.Y - floorY > Cfg.GroundSnap) return;
        actor.Position = new Vector3(actor.Position.X, floorY, actor.Position.Z);
        actor.OnGround = true;
    }

    /// <summary>Emergency rescue if an actor ends up embedded in geometry.</summary>
    private static void Unstick(GameMap map, Actor actor)
    {
        if (!map.BoxBlocked(actor.Bounds)) return;

        for (int i = 1; i <= 40; i++)
        {
            Vector3 lifted = actor.Position + new Vector3(0f, i * 4f, 0f);
            if (!map.BoxBlocked(Aabb.Footprint(lifted, Cfg.HalfWidth, actor.HeadHeight)))
            {
                actor.Position = lifted;
                actor.Velocity = new Vector3(actor.Velocity.X, 0f, actor.Velocity.Z);
                return;
            }
        }

        // Nothing vertically; try to escape horizontally.
        var offsets = new[]
        {
            new Vector3(8f, 0f, 0f), new Vector3(-8f, 0f, 0f),
            new Vector3(0f, 0f, 8f), new Vector3(0f, 0f, -8f),
        };
        foreach (Vector3 o in offsets)
        {
            Vector3 shifted = actor.Position + o;
            if (!map.BoxBlocked(Aabb.Footprint(shifted, Cfg.HalfWidth, actor.HeadHeight)))
            {
                actor.Position = shifted;
                return;
            }
        }

        // Give up and place the actor at a known spawn to guarantee progress.
        actor.Position = map.Spawns[0].Position;
    }
}
