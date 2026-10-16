using System.Text;
using System.Text.Json;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.UI;

namespace HoodGlobalOffensive.Core;

/// <summary>
/// Owns the small, versioned player-preference document. It intentionally never
/// serializes MatchSettings directly: seed, match state, player data, weapons,
/// money and arbitrary object graphs are not part of a local preference file.
/// </summary>
public sealed class UserSettingsStore
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaxDocumentBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string? _path;
    private string? _lastSnapshot;

    /// <summary>Exposed for diagnostics and the isolated verifier, never for UI display.</summary>
    public string? SettingsPath => _path;

    public UserSettingsStore(string? path = null)
    {
        _path = path ?? ResolveDefaultPath();
    }

    /// <summary>
    /// Loads bounded preferences into the live menu settings. Any unreadable,
    /// oversized, malformed or unsupported document falls back to defaults and
    /// is preserved as a uniquely named corrupt copy when possible.
    /// </summary>
    public void LoadInto(MatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(_path))
        {
            Console.Error.WriteLine("Player settings are unavailable because no local profile directory was found.");
            _lastSnapshot = Serialize(Capture(settings));
            return;
        }

        if (!File.Exists(_path))
        {
            _lastSnapshot = Serialize(Capture(settings));
            return;
        }

        try
        {
            var info = new FileInfo(_path);
            if (info.Length > MaxDocumentBytes)
                throw new InvalidDataException($"Settings document is larger than {MaxDocumentBytes} bytes.");

            string text = File.ReadAllText(_path, Encoding.UTF8);
            if (Encoding.UTF8.GetByteCount(text) > MaxDocumentBytes)
                throw new InvalidDataException($"Settings document exceeds the {MaxDocumentBytes}-byte limit.");

            StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(text, JsonOptions);
            if (stored is null) throw new InvalidDataException("Settings document was empty.");
            if (stored.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported settings schema {stored.SchemaVersion}.");

            Apply(stored, settings);
        }
        catch (Exception ex) when (IsRecoverableLoadFailure(ex))
        {
            Console.Error.WriteLine($"Player settings could not be read; using defaults for this session: {ex.Message}");
            TryArchiveCorruptFile();
        }

        _lastSnapshot = Serialize(Capture(settings));
    }

    /// <summary>
    /// Atomically saves only if an actual preference changed. Failure is nonfatal:
    /// gameplay, input and closing the game must remain available without a
    /// writable profile directory.
    /// </summary>
    public bool SaveIfChanged(MatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(_path)) return false;

        string snapshot = Serialize(Capture(settings));
        if (string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal)) return false;

        try
        {
            WriteAtomically(_path, snapshot);
            _lastSnapshot = snapshot;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"Player settings could not be saved: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveDefaultPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local)
            ? null
            : Path.Combine(local, "HoodGlobalOffensive", "settings.json");
    }

    private static bool IsRecoverableLoadFailure(Exception ex) => ex is IOException
        or UnauthorizedAccessException
        or JsonException
        or InvalidDataException
        or NotSupportedException;

    private static void Apply(StoredSettings stored, MatchSettings settings)
    {
        settings.BotCount = U.ClampInt(stored.BotCount, 1, Cfg.MaxBots);
        settings.Skill = IsDefined<BotSkill>(stored.BotSkill) ? (BotSkill)stored.BotSkill : BotSkill.Normal;
        settings.KillsToWin = U.ClampInt(stored.KillsToWin, 5, 100);
        settings.Sensitivity = float.IsFinite(stored.Sensitivity)
            ? U.Clamp(stored.Sensitivity, 0.3f, 8f)
            : Cfg.Sensitivity;

        CrosshairPreset? preset = IsDefined<CrosshairPreset>(stored.CrosshairPreset)
            ? (CrosshairPreset)stored.CrosshairPreset
            : null;
        CrosshairColorOption color = IsDefined<CrosshairColorOption>(stored.CrosshairColor)
            ? (CrosshairColorOption)stored.CrosshairColor
            : CrosshairColorOption.Green;
        CrosshairStyle style = IsDefined<CrosshairStyle>(stored.CrosshairStyle)
            ? (CrosshairStyle)stored.CrosshairStyle
            : CrosshairStyle.Cross;

        settings.Crosshair.Restore(new CrosshairPreference(
            preset,
            color,
            style,
            stored.CrosshairDynamicGap,
            stored.CrosshairCentreDot,
            stored.CrosshairBaseGap,
            stored.CrosshairArmLength,
            stored.CrosshairThickness));

        settings.CameraFeedback.Restore(IsDefined<CameraFeedbackLevel>(stored.CameraFeedbackLevel)
            ? (CameraFeedbackLevel)stored.CameraFeedbackLevel
            : CameraFeedbackLevel.Normal);
    }

    private static StoredSettings Capture(MatchSettings settings)
    {
        CrosshairPreference crosshair = settings.Crosshair.CapturePreference();
        return new StoredSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            BotCount = U.ClampInt(settings.BotCount, 1, Cfg.MaxBots),
            BotSkill = (int)settings.Skill,
            KillsToWin = U.ClampInt(settings.KillsToWin, 5, 100),
            Sensitivity = float.IsFinite(settings.Sensitivity)
                ? U.Clamp(settings.Sensitivity, 0.3f, 8f)
                : Cfg.Sensitivity,
            CrosshairPreset = crosshair.Preset is CrosshairPreset preset ? (int)preset : -1,
            CrosshairColor = (int)crosshair.Color,
            CrosshairStyle = (int)crosshair.Style,
            CrosshairDynamicGap = crosshair.DynamicGap,
            CrosshairCentreDot = crosshair.CentreDot,
            CrosshairBaseGap = crosshair.BaseGap,
            CrosshairArmLength = crosshair.ArmLength,
            CrosshairThickness = crosshair.Thickness,
            CameraFeedbackLevel = (int)settings.CameraFeedback.Level,
        };
    }

    private static string Serialize(StoredSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    private static bool IsDefined<TEnum>(int value) where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value);

    private static void WriteAtomically(string path, string document)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("Settings path has no directory.");
        Directory.CreateDirectory(directory);

        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(document);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // The exact, GUID-named temp file is ours; it is safe to clean only
            // that known file after a failed atomic move.
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private void TryArchiveCorruptFile()
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory)) return;
            string stem = Path.GetFileNameWithoutExtension(_path);
            string backup = Path.Combine(directory,
                $"{stem}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}.json");
            File.Move(_path, backup);
        }
        catch
        {
            // A profile can be read but not renamed. Keep it untouched and still
            // let the player continue with defaults for this session.
        }
    }

    /// <summary>Closed JSON surface; public properties are intentional for STJ.</summary>
    private sealed class StoredSettings
    {
        public int SchemaVersion { get; set; }
        public int BotCount { get; set; } = Cfg.DefaultBotCount;
        public int BotSkill { get; set; } = (int)HoodGlobalOffensive.Entities.BotSkill.Normal;
        public int KillsToWin { get; set; } = Cfg.KillsToWin;
        public float Sensitivity { get; set; } = Cfg.Sensitivity;
        public int CrosshairPreset { get; set; } = (int)HoodGlobalOffensive.UI.CrosshairPreset.Classic;
        public int CrosshairColor { get; set; } = (int)HoodGlobalOffensive.UI.CrosshairColorOption.Green;
        public int CrosshairStyle { get; set; } = (int)HoodGlobalOffensive.UI.CrosshairStyle.Cross;
        public bool CrosshairDynamicGap { get; set; } = true;
        public bool CrosshairCentreDot { get; set; } = true;
        public int CrosshairBaseGap { get; set; } = 4;
        public int CrosshairArmLength { get; set; } = 7;
        public int CrosshairThickness { get; set; } = 1;
        public int CameraFeedbackLevel { get; set; } = (int)HoodGlobalOffensive.UI.CameraFeedbackLevel.Normal;
    }
}
