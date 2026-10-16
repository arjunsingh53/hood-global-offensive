using System.Numerics;
using HoodGlobalOffensive.Core;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

/// <summary>Small immediate-mode drawing helpers shared by the HUD and the menus.</summary>
public static class Ui
{
    /// <summary>
    /// Menus use one fixed logical canvas so their hierarchy and hit targets stay
    /// intact on a resized window. Combat HUD and 3D presentation intentionally
    /// remain in physical framebuffer pixels; do not use this around gameplay.
    /// </summary>
    public const int DesignWidth = 1280;
    public const int DesignHeight = 720;

    public static readonly Color Ink = U.Rgb(236, 236, 234);
    public static readonly Color Dim = U.Rgba(236, 236, 234, 140);
    public static readonly Color Faint = U.Rgba(236, 236, 234, 70);
    public static readonly Color Accent = U.Rgb(255, 186, 56);
    public static readonly Color Danger = U.Rgb(226, 74, 62);
    public static readonly Color Good = U.Rgb(118, 206, 124);
    public static readonly Color PanelBg = U.Rgba(10, 12, 16, 205);
    public static readonly Color PanelBorder = U.Rgba(255, 255, 255, 34);

    private static bool _canvasActive;
    private static Camera2D _canvasCamera;

    /// <summary>
    /// Starts a centred 1280×720 UI canvas for menus and overlays. The matching
    /// mouse conversion keeps clickable logical rectangles honest after resize.
    /// Callers must not nest canvas scopes.
    /// </summary>
    public static CanvasScope BeginCanvas(int screenWidth, int screenHeight)
    {
        if (_canvasActive) throw new InvalidOperationException("Nested UI canvas scopes are not supported.");

        float scale = MathF.Min(screenWidth / (float)DesignWidth, screenHeight / (float)DesignHeight);
        Vector2 offset = new(
            (screenWidth - DesignWidth * scale) * 0.5f,
            (screenHeight - DesignHeight * scale) * 0.5f);

        _canvasCamera = new Camera2D
        {
            Offset = offset,
            Target = Vector2.Zero,
            Rotation = 0f,
            Zoom = scale,
        };
        _canvasActive = true;
        Raylib.BeginMode2D(_canvasCamera);
        return new CanvasScope();
    }

    /// <summary>Returns the pointer in the active menu canvas's logical pixels.</summary>
    public static Vector2 GetMousePosition() => _canvasActive
        ? Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _canvasCamera)
        : Raylib.GetMousePosition();

    public sealed class CanvasScope : IDisposable
    {
        private bool _disposed;

        internal CanvasScope() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Raylib.EndMode2D();
            _canvasActive = false;
        }
    }

    public static int Width(string s, int size) => Raylib.MeasureText(s, size);

    public static void Text(string s, int x, int y, int size, Color c)
        => Raylib.DrawText(s, x, y, size, c);

    public static void TextRight(string s, int right, int y, int size, Color c)
        => Raylib.DrawText(s, right - Raylib.MeasureText(s, size), y, size, c);

    public static void TextCenter(string s, int centerX, int y, int size, Color c)
        => Raylib.DrawText(s, centerX - Raylib.MeasureText(s, size) / 2, y, size, c);

    public static void Panel(Rectangle r, Color fill, Color border, float thickness = 1f)
    {
        Raylib.DrawRectangleRec(r, fill);
        if (thickness > 0f) Raylib.DrawRectangleLinesEx(r, thickness, border);
    }

    public static void Bar(Rectangle r, float t, Color fill, Color back)
    {
        Raylib.DrawRectangleRec(r, back);
        float width = r.Width * U.Clamp01(t);
        if (width > 0.5f)
        {
            Raylib.DrawRectangleRec(new Rectangle(r.X, r.Y, width, r.Height), fill);
        }
    }

    /// <summary>Drops a soft dark shadow behind text so it stays legible over the world.</summary>
    public static void ShadowText(string s, int x, int y, int size, Color c, bool centered = false)
    {
        int ox = centered ? x - Raylib.MeasureText(s, size) / 2 : x;
        Raylib.DrawText(s, ox + 2, y + 2, size, U.Rgba(0, 0, 0, 170));
        Raylib.DrawText(s, ox, y, size, c);
    }

    public static void ProgressRing(int cx, int cy, float radius, float t)
    {
        Raylib.DrawCircle(cx, cy, radius + 2f, U.Rgba(0, 0, 0, 150));
        Raylib.DrawRing(new System.Numerics.Vector2(cx, cy), radius - 2f, radius,
            -90f, -90f + 360f * U.Clamp01(t), 48, Accent);
    }
}
