using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using UnityEngine;

namespace HoodGlobalOffensive.Presentation
{
    public sealed class MatchDriver : MonoBehaviour
    {
        [Header("Match setup")]
        public int BotCount = 9;
        public BotSkill Skill = BotSkill.Normal;
        public ulong Seed = 20260114UL;
        public string MapName = MapRegistry.Default;

        [Header("Scene wiring")]
        public Transform PlayerBody;
        public Transform PlayerCamera;

        [Header("Input tuning")]
        public float LookGain = 1f;
        public bool InvertLookY = false;

        [Header("Audio")]
        [Tooltip("Created on this GameObject in Awake when left empty.")]
        public SimAudioPlayer Audio;

        public Simulation Sim { get; private set; }

        const float TickDt = 1f / 60f;
        float _accumulator;
        Camera? _camera;

        bool _firePressed;
        bool _zoomPressed;
        bool _reloadPressed;
        bool _grappleLeftPressed;
        bool _grappleRightPressed;
        bool _crouchTogglePressed;
        bool _meleePressed;
        bool _flashlightPressed;
        int _switchSlot = -1;
        int _switchCycle;
        bool _usePressed;

        void Awake()
        {
            Sim = new Simulation(BotCount, Skill, Seed, mapName: MapName);
            if (Audio == null) Audio = gameObject.AddComponent<SimAudioPlayer>();
            Cursor.lockState = CursorLockMode.Locked;
            SyncView();
        }

        /// <summary>
        /// Rebuilds the Simulation from the current inspector setup (same map, skill,
        /// bot count and seed). Used by <see cref="MenuSync"/> to start a fresh match
        /// from PLAY / RESTART; the blockout and actor views read the new instance on
        /// their next frame, so nothing else needs rebuilding here.
        /// </summary>
        public void Restart()
        {
            Sim = new Simulation(BotCount, Skill, Seed, mapName: MapName);
            _accumulator = 0f;
            SyncView();
        }

        void Update()
        {
            CapturePressedFlags();

            _accumulator += Time.deltaTime;
            bool stepped = false;
            while (_accumulator >= TickDt)
            {
                Sim.Update(BuildInput(), TickDt);
                // Drain per fixed step: the sim clears Sounds at the start of the
                // next Update, so batching across steps would drop cue bursts.
                if (Audio != null) Audio.PlaySimulationSounds(Sim);
                _accumulator -= TickDt;
                stepped = true;
            }
            _accumulator = Mathf.Min(_accumulator, TickDt);

            if (stepped) SyncView();
        }

        void CapturePressedFlags()
        {
            _firePressed |= Input.GetMouseButtonDown(0);
            _zoomPressed |= Input.GetMouseButtonDown(1);
            _reloadPressed |= Input.GetKeyDown(KeyCode.R);
            _grappleLeftPressed |= Input.GetKeyDown(KeyCode.Q);
            _grappleRightPressed |= Input.GetKeyDown(KeyCode.E);
            _usePressed |= _grappleRightPressed;
            _crouchTogglePressed |= Input.GetKeyDown(KeyCode.C);
            _meleePressed |= Input.GetKeyDown(KeyCode.V);
            _flashlightPressed |= Input.GetKeyDown(KeyCode.F);

            if (Input.GetKeyDown(KeyCode.Alpha1)) _switchSlot = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) _switchSlot = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) _switchSlot = 3;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) _switchSlot = 4;

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && _switchCycle == 0)
                _switchCycle = scroll > 0f ? 1 : -1;
        }

        InputFrame BuildInput()
        {
            var frame = InputFrame.Neutral;
            frame.Move = new System.Numerics.Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
            // Sim expects raylib GetMouseDelta counts: +X = right, +Y = down
            // (Player.cs subtracts Look, so +Y pitches down). Unity's "Mouse Y"
            // axis is +Y = up, so negate it to keep vertical look non-inverted;
            // InvertLookY flips that back for players who want inverted pitch.
            float lookYSign = InvertLookY ? 1f : -1f;
            frame.Look = new System.Numerics.Vector2(
                Input.GetAxisRaw("Mouse X") * LookGain,
                lookYSign * Input.GetAxisRaw("Mouse Y") * LookGain);
            frame.Jump = Input.GetButton("Jump");
            frame.Crouch = Input.GetKey(KeyCode.LeftControl);
            frame.FireDown = Input.GetMouseButton(0);
            frame.FirePressed = Take(ref _firePressed);
            frame.ZoomPressed = Take(ref _zoomPressed);
            frame.ZoomDown = Input.GetMouseButton(1);
            frame.Reload = Take(ref _reloadPressed);
            frame.GrappleLeftPressed = Take(ref _grappleLeftPressed);
            frame.GrappleRightPressed = Take(ref _grappleRightPressed);
            frame.UsePressed = Take(ref _usePressed);
            frame.CrouchTogglePressed = Take(ref _crouchTogglePressed);
            frame.MeleePressed = Take(ref _meleePressed);
            frame.FlashlightTogglePressed = Take(ref _flashlightPressed);
            frame.SwitchSlot = TakeSlot();
            frame.SwitchCycle = TakeCycle();
            return frame;
        }

        static bool Take(ref bool flag)
        {
            bool value = flag;
            flag = false;
            return value;
        }

        int TakeSlot()
        {
            int slot = _switchSlot;
            _switchSlot = -1;
            return slot;
        }

        int TakeCycle()
        {
            int cycle = _switchCycle;
            _switchCycle = 0;
            return cycle;
        }

        public static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        void SyncView()
        {
            Player p = Sim.Player;
            Vector3 feet = ToUnity(p.Position);
            Vector3 aim = ToUnity(U.DirFromAngles(p.Yaw, p.Pitch));

            if (PlayerBody == null) PlayerBody = transform;
            PlayerBody.position = feet;
            PlayerBody.rotation = Quaternion.LookRotation(ToUnity(U.DirFromAngles(p.Yaw, 0f)), Vector3.up);

            if (PlayerCamera != null)
            {
                PlayerCamera.position = feet + Vector3.up * p.EyeHeight;
                PlayerCamera.rotation = Quaternion.LookRotation(aim, Vector3.up);
                // The sim owns the FOV (weapon zoom lerps CurrentFov).
                if (_camera == null) _camera = PlayerCamera.GetComponent<Camera>();
                if (_camera != null) _camera.fieldOfView = p.CurrentFov;
            }
        }
    }
}
