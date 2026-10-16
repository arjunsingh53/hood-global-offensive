using System;
using System.Numerics;

namespace HoodGlobalOffensive.Core
{
    /// <summary>Small math helpers shared by every subsystem.</summary>
    public static class U
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static int ClampInt(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static float Sign(float v) => v < 0f ? -1f : 1f;

        /// <summary>Frame-rate independent exponential approach.</summary>
        public static float Approach(float current, float target, float rate, float dt)
            => target + (current - target) * MathF.Exp(-rate * dt);

        public static Vector3 Approach(Vector3 current, Vector3 target, float rate, float dt)
            => target + (current - target) * MathF.Exp(-rate * dt);

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float d = target - current;
            if (MathF.Abs(d) <= maxDelta) return target;
            return current + MathF.Sign(d) * maxDelta;
        }

        public static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

        public static float FlatLength(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

        public static Vector3 FlatNormalized(Vector3 v)
        {
            float len = FlatLength(v);
            return len < 1e-5f ? Vector3.Zero : new Vector3(v.X / len, 0f, v.Z / len);
        }

        public static Vector3 NormalizeOrZero(Vector3 v)
        {
            float len = v.Length();
            return len < 1e-6f ? Vector3.Zero : v / len;
        }

        /// <summary>
        /// Forward vector for a view angle. Yaw 0 looks down -Z; increasing yaw
        /// turns left, increasing pitch looks up.
        /// </summary>
        public static Vector3 DirFromAngles(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Deg2Rad;
            float pitch = pitchDeg * Deg2Rad;
            float cp = MathF.Cos(pitch);
            return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
        }

        /// <summary>Horizontal right vector for a yaw angle.</summary>
        public static Vector3 RightFromYaw(float yawDeg)
        {
            float yaw = yawDeg * Deg2Rad;
            return new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        }

        /// <summary>Signed shortest difference between two angles in degrees.</summary>
        public static float AngleDelta(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        public static float YawFromDirection(Vector3 dir) => MathF.Atan2(-dir.X, -dir.Z) * Rad2Deg;

        public static float PitchFromDirection(Vector3 dir)
        {
            float flat = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            return MathF.Atan2(dir.Y, flat) * Rad2Deg;
        }

        // ---------------------------------------------------------------- palette
        /// <summary>Opaque ARGB packed colour for the view layer; the simulation never shades.</summary>
        public static uint Argb(int r, int g, int b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

        public static uint Argb(int r, int g, int b, int a) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    /// <summary>Deterministic xorshift RNG so a match can be replayed exactly.</summary>
    public struct Rng
    {
        private ulong _state;

        public Rng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        public ulong NextU64()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return _state;
        }

        /// <summary>Uniform float in [0,1).</summary>
        public float NextFloat() => (NextU64() >> 11) * (1.0f / 9007199254740992.0f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextU64() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(float p) => NextFloat() < p;
    }
}
