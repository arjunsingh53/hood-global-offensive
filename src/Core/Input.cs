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
    /// <summary>One-shot held-crouch edge. It can start a momentum slide without changing held crouch semantics.</summary>
    public bool CrouchPressed;
    /// <summary>One-shot source-compatible crouch-toggle intent (C).</summary>
    public bool CrouchTogglePressed;
    /// <summary>
    /// One-shot prone toggle intent (Z).  It is deliberately separate from
    /// crouch so a prone player cannot accidentally stand up while initiating a
    /// slide or using the legacy C crouch toggle.
    /// </summary>
    public bool ProneTogglePressed;
    public bool Walk;
    /// <summary>One-shot sprint mode cycle intent (Shift).</summary>
    public bool SprintPressed;
    public bool FireDown;
    public bool FirePressed;
    /// <summary>One-shot inspect intent for the current first-person weapon.</summary>
    public bool InspectPressed;
    /// <summary>One-shot AUTO/SEMI selector intent for compatible player weapons.</summary>
    public bool FireModePressed;
    public bool Reload;
    /// <summary>Held secondary-fire state. Iron-sight weapons use hold-to-aim.</summary>
    public bool ZoomDown;
    public bool ZoomPressed;
    /// <summary>Weapon slot to select this frame, or -1 for none.</summary>
    public int SwitchSlot;

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
    Buy,
}

public static class GameplayOverlayInput
{
    /// <summary>
    /// Escape always wins when two keys arrive in the same frame. A completed
    /// match owns its own screen, so it never opens a gameplay overlay.
    /// </summary>
    public static GameplayOverlayAction Resolve(bool escapePressed, bool buyPressed, bool matchOver)
    {
        if (matchOver) return GameplayOverlayAction.None;
        if (escapePressed) return GameplayOverlayAction.Pause;
        return buyPressed ? GameplayOverlayAction.Buy : GameplayOverlayAction.None;
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
        f.CrouchPressed = Raylib.IsKeyPressed(KeyboardKey.LeftControl);
        f.CrouchTogglePressed = Raylib.IsKeyPressed(KeyboardKey.C);
        // Z is intentionally unassigned elsewhere in the live input map. It
        // toggles the safe low-prone stance; Ctrl and C retain their existing
        // held/toggle crouch semantics.
        f.ProneTogglePressed = Raylib.IsKeyPressed(KeyboardKey.Z);
        // The referenced movement controller uses a press edge, not a held key:
        // Shift cycles regular sprint -> tactical sprint -> off while moving forward.
        f.SprintPressed = Raylib.IsKeyPressed(KeyboardKey.LeftShift);
        // Keep HGO's original quiet-walk option without competing with sprint.
        f.Walk = Raylib.IsKeyDown(KeyboardKey.LeftAlt);
        f.FireDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        f.FirePressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
        f.InspectPressed = Raylib.IsKeyPressed(KeyboardKey.X);
        f.FireModePressed = Raylib.IsKeyPressed(KeyboardKey.V);
        f.Reload = Raylib.IsKeyPressed(KeyboardKey.R);
        f.ZoomDown = Raylib.IsMouseButtonDown(MouseButton.Right);
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
