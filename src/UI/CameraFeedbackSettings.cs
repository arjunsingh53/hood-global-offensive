using HoodGlobalOffensive.Core;

namespace HoodGlobalOffensive.UI;

/// <summary>
/// Session-local strength for the render-only camera and viewmodel feedback.
/// The preference stays outside Player and Simulation; changing it cannot alter
/// movement, recoil, hit-scan, weapon state or deterministic match results.
/// </summary>
public sealed class CameraFeedbackSettings
{
    public CameraFeedbackLevel Level = CameraFeedbackLevel.Normal;

    public string Label => Level switch
    {
        CameraFeedbackLevel.Off => "OFF",
        CameraFeedbackLevel.Low => "LOW",
        _ => "NORMAL",
    };

    public float Strength => Level switch
    {
        CameraFeedbackLevel.Off => 0f,
        CameraFeedbackLevel.Low => 0.45f,
        _ => 1f,
    };

    public void Cycle(int direction)
    {
        int count = Enum.GetValues<CameraFeedbackLevel>().Length;
        int next = ((int)Level + direction) % count;
        if (next < 0) next += count;
        Level = (CameraFeedbackLevel)next;
    }
}

public enum CameraFeedbackLevel
{
    Off,
    Low,
    Normal,
}
