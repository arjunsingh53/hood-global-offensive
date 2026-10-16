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
    /// <summary>Held jump. Holding it chains bunny-hops on landing.</summary>
    public bool Jump;
    public bool Crouch;
    /// <summary>One-shot crouch-toggle intent (C).</summary>
    public bool CrouchTogglePressed;
    public bool FireDown;
    public bool FirePressed;
    public bool Reload;
    /// <summary>Held secondary-fire state. Rifles/pistols use hold-to-aim; the sniper toggles its scope.</summary>
    public bool ZoomDown;
    public bool ZoomPressed;
    /// <summary>One-shot left grapple (Q).</summary>
    public bool GrappleLeftPressed;
    /// <summary>One-shot right grapple (E). Near a cabinet, Game resolves E as interact instead.</summary>
    public bool GrappleRightPressed;
    /// <summary>One-shot cabinet interact (E). Context-resolved against the right grapple.</summary>
    public bool UsePressed;
    /// <summary>One-shot quick knife swing (V), never switches weapons.</summary>
    public bool MeleePressed;
    /// <summary>One-shot flashlight toggle (F).</summary>
    public bool FlashlightTogglePressed;
    /// <summary>Weapon slot to select this frame (1-4), or -1 for none.</summary>
    public int SwitchSlot;
    /// <summary>Mouse-wheel weapon cycle: -1 previous, +1 next, 0 none.</summary>
    public int SwitchCycle;

    public static InputFrame Neutral => new() { SwitchSlot = -1 };
}

/// <summary>
/// Decides which full-screen overlay owns a gameplay key press. Keeping this
/// decision outside Raylib lets the state routing be verified without a window.
/// </summary>
public enum GameplayOverlayAction
{
    None,
    Pause,
}

public static class GameplayOverlayInput
{
    /// <summary>
    /// Escape always wins when two keys arrive in the same frame. A completed
    /// match owns its own screen, so it never opens a gameplay overlay.
    /// </summary>
    public static GameplayOverlayAction Resolve(bool escapePressed, bool matchOver)
    {
        if (matchOver) return GameplayOverlayAction.None;
        return escapePressed ? GameplayOverlayAction.Pause : GameplayOverlayAction.None;
    }
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
        f.CrouchTogglePressed = Raylib.IsKeyPressed(KeyboardKey.C);
        f.FireDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        f.FirePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
        f.Reload = Raylib.IsKeyPressed(KeyboardKey.R);
        f.ZoomDown = Raylib.IsMouseButtonDown(MouseButton.Right);
        f.ZoomPressed = Raylib.IsMouseButtonPressed(MouseButton.Right);

        // Redmatch-style dual hooks: Q fires the left hook, E the right. E is
        // also the cabinet interact key; the simulation resolves proximity so a
        // standing player opens the upgrade menu instead of hooking the wall
        // behind the cabinet.
        f.GrappleLeftPressed = Raylib.IsKeyPressed(KeyboardKey.Q);
        f.GrappleRightPressed = Raylib.IsKeyPressed(KeyboardKey.E);
        f.UsePressed = Raylib.IsKeyPressed(KeyboardKey.E);
        f.MeleePressed = Raylib.IsKeyPressed(KeyboardKey.V);
        f.FlashlightTogglePressed = Raylib.IsKeyPressed(KeyboardKey.F);

        if (Raylib.IsKeyPressed(KeyboardKey.One)) f.SwitchSlot = 1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Two)) f.SwitchSlot = 2;
        else if (Raylib.IsKeyPressed(KeyboardKey.Three)) f.SwitchSlot = 3;
        else if (Raylib.IsKeyPressed(KeyboardKey.Four)) f.SwitchSlot = 4;

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel > 0.1f) f.SwitchCycle = 1;
        else if (wheel < -0.1f) f.SwitchCycle = -1;

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
