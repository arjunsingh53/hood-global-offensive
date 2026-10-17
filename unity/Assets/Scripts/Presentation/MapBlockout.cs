using System.Collections.Generic;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Builds a primitive blockout of the current sim map (one scaled cube per
    /// brush, tinted with the brush's packed ARGB colour) so the Unity project is
    /// walkable before the real mesh pass. Runs in Start, after MatchDriver.Awake
    /// has created the Simulation, and is idempotent per Simulation instance.
    /// </summary>
    public sealed class MapBlockout : MonoBehaviour
    {
        [Header("Out fit")]
        [Tooltip("Root created under this transform; one scaled cube per sim brush.")]
        public Transform BrushRoot;

        [Tooltip("Colour used when a brush carries no recognisable ARGB tint.")]
        public Color FallbackBrushColor = new Color(0.28f, 0.30f, 0.34f);

        private GameMap? _builtMap;

        void Start()
        {
            MatchDriver? driver = GetComponent<MatchDriver>();
            if (driver == null || driver.Sim == null)
            {
                UnityEngine.Debug.LogWarning("MapBlockout: no MatchDriver with a Simulation on this GameObject.");
                enabled = false;
                return;
            }
            Build(driver.Sim);
        }

        public void Build(Simulation sim)
        {
            if (_builtMap == sim.Map) return;
            _builtMap = sim.Map;

            if (BrushRoot == null)
            {
                BrushRoot = new GameObject("MapBlockout BrushRoot").transform;
            }

            foreach (Brush brush in sim.Map.Brushes)
            {
                Vector3 size = MapBlockoutSize(brush.Box);
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f) continue;

                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"{brush.Kind} brush";
                cube.transform.SetParent(BrushRoot);
                cube.transform.position = ToUnity(brush.Box.Center);
                cube.transform.localScale = size;
                SetBrushColor(cube, brush.Color);
            }
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) =>
            new Vector3(v.X, v.Y, v.Z);

        private static Vector3 MapBlockoutSize(Aabb box)
        {
            System.Numerics.Vector3 s = box.Size;
            // Degenerate axes (the 1/64-th thin crates/floors show up as flat
            // boxes) still need bevel room or they z-fight to invisibility.
            return new Vector3(
                System.MathF.Max(System.MathF.Abs(s.X), Cfg.HalfWidth / 16f),
                System.MathF.Max(System.MathF.Abs(s.Y), Cfg.HalfWidth / 16f),
                System.MathF.Max(System.MathF.Abs(s.Z), Cfg.HalfWidth / 16f));
        }

        private void SetBrushColor(GameObject cube, uint argb)
        {
            Renderer meshRenderer = cube.GetComponent<Renderer>();
            if (meshRenderer == null) return;

            int r = (int)((argb >> 16) & 0xFFu);
            int g = (int)((argb >> 8) & 0xFFu);
            int b = (int)(argb & 0xFFu);
            int a = (int)((argb >> 24) & 0xFFu);

            // Fully opaque packed colours (always the case for map brushes) use the
            // stored tint; a zeroed tint falls back so an unauthored brush is visible.
            Color tint;
            if (argb == 0u) tint = FallbackBrushColor;
            else
            {
                float denominator = a <= 0 ? 255f : a;
                tint = new Color(r / denominator, g / denominator, b / denominator, 1f);
            }

            if (meshRenderer.material != null) meshRenderer.material.color = tint;
        }
    }
}
