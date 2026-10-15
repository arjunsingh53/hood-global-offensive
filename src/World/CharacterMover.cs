using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;

namespace HoodGlobalOffensive.World;

/// <summary>
/// Quake-derived character physics: ground friction, run/air acceleration with a
/// speed cap (which is what makes strafe-jumping work), gravity, jumping and
/// swept axis-by-axis collision with automatic step-up onto low ledges.
/// </summary>
public static class CharacterMover
{
    private const float SubStep = 6f;

    public static void Move(GameMap map, Actor actor, Vector3 wishDir, float wishSpeed,
        bool jump, bool crouchHeld, bool walk, float dt, float groundAccelerationMultiplier = 1f)
    {
        Vector3 vel = actor.Velocity;

        // ------------------------------------------------------------ crouch blend
        bool wantCrouch = crouchHeld;
        float targetCrouch = wantCrouch ? 1f : 0f;
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

        actor.CrouchAmount = U.MoveTowards(actor.CrouchAmount, targetCrouch, Cfg.CrouchBlendSpeed * dt);
        _ = previousCrouch;

        float speedCap = wishSpeed;
        if (actor.CrouchAmount > 0.5f) speedCap = MathF.Min(speedCap, Cfg.CrouchSpeed);
        if (walk) speedCap = MathF.Min(speedCap, Cfg.WalkSpeed);

        // ------------------------------------------------------------ ground / air acceleration
        if (actor.OnGround)
        {
            ApplyFriction(ref vel, dt);
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

    private static void ApplyFriction(ref Vector3 vel, float dt)
    {
        float speed = U.FlatLength(vel);
        if (speed < 1e-4f)
        {
            vel.X = 0f;
            vel.Z = 0f;
            return;
        }

        float control = speed < Cfg.StopSpeed ? Cfg.StopSpeed : speed;
        float drop = control * Cfg.Friction * dt;
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

    /// <summary>Air acceleration is capped for the wish direction, enabling strafe-jumping.</summary>
    private static void AirAccelerate(ref Vector3 vel, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishSpeed <= 0f) return;
        float capped = MathF.Min(wishSpeed, Cfg.AirSpeedCap);
        float currentSpeed = Vector3.Dot(vel, wishDir);
        float addSpeed = capped - currentSpeed;
        if (addSpeed <= 0f) return;

        float accelSpeed = accel * wishSpeed * dt;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        vel += wishDir * accelSpeed;
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
