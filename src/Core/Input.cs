using System.Numerics;
using Raylib_cs;

namespace HoodGlobalOffensive.Core;

/// <summary>
/// A single frame of intent. Everything downstream of this struct is
/// deterministic, which lets the headless harness drive the game with no window.
/// </summary>
public struct InputFrame
{
    /// <summary>X = strafe (-1 left .. 1 right), Y = forward (-1 back .. 1 forward).</summary>
    public Vector2 Move;
    /// <summary>Mouse movement in counts since the previous frame.</summary>
    public Vector2 Look;
    public bool Jump;
    public bool Crouch;
    public bool Walk;
    public bool FireDown;
    public bool FirePressed;
    public bool Reload;
    public bool ZoomPressed;
    /// <summary>Weapon slot to select this frame, or -1 for none.</summary>
    public int SwitchSlot;

    public static InputFrame Neutral => new() { SwitchSlot = -1 };
}

/// <summary>Reads the real keyboard/mouse into an <see cref="InputFrame"/>.</summary>
public static class RealInput
{
    public static InputFrame Poll(bool acceptLook)
    {
        var f = InputFrame.Neutral;

        float right = 0f, fwd = 0f;
        if (Raylib.IsKeyDown(KeyboardKey.W)) fwd += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.S)) fwd -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.D)) right += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.A)) right -= 1f;
        f.Move = new Vector2(right, fwd);

        f.Look = acceptLook ? Raylib.GetMouseDelta() : Vector2.Zero;
        f.Jump = Raylib.IsKeyDown(KeyboardKey.Space);
        f.Crouch = Raylib.IsKeyDown(KeyboardKey.LeftControl);
        f.Walk = Raylib.IsKeyDown(KeyboardKey.LeftShift);
        f.FireDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        f.FirePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
        f.Reload = Raylib.IsKeyPressed(KeyboardKey.R);
        f.ZoomPressed = Raylib.IsMouseButtonPressed(MouseButton.Right);

        if (Raylib.IsKeyPressed(KeyboardKey.One)) f.SwitchSlot = 0;
        else if (Raylib.IsKeyPressed(KeyboardKey.Two)) f.SwitchSlot = 1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Three)) f.SwitchSlot = 2;
        else if (Raylib.IsKeyPressed(KeyboardKey.Four)) f.SwitchSlot = 3;

        return f;
    }
}

/// <summary>Replays a canned input frame; used by the headless verification harness.</summary>
public sealed class ScriptedInput
{
    private readonly Func<int, InputFrame> _generator;

    public ScriptedInput(Func<int, InputFrame> generator) => _generator = generator;

    public InputFrame Poll(int tick) => _generator(tick);
}
