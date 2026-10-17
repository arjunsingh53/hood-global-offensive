using System;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Session-local strength for the render-only camera and viewmodel feedback
    /// (reference <c>UI.CameraFeedbackSettings</c>). It stays outside Player and
    /// Simulation, so changing it cannot alter movement, recoil, hit-scan or match
    /// determinism — it only scales the footstep/landing/damage presentation offsets
    /// in <see cref="ViewModelSync"/> and the FOV lens kick in <see cref="MatchDriver"/>.
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

        /// <summary>Applies a bounded local presentation preference from the store.</summary>
        public void Restore(CameraFeedbackLevel level) =>
            Level = Enum.IsDefined(typeof(CameraFeedbackLevel), level)
                ? level
                : CameraFeedbackLevel.Normal;
    }

    public enum CameraFeedbackLevel
    {
        Off,
        Low,
        Normal,
    }
}
