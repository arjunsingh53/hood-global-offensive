using System;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// Applies the sim's view-model springs and locomotion feedback to a
    /// camera-local <see cref="ViewModelRoot"/> in Unity, porting the composition
    /// the raylib reference performs in <c>Renderer.DrawViewModel</c> (sway, bob,
    /// land dip, reload/deploy dip, view kick, the two second-order springs and
    /// the small hip yaw). All values are read-only: it never writes to the
    /// simulation, so parity stays intact — springs are ticked by the sim itself
    /// inside <c>Simulation.Update</c>.
    ///
    /// Mesh-specific placement (per-weapon art offsets, iron-sight targets,
    /// muzzle flash anchors) is intentionally NOT ported yet: people assign the
    /// rig glb's to <see cref="ViewModelRoot"/> in the editor and calibrate
    /// against captures, like the reference did.
    /// </summary>
    public sealed class ViewModelSync : MonoBehaviour
    {
        [Header("Scene wiring")]
        [Tooltip("Camera-local transform holding the mesh/prefab (child of PlayerCamera).")]
        public Transform ViewModelRoot;

        [Tooltip("Optional separate view-model camera; its FOV is pinned to Cfg.ViewModelFov.")]
        public Camera ViewModelCamera;

        [Header("Placement")]
        [Tooltip("Extra multiplier on top of the per-class pose scale.")]
        public float ModelScale = 1f;

        private MatchDriver? _driver;

        void Awake()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("ViewModelSync: no MatchDriver on this GameObject; disabling.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            Simulation? sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;
            if (ViewModelRoot == null)
            {
                Debug.LogWarning("ViewModelSync: assign a ViewModelRoot to show the view-model.");
                enabled = false;
                return;
            }

            if (ViewModelCamera != null) ViewModelCamera.fieldOfView = Cfg.ViewModelFov;
            if (sim.MatchOver)
            {
                Show(false);
                return;
            }

            Player p = sim.Player;
            Weapon weapon = p.Current;
            WeaponDef def = weapon.Def;

            // Only magnified optics stow the model behind the scope mask; iron
            // sights stay and ride up with ZoomBlend (reference rule, HUD pass).
            Show(!(def.HasScope && p.ZoomBlend > 0.70f));

            float reloadDip = ReloadDip(sim, weapon, def);
            float deployDip = DeployDip(sim, weapon, def);
            float bobX = MathF.Sin(p.BobPhase) * Cfg.ViewModelBobSide * p.BobAmount;
            float bobY = MathF.Cos(p.BobPhase * 2f) * Cfg.ViewModelBobVertical * p.BobAmount;
            ViewModelPose pose = PoseFor(def);
            float ironSight = def.HasCalibratedIronSights ? p.ZoomBlend : 0f;

            // The reference composes everything in its view basis (-Z forward,
            // +X right, +Y up). Keep those semantics here, then map them to the
            // Unity camera basis (+Z forward) when applying to the transform.
            Vector3 offset = AnchorFor(pose);
            offset.x += p.Sway.X * 2.4f + bobX;
            offset.y += -p.Sway.Y * 2.4f + bobY - p.LandDip * 6f - reloadDip * 3f - deployDip * 5.5f;
            offset.z += p.ViewKick * 0.22f;
            offset += ToUnity(p.ViewModelOffsetSpring.Value);

            ViewModelRoot.localPosition = FromViewBasis(offset);
            ViewModelRoot.localScale = new Vector3(
                pose.Scale * ModelScale, pose.Scale * ModelScale, pose.Scale * ModelScale);
            ViewModelRoot.localRotation = FlourishRotation(p, reloadDip, ironSight);
        }

        void Show(bool visible)
        {
            if (!_lastShown.HasValue || _lastShown.Value != visible)
            {
                ViewModelRoot.gameObject.SetActive(visible);
                _lastShown = visible;
            }
        }
        private bool? _lastShown;

        /// <summary>Reference ReloadStartedEmpty readability flourish.</summary>
        static float ReloadDip(Simulation sim, Weapon weapon, WeaponDef def)
        {
            if (!weapon.Reloading) return 0f;
            float duration = MathF.Max(0.001f, def.ReloadTime);
            float reloadT = U.Clamp01((sim.Time - weapon.ReloadStartTime) / duration);
            float dip = MathF.Sin(reloadT * MathF.PI);
            dip *= weapon.ReloadStartedEmpty ? 1.13f : 0.86f;
            // The animated rigs shrink this flourish (AnimatedViewmodels.FlourishScale);
            // blockout meshes use the full box-model amount. Per-weapon art should
            // calibrate like the reference did when meshes land.
            return dip;
        }

        static float DeployDip(Simulation sim, Weapon weapon, WeaponDef def)
        {
            float readyIn = weapon.ReadyTime - sim.Time;
            if (readyIn <= 0f || def.DeployTime <= 0.0001f) return 0f;
            float t = U.Clamp01(readyIn / def.DeployTime);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Numerics → Unity Vector3 (same axis order; offset stays in raylib basis).</summary>
        static Vector3 ToUnity(System.Numerics.Vector3 v) =>
            new Vector3(v.X, v.Y, v.Z);

        /// <summary>Sim-basis raylib offset → Unity camera-local (+Z forward).</summary>
        static Vector3 FromViewBasis(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        static Vector3 AnchorFor(in ViewModelPose pose) => new Vector3(
            12f,
            -(pose.Forward * pose.Scale) * 0.34f - 4f,
            -MathF.Max(0f, pose.Back) * pose.Scale - 5f);

        /// <summary>
        /// Raylib chain: Translate(anchor) → RotX(flourish) → RotZ(flourish) →
        /// the two spring rotations → RotY(hip). The anchor translate is the pivot,
        /// which is why the flourish rotates the root itself around its origin.
        /// Rotation axes are right-handed in raylib and left-handed in Unity, so
        /// each angle is negated; verify the signs against a capture in-editor.
        /// </summary>
        static Quaternion FlourishRotation(Player p, float reloadDip, float ironSight)
        {
            float flourishPitch = p.ViewKick * 0.30f - reloadDip * 22f;
            float flourishRoll = reloadDip * 15f;
            System.Numerics.Vector3 springRotation = p.ViewModelRotationSpring.Value;
            float hipYaw = U.Lerp(3f, 0f, ironSight);

            Quaternion q = Quaternion.Euler(-flourishPitch, 0f, 0f)
                * Quaternion.Euler(0f, 0f, -flourishRoll)
                * Quaternion.Euler(-springRotation.X, 0f, 0f)
                * Quaternion.Euler(0f, -springRotation.Y, 0f)
                * Quaternion.Euler(0f, 0f, -springRotation.Z)
                * Quaternion.Euler(0f, -hipYaw, 0f);
            return q;
        }

        /// <summary>Per-class hip pose table (reference <c>PoseFor</c>/<c>AnchorFor</c>).</summary>
        static ViewModelPose PoseFor(WeaponDef def) => def.Class switch
        {
            "Melee" => new ViewModelPose(27f, 0f, 1.10f),
            "Pistol" => new ViewModelPose(25f, 2f, 1.20f),
            "SMG" => new ViewModelPose(40f, 3f, 1.10f),
            "Rifle" => new ViewModelPose(50f, 8f, 1.05f),
            "Shotgun" => new ViewModelPose(51f, 4f, 1.05f),
            "Sniper" => new ViewModelPose(64f, 11f, 1.00f),
            _ => new ViewModelPose(30f, 6f, 1.15f),
        };

        private readonly struct ViewModelPose
        {
            public readonly float Forward;
            public readonly float Back;
            public readonly float Scale;

            public ViewModelPose(float forward, float back, float scale)
            {
                Forward = forward;
                Back = back;
                Scale = scale;
            }
        }
    }
}
