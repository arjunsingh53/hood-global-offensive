using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.UI;

namespace HoodGlobalOffensive.Sim;

/// <summary>
/// Window-free proof for player-preference persistence. It always uses a unique
/// temp profile, never the real LocalAppData document used by the GUI.
/// </summary>
public static class UserSettingsVerifier
{
    public static int Run()
    {
        var failures = new List<string>();
        int checks = 0;
        string root = Path.Combine(Path.GetTempPath(), $"hgo-settings-verifier-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "settings.json");

        try
        {
            Directory.CreateDirectory(root);

            var initial = new MatchSettings();
            var initialStore = new UserSettingsStore(path);
            initialStore.LoadInto(initial);
            Require(initialStore.SettingsPath == path, "explicit test profile path was not retained", ref checks, failures);
            Require(initial.BotCount == Cfg.DefaultBotCount && initial.Sensitivity == Cfg.Sensitivity,
                "missing profile did not retain clean defaults", ref checks, failures);

            initial.BotCount = 4;
            initial.Skill = BotSkill.Expert;
            initial.KillsToWin = 55;
            initial.Sensitivity = 3.7f;
            initial.Crosshair.CycleColor(1); // Custom cyan profile, not a preset alias.
            initial.Crosshair.Style = CrosshairStyle.TShape;
            initial.Crosshair.DynamicGap = false;
            initial.Crosshair.CentreDot = true;
            initial.Crosshair.BaseGap = 9;
            initial.Crosshair.ArmLength = 12;
            initial.Crosshair.Thickness = 4;
            initial.CameraFeedback.Restore(CameraFeedbackLevel.Low);
            initial.Seed = 0xDEADBEEFu;

            Require(initialStore.SaveIfChanged(initial), "changed player preferences were not written", ref checks, failures);
            Require(File.Exists(path), "settings document was not created", ref checks, failures);
            string document = File.ReadAllText(path);
            Require(!document.Contains("Seed", StringComparison.Ordinal)
                && !document.Contains("Money", StringComparison.Ordinal)
                && !document.Contains("Weapon", StringComparison.Ordinal)
                && !document.Contains("Player", StringComparison.Ordinal),
                "settings document contains gameplay or match state", ref checks, failures);
            Require(!initialStore.SaveIfChanged(initial), "unchanged preferences rewrote the settings document", ref checks, failures);

            var restored = new MatchSettings();
            new UserSettingsStore(path).LoadInto(restored);
            Require(restored.BotCount == 4 && restored.Skill == BotSkill.Expert && restored.KillsToWin == 55,
                "menu match preferences did not survive a round trip", ref checks, failures);
            Require(MathF.Abs(restored.Sensitivity - 3.7f) < 0.0001f,
                "mouse sensitivity did not survive a round trip", ref checks, failures);
            Require(restored.Crosshair.CapturePreference() == initial.Crosshair.CapturePreference(),
                "full crosshair presentation did not survive a round trip", ref checks, failures);
            Require(restored.CameraFeedback.Level == CameraFeedbackLevel.Low,
                "camera feedback level did not survive a round trip", ref checks, failures);

            File.WriteAllText(path, "{ definitely-not-json");
            var malformed = new MatchSettings();
            new UserSettingsStore(path).LoadInto(malformed);
            Require(malformed.BotCount == Cfg.DefaultBotCount && malformed.Skill == BotSkill.Normal,
                "malformed settings did not fall back to defaults", ref checks, failures);
            Require(CorruptCopies(root) == 1, "malformed settings were not preserved as a corrupt copy", ref checks, failures);

            File.WriteAllText(path, new string('x', UserSettingsStore.MaxDocumentBytes + 1));
            var oversized = new MatchSettings();
            new UserSettingsStore(path).LoadInto(oversized);
            Require(oversized.KillsToWin == Cfg.KillsToWin && CorruptCopies(root) == 2,
                "oversized settings were not rejected and archived", ref checks, failures);

            File.WriteAllText(path, "{\"SchemaVersion\":999}");
            var unsupported = new MatchSettings();
            new UserSettingsStore(path).LoadInto(unsupported);
            Require(unsupported.Sensitivity == Cfg.Sensitivity && CorruptCopies(root) == 3,
                "unsupported settings schema was not rejected and archived", ref checks, failures);

            File.WriteAllText(path,
                "{\"SchemaVersion\":1,\"BotCount\":999,\"BotSkill\":999,\"KillsToWin\":999,\"Sensitivity\":100,"
                + "\"CrosshairPreset\":999,\"CrosshairColor\":999,\"CrosshairStyle\":999,\"CrosshairDynamicGap\":true,"
                + "\"CrosshairCentreDot\":true,\"CrosshairBaseGap\":999,\"CrosshairArmLength\":-50,\"CrosshairThickness\":999,"
                + "\"CameraFeedbackLevel\":999}");
            var normalized = new MatchSettings();
            new UserSettingsStore(path).LoadInto(normalized);
            Require(normalized.BotCount == Cfg.MaxBots && normalized.KillsToWin == 100 && normalized.Sensitivity == 8f,
                "out-of-range numeric preferences were not clamped", ref checks, failures);
            Require(normalized.Skill == BotSkill.Normal && normalized.CameraFeedback.Level == CameraFeedbackLevel.Normal,
                "invalid enum preferences were not normalized", ref checks, failures);
            CrosshairPreference crosshair = normalized.Crosshair.CapturePreference();
            Require(crosshair.Preset is null && crosshair.Color == CrosshairColorOption.Green
                && crosshair.Style == CrosshairStyle.Cross && crosshair.BaseGap == 24
                && crosshair.ArmLength == 1 && crosshair.Thickness == 5,
                "invalid crosshair preference values were not normalized", ref checks, failures);
        }
        finally
        {
            // This is an exact, GUID-owned temp test directory. Guarding both
            // its parent and name prevents the verifier from ever removing a
            // real player profile if this method is modified later.
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            string fullRoot = Path.GetFullPath(root);
            if (Directory.Exists(fullRoot)
                && fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullRoot).StartsWith("hgo-settings-verifier-", StringComparison.Ordinal))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=========================================================");
        Console.WriteLine(" USER SETTINGS PERSISTENCE VERIFICATION REPORT");
        Console.WriteLine("=========================================================");
        Console.WriteLine($" checks               : {checks}");
        Console.WriteLine($" result               : {(failures.Count == 0 ? "PASS" : "FAIL")}");
        Console.WriteLine(" profile              : bounded LocalAppData-style JSON; no match/combat state");
        Console.WriteLine(" recovery             : malformed, oversized and unknown-schema files preserve a corrupt copy");
        foreach (string failure in failures) Console.WriteLine($"   FAIL: {failure}");
        Console.WriteLine("=========================================================");

        return failures.Count == 0 ? 0 : 1;
    }

    private static int CorruptCopies(string root) =>
        Directory.EnumerateFiles(root, "settings.corrupt.*.json", SearchOption.TopDirectoryOnly).Count();

    private static void Require(bool condition, string failure, ref int checks, List<string> failures)
    {
        checks++;
        if (!condition) failures.Add(failure);
    }
}
