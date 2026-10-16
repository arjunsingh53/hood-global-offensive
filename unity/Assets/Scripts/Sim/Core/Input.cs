using System;
using System.Numerics;

namespace HoodGlobalOffensive.Core
{
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

    /// <summary>Replays a canned input frame; used by the headless verification harness.</summary>
    public sealed class ScriptedInput
    {
        private readonly Func<int, InputFrame> _generator;

        public ScriptedInput(Func<int, InputFrame> generator) => _generator = generator;

        public InputFrame Poll(int tick) => _generator(tick);
    }
}
