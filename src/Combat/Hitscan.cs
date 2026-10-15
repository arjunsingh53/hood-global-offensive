using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Combat;

/// <summary>What a single bullet did.</summary>
public struct ShotResult
{
    public bool HitActor;
    public bool HitWorld;
    public bool ThroughCover;
    public Actor? Victim;
    public HitGroup Group;
    public Vector3 Point;
    public Vector3 Normal;
    public float Damage;
    public bool Killed;
    public int BrushIndex;
}

/// <summary>Turns a ray into damage. Shared by every weapon and by bots.</summary>
public static class Hitscan
{
    /// <summary>Distance to falloff reference; damage scales as (1 - falloff)^(distance / this).</summary>
    private const float FalloffUnit = 1000f;

    public static ShotResult Trace(GameMap map, IReadOnlyList<Actor> actors, Actor shooter,
        Vector3 origin, Vector3 dir, WeaponDef def, ref Rng rng)
    {
        float maxRange = def.Range;

        // ----------------------------------------------------------- world hit
        bool hasWorld = map.RayHit(origin, dir, maxRange, out float worldT, out Vector3 worldPoint,
            out Vector3 worldNormal, out int brushIndex);

        // ----------------------------------------------------------- actor hit
        float actorT = float.MaxValue;
        Actor? victim = null;
        HitGroup group = HitGroup.Generic;
        TestActors(actors, shooter, origin, dir, maxRange, ref actorT, ref victim, ref group);

        if (victim is not null && actorT <= (hasWorld ? worldT : float.MaxValue))
        {
            float damage = DamageAtRange(def, actorT, group);
            return new ShotResult
            {
                HitActor = true,
                HitWorld = false,
                Victim = victim,
                Group = group,
                Point = origin + dir * actorT,
                Normal = -dir,
                Damage = damage,
                BrushIndex = brushIndex,
            };
        }

        if (!hasWorld)
        {
            return new ShotResult
            {
                Point = origin + dir * maxRange,
                Normal = -dir,
                BrushIndex = -1,
            };
        }

        // ----------------------------------------------------------- cover penetration
        if (def.PenetrationPower > 0 && map.Brushes[brushIndex].Penetrable
            && TryPenetrate(map, actors, shooter, origin, dir, def, maxRange, worldT, brushIndex,
                ref rng, out ShotResult pen))
        {
            return pen;
        }

        return new ShotResult
        {
            HitWorld = true,
            Point = worldPoint,
            Normal = worldNormal,
            BrushIndex = brushIndex,
        };
    }

    private static bool TryPenetrate(GameMap map, IReadOnlyList<Actor> actors, Actor shooter,
        Vector3 origin, Vector3 dir, WeaponDef def, float maxRange, float firstWorldT, int firstBrush,
        ref Rng rng, out ShotResult result)
    {
        result = default;

        Vector3 restart = origin + dir * (firstWorldT + 1f);
        float remaining = maxRange - firstWorldT - 1f;
        if (remaining <= 0f) return false;

        bool hasWorld = map.RayHit(restart, dir, remaining, out float worldT,
            out Vector3 worldPoint, out Vector3 worldNormal, out int brushIndex);

        float actorT = float.MaxValue;
        Actor? victim = null;
        HitGroup group = HitGroup.Generic;
        TestActors(actors, shooter, restart, dir, remaining, ref actorT, ref victim, ref group);

        if (victim is null || actorT > (hasWorld ? worldT : float.MaxValue)) return false;

        float totalDistance = firstWorldT + actorT;
        float damage = DamageAtRange(def, totalDistance, group) * def.PenetrationDamageScale;

        result = new ShotResult
        {
            HitActor = true,
            HitWorld = false,
            ThroughCover = true,
            Victim = victim,
            Group = group,
            Point = restart + dir * actorT,
            Normal = -dir,
            Damage = damage,
            BrushIndex = firstBrush,
        };
        _ = brushIndex;
        _ = worldPoint;
        _ = worldNormal;
        return true;
    }

    private static void TestActors(IReadOnlyList<Actor> actors, Actor shooter, Vector3 origin, Vector3 dir,
        float maxRange, ref float bestT, ref Actor? bestVictim, ref HitGroup bestGroup)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            Actor actor = actors[i];
            if (ReferenceEquals(actor, shooter) || !actor.Alive) continue;

            // Cheap reject before the three box tests.
            Vector3 to = actor.Position - origin;
            float along = Vector3.Dot(to, dir);
            if (along < -80f || along > maxRange + 80f) continue;

            Consider(origin, dir, maxRange, actor.HeadBounds, HitGroup.Head, actor, ref bestT, ref bestVictim, ref bestGroup);
            Consider(origin, dir, maxRange, actor.BodyBounds, HitGroup.Chest, actor, ref bestT, ref bestVictim, ref bestGroup);
            Consider(origin, dir, maxRange, actor.LegBounds, HitGroup.Leg, actor, ref bestT, ref bestVictim, ref bestGroup);
        }
    }

    private static void Consider(Vector3 origin, Vector3 dir, float maxRange, in Aabb box, HitGroup group,
        Actor actor, ref float bestT, ref Actor? bestVictim, ref HitGroup bestGroup)
    {
        if (!box.Raycast(origin, dir, maxRange, out float t, out _)) return;
        if (t >= bestT) return;
        bestT = t;
        bestVictim = actor;
        bestGroup = group;
    }

    /// <summary>
    /// Returns post-falloff damage before the target applies armour. Kept public
    /// so the deterministic reach check exercises the same curve as live shots.
    /// </summary>
    public static float DamageAtRange(WeaponDef def, float distance, HitGroup group)
    {
        float damage = def.Damage;
        if (def.Falloff > 0f && distance > 0f)
        {
            damage *= MathF.Pow(1f - def.Falloff, distance / FalloffUnit);
        }

        if (group == HitGroup.Head) damage *= def.HeadshotMultiplier;
        return damage;
    }

    /// <summary>Scatters a direction inside a cone of the given half-angle in degrees.</summary>
    public static Vector3 ApplySpread(Vector3 dir, float spreadDegrees, ref Rng rng)
    {
        if (spreadDegrees <= 0.0001f) return dir;

        float radius = MathF.Tan(spreadDegrees * U.Deg2Rad);
        float angle = rng.Range(0f, MathF.Tau);
        float magnitude = MathF.Sqrt(rng.NextFloat()) * radius;

        Vector3 reference = MathF.Abs(dir.Y) > 0.99f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(dir, reference));
        Vector3 up = Vector3.Cross(right, dir);

        Vector3 offset = right * (MathF.Cos(angle) * magnitude) + up * (MathF.Sin(angle) * magnitude);
        return Vector3.Normalize(dir + offset);
    }
}
