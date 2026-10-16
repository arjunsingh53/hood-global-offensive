using System.Numerics;
using HoodGlobalOffensive.Audio;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Verifies listener-local routing, the original procedural-layer contract, and
/// actual Industrial Zone collision-backed audio policy without opening an
/// audio device. A speaker/headphone pass remains necessary for final mix taste.
/// </summary>
public static class AudioMixVerifier
{
    public static int Run()
    {
        var failures = new List<string>();
        Vector3 listener = Vector3.Zero;
        var map = new GameMap();

        SoundPlayback local = AudioMix.Resolve(
            new SoundEvent("hitmarker", new Vector3(2200f, 0f, -800f), 0.75f, 1f, SoundEventScope.Local),
            listener, 0f);
        SoundPlayback near = AudioMix.Resolve(
            new SoundEvent("rifle_ak", new Vector3(0f, 0f, -100f), 0.8f, 1f), listener, 0f);
        SoundPlayback far = AudioMix.Resolve(
            new SoundEvent("rifle_ak", new Vector3(0f, 0f, -2200f), 0.8f, 1f), listener, 0f);
        SoundPlayback right = AudioMix.Resolve(
            new SoundEvent("rifle_ak", new Vector3(100f, 0f, 0f), 0.8f, 1f), listener, 0f);
        SoundPlayback left = AudioMix.Resolve(
            new SoundEvent("rifle_ak", new Vector3(-100f, 0f, 0f), 0.8f, 1f), listener, 0f);
        SoundPlayback duckedWorld = AudioMix.Resolve(
            new SoundEvent("rifle_ak", new Vector3(0f, 0f, -100f), 0.8f, 1f), listener, 0f, 1f);
        SoundPlayback heartbeat = AudioMix.Resolve(
            new SoundEvent("heartbeat", new Vector3(1800f, 0f, 1200f), 0.16f, 1f, SoundEventScope.Local),
            listener, 0f, 1f);

        Require(MathF.Abs(local.Distance) < 1e-4f && MathF.Abs(local.Pan - 0.5f) < 1e-4f,
            "listener-local cue was attenuated or panned", failures);
        Require(near.Volume > far.Volume && far.Volume > 0f,
            "world attenuation was not monotonic", failures);
        Require(far.Spectrum == SoundSpectrumProfile.Distant,
            "long-distance world cue did not select the distant spectrum", failures);
        Require(right.Pan > 0.5f && left.Pan < 0.5f,
            "world cues did not pan relative to listener yaw", failures);
        Require(duckedWorld.Volume < near.Volume && duckedWorld.Volume > 0f,
            "critical-health world-sound ducking was not bounded and monotonic", failures);
        Require(MathF.Abs(heartbeat.Pan - 0.5f) < 1e-4f && MathF.Abs(heartbeat.Distance) < 1e-4f,
            "internal heartbeat was attenuated or panned like a world cue", failures);
        Require(!string.Equals(WeaponRegistry.Ak47.FireSound, WeaponRegistry.M4A4.FireSound,
                StringComparison.OrdinalIgnoreCase),
            "AK and M4 still share an indistinguishable fire-cue id", failures);

        bool clearFound = TryFindClearMapPair(map, out Vector3 clearListener, out Vector3 clearSource);
        bool blockedFound = TryFindBlockedMapPair(map, out Vector3 blockedListener, out Vector3 blockedSource);
        SoundPlayback clear = clearFound
            ? AudioMix.Resolve(new SoundEvent("rifle_ak", clearSource, 0.8f, 1f), clearListener, 0f, map)
            : default;
        SoundPlayback blocked = blockedFound
            ? AudioMix.Resolve(new SoundEvent("rifle_ak", blockedSource, 0.8f, 1f), blockedListener, 0f, map)
            : default;
        SoundPlayback unblockedControl = blockedFound
            ? AudioMix.Resolve(new SoundEvent("rifle_ak", blockedSource, 0.8f, 1f), blockedListener, 0f)
            : default;

        Require(clearFound, "no clear pair could be sampled from the Industrial Zone collision map", failures);
        Require(!clearFound || !clear.Occluded,
            "clear Industrial Zone segment was incorrectly routed as occluded", failures);
        Require(blockedFound, "no blocked pair could be sampled from the Industrial Zone collision map", failures);
        Require(!blockedFound || (blocked.Occluded && blocked.Spectrum == SoundSpectrumProfile.Occluded),
            "blocked Industrial Zone segment did not select the occluded spectrum", failures);
        Require(!blockedFound || blocked.Volume < unblockedControl.Volume,
            "wall occlusion did not attenuate the real-map route", failures);

        bool floorFound = map.FindFloorSurface(map.Spawns[0].Position + Vector3.UnitY * 0.5f, 1f,
            Cfg.GroundSnap + Cfg.StepHeight + 2f, out _, out SurfaceKind floorSurface);
        Brush crate = map.Brushes.FirstOrDefault(b => b.Kind == SurfaceKind.Crate);
        Vector3 crateCenter = (crate.Box.Min + crate.Box.Max) * 0.5f;
        SurfaceKind crateSurface = SurfaceKind.Floor;
        bool crateFound = crate.Box.Max.Y > crate.Box.Min.Y
            && map.FindFloorSurface(new Vector3(crateCenter.X, crate.Box.Max.Y + 0.5f, crateCenter.Z), 1f, 4f,
                out _, out crateSurface);

        Require(floorFound && floorSurface == SurfaceKind.Floor,
            "Industrial Zone spawn floor did not expose Floor footstep metadata", failures);
        Require(crateFound && crateSurface == SurfaceKind.Crate,
            "Industrial Zone crate top did not expose Crate footstep metadata", failures);

        byte[] nearShot = WavGen.Gunshot(991, 0.34f, 112f, 0.9f, 0.64f, 0.52f);
        byte[] nearShotAgain = WavGen.Gunshot(991, 0.34f, 112f, 0.9f, 0.64f, 0.52f);
        byte[] distantShot = WavGen.Gunshot(991, 0.34f, 112f, 0.9f, 0.64f, 0.52f,
            WavGen.GunshotPerspective.Distant);
        byte[] occludedShot = WavGen.Gunshot(991, 0.34f, 112f, 0.9f, 0.64f, 0.52f,
            WavGen.GunshotPerspective.Occluded);

        Require(nearShot.SequenceEqual(nearShotAgain),
            "procedural gunshot stack was not deterministic for a fixed recipe", failures);
        Require(IsPcmWave(nearShot) && IsPcmWave(distantShot) && IsPcmWave(occludedShot),
            "gunshot profile did not encode a valid PCM WAV buffer", failures);
        Require(!nearShot.SequenceEqual(distantShot) && !nearShot.SequenceEqual(occludedShot),
            "near, distant, and occluded firearm variants are indistinguishable", failures);
        Require(SampleEnergy(nearShot, 0f, 0.012f) > 0.004f
                && SampleEnergy(nearShot, 0.060f, 0.105f) > 0.001f
                && SampleEnergy(nearShot, 0.180f, 0.300f) > 0.0003f,
            "gunshot stack lost one of its transient/body-mechanical/reflection energy windows", failures);

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" TACTICAL AUDIO VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine(" checks               : 17");
        Console.WriteLine($" result               : {(failures.Count == 0 ? "PASS" : "FAIL")}");
        Console.WriteLine($" local pan/distance   : {local.Pan:0.00} / {local.Distance:0.0}");
        Console.WriteLine($" world near/far gain  : {near.Volume:0.000} / {far.Volume:0.000} ({far.Spectrum})");
        Console.WriteLine($" left/right pan       : {left.Pan:0.00} / {right.Pan:0.00}");
        Console.WriteLine($" map clear/blocked    : {clearFound}/{blockedFound}  occluded gain {blocked.Volume:0.000}");
        Console.WriteLine($" footsteps            : floor {floorSurface} / crate {crateSurface}");
        Console.WriteLine($" stack bytes          : near {nearShot.Length}  distant {distantShot.Length}  occluded {occludedShot.Length}");
        foreach (string failure in failures) Console.WriteLine($"   FAIL: {failure}");
        Console.WriteLine("=========================================================");

        return failures.Count == 0 ? 0 : 1;
    }

    private static bool TryFindClearMapPair(GameMap map, out Vector3 listener, out Vector3 source)
    {
        // First prefer genuine player-height sightlines between authored spawn
        // lanes; fall back to a high observer segment only if the compact map
        // has no pair with a direct tactical line of sight.
        for (int i = 0; i < map.Spawns.Length; i++)
        {
            Vector3 a = map.Spawns[i].Position + Vector3.UnitY * Cfg.EyeStand;
            for (int j = i + 1; j < map.Spawns.Length; j++)
            {
                Vector3 b = map.Spawns[j].Position + Vector3.UnitY * Cfg.EyeStand;
                if (Vector3.Distance(a, b) > 160f && map.LineOfSight(a, b))
                {
                    listener = a;
                    source = b;
                    return true;
                }
            }
        }

        listener = new Vector3(-900f, 360f, -800f);
        source = new Vector3(900f, 360f, 800f);
        return map.LineOfSight(listener, source);
    }

    private static bool TryFindBlockedMapPair(GameMap map, out Vector3 listener, out Vector3 source)
    {
        foreach (Brush brush in map.Brushes)
        {
            if (brush.Kind == SurfaceKind.Floor || brush.Box.Max.Y <= 4f) continue;

            Vector3 center = (brush.Box.Min + brush.Box.Max) * 0.5f;
            float y = U.Clamp(Cfg.EyeStand, brush.Box.Min.Y + 2f, brush.Box.Max.Y - 2f);
            float halfX = (brush.Box.Max.X - brush.Box.Min.X) * 0.5f;
            float halfZ = (brush.Box.Max.Z - brush.Box.Min.Z) * 0.5f;
            // Cross the thin dimension so both endpoints sit outside the
            // selected blocker rather than sliding along inside a long wall.
            Vector3 axis = halfX <= halfZ ? Vector3.UnitX : Vector3.UnitZ;
            float clearance = (halfX <= halfZ ? halfX : halfZ) + 28f;

            Vector3 point = new(center.X, y, center.Z);
            Vector3 a = point - axis * clearance;
            Vector3 b = point + axis * clearance;
            if (map.SegmentBlocker(a, b) >= 0)
            {
                listener = a;
                source = b;
                return true;
            }
        }

        listener = Vector3.Zero;
        source = Vector3.Zero;
        return false;
    }

    private static bool IsPcmWave(byte[] data)
        => data.Length > 44
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'A' && data[10] == (byte)'V' && data[11] == (byte)'E'
            && data[20] == 1 && data[21] == 0 && data[22] == 1 && data[23] == 0;

    private static float SampleEnergy(byte[] wave, float startSeconds, float endSeconds)
    {
        int start = Math.Clamp(44 + (int)(startSeconds * WavGen.SampleRate) * 2, 44, wave.Length);
        int end = Math.Clamp(44 + (int)(endSeconds * WavGen.SampleRate) * 2, start, wave.Length);
        if ((end - start) < 2) return 0f;

        double total = 0d;
        int count = 0;
        for (int offset = start; offset + 1 < end; offset += 2)
        {
            short sample = (short)(wave[offset] | (wave[offset + 1] << 8));
            total += Math.Abs(sample / 32768d);
            count++;
        }

        return count > 0 ? (float)(total / count) : 0f;
    }

    private static void Require(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }
}
