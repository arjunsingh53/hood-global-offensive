using System;
using System.Collections.Generic;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// First-pass world combat effects: pooled primitive stand-ins for the sim's
    /// effect lists (tracers, impact debris and blood mist, blood droplets, and
    /// surface/blood decals), synced in LateUpdate after MatchDriver has
    /// fixed-stepped. Mirrors the reference draw passes
    /// (<c>Renderer.DrawTracers</c> / <c>DrawParticles</c> / <c>DrawDecalList</c>):
    /// tracers stretch along From→To with the reference's 0.08 minimum width,
    /// particles and droplets use the reference's life-driven size curves, and
    /// decals flatten a cube along the dominant normal axis exactly like
    /// <c>DrawDecalList</c> does. The sim owns every lifetime — it ages and
    /// prunes each list inside <c>Simulation.Update</c> — so this view only
    /// reads. Divergence: primitive default materials are opaque, so the
    /// reference's alpha fades are not reproduced; the scale curves carry the
    /// life cue where the reference already scales, and tracers/decals simply
    /// vanish when the sim prunes them (an art-pass material can restore fades).
    /// </summary>
    public sealed class EffectsSync : MonoBehaviour
    {
        [Header("Scene wiring")]
        [Tooltip("Root for the pooled effect primitives. Created when empty.")]
        public Transform EffectRoot;

        private MatchDriver? _driver;

        // One pool per primitive kind; index i of a pool shows item i of the
        // current sim list. Items are transient, so remapping identities between
        // frames is invisible on blockout primitives.
        private readonly List<GameObject> _tracers = new();
        private readonly List<GameObject> _particles = new();
        private readonly List<GameObject> _mists = new();
        private readonly List<GameObject> _droplets = new();
        private readonly List<GameObject> _decals = new();

        private int _tracersShown;
        private int _particlesShown;
        private int _mistsShown;
        private int _dropletsShown;
        private int _decalsShown;

        void Awake()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("EffectsSync: no MatchDriver on this GameObject; disabling.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            Simulation? sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;

            SyncTracers(sim);
            SyncParticles(sim);
            SyncDroplets(sim);
            SyncDecals(sim);
        }

        private void SyncTracers(Simulation sim)
        {
            int shown = 0;
            foreach (Tracer tracer in sim.Tracers)
            {
                Vector3 from = ToUnity(tracer.From);
                Vector3 delta = ToUnity(tracer.To) - from;
                float length = MathF.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
                // The reference skips degenerate trails (lengthSquared < 0.0001)
                // before drawing anything.
                if (length < 0.01f) continue;

                GameObject view = Get(_tracers, shown, PrimitiveType.Cube, "Tracer");
                float width = MathF.Max(0.08f, tracer.Width);
                view.transform.position = from + delta * 0.5f;
                view.transform.rotation = Quaternion.LookRotation(delta * (1f / length), Vector3.up);
                view.transform.localScale = new Vector3(width, width, length);
                SetColor(view, tracer.Color);
                view.SetActive(true);
                shown++;
            }
            HideExtra(_tracers, shown, ref _tracersShown);
        }

        private void SyncParticles(Simulation sim)
        {
            int debris = 0;
            int mists = 0;
            // Both lists go through the reference's shared particle drawer.
            ShowParticleList(sim.Particles, ref debris, ref mists);
            ShowParticleList(sim.BloodMists, ref debris, ref mists);
            HideExtra(_particles, debris, ref _particlesShown);
            HideExtra(_mists, mists, ref _mistsShown);
        }

        private void ShowParticleList(IReadOnlyList<Particle> particles, ref int debris, ref int mists)
        {
            foreach (Particle p in particles)
            {
                float t = U.Clamp01(p.Life / p.MaxLife);
                float size = p.Size * (0.5f + t * 0.5f);
                if (p.Kind == ParticleKind.BloodMist)
                {
                    // Reference DrawParticleList: a faceted puff (sphere) whose
                    // radius widens as the mist dissipates.
                    float radius = size * (1f + (1f - t) * 0.55f) * 0.5f;
                    GameObject puff = Get(_mists, mists, PrimitiveType.Sphere, "Blood mist");
                    puff.transform.position = ToUnity(p.Position);
                    puff.transform.localScale = new Vector3(radius * 2f, radius * 2f, radius * 2f);
                    SetColor(puff, p.Color);
                    puff.SetActive(true);
                    mists++;
                }
                else
                {
                    GameObject chip = Get(_particles, debris, PrimitiveType.Cube, "Debris");
                    chip.transform.position = ToUnity(p.Position);
                    chip.transform.localScale = new Vector3(size, size, size);
                    SetColor(chip, p.Color);
                    chip.SetActive(true);
                    debris++;
                }
            }
        }

        private void SyncDroplets(Simulation sim)
        {
            int shown = 0;
            foreach (BloodDroplet d in sim.BloodDroplets)
            {
                float t = U.Clamp01(d.Life / d.MaxLife);
                float size = d.Size * (0.45f + t * 0.55f);
                GameObject view = Get(_droplets, shown, PrimitiveType.Cube, "Blood droplet");
                view.transform.position = ToUnity(d.Position);
                view.transform.localScale = new Vector3(size, size, size);
                SetColor(view, d.Color);
                view.SetActive(true);
                shown++;
            }
            HideExtra(_droplets, shown, ref _dropletsShown);
        }

        private void SyncDecals(Simulation sim)
        {
            int shown = 0;
            shown = ShowDecalList(sim.Decals, shown);
            shown = ShowDecalList(sim.BloodSplats, shown);
            HideExtra(_decals, shown, ref _decalsShown);
        }

        private int ShowDecalList(IReadOnlyList<Decal> decals, int shown)
        {
            foreach (Decal d in decals)
            {
                // Reference DrawDecalList: the cube flattens along the dominant
                // normal axis, so it stays axis-aligned (no rotation needed).
                Vector3 n = ToUnity(d.Normal);
                float ax = MathF.Abs(n.x), ay = MathF.Abs(n.y), az = MathF.Abs(n.z);
                Vector3 size;
                if (ay >= ax && ay >= az) size = new Vector3(d.Size, 0.35f, d.Size);
                else if (ax >= az) size = new Vector3(0.35f, d.Size, d.Size);
                else size = new Vector3(d.Size, d.Size, 0.35f);

                GameObject view = Get(_decals, shown, PrimitiveType.Cube, "Decal");
                view.transform.position = ToUnity(d.Position);
                view.transform.rotation = Quaternion.identity;
                view.transform.localScale = size;
                SetColor(view, d.Color);
                view.SetActive(true);
                shown++;
            }
            return shown;
        }

        private GameObject Get(List<GameObject> pool, int index, PrimitiveType type, string baseName)
        {
            while (pool.Count <= index)
            {
                if (EffectRoot == null) EffectRoot = new GameObject("EffectsSync Root").transform;
                GameObject view = GameObject.CreatePrimitive(type);
                view.name = $"{baseName} {pool.Count}";
                view.transform.SetParent(EffectRoot);
                pool.Add(view);
            }
            return pool[index];
        }

        private static void HideExtra(List<GameObject> pool, int shown, ref int previouslyShown)
        {
            for (int i = shown; i < previouslyShown; i++) pool[i].SetActive(false);
            previouslyShown = shown;
        }

        private static void SetColor(GameObject view, uint argb)
        {
            Renderer meshRenderer = view.GetComponent<Renderer>();
            if (meshRenderer == null || meshRenderer.material == null) return;
            meshRenderer.material.color = Unpack(argb);
        }

        /// <summary>U.Argb packs A&lt;&lt;24 | R&lt;&lt;16 | G&lt;&lt;8 | B; the opaque blockout drops alpha.</summary>
        private static Color Unpack(uint argb)
        {
            float r = ((argb >> 16) & 0xFFu) / 255f;
            float g = ((argb >> 8) & 0xFFu) / 255f;
            float b = (argb & 0xFFu) / 255f;
            return new Color(r, g, b, 1f);
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) =>
            new Vector3(v.X, v.Y, v.Z);
    }
}
