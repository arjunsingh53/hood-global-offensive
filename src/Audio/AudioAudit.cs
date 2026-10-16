namespace HoodGlobalOffensive.Audio;

/// <summary>
/// Writes the generated fallback cues to disk so the sound design can be
/// auditioned without launching the game or opening an audio device
/// (<c>--sfx-dump &lt;dir&gt;</c>). The firearm families come from
/// <see cref="AudioBank.WeaponFamilies"/>, the same table the live bank uses,
/// so the two can never drift apart.
/// </summary>
public static class AudioAudit
{
    public static int Dump(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var files = new List<(string Name, byte[] Wav)>();

            foreach (AudioBank.WeaponFamily family in AudioBank.WeaponFamilies)
            {
                files.Add(($"{family.Name}.wav", Gunshot(family, WavGen.GunshotPerspective.Near)));
                files.Add(($"{family.Name}-distant.wav", Gunshot(family, WavGen.GunshotPerspective.Distant)));
                files.Add(($"{family.Name}-occluded.wav", Gunshot(family, WavGen.GunshotPerspective.Occluded)));
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

            foreach ((string name, byte[] wav) in files)
            {
                File.WriteAllBytes(Path.Combine(directory, name), wav);
            }

            Console.WriteLine($"wrote {files.Count} generated cues to {Path.GetFullPath(directory)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"sfx dump failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static byte[] Gunshot(AudioBank.WeaponFamily family, WavGen.GunshotPerspective perspective)
        => WavGen.Gunshot(family.Seed, family.Duration, family.BodyFreq, family.Noise, family.Punch,
            family.Tail, perspective);
}
