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

    /// <summary>
    /// Small RBJ-cookbook biquad. The firearm and impact layers are all
    /// filter-shaped noise, so the cues carry a physical timbre instead of the
    /// buzz a saturated pure tone produces.
    /// </summary>
    private sealed class Biquad
    {
        private readonly float _b0, _b1, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        private Biquad(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        private static (float cos, float alpha) Coefficients(float freq, float q)
        {
            float w = MathF.Tau * U.Clamp(freq, 20f, SampleRate * 0.45f) / SampleRate;
            return (MathF.Cos(w), MathF.Sin(w) / (2f * MathF.Max(0.1f, q)));
        }

        public static Biquad LowPass(float freq, float q = 0.707f)
        {
            (float c, float a) = Coefficients(freq, q);
            float b0 = (1f - c) * 0.5f;
            return new Biquad(b0, 1f - c, b0, 1f + a, -2f * c, 1f - a);
        }

        public static Biquad HighPass(float freq, float q = 0.707f)
        {
            (float c, float a) = Coefficients(freq, q);
            float b0 = (1f + c) * 0.5f;
            return new Biquad(b0, -(1f + c), b0, 1f + a, -2f * c, 1f - a);
        }

        public static Biquad BandPass(float freq, float q = 1f)
        {
            (float c, float a) = Coefficients(freq, q);
            return new Biquad(a, 0f, -a, 1f + a, -2f * c, 1f - a);
        }
    }

    // ---------------------------------------------------------------- synthesisers

    /// <summary>
    /// Builds an original layered firearm report from filter-shaped noise:
    /// 1) a muzzle snap around 2 kHz with a midrange bark, 2) a resonant low
    /// body plus a filtered sub, 3) a pressure tail whose low-pass sweeps
    /// darker, 4) a struck-metal mechanical clack, and 5) a first reflection.
    /// The distant and occluded variants are rendered from the same
    /// deterministic recipe with reduced high-frequency/mechanical content,
    /// rather than attempting unsafe real-time filtering on a Raylib voice.
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

        (float snapGain, float bodyGain, float mechGain, float tailGain,
            float tailCutStart, float tailCutEnd, float tailSweep, float reflGain, float reflCut) = perspective switch
        {
            GunshotPerspective.Near => (1.00f, 1.00f, 1.00f, 1.00f, 3200f, 620f, 0.075f, 1.00f, 900f),
            GunshotPerspective.Distant => (0.26f, 0.88f, 0.00f, 0.72f, 1100f, 330f, 0.100f, 0.55f, 460f),
            _ => (0.07f, 0.70f, 0.00f, 0.42f, 520f, 190f, 0.130f, 0.30f, 260f),
        };

        var snap = Biquad.BandPass(2100f, 0.85f);
        var pop = Biquad.BandPass(520f, 0.9f);
        var body = Biquad.BandPass(bodyFreq, 1.5f);
        var sub = Biquad.LowPass(150f, 0.9f);
        var mechRing = Biquad.BandPass(1450f + (seed % 9UL) * 42f, 6f);
        var mechTick = Biquad.BandPass(3200f, 1.1f);
        var refl = Biquad.BandPass(bodyFreq * 1.12f, 1.0f);
        var reflLow = Biquad.LowPass(reflCut, 0.8f);
        float tailLp = 0f;

        // Seed-derived offsets avoid every weapon having the same machine click
        // and slap-back interval while remaining perfectly deterministic.
        float mechanicalAt = 0.048f + (seed % 7UL) * 0.006f;
        float reflectionAt = perspective == GunshotPerspective.Near
            ? 0.060f + (seed % 5UL) * 0.008f
            : 0.042f + (seed % 4UL) * 0.007f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float raw = Noise(ref rng);

            // Layer 1: muzzle snap. A high pressure clap with a short midrange
            // bark under it; both are filtered noise, so no tone ever leaks.
            float snapEnv = MathF.Exp(-t * 460f) + MathF.Exp(-t * 74f) * 0.20f;
            float snapLayer = (snap.Process(raw) + pop.Process(raw) * 0.55f)
                * snapEnv * noiseAmount * snapGain * 1.35f;

            // Layer 2: resonant low body with a filtered sub under it, fast
            // enough attack that the report has weight from the first sample.
            float bodyEnv = (1f - MathF.Exp(-t * 2400f)) * MathF.Exp(-t * 16f);
            float bodyLayer = (body.Process(raw) * 1.15f + sub.Process(raw) * 0.55f)
                * bodyEnv * punch * bodyGain * 1.4f;

            // Layer 3: blast tail. A one-pole low-pass sweeps darker over the
            // decay, reading as the pressure wave rolling out over the map.
            float cutoff = U.Lerp(tailCutStart, tailCutEnd, MathF.Min(1f, t / tailSweep));
            tailLp += (raw - tailLp) * (1f - MathF.Exp(-MathF.Tau * cutoff / SampleRate));
            float tailEnv = (1f - MathF.Exp(-t * 90f)) * MathF.Exp(-t * 6.4f);
            float tailLayer = tailLp * tailEnv * tail * tailGain * 1.35f;

            // Layer 4: mechanical bolt/casing clack, a struck resonance plus a
            // bright tick. Distant and occluded profiles drop this cue on
            // purpose since it is not intelligible at range.
            float mechanicalLayer = 0f;
            if (mechGain > 0f && t >= mechanicalAt)
            {
                float mt = t - mechanicalAt;
                float mechEnv = (1f - MathF.Exp(-mt * 900f)) * MathF.Exp(-mt * 72f);
                mechanicalLayer = (mechRing.Process(raw) * 0.40f + mechTick.Process(raw) * 0.34f)
                    * mechEnv * (0.40f + punch * 0.22f) * mechGain;
            }

            // Layer 5: one early reflection, darker and delayed, for close-map
            // definition.
            float reflectionLayer = 0f;
            if (t >= reflectionAt)
            {
                float rt = t - reflectionAt;
                float reflEnv = (1f - MathF.Exp(-rt * 140f)) * MathF.Exp(-rt * 7.2f);
                reflectionLayer = reflLow.Process(refl.Process(raw)) * reflEnv * tail * reflGain;
            }

            float v = snapLayer + bodyLayer + tailLayer + mechanicalLayer + reflectionLayer;
            s[i] = MathF.Tanh(v * 2.4f) * 0.82f;
        }

        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>Fleshy hit: low body thud with a wet high tick.</summary>
    public static byte[] Hit(ulong seed, float duration, float freq, float noise)
    {
        var rng = new Rng(seed);
        float[] s = Allocate(duration);
        int n = s.Length;
        var thud = Biquad.LowPass(MathF.Max(80f, freq * 1.1f), 0.9f);
        var wet = Biquad.BandPass(freq * 3.4f, 1.15f);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float raw = Noise(ref rng);
            float thudEnv = (1f - MathF.Exp(-t * 900f)) * MathF.Exp(-t * 26f);
            float wetEnv = MathF.Exp(-t * 140f);
            float v = thud.Process(raw) * thudEnv * 2.2f + wet.Process(raw) * wetEnv * noise * 1.8f;
            s[i] = MathF.Tanh(v * 1.2f) * 0.78f;
        }

        Fade(s, 0.003f);
        return Encode(s);
    }

    /// <summary>Dry world impact: a low thud with a chip and a faint ring.</summary>
    public static byte[] Impact(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.16f;
        float[] s = Allocate(duration);
        int n = s.Length;
        var thud = Biquad.BandPass(780f + (seed % 5UL) * 46f, 0.85f);
        var chip = Biquad.BandPass(2600f, 1.35f);
        float ringFreq = 2280f + (seed % 7UL) * 55f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float raw = Noise(ref rng);
            float thudEnv = (1f - MathF.Exp(-t * 3000f)) * MathF.Exp(-t * 58f);
            float chipEnv = MathF.Exp(-t * 190f);
            float ring = MathF.Sin(MathF.Tau * ringFreq * t) * MathF.Exp(-t * 95f) * 0.09f;
            float v = thud.Process(raw) * thudEnv * 1.5f + chip.Process(raw) * chipEnv * 0.55f + ring;
            s[i] = MathF.Tanh(v * 1.5f) * 0.78f;
        }

        Fade(s, 0.002f);
        return Encode(s);
    }

    /// <summary>
    /// Magazine reload: four metallic clicks, each a high-Q resonant ring plus
    /// a bright tick rather than a plain tone.
    /// </summary>
    public static byte[] Reload(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.75f;
        float[] s = Allocate(duration);
        int n = s.Length;

        void Click(double at, float gain, float freq, float q)
        {
            int start = (int)(at * SampleRate);
            var ring = Biquad.BandPass(freq, q);
            var tick = Biquad.BandPass(freq * 2.6f, 1.2f);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)SampleRate;
                if (MathF.Exp(-t * 85f) < 0.0005f) break;
                float env = (1f - MathF.Exp(-t * 1400f)) * MathF.Exp(-t * 85f);
                float raw = Noise(ref rng);
                s[i] += (ring.Process(raw) * 0.9f + tick.Process(raw) * 0.35f) * env * gain;
            }
        }

        Click(0.02, 0.88f, 860f, 4.5f);
        Click(0.20, 0.64f, 520f, 3.5f);
        Click(0.44, 1.00f, 1080f, 5.0f);
        Click(0.52, 0.58f, 1500f, 6.0f);

        for (int i = 0; i < n; i++) s[i] = MathF.Tanh(s[i] * 1.8f) * 0.8f;
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
    /// Short, dry local confirmation for a successful player hit. It is separate
    /// from the in-world flesh impact so distance never makes the HUD confirmation
    /// vanish before the player hears it. Two clean partials (a major third) over
    /// a tiny filtered tick; deliberately unsaturised so it stays crisp.
    /// </summary>
    public static byte[] Hitmarker(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.075f;
        float[] s = Allocate(duration);
        var tick = Biquad.BandPass(2400f, 1.1f);
        float detune = 1f + ((seed % 5UL) - 2UL) * 0.004f;
        int stagger = (int)(0.004f * SampleRate);

        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;
            float attack = 1f - MathF.Exp(-t * 1500f);
            float click = tick.Process(Noise(ref rng)) * MathF.Exp(-t * 280f) * 0.34f;
            float low = MathF.Sin(MathF.Tau * 1245f * detune * t) * MathF.Exp(-t * 78f) * 0.42f;
            float high = 0f;
            if (i >= stagger)
            {
                float late = (i - stagger) / (float)SampleRate;
                high = MathF.Sin(MathF.Tau * 1868f * detune * late) * MathF.Exp(-late * 66f) * 0.30f;
            }
            s[i] = (click + low + high) * attack * 0.88f;
        }

        Fade(s, 0.0015f);
        return Encode(s);
    }

    /// <summary>Three-stage bright local confirmation used for a player headshot.</summary>
    public static byte[] Headshot(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.17f;
        float[] s = Allocate(duration);
        var tick = Biquad.BandPass(3100f, 1.2f);
        float detune = 1f + ((seed % 7UL) - 3UL) * 0.003f;
        int stagger1 = (int)(0.006f * SampleRate);
        int stagger2 = (int)(0.013f * SampleRate);

        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)SampleRate;
            float attack = 1f - MathF.Exp(-t * 1600f);
            float click = tick.Process(Noise(ref rng)) * MathF.Exp(-t * 240f) * 0.30f;
            float p1 = MathF.Sin(MathF.Tau * 1440f * detune * t) * MathF.Exp(-t * 58f) * 0.40f;
            float p2 = 0f;
            float p3 = 0f;
            if (i >= stagger1)
            {
                float late = (i - stagger1) / (float)SampleRate;
                p2 = MathF.Sin(MathF.Tau * 2160f * detune * late) * MathF.Exp(-late * 54f) * 0.30f;
            }
            if (i >= stagger2)
            {
                float late = (i - stagger2) / (float)SampleRate;
                p3 = MathF.Sin(MathF.Tau * 3240f * detune * late) * MathF.Exp(-late * 90f) * 0.12f;
            }
            s[i] = (click + p1 + p2 + p3) * attack * 0.88f;
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
                // Break on the decay only: the attack term starts at zero.
                if (MathF.Exp(-t * 22f) < 0.0005f) break;

                float env = (1f - MathF.Exp(-t * 110f)) * MathF.Exp(-t * 22f);
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
