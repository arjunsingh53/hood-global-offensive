using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using Raylib_cs;

namespace HoodGlobalOffensive.Audio;

/// <summary>
/// Holds the gameplay sound bank and plays events from the simulation with
/// distance-based attenuation.  A user-supplied, licensed WAV/OGG/MP3 in
/// <c>assets/audio</c> overrides its matching original fallback at load time;
/// the procedural layer keeps the game playable when no external pack is present.
/// </summary>
public sealed class AudioBank : IDisposable
{
    private const int Voices = 3;
    private const float MaxAudibleDistance = 2600f;

    private readonly Dictionary<string, Sound[]> _sounds = new();
    private readonly Dictionary<string, int> _cursor = new();
    private bool _disposed;
    private bool _audioDeviceOpened;

    public bool Ready { get; private set; }
    public float MasterVolume { get; set; } = 0.7f;

    public void Load()
    {
        Raylib.InitAudioDevice();
        _audioDeviceOpened = Raylib.IsAudioDeviceReady();
        if (!_audioDeviceOpened)
        {
            Ready = false;
            return;
        }

        Raylib.SetMasterVolume(MasterVolume);

        // Weapons: distinct body frequency, noise and tail per class. A licensed
        // gun.wav reference may intentionally replace the shared presentation;
        // individual class files take precedence when present.
        AddReferenceOrGenerated("rifle", "assets/audio/gun-rifle.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(11, 0.34f, 128f, 0.95f, 0.55f, 0.5f));
        AddReferenceOrGenerated("pistol", "assets/audio/gun-pistol.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(22, 0.24f, 176f, 0.8f, 0.45f, 0.35f));
        AddReferenceOrGenerated("deagle", "assets/audio/gun-deagle.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(33, 0.38f, 104f, 1.0f, 0.75f, 0.6f));
        AddReferenceOrGenerated("smg", "assets/audio/gun-smg.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(44, 0.20f, 190f, 0.75f, 0.38f, 0.3f));
        AddReferenceOrGenerated("shotgun", "assets/audio/gun-shotgun.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(55, 0.46f, 78f, 1.0f, 0.85f, 0.75f));
        AddReferenceOrGenerated("awp", "assets/audio/gun-sniper.wav", "assets/audio/gun.wav",
            WavGen.Gunshot(66, 0.62f, 68f, 1.0f, 1.0f, 0.85f));
        Add("knife", WavGen.Swoosh(77));

        Add("hit_flesh", WavGen.Hit(88, 0.22f, 190f, 0.6f));
        Add("hit_head", WavGen.Hit(99, 0.3f, 320f, 0.5f));
        Add("impact", WavGen.Impact(111));
        AddReferenceOrGenerated("reload", "assets/audio/reload.wav", WavGen.Reload(122));
        AddReferenceOrGenerated("step", "assets/audio/footstep.wav", WavGen.Step(133));
        AddReferenceOrGenerated("hitmarker", "assets/audio/hitmarker.wav", WavGen.Hitmarker(144));
        AddReferenceOrGenerated("headshot", "assets/audio/headshot.wav", WavGen.Headshot(155));
        Add("buy", WavGen.Blip(520f, 900f, 0.16f, 0.35f));
        Add("spawn", WavGen.Blip(300f, 620f, 0.22f, 0.3f));
        Add("kill", WavGen.Blip(700f, 1180f, 0.2f, 0.4f));
        Add("match_end", WavGen.Fanfare());

        Ready = _sounds.Count > 0;
    }

    private void Add(string name, byte[] wav)
    {
        var voices = new Sound[Voices];
        int loaded = 0;
        try
        {
            for (; loaded < Voices; loaded++)
            {
                Wave wave = Raylib.LoadWaveFromMemory(".wav", wav);
                try
                {
                    Sound sound = Raylib.LoadSoundFromWave(wave);
                    if (!Raylib.IsSoundValid(sound))
                        throw new InvalidOperationException($"Generated {name} voice {loaded + 1} was not accepted by the audio device.");
                    voices[loaded] = sound;
                }
                finally
                {
                    // LoadSoundFromWave owns a copy for its AudioStream. The temporary
                    // Wave still owns its PCM buffer and must be released here.
                    Raylib.UnloadWave(wave);
                }
            }
        }
        catch
        {
            UnloadVoices(voices, loaded);
            throw;
        }

        _sounds[name] = voices;
        _cursor[name] = 0;
    }

    /// <summary>
    /// Uses the first supplied reference file that exists; otherwise preserves the
    /// original synthesized fallback.  This deliberately does not fetch media from
    /// a web page: callers must put a licensed source file in the asset folder.
    /// </summary>
    private void AddReferenceOrGenerated(string name, string assetPath, byte[] fallback)
        => AddReferenceOrGenerated(name, assetPath, string.Empty, fallback);

    private void AddReferenceOrGenerated(string name, string preferredPath, string sharedPath, byte[] fallback)
    {
        string? source = ResolveAsset(preferredPath) ?? ResolveAsset(sharedPath);
        if (source is null)
        {
            Add(name, fallback);
            return;
        }

        try
        {
            var voices = new Sound[Voices];
            int loaded = 0;
            try
            {
                for (; loaded < Voices; loaded++)
                {
                    Sound sound = Raylib.LoadSound(source);
                    if (!Raylib.IsSoundValid(sound))
                        throw new InvalidDataException($"The file did not produce a valid sound: {source}");
                    voices[loaded] = sound;
                }
            }
            catch
            {
                UnloadVoices(voices, loaded);
                throw;
            }

            _sounds[name] = voices;
            _cursor[name] = 0;
            Console.WriteLine($"audio reference {name}: {source}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"audio reference {source} could not load ({ex.Message}); using original {name} fallback");
            Add(name, fallback);
        }
    }

    private static string? ResolveAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // The documented override workflow permits WAV, OGG, or MP3.  The .wav
        // paths passed above establish priority; sibling extensions are only used
        // when that preferred WAV is absent.
        string extension = Path.GetExtension(path);
        var candidates = new List<string> { path };
        if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.ChangeExtension(path, ".ogg"));
            candidates.Add(Path.ChangeExtension(path, ".mp3"));
        }

        foreach (string candidate in candidates)
        {
            string beside = Path.Combine(AppContext.BaseDirectory, candidate);
            if (File.Exists(beside)) return beside;
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static void UnloadVoices(Sound[] voices, int count)
    {
        for (int i = 0; i < count; i++) Raylib.UnloadSound(voices[i]);
    }

    public void Play(SoundEvent evt, Vector3 listener)
    {
        if (!Ready) return;

        float distance = evt.Name switch
        {
            "step" => (evt.Position - listener).Length(),
            "buy" or "kill" or "match_end" or "spawn" or "hitmarker" or "headshot" => 0f,
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

        // A load may fail halfway through (for example, after an audio device was
        // opened but before every generated voice was created).  Teardown must not
        // depend on Ready, otherwise those native handles leak on game shutdown.
        foreach (Sound[] voices in _sounds.Values)
        {
            foreach (Sound sound in voices) Raylib.UnloadSound(sound);
        }

        _sounds.Clear();
        _cursor.Clear();
        Ready = false;

        if (_audioDeviceOpened && Raylib.IsAudioDeviceReady()) Raylib.CloseAudioDevice();
        _audioDeviceOpened = false;
    }
}
