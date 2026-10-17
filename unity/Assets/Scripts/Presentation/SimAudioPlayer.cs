using System;
using System.Collections.Generic;
using HoodGlobalOffensive.Audio;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Unity audio front end for the engine-free bank. It wraps the 16-bit PCM WAV
    /// buffers produced by <see cref="AudioCatalog"/> into <c>AudioClip</c>s and plays
    /// the events the simulation raises each tick through <see cref="AudioMix"/>.
    ///
    /// The raylib reference keeps three voices per cue; a single round-robin pool of
    /// <see cref="Voices"/> sources gives the same overlap without one GameObject per
    /// cue. The mix math (scope, distance attenuation, listener-space pan, occlusion,
    /// trauma ducking) is unchanged — this class only turns the resolved
    /// <see cref="SoundPlayback"/> into clip/volume/pitch/pan. It is compile-verified
    /// editor-free by <c>tools/presentation-check</c>; actual playback needs a device.
    /// </summary>
    public sealed class SimAudioPlayer : MonoBehaviour
    {
        [Header("Audio")]
        public int Voices = 24;
        public float MasterVolume = 0.7f;

        public bool Ready { get; private set; }

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        AudioSource[] _voices = Array.Empty<AudioSource>();
        int _cursor;

        void Awake()
        {
            BuildClips();
            BuildVoices();
        }

        void OnDestroy()
        {
            foreach (AudioClip clip in _clips.Values) Destroy(clip);
            _clips.Clear();
            Ready = false;
        }

        void BuildClips()
        {
            foreach ((string Name, byte[] Wav) cue in AudioCatalog.Cues())
            {
                AudioClip? clip = CreateClip(cue.Name, cue.Wav);
                if (clip != null) _clips[RuntimeKey(cue.Name)] = clip;
            }

            Ready = _clips.Count > 0;
        }

        void BuildVoices()
        {
            int count = Voices < 1 ? 1 : Voices;
            _voices = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // pan/attenuation are pre-resolved by AudioMix
                _voices[i] = source;
            }
        }

        /// <summary>
        /// Wraps one canonical 44-byte-header mono PCM WAV (what <c>WavGen</c> emits)
        /// in an <c>AudioClip</c>. Returns null for anything unexpected so one bad cue
        /// cannot take the whole bank down.
        /// </summary>
        static AudioClip? CreateClip(string fileName, byte[] wav)
        {
            if (wav == null || wav.Length < 46) return null;
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return null;
            if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E') return null;

            int channels = ReadShort(wav, 22);
            int frequency = ReadInt(wav, 24);
            int bits = ReadShort(wav, 34);
            if (channels != 1 || bits != 16) return null;

            int dataBytes = ReadInt(wav, 40);
            int available = wav.Length - 44;
            if (dataBytes <= 0 || dataBytes > available) dataBytes = available;

            int sampleCount = dataBytes / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short value = (short)(wav[44 + i * 2] | (wav[44 + i * 2 + 1] << 8));
                samples[i] = value / 32768f;
            }

            AudioClip clip = AudioClip.Create(fileName, sampleCount, channels, frequency, false);
            if (clip == null) return null;
            clip.SetData(samples, 0);
            return clip;
        }

        static int ReadShort(byte[] b, int offset) => b[offset] | (b[offset + 1] << 8);

        static int ReadInt(byte[] b, int offset) =>
            b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);

        /// <summary>
        /// Maps a catalog file name to the runtime cue key the simulation emits:
        /// <c>rifle_ak-distant.wav</c> → <c>rifle_ak.distant</c>, <c>step-floor.wav</c>
        /// → <c>step.floor</c>. Mirrors the reference bank's cue keys.
        /// </summary>
        static string RuntimeKey(string fileName)
        {
            string key = fileName;
            if (key.EndsWith(".wav", StringComparison.Ordinal)) key = key.Substring(0, key.Length - 4);
            if (key.EndsWith("-distant", StringComparison.Ordinal))
                return key.Substring(0, key.Length - 8) + ".distant";
            if (key.EndsWith("-occluded", StringComparison.Ordinal))
                return key.Substring(0, key.Length - 9) + ".occluded";
            if (key.StartsWith("step-", StringComparison.Ordinal)) return "step." + key.Substring(5);
            return key;
        }

        /// <summary>
        /// Plays every cue the simulation raised this tick. Call once per
        /// <c>Simulation.Update</c>/code> — the sim clears <c>Sounds</c> at the start of
        /// the next tick, so sounds are drained per step, exactly like the reference.
        /// </summary>
        public void PlaySimulationSounds(Simulation sim)
        {
            if (!Ready || sim == null) return;

            for (int i = 0; i < sim.Sounds.Count; i++)
            {
                SoundEvent evt = sim.Sounds[i];
                SoundPlayback mix = AudioMix.Resolve(
                    evt, sim.Player.EyePosition, sim.Player.Yaw, sim.Map, sim.Player.LowHealthTrauma);
                if (mix.Volume < 0.012f) continue;
                Play(ResolveCue(evt.Name, mix.Spectrum), mix.Volume, evt.Pitch, mix.Pan);
            }
        }

        /// <summary>
        /// Same profile fallback as the reference: prefer the distant/occluded
        /// derivative, fall back to the base cue, then the legacy <c>step</c> cue for
        /// a custom footstep pack that ships only its base file.
        /// </summary>
        string ResolveCue(string requested, SoundSpectrumProfile spectrum)
        {
            string profileCue = spectrum == SoundSpectrumProfile.Distant ? requested + ".distant"
                : spectrum == SoundSpectrumProfile.Occluded ? requested + ".occluded"
                : requested;

            if (_clips.ContainsKey(profileCue)) return profileCue;
            if (_clips.ContainsKey(requested)) return requested;
            if (requested.StartsWith("step.", StringComparison.Ordinal) && _clips.ContainsKey("step"))
                return "step";
            return requested;
        }

        public void Play(string name, float volume, float pitch = 1f, float pan = 0.5f)
        {
            if (!Ready || _voices.Length == 0) return;

            AudioClip? clip;
            if (!_clips.TryGetValue(name, out clip) || clip == null) return;

            AudioSource voice = _voices[_cursor];
            _cursor = (_cursor + 1) % _voices.Length;

            voice.clip = clip;
            voice.volume = U.Clamp(volume, 0f, 1f) * 0.9f * MasterVolume;
            voice.pitch = U.Clamp(pitch, 0.3f, 3f);
            voice.panStereo = U.Clamp(pan, 0f, 1f) * 2f - 1f;
            voice.Play();
        }
    }
}
