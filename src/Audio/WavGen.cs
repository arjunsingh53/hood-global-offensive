using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.Audio;

/// <summary>
/// Builds 16-bit mono PCM WAV files in memory. Every sound effect in the game is
/// synthesised here, so there are no binary assets to ship.
/// </summary>
public static class WavGen
{
    public const int SampleRate = 44100;

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

    /// <summary>A gunshot: a bright noise crack with a low-frequency thump underneath.</summary>
    public static byte[] Gunshot(ulong seed, float duration, float bodyFreq, float noiseAmount,
        float punch, float tail)
    {
        var rng = new Rng(seed);
        float[] s = Allocate(duration);
        int n = s.Length;

        // one-pole low-pass state for the "body" layer
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;

            float crackEnv = MathF.Exp(-p * 62f);
            float bodyEnv = MathF.Exp(-p * 17f);
            float tailEnv = MathF.Exp(-p * 4.2f);

            float crack = Noise(ref rng) * crackEnv * noiseAmount;
            float body = MathF.Sin(MathF.Tau * bodyFreq * t) * bodyEnv * punch;
            float lowNoise = Noise(ref rng) * tailEnv * tail;
            lp += (lowNoise - lp) * 0.06f;

            // slight distortion so the crack has grit
            float v = crack + body + lp * 1.6f;
            v = MathF.Tanh(v * 1.9f) * 0.85f;

            s[i] = v * 0.92f;
        }

        Fade(s, 0.004f);
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

    /// <summary>Footstep: a soft filtered noise burst.</summary>
    public static byte[] Step(ulong seed)
    {
        var rng = new Rng(seed);
        const float duration = 0.13f;
        float[] s = Allocate(duration);
        int n = s.Length;
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float env = MathF.Exp(-p * 30f) * (1f - MathF.Exp(-p * 60f));
            lp += (Noise(ref rng) - lp) * 0.10f;
            s[i] = lp * env * 2.2f;
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
