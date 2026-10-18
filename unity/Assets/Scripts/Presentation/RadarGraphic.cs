using System;
using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using UnityEngine;
using UnityEngine.UI;
using NVector3 = System.Numerics.Vector3;

namespace HoodGlobalOffensive.Presentation
{
    // The player-centred rotating scanner from the reference HUD (DrawRadar).
    // All geometry is ported in the reference's logical (1280x720) units with the
    // scanner centre kept at (91, 89); Local() applies the 1.5x canvas scale and
    // flips into the rect's centre-origin y-up space when emitting the mesh.
    public sealed class RadarGraphic : MaskableGraphic
    {
        private const float CenterX = 91f;
        private const float CenterY = 89f;
        private const float CanvasScale = 1.5f;
        private const float Radius = 67f;
        private const float WorldRange = 720f;

        private readonly UIVertex[] _quad = new UIVertex[4];
        private Simulation? _sim;
        private float _opacity = 1f;

        public void Sync(Simulation sim, float opacity)
        {
            _sim = sim;
            _opacity = opacity;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Simulation? sim = _sim;
            if (sim == null) return;

            float opacity = _opacity;
            float scale = Radius / WorldRange;
            Player player = sim.Player;
            P center = new(CenterX, CenterY);

            Disc(vh, center, Radius + 2f, C(0, 0, 0, 168, opacity), 48);
            Disc(vh, center, Radius, C(9, 16, 23, 226, opacity), 48);
            Grid(vh, center, opacity);

            foreach (Brush brush in sim.Map.Brushes)
            {
                if (brush.Kind == SurfaceKind.Floor) continue;

                P a = WorldToRadar(new NVector3(brush.Box.Min.X, 0f, brush.Box.Min.Z), player.Position, player.Yaw, center, scale);
                P b = WorldToRadar(new NVector3(brush.Box.Max.X, 0f, brush.Box.Min.Z), player.Position, player.Yaw, center, scale);
                P c = WorldToRadar(new NVector3(brush.Box.Max.X, 0f, brush.Box.Max.Z), player.Position, player.Yaw, center, scale);
                P d = WorldToRadar(new NVector3(brush.Box.Min.X, 0f, brush.Box.Max.Z), player.Position, player.Yaw, center, scale);
                Color32 geometry = brush.Kind == SurfaceKind.Crate
                    ? C(226, 190, 108, 116, opacity)
                    : brush.Kind == SurfaceKind.Metal
                        ? C(114, 194, 226, 105, opacity)
                        : C(204, 212, 216, 80, opacity);

                Segment(vh, a, b, center, Radius - 1f, geometry);
                Segment(vh, b, c, center, Radius - 1f, geometry);
                Segment(vh, c, d, center, Radius - 1f, geometry);
                Segment(vh, d, a, center, Radius - 1f, geometry);
            }

            // Cabinets are fixed, high-value map objectives: always shown.
            foreach (UpgradeStation station in sim.Stations)
            {
                P point = WorldToRadar(station.Position, player.Position, player.Yaw, center, scale);
                if ((point - center).Length() <= Radius - 6f)
                {
                    Disc(vh, point, 3.2f, C(255, 186, 56, 235, opacity), 10);
                    Ring(vh, point, 6f, 1f, C(255, 186, 56, 110, opacity), 14);
                }
            }

            NVector3 eye = player.EyePosition;
            foreach (Actor actor in sim.Actors)
            {
                if (actor.IsPlayer || !actor.Alive) continue;

                NVector3 delta = actor.Position - player.Position;
                float distance = U.FlatLength(delta);
                if (distance > 1500f) continue;

                bool visible = sim.Map.LineOfSight(eye, actor.EyePosition) && distance < 1100f;
                float contactFade = U.Clamp01(1f - distance / 1500f);
                Color32 contact = visible
                    ? C(236, 74, 62, 120 + 135 * contactFade, opacity)
                    : C(236, 74, 62, 70 * contactFade, opacity);

                P contactPoint = WorldToRadar(actor.Position, player.Position, player.Yaw, center, scale);
                P fromCenter = contactPoint - center;
                float contactRadius = fromCenter.Length();
                if (contactRadius <= Radius - 7f)
                {
                    Disc(vh, contactPoint, visible ? 3.4f : 2.5f, contact, 8);
                    if (visible)
                        Ring(vh, contactPoint, 6f, 1f, C(236, 74, 62, 90, opacity), 14);
                }
                else if (contactRadius > 1e-4f)
                {
                    // Preserve map-wide awareness without filling the small radar
                    // with distant dots: an edge chevron identifies direction.
                    P direction = fromCenter * (1f / contactRadius);
                    Chevron(vh, center + direction * (Radius - 7f), direction, contact);
                }
            }

            // The local player always points up because the terrain rotates under
            // the radar. This stays quick to parse while swinging on hooks.
            Tri(vh, center + new P(0f, -7.5f), center + new P(4.75f, 4.5f), center + new P(-4.75f, 4.5f),
                C(120, 255, 140, 235, opacity));
            Ring(vh, center, 9f, 1f, C(120, 255, 140, 70, opacity), 16);
            Ring(vh, center, Radius, 1f, C(230, 240, 244, 102, opacity), 44);
            Ring(vh, center, Radius + 2f, 1f, C(112, 205, 228, 92, opacity), 44);
        }

        private void Grid(VertexHelper vh, P center, float opacity)
        {
            Color32 grid = C(184, 202, 206, 24, opacity);
            Ring(vh, center, Radius * 0.50f, 1f, grid, 40);
            Ring(vh, center, Radius * 0.75f, 1f, grid, 40);
            Line(vh, new P(center.X - Radius, center.Y), new P(center.X + Radius, center.Y), 1f, grid);
            Line(vh, new P(center.X, center.Y - Radius), new P(center.X, center.Y + Radius), 1f, grid);
        }

        private static P WorldToRadar(NVector3 world, NVector3 playerPosition, float playerYaw, P center, float scale)
        {
            NVector3 delta = world - playerPosition;
            NVector3 right = U.RightFromYaw(playerYaw);
            NVector3 forward = U.DirFromAngles(playerYaw, 0f);
            float localRight = delta.X * right.X + delta.Z * right.Z;
            float localForward = delta.X * forward.X + delta.Z * forward.Z;
            return new P(center.X + localRight * scale, center.Y - localForward * scale);
        }

        private void Segment(VertexHelper vh, P a, P b, P center, float radius, Color32 color)
        {
            if (!TryClipSegmentToCircle(a, b, center, radius, out P clippedA, out P clippedB)) return;
            Line(vh, clippedA, clippedB, 1f, color);
        }

        private static bool TryClipSegmentToCircle(P a, P b, P center, float radius, out P clippedA, out P clippedB)
        {
            P delta = b - a;
            P relative = a - center;
            float aa = P.Dot(delta, delta);
            if (aa < 1e-6f)
            {
                bool inside = relative.LengthSquared() <= radius * radius;
                clippedA = a;
                clippedB = b;
                return inside;
            }

            float bb = 2f * P.Dot(relative, delta);
            float cc = P.Dot(relative, relative) - radius * radius;
            float discriminant = bb * bb - 4f * aa * cc;
            if (discriminant < 0f)
            {
                clippedA = default;
                clippedB = default;
                return false;
            }

            float root = MathF.Sqrt(discriminant);
            float enter = MathF.Max(0f, (-bb - root) / (2f * aa));
            float exit = MathF.Min(1f, (-bb + root) / (2f * aa));
            if (enter > exit)
            {
                clippedA = default;
                clippedB = default;
                return false;
            }

            clippedA = a + delta * enter;
            clippedB = a + delta * exit;
            return true;
        }

        private void Chevron(VertexHelper vh, P point, P direction, Color32 color)
        {
            P perpendicular = new(-direction.Y, direction.X);
            Tri(vh, point + direction * 3.5f,
                point - direction * 2.5f + perpendicular * 2.8f,
                point - direction * 2.5f - perpendicular * 2.8f,
                color);
        }

        private UnityEngine.Vector3 Local(P p) => new((p.X - CenterX) * CanvasScale, -(p.Y - CenterY) * CanvasScale, 0f);

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

        private void Tri(VertexHelper vh, P a, P b, P c, Color32 color)
        {
            vh.AddVert(new UIVertex { position = Local(a), color = color });
            vh.AddVert(new UIVertex { position = Local(b), color = color });
            vh.AddVert(new UIVertex { position = Local(c), color = color });
            int start = vh.currentVertCount - 3;
            vh.AddTriangle(start, start + 1, start + 2);
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

        private void Disc(VertexHelper vh, P center, float radius, Color32 color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments * 2f * MathF.PI;
                float t1 = (i + 1) / (float)segments * 2f * MathF.PI;
                Tri(vh, center, center + Circle(t0) * radius, center + Circle(t1) * radius, color);
            }
        }

        private void Ring(VertexHelper vh, P center, float radius, float width, Color32 color, int segments)
        {
            float half = width * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments * 2f * MathF.PI;
                float t1 = (i + 1) / (float)segments * 2f * MathF.PI;
                Quad(vh, center + Circle(t0) * (radius + half), center + Circle(t1) * (radius + half),
                    center + Circle(t1) * (radius - half), center + Circle(t0) * (radius - half), color);
            }
        }

        private static P Circle(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

        private static Color32 C(float r, float g, float b, float a, float opacity) => new(
            (byte)ClampToByte(r),
            (byte)ClampToByte(g),
            (byte)ClampToByte(b),
            (byte)ClampToByte(a * U.Clamp01(opacity)));

        private static float ClampToByte(float value) => value < 0f ? 0f : (value > 255f ? 255f : value);

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

            public float LengthSquared() => X * X + Y * Y;

            public static float Dot(P a, P b) => a.X * b.X + a.Y * b.Y;

            public static P operator +(P a, P b) => new(a.X + b.X, a.Y + b.Y);

            public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y);

            public static P operator *(P a, float scalar) => new(a.X * scalar, a.Y * scalar);
        }
    }
}
