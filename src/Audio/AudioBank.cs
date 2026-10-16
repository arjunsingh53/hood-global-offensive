using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
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

        // Weapons use an original four-layer procedural stack.  Each fallback
        // has a near, distant, and occluded spectrum so map-aware routing can
        // remove crack/mechanical detail without a fragile run-time DSP path.
        // Licensed local overrides still win unchanged; authors can optionally
        // provide matching "-distant" / "-occluded" files for their own pack.
        AddWeaponFamily("rifle_ak", "assets/audio/gun-ak47.wav", "assets/audio/gun-rifle.wav", "assets/audio/gun.wav",
            11, 0.35f, 108f, 0.98f, 0.66f, 0.56f);
        AddWeaponFamily("rifle_m4", "assets/audio/gun-m4a4.wav", "assets/audio/gun-rifle.wav", "assets/audio/gun.wav",
            12, 0.28f, 154f, 0.82f, 0.48f, 0.40f);
        AddWeaponFamily("pistol", "assets/audio/gun-pistol.wav", "assets/audio/gun.wav", string.Empty,
            22, 0.24f, 176f, 0.8f, 0.45f, 0.35f);
        AddWeaponFamily("deagle", "assets/audio/gun-deagle.wav", "assets/audio/gun.wav", string.Empty,
            33, 0.38f, 104f, 1.0f, 0.75f, 0.6f);
        AddWeaponFamily("smg", "assets/audio/gun-smg.wav", "assets/audio/gun.wav", string.Empty,
            44, 0.20f, 190f, 0.75f, 0.38f, 0.3f);
        AddWeaponFamily("shotgun", "assets/audio/gun-shotgun.wav", "assets/audio/gun.wav", string.Empty,
            55, 0.46f, 78f, 1.0f, 0.85f, 0.75f);
        AddWeaponFamily("awp", "assets/audio/gun-sniper.wav", "assets/audio/gun.wav", string.Empty,
            66, 0.62f, 68f, 1.0f, 1.0f, 0.85f);
        AddWeaponFamily("double_barrel", "assets/audio/gun-doublebarrel.wav", "assets/audio/gun-shotgun.wav",
            "assets/audio/gun.wav", 71, 0.5f, 58f, 0.98f, 1.0f, 0.9f);
        Add("knife", WavGen.Swoosh(77));

        Add("hit_flesh", WavGen.Hit(88, 0.22f, 190f, 0.6f));
        Add("hit_head", WavGen.Hit(99, 0.3f, 320f, 0.5f));
        Add("impact", WavGen.Impact(111));
        AddReferenceOrGenerated("reload", "assets/audio/reload.wav", WavGen.Reload(122));
        AddFootstepFamily();
        AddReferenceOrGenerated("hitmarker", "assets/audio/hitmarker.wav", WavGen.Hitmarker(144));
        AddReferenceOrGenerated("headshot", "assets/audio/headshot.wav", WavGen.Headshot(155));
        AddReferenceOrGenerated("heartbeat", "assets/audio/heartbeat.wav", WavGen.Heartbeat(166));
        Add("buy", WavGen.Blip(520f, 900f, 0.16f, 0.35f));
        Add("spawn", WavGen.Blip(300f, 620f, 0.22f, 0.3f));
        Add("kill", WavGen.Blip(700f, 1180f, 0.2f, 0.4f));
        Add("match_end", WavGen.Fanfare());

        // Movement-tool and cabinet cues. The grapple trio is the constant
        // companion of every swing, so each stays short and pitchy.
        AddReferenceOrGenerated("grapple_fire", "assets/audio/grapple-fire.wav",
            WavGen.Blip(1500f, 420f, 0.10f, 0.25f));
        AddReferenceOrGenerated("grapple_attach", "assets/audio/grapple-attach.wav",
            WavGen.Hit(189, 0.12f, 260f, 0.5f));
        AddReferenceOrGenerated("grapple_release", "assets/audio/grapple-release.wav",
            WavGen.Blip(420f, 300f, 0.07f, 0.16f));
        AddReferenceOrGenerated("flashlight", "assets/audio/flashlight.wav",
            WavGen.Blip(900f, 700f, 0.04f, 0.18f));
        AddReferenceOrGenerated("upgrade", "assets/audio/upgrade.wav",
            WavGen.Blip(600f, 1250f, 0.28f, 0.32f));
        AddReferenceOrGenerated("upgrade_deny", "assets/audio/upgrade-deny.wav",
            WavGen.Blip(320f, 150f, 0.22f, 0.3f));

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
    /// Loads a firearm family without ever replacing a player-supplied local
    /// recording. Generated HGO fallbacks receive all three spectral profiles;
    /// an external pack remains its exact source unless it deliberately ships a
    /// profile derivative beside it.
    /// </summary>
    private void AddWeaponFamily(string name, string preferredPath, string sharedPath, string genericPath,
        ulong seed, float duration, float bodyFrequency, float noiseAmount, float punch, float tail)
    {
        byte[] near = WavGen.Gunshot(seed, duration, bodyFrequency, noiseAmount, punch, tail,
            WavGen.GunshotPerspective.Near);
        byte[] distant = WavGen.Gunshot(seed, duration, bodyFrequency, noiseAmount, punch, tail,
            WavGen.GunshotPerspective.Distant);
        byte[] occluded = WavGen.Gunshot(seed, duration, bodyFrequency, noiseAmount, punch, tail,
            WavGen.GunshotPerspective.Occluded);

        string? source = ResolveFirst(preferredPath, sharedPath, genericPath);
        if (source is null)
        {
            Add(name, near);
            Add(name + ".distant", distant);
            Add(name + ".occluded", occluded);
            return;
        }

        try
        {
            AddFromFile(name, source);
            AddOptionalReference(name + ".distant", ProfilePath(preferredPath, "distant"),
                ProfilePath(sharedPath, "distant"), ProfilePath(genericPath, "distant"));
            AddOptionalReference(name + ".occluded", ProfilePath(preferredPath, "occluded"),
                ProfilePath(sharedPath, "occluded"), ProfilePath(genericPath, "occluded"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"audio reference {source} could not load ({ex.Message}); using original {name} fallback");
            Add(name, near);
            Add(name + ".distant", distant);
            Add(name + ".occluded", occluded);
        }
    }

    /// <summary>
    /// Keeps the existing footstep override route intact while exposing only the
    /// material names actually authored by <see cref="GameMap"/>. No hidden
    /// texture or imagined environment material is inferred here.
    /// </summary>
    private void AddFootstepFamily()
    {
        const string basePath = "assets/audio/footstep.wav";
        string? source = ResolveAsset(basePath);
        if (source is null)
        {
            Add("step", WavGen.Step(133));
            Add("step.floor", WavGen.Step(134, WavGen.FootstepSurface.Floor));
            Add("step.crate", WavGen.Step(135, WavGen.FootstepSurface.Crate));
            Add("step.metal", WavGen.Step(136, WavGen.FootstepSurface.Metal));
            Add("step.platform", WavGen.Step(137, WavGen.FootstepSurface.Platform));
            return;
        }

        try
        {
            AddFromFile("step", source);
            AddOptionalReference("step.floor", ProfilePath(basePath, "floor"));
            AddOptionalReference("step.crate", ProfilePath(basePath, "crate"));
            AddOptionalReference("step.metal", ProfilePath(basePath, "metal"));
            AddOptionalReference("step.platform", ProfilePath(basePath, "platform"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"audio reference {source} could not load ({ex.Message}); using original step fallback");
            Add("step", WavGen.Step(133));
            Add("step.floor", WavGen.Step(134, WavGen.FootstepSurface.Floor));
            Add("step.crate", WavGen.Step(135, WavGen.FootstepSurface.Crate));
            Add("step.metal", WavGen.Step(136, WavGen.FootstepSurface.Metal));
            Add("step.platform", WavGen.Step(137, WavGen.FootstepSurface.Platform));
        }
    }

    private void AddOptionalReference(string name, params string[] paths)
    {
        string? source = ResolveFirst(paths);
        if (source is null) return;

        try
        {
            AddFromFile(name, source);
        }
        catch (Exception ex)
        {
            // An optional derivative must never disable the primary custom pack.
            Console.Error.WriteLine($"audio reference {source} could not load ({ex.Message}); using base cue for {name}");
        }
    }

    /// <summary>
    /// Uses the first supplied reference file that exists; otherwise preserves the
    /// original synthesized fallback.  This deliberately does not fetch media from
    /// a web page: callers must put a licensed source file in the asset folder.
    /// </summary>
    private void AddReferenceOrGenerated(string name, string assetPath, byte[] fallback)
        => AddReferenceOrGenerated(name, assetPath, string.Empty, fallback);

    private void AddReferenceOrGenerated(string name, string preferredPath, string sharedPath, byte[] fallback)
        => AddReferenceOrGenerated(name, preferredPath, sharedPath, string.Empty, fallback);

    private void AddReferenceOrGenerated(string name, string preferredPath, string sharedPath, string genericPath,
        byte[] fallback)
    {
        string? source = ResolveFirst(preferredPath, sharedPath, genericPath);
        if (source is null)
        {
            Add(name, fallback);
            return;
        }

        try
        {
            AddFromFile(name, source);
            Console.WriteLine($"audio reference {name}: {source}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"audio reference {source} could not load ({ex.Message}); using original {name} fallback");
            Add(name, fallback);
        }
    }

    private void AddFromFile(string name, string source)
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
    }

    private static string ProfilePath(string path, string profile)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(directory, $"{stem}-{profile}.wav");
    }

    private static string? ResolveFirst(params string[] paths)
    {
        foreach (string path in paths)
        {
            string? resolved = ResolveAsset(path);
            if (resolved is not null) return resolved;
        }

        return null;
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

    /// <summary>
    /// Plays one simulation cue through deterministic listener routing. Passing
    /// the live map enables static-geometry occlusion; callers without a map
    /// retain the same distance/pan policy but cannot claim wall filtering.
    /// </summary>
    public void Play(SoundEvent evt, Vector3 listener, float listenerYaw, float worldTrauma = 0f,
        GameMap? map = null)
    {
        if (!Ready) return;

        SoundPlayback mix = AudioMix.Resolve(evt, listener, listenerYaw, map, worldTrauma);
        if (mix.Volume < 0.012f) return;
        Play(ResolveCue(evt.Name, mix.Spectrum), mix.Volume, evt.Pitch, mix.Pan);
    }

    private string ResolveCue(string requested, SoundSpectrumProfile spectrum)
    {
        string profileCue = spectrum switch
        {
            SoundSpectrumProfile.Distant => requested + ".distant",
            SoundSpectrumProfile.Occluded => requested + ".occluded",
            _ => requested,
        };
        if (_sounds.ContainsKey(profileCue)) return profileCue;
        if (_sounds.ContainsKey(requested)) return requested;

        // A custom footstep pack may only provide its legacy base file. Surface
        // metadata still reaches the audio route; it cleanly falls back rather
        // than silently dropping footsteps because an optional derivative is
        // absent.
        if (requested.StartsWith("step.", StringComparison.Ordinal) && _sounds.ContainsKey("step"))
            return "step";

        return requested;
    }

    public void Play(string name, float volume, float pitch = 1f, float pan = 0.5f)
    {
        if (!Ready || _disposed) return;
        if (!_sounds.TryGetValue(name, out Sound[]? voices)) return;

        int index = _cursor[name];
        _cursor[name] = (index + 1) % voices.Length;

        Sound sound = voices[index];
        Raylib.SetSoundVolume(sound, U.Clamp(volume, 0f, 1f) * 0.9f);
        Raylib.SetSoundPitch(sound, U.Clamp(pitch, 0.3f, 3f));
        Raylib.SetSoundPan(sound, U.Clamp(pan, 0f, 1f));
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
