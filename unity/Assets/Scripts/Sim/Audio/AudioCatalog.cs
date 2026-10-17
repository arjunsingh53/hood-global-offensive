using System.Collections.Generic;

namespace HoodGlobalOffensive.Audio
{
    /// <summary>
    /// One synthesised firearm family: cue name and the parameters of its generated
    /// report. Mirrors the reference <c>AudioBank.WeaponFamily</c> minus the
    /// asset-override paths, which only the raylib loader needs. Keep the values in
    /// lock-step with <c>src/Audio/AudioBank.cs</c> so the ported fallbacks and the
    /// reference <c>--sfx-dump</c> captures can never drift.
    /// </summary>
    public readonly struct WeaponFamily
    {
        public readonly string Name;
        public readonly ulong Seed;
        public readonly float Duration;
        public readonly float BodyFreq;
        public readonly float Noise;
        public readonly float Punch;
        public readonly float Tail;

        public WeaponFamily(string name, ulong seed, float duration, float bodyFreq, float noise, float punch, float tail)
        {
            Name = name;
            Seed = seed;
            Duration = duration;
            BodyFreq = bodyFreq;
            Noise = noise;
            Punch = punch;
            Tail = tail;
        }
    }

    /// <summary>
    /// The engine-free master list of every procedural cue the game synthesises, in
    /// the exact order the reference <c>AudioAudit.Dump</c> writes them. The Unity
    /// presentation audio bank builds its clips from <see cref="Cues"/>, and the
    /// parity suite hashes the same list against reference captures — one table, so
    /// the loader and the gate cannot disagree.
    /// </summary>
    public static class AudioCatalog
    {
        /// <summary>Firearm families; same seeds/parameters as the reference bank.</summary>
        public static readonly WeaponFamily[] WeaponFamilies =
        {
            new WeaponFamily("rifle_ak", 11, 0.35f, 108f, 0.98f, 0.66f, 0.56f),
            new WeaponFamily("rifle_m4", 12, 0.28f, 154f, 0.82f, 0.48f, 0.40f),
            new WeaponFamily("pistol", 22, 0.24f, 176f, 0.8f, 0.45f, 0.35f),
            new WeaponFamily("deagle", 33, 0.38f, 104f, 1.0f, 0.75f, 0.6f),
            new WeaponFamily("smg", 44, 0.20f, 190f, 0.75f, 0.38f, 0.3f),
            new WeaponFamily("shotgun", 55, 0.46f, 78f, 1.0f, 0.85f, 0.75f),
            new WeaponFamily("awp", 66, 0.62f, 68f, 1.0f, 1.0f, 0.85f),
            new WeaponFamily("double_barrel", 71, 0.5f, 58f, 0.98f, 1.0f, 0.9f),
        };

        /// <summary>
        /// Every generated cue as <c>(name, wav)</c>, in reference dump order: all
        /// three spectral profiles of each firearm family, then the fixed effect set.
        /// </summary>
        public static List<(string Name, byte[] Wav)> Cues()
        {
            var files = new List<(string Name, byte[] Wav)>();

            foreach (WeaponFamily family in WeaponFamilies)
            {
                files.Add((family.Name + ".wav", Gunshot(family, WavGen.GunshotPerspective.Near)));
                files.Add((family.Name + "-distant.wav", Gunshot(family, WavGen.GunshotPerspective.Distant)));
                files.Add((family.Name + "-occluded.wav", Gunshot(family, WavGen.GunshotPerspective.Occluded)));
            }

            files.Add(("knife.wav", WavGen.Swoosh(77)));
            files.Add(("hit_flesh.wav", WavGen.Hit(88, 0.22f, 190f, 0.6f)));
            files.Add(("hit_head.wav", WavGen.Hit(99, 0.3f, 320f, 0.5f)));
            files.Add(("impact.wav", WavGen.Impact(111)));
            files.Add(("reload.wav", WavGen.Reload(122)));
            files.Add(("step.wav", WavGen.Step(133)));
            files.Add(("step-floor.wav", WavGen.Step(134, WavGen.FootstepSurface.Floor)));
            files.Add(("step-crate.wav", WavGen.Step(135, WavGen.FootstepSurface.Crate)));
            files.Add(("step-metal.wav", WavGen.Step(136, WavGen.FootstepSurface.Metal)));
            files.Add(("step-platform.wav", WavGen.Step(137, WavGen.FootstepSurface.Platform)));
            files.Add(("hitmarker.wav", WavGen.Hitmarker(144)));
            files.Add(("headshot.wav", WavGen.Headshot(155)));
            files.Add(("heartbeat.wav", WavGen.Heartbeat(166)));
            files.Add(("buy.wav", WavGen.Blip(520f, 900f, 0.16f, 0.35f)));
            files.Add(("spawn.wav", WavGen.Blip(300f, 620f, 0.22f, 0.3f)));
            files.Add(("kill.wav", WavGen.Blip(700f, 1180f, 0.2f, 0.4f)));
            files.Add(("match_end.wav", WavGen.Fanfare()));
            files.Add(("grapple_fire.wav", WavGen.Blip(1500f, 420f, 0.10f, 0.25f)));
            files.Add(("grapple_attach.wav", WavGen.Hit(189, 0.12f, 260f, 0.5f)));
            files.Add(("grapple_release.wav", WavGen.Blip(420f, 300f, 0.07f, 0.16f)));
            files.Add(("flashlight.wav", WavGen.Blip(900f, 700f, 0.04f, 0.18f)));
            files.Add(("upgrade.wav", WavGen.Blip(600f, 1250f, 0.28f, 0.32f)));
            files.Add(("upgrade_deny.wav", WavGen.Blip(320f, 150f, 0.22f, 0.3f)));

            return files;
        }

        private static byte[] Gunshot(WeaponFamily family, WavGen.GunshotPerspective perspective)
            => WavGen.Gunshot(family.Seed, family.Duration, family.BodyFreq, family.Noise, family.Punch,
                family.Tail, perspective);
    }
}
