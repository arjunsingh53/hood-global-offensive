using System.Collections.Generic;
using HoodGlobalOffensive.Core;using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// First-pass bot bodies: one yaw-facing capsule per sim actor (the local
    /// player is skipped — the camera is inside them), tinted red/cyan by team.
    /// Pure view state: it never writes to the simulation and only pushes data
    /// to scene objects in LateUpdate, after MatchDriver has fixed-stepped.
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        [Header("Scene wiring")]
        [Tooltip("Root for the auto-created capsules. Created when empty.")]
        public Transform ActorRoot;

        [Tooltip("Colour for actors sharing Sim.Player.Team.")]
        public Color AllyColor = new Color(0.18f, 0.56f, 0.86f);

        [Tooltip("Colour for actors on any other team.")]
        public Color EnemyColor = new Color(0.86f, 0.22f, 0.18f);

        private readonly Dictionary<int, GameObject> _views = new();
        private MatchDriver? _driver;

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

            foreach (Actor actor in sim.Actors)
            {
                if (actor.IsPlayer) continue;

                if (!actor.Alive)
                {
                    if (_views.TryGetValue(actor.Id, out GameObject? dead) && dead != null)
                    {
                        _views.Remove(actor.Id);
                        Object.Destroy(dead);
                    }
                    continue;
                }

                GameObject view = GetOrCreate(actor);
                Transform t = view.transform;
                t.position = new Vector3(
                    actor.Position.X,
                    actor.Position.Y + actor.HeadHeight * 0.5f,
                    actor.Position.Z);
                // Same yaw convention MatchDriver uses for the body: the sim's
                // forward rays feed LookRotation, so Unity resolves the sign.
                t.rotation = Quaternion.LookRotation(ToUnity(U.DirFromAngles(actor.Yaw, 0f)), Vector3.up);
                // Unity capsule primitive: radius 0.5, height 2.
                t.localScale = new Vector3(
                    Cfg.HalfWidth * 4f,
                    actor.HeadHeight * 0.5f,
                    Cfg.HalfWidth * 4f);

                Renderer meshRenderer = view.GetComponent<Renderer>();
                if (meshRenderer != null && meshRenderer.material != null)
                {
                    meshRenderer.material.color =
                        actor.Team == local.Team ? AllyColor : EnemyColor;
                }
            }
        }

        private GameObject GetOrCreate(Actor actor)
        {
            if (_views.TryGetValue(actor.Id, out GameObject? existing) && existing != null) return existing;

            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            view.name = actor.Name;
            if (ActorRoot == null) ActorRoot = new GameObject("ActorViews Root").transform;
            view.transform.SetParent(ActorRoot);
            _views[actor.Id] = view;
            return view;
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) =>
            new Vector3(v.X, v.Y, v.Z);
    }
}
