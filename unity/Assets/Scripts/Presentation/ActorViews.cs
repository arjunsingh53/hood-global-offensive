using System.Collections.Generic;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Blockout bot bodies: one yaw-facing capsule per sim actor (the local player
    /// is skipped - the camera is inside them), tinted by team, plus the reference's
    /// per-actor box-built head silhouette (<c>CharacterModels.DrawCapsuleHeadAccessory</c>)
    /// so actors read apart at a glance before the real rigs land.
    /// Pure view state: it never writes to the simulation and only pushes data
    /// to scene objects in LateUpdate, after MatchDriver has fixed-stepped.
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        [Header("Scene wiring")]
        [Tooltip("Root for the auto-created actor bodies. Created when empty.")]
        public Transform ActorRoot;

        [Tooltip("Colour for actors sharing Sim.Player.Team.")]
        public Color AllyColor = new Color(0.18f, 0.56f, 0.86f);

        [Tooltip("Colour for actors on any other team.")]
        public Color EnemyColor = new Color(0.86f, 0.22f, 0.18f);

        // Reference accessory palette (CharacterModels.DrawCapsuleHeadAccessory).
        private static readonly Color Warm = Rgb(255, 142, 37);
        private static readonly Color Cool = Rgb(35, 178, 209);
        private static readonly Color Dark = Rgb(26, 43, 54);

        private readonly Dictionary<int, ActorView> _views = new();
        private MatchDriver? _driver;
        private Simulation? _builtSim;

        /// <summary>One actor's scene objects: yaw root, body capsule, head-art pivot.</summary>
        private sealed class ActorView
        {
            public GameObject Root = null!;
            public Transform Body = null!;
            public Renderer BodyRenderer = null!;
            public Transform HeadArt = null!;
        }

        /// <summary>
        /// Head silhouettes, verbatim from the reference: (x, above head, z, size, roll, colour)
        /// in the model frame. The model frame is yawed 180 from the actor frame (the reference
        /// draw uses the HGO yaw offset), which <see cref="ActorView.HeadArt"/> bakes in once.
        /// </summary>
        private readonly struct Block
        {
            public readonly float X, Up, Z;
            public readonly Vector3 Size;
            public readonly float Roll;
            public readonly Color Colour;

            public Block(float x, float up, float z, Vector3 size, float roll, Color colour)
            {
                X = x; Up = up; Z = z; Size = size; Roll = roll; Colour = colour;
            }
        }

        // Style index is Math.Abs(actor.Id) % 4, matching the reference.
        private static readonly Block[][] Silhouettes =
        {
            // 0: compact pixel cone.
            new[]
            {
                new Block(0f, 3f, 0f, new Vector3(18f, 5f, 18f), 0f, Warm),
                new Block(0f, 7f, 0f, new Vector3(12f, 5f, 12f), 0f, Warm),
                new Block(0f, 11f, 0f, new Vector3(6f, 4f, 6f), 0f, Warm),
            },
            // 1: two leaning horns on a low visor block.
            new[]
            {
                new Block(-9f, 6f, 0f, new Vector3(5f, 15f, 6f), -24f, Warm),
                new Block(9f, 6f, 0f, new Vector3(5f, 15f, 6f), 24f, Warm),
                new Block(0f, 1.5f, 0f, new Vector3(22f, 3f, 12f), 0f, Dark),
            },
            // 2: square low-poly halo above the head.
            new[]
            {
                new Block(0f, 8f, -9f, new Vector3(27f, 2.5f, 3f), 0f, Cool),
                new Block(0f, 8f, 9f, new Vector3(27f, 2.5f, 3f), 0f, Cool),
                new Block(-12f, 8f, 0f, new Vector3(3f, 2.5f, 21f), 0f, Cool),
                new Block(12f, 8f, 0f, new Vector3(3f, 2.5f, 21f), 0f, Cool),
            },
            // 3: top hat - brim, crown, bright hatband.
            new[]
            {
                new Block(0f, 1.5f, 0f, new Vector3(26f, 3f, 24f), 0f, Dark),
                new Block(0f, 8f, 0f, new Vector3(16f, 12f, 16f), 0f, Dark),
                new Block(0f, 5.5f, 0f, new Vector3(17f, 2f, 17f), 0f, Warm),
            },
        };

        void Awake()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("ActorViews: no MatchDriver on this GameObject; disabling.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            Simulation? sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;
            Player local = sim.Player;

            // A restarted match builds a fresh Simulation (and can change the map or
            // bot count), so every view from the old match is stale - actor ids are
            // only unique within one simulation.
            if (!ReferenceEquals(sim, _builtSim))
            {
                ClearViews();
                _builtSim = sim;
            }

            foreach (Actor actor in sim.Actors)
            {
                if (actor.IsPlayer) continue;

                if (!actor.Alive)
                {
                    if (_views.TryGetValue(actor.Id, out ActorView? dead))
                    {
                        _views.Remove(actor.Id);
                        Object.Destroy(dead.Root);
                    }
                    continue;
                }

                ActorView view = GetOrCreate(actor);
                Transform t = view.Root.transform;
                t.position = new Vector3(actor.Position.X, actor.Position.Y, actor.Position.Z);
                // Same yaw convention MatchDriver uses for the body: the sim's
                // forward rays feed LookRotation, so Unity resolves the sign.
                t.rotation = Quaternion.LookRotation(ToUnity(U.DirFromAngles(actor.Yaw, 0f)), Vector3.up);

                // Unity capsule primitive: radius 0.5, height 2, so its local scale is
                // (diameter, height / 2, diameter). Cfg.HalfWidth is the actor's half
                // width, hence a diameter of 2 * HalfWidth.
                float height = actor.HeadHeight;
                view.Body.localPosition = new Vector3(0f, height * 0.5f, 0f);
                view.Body.localScale = new Vector3(Cfg.HalfWidth * 2f, height * 0.5f, Cfg.HalfWidth * 2f);

                // The head art hangs off the top of the body and follows the crouch
                // height without being squashed by the capsule scale.
                view.HeadArt.localPosition = new Vector3(0f, height, 0f);

                if (view.BodyRenderer != null && view.BodyRenderer.material != null)
                {
                    view.BodyRenderer.material.color =
                        actor.Team == local.Team ? AllyColor : EnemyColor;
                }
            }
        }

        private void ClearViews()
        {
            foreach (ActorView view in _views.Values)
            {
                if (view.Root != null) Object.Destroy(view.Root);
            }
            _views.Clear();
        }

        private ActorView GetOrCreate(Actor actor)
        {
            if (_views.TryGetValue(actor.Id, out ActorView? existing)) return existing;

            if (ActorRoot == null) ActorRoot = new GameObject("ActorViews Root").transform;

            ActorView view = new ActorView();

            view.Root = new GameObject(actor.Name);
            view.Root.transform.SetParent(ActorRoot);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = $"{actor.Name} body";
            body.transform.SetParent(view.Root.transform);
            view.Body = body.transform;
            Renderer? bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null) view.BodyRenderer = bodyRenderer;

            GameObject headArt = new GameObject($"{actor.Name} head art");
            headArt.transform.SetParent(view.Root.transform);
            // The reference draws the head blocks in the model frame, which is yawed
            // 180 from the actor frame by the HGO yaw offset; bake that in once so the
            // block coordinates below are the reference's own numbers.
            headArt.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            view.HeadArt = headArt.transform;

            BuildHeadArt(view.HeadArt, System.Math.Abs(actor.Id) % Silhouettes.Length);

            _views[actor.Id] = view;
            return view;
        }

        private static void BuildHeadArt(Transform parent, int style)
        {
            Block[] blocks = Silhouettes[style];
            for (int i = 0; i < blocks.Length; i++)
            {
                Block block = blocks[i];
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Head art {style}.{i}";
                cube.transform.SetParent(parent);
                cube.transform.localPosition = new Vector3(block.X, block.Up, block.Z);
                cube.transform.localScale = block.Size;
                if (block.Roll != 0f)
                {
                    cube.transform.localRotation = Quaternion.Euler(0f, 0f, block.Roll);
                }

                Renderer? renderer = cube.GetComponent<Renderer>();
                if (renderer != null && renderer.material != null) renderer.material.color = block.Colour;
            }
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        private static Vector3 ToUnity(System.Numerics.Vector3 v) =>
            new Vector3(v.X, v.Y, v.Z);
    }
}
