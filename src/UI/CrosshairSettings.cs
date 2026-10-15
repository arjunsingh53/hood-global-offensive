using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

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

    private void ApplyPreset(CrosshairPreset preset)
    {
        Preset = preset;

        switch (preset)
        {
            case CrosshairPreset.Classic:
                Color = CrosshairColorOption.Green;
                Style = CrosshairStyle.Cross;
                DynamicGap = true;
                CentreDot = true;
                BaseGap = 4;
                ArmLength = 7;
                Thickness = 1;
                break;

            case CrosshairPreset.Compact:
                Color = CrosshairColorOption.Cyan;
                Style = CrosshairStyle.Cross;
                DynamicGap = true;
                CentreDot = false;
                BaseGap = 2;
                ArmLength = 5;
                Thickness = 1;
                break;

            case CrosshairPreset.TShape:
                Color = CrosshairColorOption.Yellow;
                Style = CrosshairStyle.TShape;
                DynamicGap = true;
                CentreDot = false;
                BaseGap = 4;
                ArmLength = 8;
                Thickness = 2;
                break;

            case CrosshairPreset.Dot:
                Color = CrosshairColorOption.White;
                Style = CrosshairStyle.Dot;
                DynamicGap = false;
                CentreDot = true;
                BaseGap = 0;
                ArmLength = 1;
                Thickness = 3;
                break;
        }
    }

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
