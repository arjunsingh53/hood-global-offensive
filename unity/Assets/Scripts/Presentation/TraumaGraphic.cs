using System;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Procedural uGUI mesh port of the reference critical-health trauma vignette
    /// (<c>Hud.DrawHealthVignette</c>): four GPU-interpolated edge gradient bands,
    /// four layers of peripheral chromatic fringe (red left, blue right), ten
    /// authored edge trauma marks (solid disc + faint halo ring), and the neutral
    /// desaturation wash. Driven by the sim's <c>Player.LowHealthTrauma</c>,
    /// <c>Player.TraumaPulse</c> and <c>Simulation.DamageFlash</c>. Zero allocations
    /// per frame, drawn as a single procedural canvas graphic.
    /// </summary>
    public sealed class TraumaGraphic : MaskableGraphic
    {
        private readonly struct TraumaMark
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Radius;

            public TraumaMark(float x, float y, float radius)
            {
                X = x;
                Y = y;
                Radius = radius;
            }
        }

        private static readonly TraumaMark[] TraumaMarks =
        {
            new TraumaMark(0.055f, 0.115f, 0.013f),
            new TraumaMark(0.118f, 0.060f, 0.008f),
            new TraumaMark(0.215f, 0.145f, 0.010f),
            new TraumaMark(0.932f, 0.095f, 0.012f),
            new TraumaMark(0.840f, 0.055f, 0.007f),
            new TraumaMark(0.962f, 0.225f, 0.010f),
            new TraumaMark(0.075f, 0.805f, 0.011f),
            new TraumaMark(0.188f, 0.930f, 0.009f),
            new TraumaMark(0.880f, 0.890f, 0.013f),
            new TraumaMark(0.955f, 0.765f, 0.008f),
        };

        private readonly UIVertex[] _quad = new UIVertex[4];
        private Simulation? _sim;

        public void Sync(Simulation sim)
        {
            _sim = sim;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Simulation? sim = _sim;
            if (sim == null) return;

            Player p = sim.Player;
            if (!p.Alive) return;

            float healthTrauma = p.LowHealthTrauma;
            float transientDamage = U.Clamp01(sim.DamageFlash) * 0.58f;
            float intensity = MathF.Max(healthTrauma, transientDamage);
            if (intensity <= 0.01f) return;

            float pulse = 0.66f + 0.34f * p.TraumaPulse;
            float vignette = U.Clamp01(intensity * pulse);

            const float HalfW = 960f;
            const float HalfH = 540f;

            // 1. Neutral desaturation wash ---------------------------------
            int washAlpha = (int)(healthTrauma * Cfg.LowHealthNeutralWashAlpha);
            if (washAlpha > 0)
            {
                Color32 wash = new Color32(142, 148, 154, (byte)washAlpha);
                Quad(vh,
                    new Vector3(-HalfW, -HalfH, 0f), wash,
                    new Vector3(-HalfW, HalfH, 0f), wash,
                    new Vector3(HalfW, HalfH, 0f), wash,
                    new Vector3(HalfW, -HalfH, 0f), wash);
            }

            // 2. Peripheral chromatic fringe (red left, blue right) --------
            if (healthTrauma > 0.01f)
            {
                const int layers = 4;
                for (int i = 0; i < layers; i++)
                {
                    float t = i / (float)layers;
                    int alpha = (int)(healthTrauma * (0.34f + pulse * 0.22f) * 42f * (1f - t));
                    if (alpha <= 0) continue;

                    float width = MathF.Max(1f, 2f + t * 4f) * 1.5f;
                    float inset = (t * 5f) * 1.5f;

                    // Left red strip
                    float x0L = -HalfW + inset;
                    float x1L = x0L + width;
                    Color32 red = new Color32(176, 34, 30, (byte)alpha);
                    Quad(vh,
                        new Vector3(x0L, -HalfH, 0f), red,
                        new Vector3(x0L, HalfH, 0f), red,
                        new Vector3(x1L, HalfH, 0f), red,
                        new Vector3(x1L, -HalfH, 0f), red);

                    // Right blue strip
                    float x1R = HalfW - inset;
                    float x0R = x1R - width;
                    Color32 blue = new Color32(42, 74, 140, (byte)alpha);
                    Quad(vh,
                        new Vector3(x0R, -HalfH, 0f), blue,
                        new Vector3(x0R, HalfH, 0f), blue,
                        new Vector3(x1R, HalfH, 0f), blue,
                        new Vector3(x1R, -HalfH, 0f), blue);
                }
            }

            // 3. Trauma marks (stable edge condition markers) --------------
            if (healthTrauma > 0.025f)
            {
                int alpha = (int)(healthTrauma * (0.48f + pulse * 0.32f) * 118f);
                if (alpha > 0)
                {
                    Color32 markColor = new Color32(102, 15, 16, (byte)alpha);
                    Color32 ringColor = new Color32(185, 58, 48, (byte)(alpha / 3));

                    for (int i = 0; i < TraumaMarks.Length; i++)
                    {
                        TraumaMark mark = TraumaMarks[i];
                        float radius = MathF.Max(3f, mark.Radius * 1080f);
                        float cx = (mark.X - 0.5f) * 1920f;
                        float cy = (0.5f - mark.Y) * 1080f;

                        Disc(vh, new Vector3(cx, cy, 0f), radius, markColor, 12);
                        Ring(vh, new Vector3(cx, cy, 0f), radius + 1.5f, 1.5f, ringColor, 14);
                    }
                }
            }

            // 4. Four-sided edge vignette gradients ------------------------
            int vigAlpha = (int)(vignette * 164f);
            if (vigAlpha > 0)
            {
                float bandH = 1080f * 0.255f;
                float bandW = 1920f * 0.225f;
                Color32 outer = new Color32(147, 18, 17, (byte)vigAlpha);
                Color32 clear = new Color32(147, 18, 17, 0);

                // Top band (fading downward)
                Quad(vh,
                    new Vector3(-HalfW, HalfH - bandH, 0f), clear,
                    new Vector3(-HalfW, HalfH, 0f), outer,
                    new Vector3(HalfW, HalfH, 0f), outer,
                    new Vector3(HalfW, HalfH - bandH, 0f), clear);

                // Bottom band (fading upward)
                Quad(vh,
                    new Vector3(-HalfW, -HalfH, 0f), outer,
                    new Vector3(-HalfW, -HalfH + bandH, 0f), clear,
                    new Vector3(HalfW, -HalfH + bandH, 0f), clear,
                    new Vector3(HalfW, -HalfH, 0f), outer);

                // Left band (fading rightward)
                Quad(vh,
                    new Vector3(-HalfW, -HalfH, 0f), outer,
                    new Vector3(-HalfW, HalfH, 0f), outer,
                    new Vector3(-HalfW + bandW, HalfH, 0f), clear,
                    new Vector3(-HalfW + bandW, -HalfH, 0f), clear);

                // Right band (fading leftward)
                Quad(vh,
                    new Vector3(HalfW - bandW, -HalfH, 0f), clear,
                    new Vector3(HalfW - bandW, HalfH, 0f), clear,
                    new Vector3(HalfW, HalfH, 0f), outer,
                    new Vector3(HalfW, -HalfH, 0f), outer);
            }
        }

        private void Quad(VertexHelper vh,
            Vector3 a, Color32 ca,
            Vector3 b, Color32 cb,
            Vector3 c, Color32 cc,
            Vector3 d, Color32 cd)
        {
            _quad[0].position = a;
            _quad[0].color = ca;
            _quad[1].position = b;
            _quad[1].color = cb;
            _quad[2].position = c;
            _quad[2].color = cc;
            _quad[3].position = d;
            _quad[3].color = cd;
            vh.AddUIVertexQuad(_quad);
        }

        private void Tri(VertexHelper vh, Vector3 a, Vector3 b, Vector3 c, Color32 color)
        {
            vh.AddVert(new UIVertex { position = a, color = color });
            vh.AddVert(new UIVertex { position = b, color = color });
            vh.AddVert(new UIVertex { position = c, color = color });
            int start = vh.currentVertCount - 3;
            vh.AddTriangle(start, start + 1, start + 2);
        }

        private void Disc(VertexHelper vh, Vector3 center, float radius, Color32 color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments * 2f * MathF.PI;
                float t1 = (i + 1) / (float)segments * 2f * MathF.PI;
                Vector3 p0 = center + new Vector3(MathF.Cos(t0) * radius, MathF.Sin(t0) * radius, 0f);
                Vector3 p1 = center + new Vector3(MathF.Cos(t1) * radius, MathF.Sin(t1) * radius, 0f);
                Tri(vh, center, p0, p1, color);
            }
        }

        private void Ring(VertexHelper vh, Vector3 center, float radius, float width, Color32 color, int segments)
        {
            float half = width * 0.5f;
            float rOuter = radius + half;
            float rInner = radius - half;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments * 2f * MathF.PI;
                float t1 = (i + 1) / (float)segments * 2f * MathF.PI;
                Vector3 a = center + new Vector3(MathF.Cos(t0) * rOuter, MathF.Sin(t0) * rOuter, 0f);
                Vector3 b = center + new Vector3(MathF.Cos(t1) * rOuter, MathF.Sin(t1) * rOuter, 0f);
                Vector3 c = center + new Vector3(MathF.Cos(t1) * rInner, MathF.Sin(t1) * rInner, 0f);
                Vector3 d = center + new Vector3(MathF.Cos(t0) * rInner, MathF.Sin(t0) * rInner, 0f);
                Quad(vh, a, color, b, color, c, color, d, color);
            }
        }
    }
}
