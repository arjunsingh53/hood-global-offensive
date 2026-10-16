using System;
using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;

namespace HoodGlobalOffensive.World
{
    /// <summary>One of the player's two hooks (Q and E).</summary>
    public sealed class GrappleHook
    {
        public bool Attached;
        public Vector3 Anchor;
        /// <summary>Distance recorded at attach time. Beyond it the rope goes taut.</summary>
        public float RopeLength;
        /// <summary>One-frame presentation flag consumed by the renderer.</summary>
        public bool JustAttached;

        public void Detach()
        {
            Attached = false;
            Anchor = default;
            RopeLength = 0f;
        }
    }

    /// <summary>
    /// Redmatch-style dual grappling hooks.
    ///
    /// The rope behaves like a distance-proportional spring: the farther the
    /// anchor, the harder it pulls, which is exactly the reference community's
    /// rule of thumb ("farther away = more speed in all directions, higher up =
    /// more upward speed"). Once the rope is longer than its attach length it goes
    /// taut through a pendulum-style constraint that removes only the outward
    /// radial velocity, so swinging conserves momentum while two hooks on opposite
    /// points spin the player into a slingshot.
    ///
    /// Releasing a hook never touches the velocity: every unit of speed earned by
    /// the swing is kept, which is what makes grapple chains and wall boosts work.
    /// </summary>
    public static class GrappleSystem
    {
        /// <summary>
        /// Shoots a hook from the eye along the aim ray. Returns false when the ray
        /// misses world geometry inside the grapple range.
        /// </summary>
        public static bool TryAttach(GameMap map, Actor actor, GrappleHook hook)
        {
            hook.Detach();
            hook.JustAttached = false;

            Vector3 origin = actor.EyePosition;
            Vector3 dir = actor.AimDirection;
            if (!map.RayHit(origin, dir, Cfg.GrappleRange, out float t, out Vector3 point,
                out _, out _))
            {
                return false;
            }

            hook.Attached = true;
            hook.Anchor = point;
            hook.RopeLength = MathF.Max(Cfg.GrappleMinLength, t);
            hook.JustAttached = true;
            return true;
        }

        /// <summary>
        /// Applies this hook's physics to the actor's velocity. Returns false when
        /// the hook must detach this tick (too close, blocked line, or already off).
        /// </summary>
        public static bool Tick(GameMap map, Actor actor, GrappleHook hook, float dt)
        {
            if (!hook.Attached) return false;

            Vector3 origin = actor.EyePosition;
            Vector3 toAnchor = hook.Anchor - origin;
            float distance = toAnchor.Length();
            if (distance < Cfg.GrappleMinLength)
            {
                hook.Detach();
                return false;
            }

            Vector3 dir = toAnchor / MathF.Max(distance, 1e-4f);

            // The rope cannot pass through geometry: if the anchor line is blocked,
            // the hook snaps off rather than dragging the player into a wall.
            if (map.RayHit(origin, dir, distance - 2f, out _, out _, out _, out _))
            {
                hook.Detach();
                return false;
            }

            // Distance-proportional pull. This is the "farther = faster" rule.
            float pull = MathF.Min(distance * Cfg.GrapplePullPerUnit, Cfg.GrapplePullMax);
            actor.Velocity += dir * (pull * dt);

            // Taut rope: outside the attach length the only forbidden motion is
            // moving further away. Tangential (swing) speed is fully preserved.
            float radial = Vector3.Dot(actor.Velocity, dir);
            bool taut = distance >= hook.RopeLength - 2f;
            if (taut && radial < 0f)
            {
                actor.Velocity -= dir * radial;
            }

            // Rope-driven speed must not outrun the swept collision solver.
            float speed = actor.Velocity.Length();
            if (speed > Cfg.GrappleMaxRopeSpeed)
            {
                actor.Velocity *= Cfg.GrappleMaxRopeSpeed / speed;
            }

            return true;
        }
    }
}
