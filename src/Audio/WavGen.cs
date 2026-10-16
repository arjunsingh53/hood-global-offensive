using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.Audio;

/// <summary>
/// Builds 16-bit mono PCM WAV files in memory. Every sound effect in the game is
/// synthesised here, so there are no binary assets to ship.
/// </summary>
public static class WavGen
{
    public const int SampleRate = 44100;

    /// <summary>
    /// The listener-facing render of a generated firearm cue.  These are not
    /// gameplay categories: they select an original pre-rendered spectrum when
    /// Raylib's simple Sound API cannot apply a safe per-voice low-pass filter.
    /// </summary>
    public enum GunshotPerspective
    {
        Near,
        Distant,
        Occluded,
    }

    /// <summary>Material families authored by the collision sidecar.</summary>
    public enum FootstepSurface
    {
        Standard,
        Floor,
        Crate,
        Metal,
        Platform,
    }

    private static byte[] Encode(float[] samples)
    {
        int dataBytes = samples.Length * 2;
        var buffer = new byte[44 + dataBytes];

        void WriteAscii(int offset, string s)
        {
            for (int i = 0; i < s.Length; i++) buffer[offset + i] = (byte)s[i];
        }

        WriteAscii(0, "RIFF");
        WriteInt(buffer, 4, 36 + dataBytes);
        WriteAscii(8, "WAVE");
        WriteAscii(12, "fmt ");
        WriteInt(buffer, 16, 16);          // PCM chunk size
        WriteShort(buffer, 20, 1);         // format = PCM
        WriteShort(buffer, 22, 1);         // channels
        WriteInt(buffer, 24, SampleRate);
        WriteInt(buffer, 28, SampleRate * 2);
        WriteShort(buffer, 32, 2);         // block align
        WriteShort(buffer, 34, 16);        // bits per sample
        WriteAscii(36, "data");
        WriteInt(buffer, 40, dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(U.Clamp(samples[i], -1f, 1f) * 32767f);
            buffer[44 + i * 2] = (byte)(value & 0xFF);
            buffer[44 + i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return buffer;
    }

    private static void WriteInt(byte[] b, int offset, int value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
        b[offset + 2] = (byte)((value >> 16) & 0xFF);
        b[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteShort(byte[] b, int offset, int value)
    {
        b[offset] = (byte)(value & 0xFF);
        b[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static float[] Allocate(float seconds) => new float[(int)(seconds * SampleRate)];

    /// <summary>White noise, used as the basis for gunshots and impacts.</summary>
    private static float Noise(ref Rng rng) => rng.Range(-1f, 1f);

    // ---------------------------------------------------------------- synthesisers

    /// <summary>
    /// Builds an original four-layer firearm report:
    /// 1) a sub-15 ms uncorking crack, 2) a low body/punch, 3) delayed bolt
    /// reciprocity, and 4) a diffuse reflection.  The distant and occluded
    /// variants are intentionally rendered from the same deterministic recipe
    /// with reduced high-frequency/mechanical content, rather than attempting
    /// unsafe real-time filtering on a Raylib Sound voice.
    /// </summary>
    public static byte[] Gunshot(ulong seed, float duration, float bodyFreq, float noiseAmount,
        float punch, float tail, GunshotPerspective perspective = GunshotPerspective.Near)
    {
        var rng = new Rng(seed);
        duration = MathF.Max(0.08f, duration);

        // The reflection deliberately outlives the direct sound. It reads as a
        // compact exterior tail rather than trying to impersonate a copied map
        // recording, and stays bounded during automatic fire.
        float reflectionTail = perspective switch
        {
            GunshotPerspective.Near => MathF.Min(0.58f, duration * 1.25f + 0.20f),
            GunshotPerspective.Distant => MathF.Min(0.38f, duration * 0.85f + 0.14f),
            _ => MathF.Min(0.28f, duration * 0.65f + 0.10f),
        };
        float[] s = Allocate(duration + reflectionTail);
        int n = s.Length;

        // Independent one-pole states keep the layers intentionally distinct.
        float bodyLow = 0f;
        float highReject = 0f;
        float reflectionLow = 0f;
        float mechanicsLow = 0f;

        (float crack, float body, float mechanics, float reflection, float reflectionCutoff) = perspective switch
        {
            GunshotPerspective.Near => (1.00f, 1.00f, 1.00f, 1.00f, 0.055f),
            GunshotPerspective.Distant => (0.28f, 0.78f, 0.00f, 0.72f, 0.030f),
            _ => (0.10f, 0.66f, 0.00f, 0.46f, 0.016f),
        };

        // Seed-derived offsets avoid every weapon having the same machine click
        // and slap-back interval while remaining perfectly deterministic.
        float mechanicalAt = 0.048f + (seed % 7UL) * 0.006f;
        float reflectionAt = perspective == GunshotPerspective.Near
            ? 0.062f + (seed % 5UL) * 0.008f
            : 0.042f + (seed % 4UL) * 0.007f;
        float mechanicalFreq = 680f + (seed % 9UL) * 31f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float raw = Noise(ref rng);

            // Layer 1: a hard, high-frequency pressure transient.  The high
            // component is made by subtracting a fast low-pass estimate; this
            // stays deterministic and needs no FFT allocation.
            highReject += (raw - highReject) * 0.22f;
            float crackEnv = MathF.Exp(-t * 280f) + MathF.Exp(-t * 68f) * 0.16f;
            float crackLayer = (raw - highReject) * crackEnv * noiseAmount * crack;

            // Layer 2: the low body combines a resonant tone with a filtered
            // noisy pressure component, which gives rifle classes mass without
            // relying on a third-party recording.
            bodyLow += (raw - bodyLow) * 0.055f;
            float bodyEnv = MathF.Exp(-t * 19f);
            float bodyTone = MathF.Sin(MathF.Tau * bodyFreq * t)
                + MathF.Sin(MathF.Tau * bodyFreq * 0.53f * t) * 0.26f;
            float bodyLayer = (bodyTone * 0.68f + bodyLow * 0.92f) * bodyEnv * punch * body;

            // Layer 3: a delayed mechanical bolt/casing texture. Distant and
            // occluded profiles intentionally remove this intelligibility cue.
            float mechanicalLayer = 0f;
            if (mechanics > 0f && t >= mechanicalAt)
            {
                float mt = t - mechanicalAt;
                mechanicsLow += (raw - mechanicsLow) * 0.18f;
                float mechanicalEnv = (1f - MathF.Exp(-mt * 170f)) * MathF.Exp(-mt * 42f);
                float mechanism = MathF.Sin(MathF.Tau * mechanicalFreq * mt) * 0.34f
                    + (raw - mechanicsLow) * 0.82f;
                mechanicalLayer = mechanism * mechanicalEnv * (0.34f + punch * 0.20f) * mechanics;
            }

            // Layer 4: one early reflection followed by a diffuse, low-passed
            // tail. The first flutter supplies close-map definition; the longer
            // tail avoids the dry, single-sample sound of the earlier fallback.
            float reflectionLayer = 0f;
            if (t >= reflectionAt)
            {
                float rt = t - reflectionAt;
                reflectionLow += (raw - reflectionLow) * reflectionCutoff;
                float onset = 1f - MathF.Exp(-rt * 95f);
                float diffuse = onset * MathF.Exp(-rt * 4.5f);
                float flutter = MathF.Sin(MathF.Tau * (bodyFreq * 0.37f) * rt) * MathF.Exp(-rt * 19f) * 0.20f;
                reflectionLayer = (reflectionLow * 0.96f + flutter) * diffuse * tail * reflection;
            }

            float v = crackLayer + bodyLayer + mechanicalLayer + reflectionLayer;
            s[i] = MathF.Tanh(v * 1.46f) * 0.76f;
        }

        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>Fleshy hit: short filtered thud plus a wet click.</summary>
    public static byte[] Hit(ulong seed, float duration, float freq, float noise)
    {
        var rng = new Rng(seed);
        float[] s = Allocate(duration);
        int n = s.Length;
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;

            float env = MathF.Exp(-p * 26f);
            float tone = MathF.Sin(MathF.Tau * freq * t) * env;
            float hiss = Noise(ref rng) * MathF.Exp(-p * 60f) * noise;
            lp += (hiss - lp) * 0.22f;

            s[i] = (tone * 0.7f + lp * 0.9f) * 0.8f;
        }

        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>Dry wall impact: a short tick with a little resonance.</summary>
    public static byte[] Impact(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.16f;
        float[] s = Allocate(duration);
        int n = s.Length;
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float env = MathF.Exp(-p * 42f);
            float click = Noise(ref rng) * env;
            lp += (click - lp) * 0.35f;
            float ring = MathF.Sin(MathF.Tau * 1750f * t) * MathF.Exp(-p * 26f) * 0.22f;
            s[i] = (lp * 0.85f + ring) * 0.7f;
        }

        Fade(s, 0.002f);
        return Encode(s);
    }

    /// <summary>Rifle magazine reload: two metallic clicks.</summary>
    public static byte[] Reload(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.75f;
        float[] s = Allocate(duration);
        int n = s.Length;

        void Click(double at, float gain, float freq)
        {
            int start = (int)(at * SampleRate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = MathF.Exp(-t * 55f);
                if (env < 0.0005f) break;
                float tone = MathF.Sin(MathF.Tau * freq * t) * env;
                float noise = Noise(ref rng) * env * 0.5f;
                s[i] += (tone * 0.45f + noise) * gain;
            }
        }

        Click(0.02, 0.5f, 620f);
        Click(0.20, 0.35f, 420f);
        Click(0.44, 0.6f, 780f);
        Click(0.52, 0.3f, 900f);

        for (int i = 0; i < n; i++) s[i] = MathF.Tanh(s[i] * 1.6f) * 0.7f;
        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>
    /// Builds a compact footstep from collision-sidecar material data.  These
    /// are intentionally broad material families, not guesses about hidden GLB
    /// textures: the caller only selects names that the level exposes through
    /// <c>SurfaceKind</c>.
    /// </summary>
    public static byte[] Step(ulong seed, FootstepSurface surface = FootstepSurface.Standard)
    {
        var rng = new Rng(seed);
        const float duration = 0.15f;
        float[] s = Allocate(duration);
        int n = s.Length;
        float lp = 0f;

        (float noiseGain, float cutoff, float knockFreq, float knockGain, float ringFreq, float ringGain) = surface switch
        {
            FootstepSurface.Floor => (2.25f, 0.10f, 108f, 0.28f, 0f, 0f),
            FootstepSurface.Crate => (1.55f, 0.075f, 172f, 0.54f, 0f, 0f),
            FootstepSurface.Metal => (1.25f, 0.13f, 240f, 0.24f, 1230f, 0.26f),
            FootstepSurface.Platform => (1.70f, 0.09f, 144f, 0.38f, 620f, 0.10f),
            _ => (2.20f, 0.10f, 118f, 0.18f, 0f, 0f),
        };

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float env = MathF.Exp(-p * 30f) * (1f - MathF.Exp(-p * 60f));
            lp += (Noise(ref rng) - lp) * cutoff;
            float knock = MathF.Sin(MathF.Tau * knockFreq * t) * MathF.Exp(-t * 36f) * knockGain;
            float ring = ringFreq > 0f
                ? MathF.Sin(MathF.Tau * ringFreq * t) * MathF.Exp(-t * 48f) * ringGain
                : 0f;
            s[i] = (lp * noiseGain + knock + ring) * env;
        }

        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>
    /// Short, dry local confirmation for a successful player hit.  It is separate
    /// from the in-world flesh impact so distance never makes the HUD confirmation
    /// vanish before the player hears it.
    /// </summary>
    public static byte[] Hitmarker(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.075f;
        float[] s = Allocate(duration);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Exp(-t * 62f);
            float tick = MathF.Sin(MathF.Tau * 1820f * t) * env * 0.68f;
            float snap = Noise(ref rng) * MathF.Exp(-t * 140f) * 0.24f;
            s[i] = MathF.Tanh((tick + snap) * 1.35f) * 0.75f;
        }
        Fade(s, 0.0015f);
        return Encode(s);
    }

    /// <summary>Two-stage local confirmation used for a player headshot.</summary>
    public static byte[] Headshot(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.17f;
        float[] s = Allocate(duration);
        int second = (int)(0.045f * SampleRate);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;
            float primary = MathF.Sin(MathF.Tau * 1180f * t) * MathF.Exp(-t * 38f) * 0.62f;
            float follow = 0f;
            if (i >= second)
            {
                float later = (i - second) / (float)SampleRate;
                follow = MathF.Sin(MathF.Tau * 1760f * later) * MathF.Exp(-later * 44f) * 0.44f;
            }
            float snap = Noise(ref rng) * MathF.Exp(-t * 120f) * 0.18f;
            s[i] = MathF.Tanh((primary + follow + snap) * 1.3f) * 0.78f;
        }
        Fade(s, 0.0015f);
        return Encode(s);
    }

    /// <summary>
    /// A compact, original two-stage internal heartbeat.  It is intentionally
    /// synthesised here rather than sampled from a third-party game or video so
    /// critical-health feedback remains distributable with the project.
    /// </summary>
    public static byte[] Heartbeat(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.30f;
        float[] s = Allocate(duration);

        void AddBeat(float at, float gain)
        {
            int start = (int)(at * SampleRate);
            for (int i = start; i < s.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = (1f - MathF.Exp(-t * 110f)) * MathF.Exp(-t * 22f);
                if (env < 0.0005f) break;

                float body = MathF.Sin(MathF.Tau * 63f * t) * 0.80f;
                float knock = MathF.Sin(MathF.Tau * 118f * t) * 0.22f;
                float texture = Noise(ref rng) * 0.045f;
                s[i] += (body + knock + texture) * env * gain;
            }
        }

        AddBeat(0.00f, 0.92f);
        AddBeat(0.115f, 0.58f);
        for (int i = 0; i < s.Length; i++) s[i] = MathF.Tanh(s[i] * 1.55f) * 0.62f;
        Fade(s, 0.004f);
        return Encode(s);
    }

    /// <summary>Knife swing: airy swoosh.</summary>
    public static byte[] Swoosh(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.28f;
        float[] s = Allocate(duration);
        int n = s.Length;
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            float env = MathF.Sin(p * MathF.PI);
            lp += (Noise(ref rng) - lp) * (0.05f + 0.25f * env);
            s[i] = lp * env * 1.3f;
        }

        Fade(s, 0.006f);
        return Encode(s);
    }

    /// <summary>Rising confirmation blip for purchases and UI feedback.</summary>
    public static byte[] Blip(float f0, float f1, float duration, float gain)
    {
        float[] s = Allocate(duration);
        int n = s.Length;
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            float freq = U.Lerp(f0, f1, p);
            phase += MathF.Tau * freq / SampleRate;
            float env = MathF.Sin(p * MathF.PI);
            s[i] = MathF.Sin(phase) * env * gain;
        }

        Fade(s, 0.005f);
        return Encode(s);
    }

    /// <summary>Long descending tone used for the end of a match.</summary>
    public static byte[] Fanfare()
    {
        const float duration = 1.6f;
        float[] s = Allocate(duration);
        int n = s.Length;

        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        for (int k = 0; k < notes.Length; k++)
        {
            double at = k * 0.16;
            int start = (int)(at * SampleRate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = MathF.Exp(-t * 3.2f) * (1f - MathF.Exp(-t * 90f));
                s[i] += MathF.Sin(MathF.Tau * notes[k] * t) * env * 0.22f;
            }
        }

        for (int i = 0; i < n; i++) s[i] = MathF.Tanh(s[i] * 1.4f) * 0.8f;
        Fade(s, 0.01f);
        return Encode(s);
    }

    /// <summary>Applies a short fade in/out so buffers never click.</summary>
    private static void Fade(float[] s, float seconds)
    {
        int ramp = Math.Min((int)(seconds * SampleRate), s.Length / 2);
        if (ramp <= 0) return;

        for (int i = 0; i < ramp; i++)
        {
            float g = i / (float)ramp;
            s[i] *= g;
            s[s.Length - 1 - i] *= g;
        }
    }
}
