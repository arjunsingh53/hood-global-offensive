using System.Numerics;
using Raylib_cs;

namespace HoodGlobalOffensive.World;

/// <summary>Axis-aligned box. Movement, bullets and bot vision all share this primitive.</summary>
public struct Aabb
{
    public Vector3 Min;
    public Vector3 Max;

    public Aabb(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Box for a character whose feet (centre of the base) are at <paramref name="feet"/>.</summary>
    public static Aabb Footprint(Vector3 feet, float halfWidth, float height) => new(
        new Vector3(feet.X - halfWidth, feet.Y, feet.Z - halfWidth),
        new Vector3(feet.X + halfWidth, feet.Y + height, feet.Z + halfWidth));

    public static Aabb FromCenterSize(Vector3 center, Vector3 size) =>
        new(center - size * 0.5f, center + size * 0.5f);

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public float Height => Max.Y - Min.Y;

    public Aabb Translated(Vector3 d) => new(Min + d, Max + d);

    public Aabb Expanded(float e)
    {
        var v = new Vector3(e, e, e);
        return new Aabb(Min - v, Max + v);
    }

    public Aabb Expanded(Vector3 e) => new(Min - e, Max + e);

    public bool Intersects(in Aabb o) =>
        Min.X < o.Max.X && Max.X > o.Min.X &&
        Min.Y < o.Max.Y && Max.Y > o.Min.Y &&
        Min.Z < o.Max.Z && Max.Z > o.Min.Z;

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    public bool OverlapsXz(in Aabb o) =>
        Min.X < o.Max.X && Max.X > o.Min.X &&
        Min.Z < o.Max.Z && Max.Z > o.Min.Z;

    public BoundingBox ToRaylib() => new() { Min = Min, Max = Max };

    public static Aabb Union(in Aabb a, in Aabb b) => new(
        Vector3.Min(a.Min, b.Min),
        Vector3.Max(a.Max, b.Max));

    /// <summary>Squared distance from a point to the nearest surface of the box (0 when inside).</summary>
    public float SquaredDistanceTo(Vector3 p)
    {
        float dx = MathF.Max(MathF.Max(Min.X - p.X, 0f), p.X - Max.X);
        float dy = MathF.Max(MathF.Max(Min.Y - p.Y, 0f), p.Y - Max.Y);
        float dz = MathF.Max(MathF.Max(Min.Z - p.Z, 0f), p.Z - Max.Z);
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>
    /// Slab-method ray/box test. <paramref name="t"/> is the entry distance along the ray
    /// and <paramref name="normal"/> the surface normal at that point.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float t, out Vector3 normal)
    {
        t = 0f;
        normal = Vector3.Zero;

        float tMin = 0f;
        float tMax = maxDist;
        Vector3 hitNormal = Vector3.Zero;

        // X
        if (!Slab(origin.X, dir.X, Min.X, Max.X, ref tMin, ref tMax, new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), ref hitNormal)) return false;
        if (!Slab(origin.Y, dir.Y, Min.Y, Max.Y, ref tMin, ref tMax, new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), ref hitNormal)) return false;
        if (!Slab(origin.Z, dir.Z, Min.Z, Max.Z, ref tMin, ref tMax, new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), ref hitNormal)) return false;

        t = tMin;
        normal = hitNormal;
        return true;
    }

    private static bool Slab(float o, float d, float lo, float hi,
        ref float tMin, ref float tMax, Vector3 negNormal, Vector3 posNormal, ref Vector3 normal)
    {
        if (MathF.Abs(d) < 1e-8f)
        {
            if (o < lo || o > hi) return false;
            return true;
        }

        float inv = 1f / d;
        float t1 = (lo - o) * inv;
        float t2 = (hi - o) * inv;
        Vector3 n = negNormal;

        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
            n = posNormal;
        }

        if (t1 > tMin)
        {
            tMin = t1;
            normal = n;
        }

        if (t2 < tMax) tMax = t2;
        return tMin <= tMax;
    }

    /// <summary>True when the straight segment a-&gt;b enters the box.</summary>
    public bool BlocksSegment(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-5f) return Contains(a);
        return Raycast(a, d / len, len, out _, out _);
    }
}
