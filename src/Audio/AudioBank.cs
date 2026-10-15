using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using Raylib_cs;

namespace HoodGlobalOffensive.Audio;

/// <summary>
/// Holds the procedural sound bank and plays events from the simulation with
/// distance-based attenuation. Degrades silently when no audio device exists.
/// </summary>
public sealed class AudioBank : IDisposable
{
    private const int Voices = 3;
    private const float MaxAudibleDistance = 2600f;

    private readonly Dictionary<string, Sound[]> _sounds = new();
    private readonly Dictionary<string, int> _cursor = new();
    private bool _disposed;

    public bool Ready { get; private set; }
    public float MasterVolume { get; set; } = 0.7f;

    public void Load()
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady())
        {
            Ready = false;
            return;
        }

        Raylib.SetMasterVolume(MasterVolume);

        // Weapons: distinct body frequency, noise and tail per class.
        Add("rifle", WavGen.Gunshot(11, 0.34f, 128f, 0.95f, 0.55f, 0.5f));
        Add("pistol", WavGen.Gunshot(22, 0.24f, 176f, 0.8f, 0.45f, 0.35f));
        Add("deagle", WavGen.Gunshot(33, 0.38f, 104f, 1.0f, 0.75f, 0.6f));
        Add("smg", WavGen.Gunshot(44, 0.20f, 190f, 0.75f, 0.38f, 0.3f));
        Add("shotgun", WavGen.Gunshot(55, 0.46f, 78f, 1.0f, 0.85f, 0.75f));
        Add("awp", WavGen.Gunshot(66, 0.62f, 68f, 1.0f, 1.0f, 0.85f));
        Add("knife", WavGen.Swoosh(77));

        Add("hit_flesh", WavGen.Hit(88, 0.22f, 190f, 0.6f));
        Add("hit_head", WavGen.Hit(99, 0.3f, 320f, 0.5f));
        Add("impact", WavGen.Impact(111));
        Add("reload", WavGen.Reload(122));
        Add("step", WavGen.Step(133));
        Add("buy", WavGen.Blip(520f, 900f, 0.16f, 0.35f));
        Add("spawn", WavGen.Blip(300f, 620f, 0.22f, 0.3f));
        Add("kill", WavGen.Blip(700f, 1180f, 0.2f, 0.4f));
        Add("match_end", WavGen.Fanfare());

        Ready = _sounds.Count > 0;
    }

    private void Add(string name, byte[] wav)
    {
        var voices = new Sound[Voices];
        for (int i = 0; i < Voices; i++)
        {
            Wave wave = Raylib.LoadWaveFromMemory(".wav", wav);
            voices[i] = Raylib.LoadSoundFromWave(wave);
        }

        _sounds[name] = voices;
        _cursor[name] = 0;
    }

    public void Play(SoundEvent evt, Vector3 listener)
    {
        if (!Ready) return;

        float distance = evt.Name switch
        {
            "step" => (evt.Position - listener).Length(),
            "buy" or "kill" or "match_end" or "spawn" => 0f,
            _ => (evt.Position - listener).Length(),
        };

        float attenuation = distance <= 1f
            ? 1f
            : U.Clamp01(1f - distance / MaxAudibleDistance);
        attenuation *= attenuation;

        float volume = evt.Volume * attenuation * 1.6f;
        if (volume < 0.012f) return;

        Play(evt.Name, volume, evt.Pitch, distance <= 1f);
    }

    public void Play(string name, float volume, float pitch = 1f, bool skipAttenuation = false)
    {
        if (!Ready || _disposed) return;
        if (!_sounds.TryGetValue(name, out Sound[]? voices)) return;

        int index = _cursor[name];
        _cursor[name] = (index + 1) % voices.Length;

        Sound sound = voices[index];
        Raylib.SetSoundVolume(sound, U.Clamp(volume * (skipAttenuation ? 1f : 1f), 0f, 1f) * 0.9f);
        Raylib.SetSoundPitch(sound, U.Clamp(pitch, 0.3f, 3f));
        Raylib.PlaySound(sound);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Ready)
        {
            foreach (Sound[] voices in _sounds.Values)
            {
                foreach (Sound sound in voices) Raylib.UnloadSound(sound);
            }
            Raylib.CloseAudioDevice();
        }

        _sounds.Clear();
        Ready = false;
    }
}
