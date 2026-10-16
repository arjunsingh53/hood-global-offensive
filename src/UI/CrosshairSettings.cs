using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

/// <summary>
/// Closed, render-only crosshair data used by local preference persistence.
/// It deliberately contains no combat, camera, player, or simulation state.
/// </summary>
public readonly record struct CrosshairPreference(
    CrosshairPreset? Preset,
    CrosshairColorOption Color,
    CrosshairStyle Style,
    bool DynamicGap,
    bool CentreDot,
    int BaseGap,
    int ArmLength,
    int Thickness);

/// <summary>
/// A local, presentation-only crosshair profile. It deliberately does not belong
/// to Player or Simulation: changing a reticle never changes aim, recoil, spread,
/// hit-scan, or the first-person weapon render.
/// </summary>
public sealed class CrosshairSettings
{
    public CrosshairPreset? Preset { get; private set; } = CrosshairPreset.Classic;
    public CrosshairColorOption Color = CrosshairColorOption.Green;
    public CrosshairStyle Style = CrosshairStyle.Cross;
    public bool DynamicGap = true;
    public bool CentreDot = true;
    public int BaseGap = 4;
    public int ArmLength = 7;
    public int Thickness = 1;

    public string PresetLabel => Preset switch
    {
        CrosshairPreset.Classic => "CLASSIC",
        CrosshairPreset.Compact => "COMPACT",
        CrosshairPreset.TShape => "T-SHAPE",
        CrosshairPreset.Dot => "DOT",
        _ => "CUSTOM",
    };

    public string ColorLabel => Color switch
    {
        CrosshairColorOption.Green => "GREEN",
        CrosshairColorOption.Cyan => "CYAN",
        CrosshairColorOption.White => "WHITE",
        CrosshairColorOption.Yellow => "YELLOW",
        CrosshairColorOption.Red => "RED",
        _ => "GREEN",
    };

    public void CyclePreset(int direction)
    {
        int current = (int)(Preset ?? CrosshairPreset.Classic);
        int count = Enum.GetValues<CrosshairPreset>().Length;
        int next = ((current + direction) % count + count) % count;
        ApplyPreset((CrosshairPreset)next);
    }

    public void CycleColor(int direction)
    {
        int current = (int)Color;
        int count = Enum.GetValues<CrosshairColorOption>().Length;
        int next = ((current + direction) % count + count) % count;
        Color = (CrosshairColorOption)next;
        Preset = null;
    }

    public void ToggleDynamicGap()
    {
        DynamicGap = !DynamicGap;
        Preset = null;
    }

    public void ToggleCentreDot()
    {
        CentreDot = !CentreDot;
        Preset = null;
    }

    public void AdjustGap(int direction)
    {
        BaseGap = U.ClampInt(BaseGap + direction, 0, 24);
        Preset = null;
    }

    public void AdjustLength(int direction)
    {
        ArmLength = U.ClampInt(ArmLength + direction, 1, 24);
        Preset = null;
    }

    public void AdjustThickness(int direction)
    {
        Thickness = U.ClampInt(Thickness + direction, 1, 5);
        Preset = null;
    }

    public void Reset() => ApplyPreset(CrosshairPreset.Classic);

    /// <summary>Captures only local reticle presentation settings for the player profile.</summary>
    public CrosshairPreference CapturePreference() => new(
        Preset, Color, Style, DynamicGap, CentreDot, BaseGap, ArmLength, Thickness);

    /// <summary>
    /// Restores a bounded presentation profile. Invalid enum values recover to
    /// safe defaults and a named preset is retained only when every one of its
    /// actual values still matches, preventing corrupt files from lying in UI.
    /// </summary>
    public void Restore(CrosshairPreference preference)
    {
        Color = Enum.IsDefined(typeof(CrosshairColorOption), preference.Color)
            ? preference.Color
            : CrosshairColorOption.Green;
        Style = Enum.IsDefined(typeof(CrosshairStyle), preference.Style)
            ? preference.Style
            : CrosshairStyle.Cross;
        DynamicGap = preference.DynamicGap;
        CentreDot = preference.CentreDot;
        BaseGap = U.ClampInt(preference.BaseGap, 0, 24);
        ArmLength = U.ClampInt(preference.ArmLength, 1, 24);
        Thickness = U.ClampInt(preference.Thickness, 1, 5);

        if (preference.Preset is CrosshairPreset preset
            && Enum.IsDefined(typeof(CrosshairPreset), preset)
            && MatchesPreset(preset))
        {
            Preset = preset;
        }
        else
        {
            Preset = null;
        }
    }

    private void ApplyPreset(CrosshairPreset preset)
    {
        Restore(ProfileFor(preset));
    }

    private bool MatchesPreset(CrosshairPreset preset)
    {
        CrosshairPreference expected = ProfileFor(preset);
        return Color == expected.Color
            && Style == expected.Style
            && DynamicGap == expected.DynamicGap
            && CentreDot == expected.CentreDot
            && BaseGap == expected.BaseGap
            && ArmLength == expected.ArmLength
            && Thickness == expected.Thickness;
    }

    private static CrosshairPreference ProfileFor(CrosshairPreset preset) => preset switch
    {
        CrosshairPreset.Classic => new(preset, CrosshairColorOption.Green, CrosshairStyle.Cross, true, true, 4, 7, 1),
        CrosshairPreset.Compact => new(preset, CrosshairColorOption.Cyan, CrosshairStyle.Cross, true, false, 2, 5, 1),
        CrosshairPreset.TShape => new(preset, CrosshairColorOption.Yellow, CrosshairStyle.TShape, true, false, 4, 8, 2),
        CrosshairPreset.Dot => new(preset, CrosshairColorOption.White, CrosshairStyle.Dot, false, true, 0, 1, 3),
        _ => new(CrosshairPreset.Classic, CrosshairColorOption.Green, CrosshairStyle.Cross, true, true, 4, 7, 1),
    };

    public Color ResolveColor(byte alpha = 225) => Color switch
    {
        CrosshairColorOption.Green => U.Rgba(120, 255, 140, alpha),
        CrosshairColorOption.Cyan => U.Rgba(72, 232, 255, alpha),
        CrosshairColorOption.White => U.Rgba(245, 245, 240, alpha),
        CrosshairColorOption.Yellow => U.Rgba(255, 210, 72, alpha),
        CrosshairColorOption.Red => U.Rgba(255, 92, 82, alpha),
        _ => U.Rgba(120, 255, 140, alpha),
    };
}

public enum CrosshairPreset
{
    Classic,
    Compact,
    TShape,
    Dot,
}

public enum CrosshairStyle
{
    Cross,
    TShape,
    Dot,
}

public enum CrosshairColorOption
{
    Green,
    Cyan,
    White,
    Yellow,
    Red,
}

/// <summary>Shared pixel renderer for the live HUD and the options preview.</summary>
public static class CrosshairRenderer
{
    /// <param name="spreadPixels">The visual-only spread expansion in screen pixels.</param>
    /// <param name="scale">A presentation scale used by the menu preview.</param>
    public static void Draw(CrosshairSettings settings, int cx, int cy, float spreadPixels,
        float scale = 1f, float maxGap = float.MaxValue)
    {
        float gap = (settings.BaseGap + (settings.DynamicGap ? MathF.Max(0f, spreadPixels) : 0f)) * scale;
        gap = MathF.Min(gap, maxGap);
        float length = settings.ArmLength * scale;
        float thickness = settings.Thickness * scale;
        Color color = settings.ResolveColor();

        if (settings.Style != CrosshairStyle.Dot)
        {
            if (settings.Thickness == 1 && MathF.Abs(scale - 1f) < 0.001f)
            {
                // Keep the legacy default rasterisation exactly intact for screenshot
                // comparisons: 1px DrawLine arms and a 1px centre dot.
                Raylib.DrawLine((int)(cx - gap - length), cy, (int)(cx - gap), cy, color);
                Raylib.DrawLine((int)(cx + gap), cy, (int)(cx + gap + length), cy, color);
                if (settings.Style != CrosshairStyle.TShape)
                    Raylib.DrawLine(cx, (int)(cy - gap - length), cx, (int)(cy - gap), color);
                Raylib.DrawLine(cx, (int)(cy + gap), cx, (int)(cy + gap + length), color);
            }
            else
            {
                DrawArm(new Vector2(cx - gap - length, cy), new Vector2(cx - gap, cy), thickness, color);
                DrawArm(new Vector2(cx + gap, cy), new Vector2(cx + gap + length, cy), thickness, color);
                if (settings.Style != CrosshairStyle.TShape)
                    DrawArm(new Vector2(cx, cy - gap - length), new Vector2(cx, cy - gap), thickness, color);
                DrawArm(new Vector2(cx, cy + gap), new Vector2(cx, cy + gap + length), thickness, color);
            }
        }

        if (!settings.CentreDot) return;

        if (settings.Thickness == 1 && MathF.Abs(scale - 1f) < 0.001f)
            Raylib.DrawPixel(cx, cy, color);
        else
            Raylib.DrawCircle(cx, cy, MathF.Max(1f, thickness * 0.5f), color);
    }

    private static void DrawArm(Vector2 start, Vector2 end, float thickness, Color color)
    {
        if (thickness <= 1f)
        {
            Raylib.DrawLine((int)start.X, (int)start.Y, (int)end.X, (int)end.Y, color);
            return;
        }

        Raylib.DrawLineEx(start, end, thickness, color);
    }
}
