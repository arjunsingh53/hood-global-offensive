using System;
using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Audio
{
    /// <summary>
    /// The spectral presentation selected for a world cue. Raylib's short Sound
    /// voices do not expose a safe real-time low-pass control, so AudioBank maps
    /// these profiles to original pre-rendered variants when it owns the fallback.
    /// </summary>
    public enum SoundSpectrumProfile
    {
        FullRange,
        Distant,
        Occluded,
    }

    /// <summary>Device-independent playback parameters resolved from one simulation sound event.</summary>
    public readonly struct SoundPlayback
    {
        public readonly float Volume;
        public readonly float Pan;
        public readonly float Distance;
        public readonly SoundSpectrumProfile Spectrum;
        public readonly bool Occluded;

        public SoundPlayback(float volume, float pan, float distance, SoundSpectrumProfile spectrum, bool occluded)
        {
            Volume = volume;
            Pan = pan;
            Distance = distance;
            Spectrum = spectrum;
            Occluded = occluded;
        }
    }

    /// <summary>
    /// Pure listener-side mix math. Keeping this separate from the engine makes
    /// scope, attenuation and pan deterministic and testable without an audio
    /// device. Ported mechanically from the raylib reference (src/Audio/AudioMix.cs)
    /// — it carries no engine references, so it compiles inside the engine-free
    /// <c>HGO.Sim</c> assembly and is verified against reference-captured values by
    /// the parity suite. Keep it C# 9 and free of UnityEngine.
    /// </summary>
    public static class AudioMix
    {
        private const float MaxAudibleDistance = 2600f;
        // HGO retains classic 72-unit actor heights; this maps the supplied 25 m
        // long-distance threshold to its established world-unit convention.
        private const float DistantProfileDistance = 1000f;
        private const float OccludedGain = 0.50f; // approximately -6 dB
        private const float DistantGain = 0.82f;

        public static SoundPlayback Resolve(SoundEvent evt, Vector3 listener, float listenerYaw,
            float worldTrauma = 0f)
            => Resolve(evt, listener, listenerYaw, map: null, worldTrauma);

        /// <summary>
        /// Resolves listener routing against the live collision sidecar when one is
        /// available. Occlusion is a real segment/ray test, not a distance guess;
        /// local UI confirmations intentionally bypass it.
        /// </summary>
        public static SoundPlayback Resolve(SoundEvent evt, Vector3 listener, float listenerYaw,
            GameMap? map, float worldTrauma = 0f)
        {
            if (evt.Scope == SoundEventScope.Local)
            {
                return new SoundPlayback(evt.Volume * 1.6f, 0.5f, 0f, SoundSpectrumProfile.FullRange, false);
            }

            Vector3 offset = evt.Position - listener;
            float distance = offset.Length();
            float attenuation = distance <= 1f
                ? 1f
                : U.Clamp01(1f - distance / MaxAudibleDistance);
            attenuation *= attenuation;

            float pan = 0.5f;
            if (distance > 1f)
            {
                // Direction is resolved in listener space, never against a fixed
                // world axis, so stereo remains intuitive as the player turns.
                float side = Vector3.Dot(U.FlatNormalized(offset), U.RightFromYaw(listenerYaw));
                pan = U.Clamp(0.5f + side * 0.42f, 0f, 1f);
            }

            bool occluded = map is not null && IsOccluded(map, listener, evt.Position, distance);
            SoundSpectrumProfile spectrum = occluded
                ? SoundSpectrumProfile.Occluded
                : distance >= DistantProfileDistance
                    ? SoundSpectrumProfile.Distant
                    : SoundSpectrumProfile.FullRange;

            // An occluded sound loses both high-frequency clarity and apparent
            // directionality. The latter is deliberately gentle so players still
            // receive usable tactical information through the Industrial Zone.
            if (spectrum == SoundSpectrumProfile.Occluded)
            {
                attenuation *= OccludedGain;
                pan = U.Lerp(0.5f, pan, 0.58f);
            }
            else if (spectrum == SoundSpectrumProfile.Distant)
            {
                attenuation *= DistantGain;
                pan = U.Lerp(0.5f, pan, 0.78f);
            }

            // Keep critical-health ducking on world cues only; local confirmations
            // and the internal heartbeat remain intelligible during critical health.
            float traumaGain = U.Lerp(1f, Cfg.LowHealthWorldSoundFloor, U.Clamp01(worldTrauma));
            return new SoundPlayback(evt.Volume * attenuation * traumaGain * 1.6f, pan, distance, spectrum, occluded);
        }

        /// <summary>
        /// Reports whether static level geometry falls between source and listener.
        /// The endpoint tolerance prevents an event placed flush on a wall from
        /// self-occluding due to ray precision.
        /// </summary>
        private static bool IsOccluded(GameMap map, Vector3 listener, Vector3 source, float distance)
        {
            if (!float.IsFinite(distance) || distance <= 1.0f) return false;

            Vector3 direction = (source - listener) / distance;
            float rayLength = MathF.Max(0f, distance - 0.75f);
            return rayLength > 0f && map.RayHit(listener, direction, rayLength,
                out _, out _, out _, out int brushIndex) && brushIndex >= 0;
        }
    }
}
