using System;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Reference <c>Hud.DrawHitmarker</c> drawn as geometry instead of a glyph:
    /// four diagonal ticks between an inner and an outer radius, stroked
    /// <c>max(1, 1.35)</c> reference units wide, riding the attack/release scale
    /// envelope (0.80 → 1.18 → 0.86) and the 0.68-power fade that the
    /// ELIMINATED / HEADSHOT labels share. Only a headshot tints the ticks
    /// (255, 86, 70 @ 245 × fade); a plain hit reads (245, 245, 245 @ 230 × fade).
    /// </summary>
    public sealed class HitmarkerGraphic : MaskableGraphic
    {
        // Reference logical (1280x720) units x the 1.5x canvas scale.
        private const float CanvasScale = 1.5f;

        private readonly UIVertex[] _quad = new UIVertex[4];
        private float _scale = 1f;
        private float _fade;
        private bool _headshot;
        private bool _visible;

        /// <summary>Pushes the shared hitmarker envelope; see <c>HudSync</c>.</summary>
        public void Sync(float scale, float fade, bool headshot, bool visible)
        {
            _scale = scale;
            _fade = fade;
            _headshot = headshot;
            _visible = visible;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_visible || _fade <= 0f) return;

            float inner = 6f * _scale * CanvasScale;
            float outer = 14f * _scale * CanvasScale;
            float width = MathF.Max(1f, 1.35f * _scale) * CanvasScale;
            Color32 color = _headshot
                ? new Color32(255, 86, 70, (byte)(245f * _fade))
                : new Color32(245, 245, 245, (byte)(230f * _fade));

            // The reference draws in screen-down space; this rect is y-up, so
            // every end's y negates (same flip as the damage-numbers pass).
            // The four ticks are symmetric, so mirroring keeps the shape exact.
            Line(vh, new P(-outer, outer), new P(-inner, inner), width, color);
            Line(vh, new P(inner, -inner), new P(outer, -outer), width, color);
            Line(vh, new P(-outer, -outer), new P(-inner, -inner), width, color);
            Line(vh, new P(inner, outer), new P(outer, inner), width, color);
        }

        private void Line(VertexHelper vh, P a, P b, float width, Color32 color)
        {
            P delta = b - a;
            float length = delta.Length();
            if (length < 1e-6f) return;

            P direction = delta * (1f / length);
            P perpendicular = new(-direction.Y, direction.X);
            float half = width * 0.5f;
            Quad(vh, a + perpendicular * half, b + perpendicular * half,
                b - perpendicular * half, a - perpendicular * half, color);
        }

        private void Quad(VertexHelper vh, P a, P b, P c, P d, Color32 color)
        {
            _quad[0].position = Local(a);
            _quad[0].color = color;
            _quad[1].position = Local(b);
            _quad[1].color = color;
            _quad[2].position = Local(c);
            _quad[2].color = color;
            _quad[3].position = Local(d);
            _quad[3].color = color;
            vh.AddUIVertexQuad(_quad);
        }

        // Local space: pivot-centred, y-up, canvas pixels.
        private static Vector3 Local(P p) => new(p.X, p.Y, 0f);

        private readonly struct P
        {
            public readonly float X;
            public readonly float Y;

            public P(float x, float y)
            {
                X = x;
                Y = y;
            }

            public float Length() => MathF.Sqrt(X * X + Y * Y);

            public static P operator +(P a, P b) => new(a.X + b.X, a.Y + b.Y);
            public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y);
            public static P operator *(P a, float scalar) => new(a.X * scalar, a.Y * scalar);
        }
    }
}
