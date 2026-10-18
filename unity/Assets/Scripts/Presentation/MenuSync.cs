using System;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// First-pass front end: a code-built uGUI menu stack (main / pause / settings /
    /// crosshair options / match-over) that owns game-flow state. It never touches the
    /// simulation's rules — leaving <see cref="Screen.Playing"/> simply disables the
    /// <see cref="MatchDriver"/> (the fixed tick stops), releases the cursor and freezes
    /// time; entering it re-enables the driver, locks the cursor and restores time.
    /// The main screen doubles as match setup (loadout slots, map, bot count, bot
    /// skill, kill limit) and pushes that onto the driver, which applies it on the
    /// next <see cref="MatchDriver.Restart"/>. Player settings (look sensitivity,
    /// Y inversion, camera-feedback level, and the crosshair profile) persist through
    /// <c>PlayerPrefs</c> and are pushed onto the driver / HUD.
    ///
    /// Keyboard-driven on purpose so it needs no EventSystem/Button wiring: Up/Down move
    /// the selection, Enter activates, Esc pauses / backs out, Left/Right adjust the
    /// focused setting. Attach it to the MatchDriver GameObject. The driver may still
    /// lock the cursor in its own <c>Awake</c>: this component applies menu state in
    /// <c>Start</c>, which is guaranteed to run after every <c>Awake</c>.
    ///
    /// The whole front end renders the reference <c>Screens</c> skin in the reference's
    /// 1280x720 design canvas mapped at the canvas's fixed 1.5x: main is
    /// <c>Screens.MainMenu.Draw</c> (left command surface, panelled label/value setup rows,
    /// LOCAL OPERATION match brief, footer hints); pause / match-over render the centred
    /// reference <c>DrawRows</c> look (40-unit title, live subtitle, 420-wide panelled rows)
    /// with the port-only settings screen reusing the pause geometry; and crosshair options
    /// is the reference's left-anchored 440x590 panel with the live reticle preview and its
    /// rule-based rows. Documented divergences: SETTINGS replaces the reference's inline
    /// MOUSE SENSITIVITY row (invert-Y has no reference row, like the main screen), and the
    /// match-over screen keeps the main screen's light scrim because <see cref="HudSync"/>'s
    /// forced scoreboard sits beneath this canvas instead of above the reference's 200 veil.
    /// </summary>
    public sealed class MenuSync : MonoBehaviour
    {
        enum Screen { Main, Playing, Paused, Settings, Crosshair, MatchOver }

        const string SensitivityKey = "HGO.LookSensitivity";
        const string InvertYKey = "HGO.InvertLookY";
        const string FeedbackKey = "HGO.CameraFeedback.Level";
        const string CrosshairPresetKey = "HGO.Crosshair.Preset";
        const string CrosshairColorKey = "HGO.Crosshair.Color";
        const string CrosshairStyleKey = "HGO.Crosshair.Style";
        const string CrosshairDynamicKey = "HGO.Crosshair.DynamicGap";
        const string CrosshairDotKey = "HGO.Crosshair.CentreDot";
        const string CrosshairGapKey = "HGO.Crosshair.BaseGap";
        const string CrosshairLengthKey = "HGO.Crosshair.ArmLength";
        const string CrosshairThicknessKey = "HGO.Crosshair.Thickness";
        const string BotsKey = "HGO.Match.BotCount";
        const string SkillKey = "HGO.Match.BotSkill";
        const string KillLimitKey = "HGO.Match.KillsToWin";
        const string MapKey = "HGO.Match.Map";
        const string LoadoutKeyPrefix = "HGO.Match.Loadout";
        const float MinSensitivity = 0.2f;
        const float MaxSensitivity = 3.0f;
        const float SensitivityStep = 0.1f;

        // The front-end palette is now the reference's exact byte values
        // (src/UI/Ui.cs + the colour literals in Screens.MainMenu.Draw)
        // instead of eyeballed floats, so the two front ends cannot drift.
        static readonly Color Scrim = new Color(4f / 255f, 7f / 255f, 11f / 255f, 94f / 255f);
        static readonly Color CommandSurface = new Color(8f / 255f, 11f / 255f, 16f / 255f, 214f / 255f);
        static readonly Color Ink = new Color(236f / 255f, 236f / 255f, 234f / 255f);
        static readonly Color Dim = new Color(236f / 255f, 236f / 255f, 234f / 255f, 140f / 255f);
        static readonly Color Faint = new Color(236f / 255f, 236f / 255f, 234f / 255f, 70f / 255f);
        static readonly Color Accent = new Color(1f, 186f / 255f, 56f / 255f);
        static readonly Color Good = new Color(118f / 255f, 206f / 255f, 124f / 255f);
        static readonly Color RowFill = new Color(1f, 1f, 1f, 10f / 255f);
        static readonly Color RowFillSelected = new Color(1f, 186f / 255f, 56f / 255f, 46f / 255f);
        static readonly Color RowBorder = new Color(1f, 1f, 1f, 22f / 255f);
        static readonly Color Rule = new Color(1f, 1f, 1f, 34f / 255f);
        static readonly Color BriefFill = new Color(8f / 255f, 11f / 255f, 16f / 255f, 196f / 255f);
        static readonly Color BriefBorder = new Color(1f, 1f, 1f, 42f / 255f);
        static readonly Color BriefRule = new Color(1f, 1f, 1f, 28f / 255f);
        // Reference per-screen scrims (Screens.Pause / CrosshairOptions / MatchOver
        // each draw their own full-screen fill over the live frame). Match-over
        // keeps Scrim: its scoreboard is drawn by HudSync beneath this canvas, so
        // the reference's near-opaque 200 veil would bury it.
        static readonly Color PauseScrim = new Color(6f / 255f, 8f / 255f, 12f / 255f, 175f / 255f);
        static readonly Color CrosshairScrim = new Color(6f / 255f, 8f / 255f, 12f / 255f, 88f / 255f);
        // Crosshair options panel (Screens.CrosshairOptions.Draw): plate, border
        // and the preview's two near-invisible guide strokes.
        static readonly Color PanelFill = new Color(11f / 255f, 13f / 255f, 18f / 255f, 232f / 255f);
        static readonly Color PanelBorder = new Color(1f, 1f, 1f, 46f / 255f);
        static readonly Color PreviewFill = new Color(3f / 255f, 7f / 255f, 11f / 255f, 196f / 255f);
        static readonly Color PreviewBorder = new Color(1f, 1f, 1f, 25f / 255f);
        static readonly Color PreviewGuide = new Color(1f, 1f, 1f, 16f / 255f);
        static readonly Color PreviewAxis = new Color(1f, 1f, 1f, 10f / 255f);

        // The reference front end is authored in Ui's 1280x720 design canvas
        // and letterboxed to the window; the menu canvas is a fixed
        // 1920x1080 CanvasScaler, so one design unit maps to 1.5 canvas px.
        const float UiScale = 1.5f;

        static float D(float v) => v * UiScale;

        static int DSize(float design) => (int)Math.Round(design * UiScale);

        // The shared reference-row bank (below) is sized for the largest
        // screen that uses it: the nine crosshair-options rows.
        const int RefRowCount = 9;

        readonly CrosshairSettings _crosshair = new();
        readonly CameraFeedbackSettings _cameraFeedback = new();
        readonly string[] _loadout = (string[])WeaponRegistry.DefaultLoadout.Clone();

        MatchDriver? _driver;
        HudSync? _hud;
        UpgradeSync? _upgradeSync;
        Font? _font;
        GameObject? _panel;
        Image? _backdrop;
        GameObject? _mainRoot;
        GameObject? _centerRoot;
        GameObject? _crossRoot;
        CrosshairGraphic? _previewCrosshair;
        Text? _centerTitle;
        Text? _centerSub1;
        Text? _centerSub2;
        // Reference-skin main menu (Screens.MainMenu.Draw): label/value row
        // panels with the selection carried by the panel highlight, plus the
        // LOCAL OPERATION match-brief card whose map name updates live.
        Text[]? _rowLabels;
        Text[]? _rowValues;
        Image[]? _rowFills;
        Image[]? _rowOutlines;
        Text? _briefMap;
        // Reference DrawRows bank shared by the centred screens (pause /
        // settings / match-over) and the crosshair-options panel.
        Text[]? _refLabels;
        Text[]? _refValues;
        Image[]? _refFills;
        Image[]? _refOutlines;

        Screen _screen = Screen.Main;
        Screen _settingsReturn = Screen.Main;
        Screen _crosshairReturn = Screen.Main;
        int _selected;

        float _sensitivity = 1f;
        bool _invertY;
        int _botCount = Cfg.DefaultBotCount;
        BotSkill _skill = BotSkill.Normal;
        int _killsToWin = Cfg.KillsToWin;
        string _mapName = MapRegistry.Default;

        void Awake()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("MenuSync: no MatchDriver on this GameObject; disabling.");
                enabled = false;
                return;
            }

            _sensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
            _invertY = PlayerPrefs.GetInt(InvertYKey, 0) != 0;
            _cameraFeedback.Restore(
                (CameraFeedbackLevel)PlayerPrefs.GetInt(FeedbackKey, (int)CameraFeedbackLevel.Normal));
            LoadCrosshair();
            LoadMatchSetup();
            ApplySettings();
            ApplyMatchSetup();
        }

        void Start()
        {
            if (_driver == null) return;

            _hud = GetComponent<HudSync>();
            _upgradeSync = GetComponent<UpgradeSync>();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            if (_font == null) Debug.LogWarning("MenuSync: no usable font; menu text will not draw.");

            BuildPanel();
            // The HUD draws the same profile instance, so edits show live.
            if (_hud != null) _hud.Crosshair = _crosshair;
            SetScreen(Screen.Main);
        }

        void Update()
        {
            if (_driver == null) return;

            // The upgrade cabinet owns input while it is up (and on the frame it
            // closes), so Esc/Enter here never fight it.
            if (_upgradeSync != null && _upgradeSync.BlocksMenuInput) return;

            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool enter = Input.GetKeyDown(KeyCode.Return);
            bool up = Input.GetKeyDown(KeyCode.UpArrow);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            bool left = Input.GetKeyDown(KeyCode.LeftArrow);
            bool right = Input.GetKeyDown(KeyCode.RightArrow);

            if (_screen == Screen.Playing)
            {
                if (_driver.Sim != null && _driver.Sim.MatchOver) { SetScreen(Screen.MatchOver); return; }
                if (esc) { SetScreen(Screen.Paused); return; }
                return;
            }

            if (esc)
            {
                if (_screen == Screen.Crosshair) { SetScreen(_crosshairReturn); return; }
                if (_screen == Screen.Settings) { SetScreen(_settingsReturn); return; }
                if (_screen == Screen.Paused) { SetScreen(Screen.Playing); return; }
                if (_screen == Screen.MatchOver) { SetScreen(Screen.Main); return; }
                return; // Main: Esc is a no-op
            }

            int count = RowCount();
            if (count > 0)
            {
                if (up) _selected = (_selected + count - 1) % count;
                if (down) _selected = (_selected + 1) % count;
            }

            if (_screen == Screen.Settings)
            {
                if (left) AdjustSensitivity(-1);
                if (right) AdjustSensitivity(1);
            }
            else if (_screen == Screen.Crosshair)
            {
                AdjustCrosshair(left ? -1 : right ? 1 : 0);
                if (_previewCrosshair != null)
                {
                    // Reference preview: a fixed 5-unit spread and scale 1.75 so
                    // the dynamic-gap setting is visible at a glance. Both are
                    // design units here (x1.5 for the canvas); the graphic
                    // multiplies the spread by the scale, exactly like
                    // CrosshairRenderer.Draw.
                    _previewCrosshair.Update(_crosshair, _crosshair.DynamicGap ? 5f : 0f, 1.75f * UiScale);
                }
            }
            else if (_screen == Screen.Main && _selected >= 1 && _selected <= 7)
            {
                // Match setup rows: SLOT 1-3 / MAP / BOTS / BOT SKILL / KILL LIMIT.
                AdjustSetup(_selected, left ? -1 : right ? 1 : 0);
            }
            else if (_selected == FeedbackRow())
            {
                // Main/pause carry the camera-feedback level inline, like the reference.
                if (left) CycleFeedback(-1);
                if (right) CycleFeedback(1);
            }

            if (enter) Activate();
            RefreshRows();
        }

        // ------------------------------------------------------------- state machine

        int RowCount()
        {
            switch (_screen)
            {
                case Screen.Main: return 12;
                case Screen.Paused: return 6;
                case Screen.MatchOver: return 2;
                case Screen.Settings: return 3;
                case Screen.Crosshair: return 9;
                default: return 0;
            }
        }

        /// <summary>Row index of the inline camera-feedback setting, or -1.</summary>
        int FeedbackRow() => _screen == Screen.Main ? 9 : _screen == Screen.Paused ? 2 : -1;

        void Activate()
        {
            switch (_screen)
            {
                case Screen.Main:
                    if (_selected == 0) BeginPlay();
                    else if (_selected == 8) OpenCrosshair(Screen.Main);
                    else if (_selected == 10) OpenSettings(Screen.Main);
                    else if (_selected == 11) Quit();
                    // Rows 1-7 (match setup) and 9 (camera feedback) adjust with Left/Right.
                    break;
                case Screen.Paused:
                    if (_selected == 0) SetScreen(Screen.Playing);
                    else if (_selected == 1) OpenCrosshair(Screen.Paused);
                    else if (_selected == 3) OpenSettings(Screen.Paused);
                    else if (_selected == 4) BeginPlay();
                    else if (_selected == 5) SetScreen(Screen.Main);
                    // Row 2 (camera feedback) is adjusted with Left/Right.
                    break;
                case Screen.MatchOver:
                    if (_selected == 0) BeginPlay();
                    else SetScreen(Screen.Main);
                    break;
                case Screen.Settings:
                    if (_selected == 1) ToggleInvert();
                    else if (_selected == 2) SetScreen(_settingsReturn);
                    // Row 0 (sensitivity) is adjusted with Left/Right; Enter is a no-op.
                    break;
                case Screen.Crosshair:
                    if (_selected == 7) { _crosshair.Reset(); SaveCrosshair(); }
                    else if (_selected == 8) SetScreen(_crosshairReturn);
                    break;
            }
        }

        void BeginPlay()
        {
            if (_driver == null) return;
            _driver.Restart();
            SetScreen(Screen.Playing);
        }

        void OpenSettings(Screen from)
        {
            _settingsReturn = from;
            SetScreen(Screen.Settings);
        }

        void OpenCrosshair(Screen from)
        {
            _crosshairReturn = from;
            SetScreen(Screen.Crosshair);
        }

        void SetScreen(Screen screen)
        {
            _screen = screen;
            _selected = 0;

            bool playing = screen == Screen.Playing;
            if (_driver != null) _driver.enabled = playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Time.timeScale = playing ? 1f : 0f;
            if (_panel != null) _panel.SetActive(!playing);
            // The reference draws its scoreboard inside the match-over screen; the HUD
            // forces it up so the same information is visible behind the menu backdrop.
            if (_hud != null) _hud.ForceScoreboard = screen == Screen.MatchOver;
            // Main shows the reference's fixed scenic backdrop over the map instead of
            // the last gameplay frame (Renderer.DrawMenuBackdrop).
            if (screen == Screen.Main && _driver != null) _driver.ParkMenuBackdrop();

            RefreshRows();
        }

        void Quit()
        {
            Time.timeScale = 1f;
            Application.Quit();
        }

        // ------------------------------------------------------------------ settings

        void AdjustSensitivity(int direction)
        {
            float value = _sensitivity + direction * SensitivityStep;
            value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            value = (float)System.Math.Round(value, 1);
            if (value == _sensitivity) return;

            _sensitivity = value;
            PlayerPrefs.SetFloat(SensitivityKey, _sensitivity);
            PlayerPrefs.Save();
            ApplySettings();
        }

        void ToggleInvert()
        {
            _invertY = !_invertY;
            PlayerPrefs.SetInt(InvertYKey, _invertY ? 1 : 0);
            PlayerPrefs.Save();
            ApplySettings();
        }

        void ApplySettings()
        {
            if (_driver == null) return;
            _driver.LookGain = _sensitivity;
            _driver.InvertLookY = _invertY;
            _driver.FeedbackStrength = _cameraFeedback.Strength;
        }

        void CycleFeedback(int direction)
        {
            _cameraFeedback.Cycle(direction);
            PlayerPrefs.SetInt(FeedbackKey, (int)_cameraFeedback.Level);
            PlayerPrefs.Save();
            ApplySettings();
        }

        // --------------------------------------------------------------- match setup

        /// <summary>
        /// Restores the match-setup fields from PlayerPrefs with the reference's bounded
        /// rules (<c>UserSettingsStore</c>): bot count and kill limit clamp, an unknown
        /// skill falls back to Normal, the map normalises through the registry, and each
        /// gun slot sanitises against the loadout pool.
        /// </summary>
        void LoadMatchSetup()
        {
            _botCount = U.ClampInt(PlayerPrefs.GetInt(BotsKey, Cfg.DefaultBotCount), 1, Cfg.MaxBots);
            int skillValue = PlayerPrefs.GetInt(SkillKey, (int)BotSkill.Normal);
            _skill = Enum.IsDefined(typeof(BotSkill), skillValue) ? (BotSkill)skillValue : BotSkill.Normal;
            _killsToWin = U.ClampInt(PlayerPrefs.GetInt(KillLimitKey, Cfg.KillsToWin), 5, 100);
            _mapName = MapRegistry.Normalize(PlayerPrefs.GetString(MapKey, MapRegistry.Default));
            for (int i = 0; i < _loadout.Length; i++)
            {
                string fallback = WeaponRegistry.DefaultLoadout[i];
                string stored = PlayerPrefs.GetString(LoadoutKeyPrefix + i, fallback);
                WeaponDef? def = Array.Find(WeaponRegistry.LoadoutPool,
                    w => string.Equals(w.Name, stored, StringComparison.OrdinalIgnoreCase));
                _loadout[i] = def != null ? def.Name : fallback;
            }
        }

        void SaveMatchSetup()
        {
            PlayerPrefs.SetInt(BotsKey, _botCount);
            PlayerPrefs.SetInt(SkillKey, (int)_skill);
            PlayerPrefs.SetInt(KillLimitKey, _killsToWin);
            PlayerPrefs.SetString(MapKey, _mapName);
            for (int i = 0; i < _loadout.Length; i++) PlayerPrefs.SetString(LoadoutKeyPrefix + i, _loadout[i]);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The setup only reaches the sim through <see cref="MatchDriver.Restart"/>, but
        /// pushing it right away keeps the inspector in step, so PLAY / RESTART always
        /// starts the match the menu shows.
        /// </summary>
        void ApplyMatchSetup()
        {
            if (_driver == null) return;
            _driver.BotCount = _botCount;
            _driver.Skill = _skill;
            _driver.KillsToWin = _killsToWin;
            _driver.MapName = _mapName;
            if (_driver.Loadout == null || _driver.Loadout.Length < _loadout.Length)
                _driver.Loadout = (string[])_loadout.Clone();
            else
                for (int i = 0; i < _loadout.Length; i++) _driver.Loadout[i] = _loadout[i];
        }

        void AdjustSetup(int row, int axis)
        {
            if (axis == 0) return;
            switch (row)
            {
                case 1: CycleLoadoutSlot(0, axis); break;
                case 2: CycleLoadoutSlot(1, axis); break;
                case 3: CycleLoadoutSlot(2, axis); break;
                case 4: CycleMap(axis); break;
                case 5: _botCount = U.ClampInt(_botCount + axis, 1, Cfg.MaxBots); break;
                case 6: _skill = (BotSkill)U.ClampInt((int)_skill + axis, 0, 3); break;
                case 7: _killsToWin = U.ClampInt(_killsToWin + axis * 5, 5, 100); break;
                default: return;
            }
            SaveMatchSetup();
            ApplyMatchSetup();
        }

        void CycleLoadoutSlot(int slot, int axis)
        {
            WeaponDef current = WeaponRegistry.ByName(_loadout[slot]);
            _loadout[slot] = WeaponRegistry.CycleLoadout(current, axis).Name;
        }

        void CycleMap(int axis)
        {
            int index = Array.IndexOf(MapRegistry.Names, _mapName);
            if (index < 0) index = 0;
            int next = ((index + axis) % MapRegistry.Names.Length + MapRegistry.Names.Length)
                % MapRegistry.Names.Length;
            _mapName = MapRegistry.Names[next];
        }

        // ----------------------------------------------------------------- crosshair

        /// <summary>
        /// Restores the reticle profile from PlayerPrefs with the reference's bounded
        /// restore rules (unknown enum values fall back, sizes clamp, and a stored
        /// preset survives only if every value still matches it). Missing keys use the
        /// Classic profile's values, so a fresh install restores Classic exactly.
        /// </summary>
        void LoadCrosshair()
        {
            int presetValue = PlayerPrefs.GetInt(CrosshairPresetKey, (int)CrosshairPreset.Classic);
            CrosshairPreset? preset = Enum.IsDefined(typeof(CrosshairPreset), presetValue)
                ? (CrosshairPreset)presetValue
                : null;
            int colorValue = PlayerPrefs.GetInt(CrosshairColorKey, (int)CrosshairColorOption.Green);
            CrosshairColorOption color = Enum.IsDefined(typeof(CrosshairColorOption), colorValue)
                ? (CrosshairColorOption)colorValue
                : CrosshairColorOption.Green;
            int styleValue = PlayerPrefs.GetInt(CrosshairStyleKey, (int)CrosshairStyle.Cross);
            CrosshairStyle style = Enum.IsDefined(typeof(CrosshairStyle), styleValue)
                ? (CrosshairStyle)styleValue
                : CrosshairStyle.Cross;

            _crosshair.Restore(new CrosshairPreference(
                preset,
                color,
                style,
                PlayerPrefs.GetInt(CrosshairDynamicKey, 1) != 0,
                PlayerPrefs.GetInt(CrosshairDotKey, 1) != 0,
                PlayerPrefs.GetInt(CrosshairGapKey, 4),
                PlayerPrefs.GetInt(CrosshairLengthKey, 7),
                PlayerPrefs.GetInt(CrosshairThicknessKey, 1)));
        }

        void SaveCrosshair()
        {
            CrosshairPreference c = _crosshair.CapturePreference();
            PlayerPrefs.SetInt(CrosshairPresetKey, c.Preset is CrosshairPreset preset ? (int)preset : -1);
            PlayerPrefs.SetInt(CrosshairColorKey, (int)c.Color);
            PlayerPrefs.SetInt(CrosshairStyleKey, (int)c.Style);
            PlayerPrefs.SetInt(CrosshairDynamicKey, c.DynamicGap ? 1 : 0);
            PlayerPrefs.SetInt(CrosshairDotKey, c.CentreDot ? 1 : 0);
            PlayerPrefs.SetInt(CrosshairGapKey, c.BaseGap);
            PlayerPrefs.SetInt(CrosshairLengthKey, c.ArmLength);
            PlayerPrefs.SetInt(CrosshairThicknessKey, c.Thickness);
            PlayerPrefs.Save();
        }

        void AdjustCrosshair(int axis)
        {
            if (axis == 0) return;
            switch (_selected)
            {
                case 0: _crosshair.CyclePreset(axis); break;
                case 1: _crosshair.CycleColor(axis); break;
                case 2: _crosshair.ToggleDynamicGap(); break;
                case 3: _crosshair.ToggleCentreDot(); break;
                case 4: _crosshair.AdjustGap(axis); break;
                case 5: _crosshair.AdjustLength(axis); break;
                case 6: _crosshair.AdjustThickness(axis); break;
                default: return;
            }
            SaveCrosshair();
        }

        // --------------------------------------------------------------------- draw

        void RefreshRows()
        {
            var sim = _driver != null ? _driver.Sim : null;

            if (_backdrop != null) _backdrop.color = BackdropFor(_screen);
            if (_mainRoot != null) _mainRoot.SetActive(_screen == Screen.Main);
            bool centre = _screen == Screen.Paused || _screen == Screen.Settings ||
                _screen == Screen.MatchOver;
            if (_centerRoot != null) _centerRoot.SetActive(centre);
            if (_crossRoot != null) _crossRoot.SetActive(_screen == Screen.Crosshair);

            switch (_screen)
            {
                case Screen.Main:
                    // Reference-skin rows: label left / value right, with the
                    // panel highlight carrying the selection (no ">" prefix).
                    // Row order is unchanged — Activate()/Update() index it.
                    SetMainRow(0, "START DEATHMATCH", "");
                    SetMainRow(1, "SLOT 1", _loadout[0]);
                    SetMainRow(2, "SLOT 2", _loadout[1]);
                    SetMainRow(3, "SLOT 3", _loadout[2]);
                    SetMainRow(4, "MAP", _mapName.ToUpperInvariant());
                    SetMainRow(5, "BOTS", $"{_botCount}");
                    SetMainRow(6, "BOT SKILL", _skill.ToString().ToUpperInvariant());
                    SetMainRow(7, "KILL LIMIT", $"{_killsToWin}");
                    SetMainRow(8, "CROSSHAIR OPTIONS", _crosshair.PresetLabel);
                    SetMainRow(9, "CAMERA FEEDBACK", _cameraFeedback.Label);
                    SetMainRow(10, "SETTINGS", "");
                    SetMainRow(11, "QUIT", "");
                    if (_briefMap != null) _briefMap.text = _mapName.ToUpperInvariant();
                    PositionRefRows(0, 0f, 0f, 0f, 0f, 0f);
                    break;
                case Screen.Paused:
                    SetCenterTitle("PAUSED", false, 108f);
                    SetCenterSub(_centerSub1, sim != null
                        ? $"{sim.Player.Kills} kills   {sim.Player.Deaths} deaths   {sim.Player.KillPoints} upgrade points"
                        : "", 154f);
                    SetCenterSub(_centerSub2, "", 0f);
                    // The port keeps its SETTINGS row where the reference
                    // adjusts MOUSE SENSITIVITY inline: sensitivity and
                    // invert-Y share one settings screen (see the class note).
                    SetRefRow(0, "RESUME", "");
                    SetRefRow(1, "CROSSHAIR OPTIONS", _crosshair.PresetLabel);
                    SetRefRow(2, "CAMERA FEEDBACK", _cameraFeedback.Label);
                    SetRefRow(3, "SETTINGS", "");
                    SetRefRow(4, "RESTART MATCH", "");
                    SetRefRow(5, "QUIT TO MENU", "");
                    PositionRefRows(6, 430f, 180f, 420f, 42f, 50f);
                    break;
                case Screen.MatchOver:
                    SetCenterTitle("MATCH OVER", true, 372f);
                    SetCenterSub(_centerSub1, sim != null ? sim.MatchResult : "", 416f);
                    SetCenterSub(_centerSub2, sim != null
                        ? $"you finished rank {sim.PlayerRank()} of {sim.Actors.Count}"
                        : "", 436f);
                    SetRefRow(0, "PLAY AGAIN", "");
                    SetRefRow(1, "BACK TO MENU", "");
                    PositionRefRows(2, 430f, 446f, 420f, 42f, 50f);
                    break;
                case Screen.Settings:
                    SetCenterTitle("SETTINGS", false, 108f);
                    SetCenterSub(_centerSub1, "LEFT / RIGHT adjusts the focused setting", 154f);
                    SetCenterSub(_centerSub2, "", 0f);
                    SetRefRow(0, "LOOK SENSITIVITY", $"{_sensitivity:0.0}");
                    SetRefRow(1, "INVERT Y", _invertY ? "ON" : "OFF");
                    SetRefRow(2, "BACK", "");
                    PositionRefRows(3, 430f, 180f, 420f, 42f, 50f);
                    break;
                case Screen.Crosshair:
                    // Reference row set (Screens.CrosshairOptions.Draw): rows
                    // sit inside the left panel on the 40-unit pitch.
                    SetRefRow(0, "PRESET", _crosshair.PresetLabel);
                    SetRefRow(1, "COLOR", _crosshair.ColorLabel);
                    SetRefRow(2, "DYNAMIC GAP", _crosshair.DynamicGap ? "ON" : "OFF");
                    SetRefRow(3, "CENTRE DOT", _crosshair.CentreDot ? "ON" : "OFF");
                    SetRefRow(4, "BASE GAP", $"{_crosshair.BaseGap} px");
                    SetRefRow(5, "ARM LENGTH", $"{_crosshair.ArmLength} px");
                    SetRefRow(6, "THICKNESS", $"{_crosshair.Thickness} px");
                    SetRefRow(7, "RESET DEFAULT", "");
                    SetRefRow(8, "BACK", "");
                    PositionRefRows(9, 42f, 261f, 404f, 35f, 40f);
                    break;
                default:
                    PositionRefRows(0, 0f, 0f, 0f, 0f, 0f);
                    break;
            }
        }

        void BuildPanel()
        {
            GameObject canvasGo = new GameObject("MenuSync Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.transform.SetParent(transform);
            _panel = canvasGo;

            Transform root = canvasGo.transform;
            // Per-screen scrim. It starts on the reference main-menu veil
            // rgba(4,7,11,94) so the parked scenic backdrop stays visible
            // behind the front end; RefreshRows recolours it per screen.
            _backdrop = MakeSkinImage(root, "Backdrop", 0f, 0f, 1280f, 720f, Scrim);

            BuildMainSurface(root);       // reference main-menu skin (hidden)
            BuildCenterSurface(root);     // centred reference skin (hidden)
            BuildCrosshairSurface(root);  // crosshair options panel (hidden)
            BuildRefRows(root);           // shared DrawRows bank (hidden)
        }

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        // ------------------------------------------- reference main-menu skin

        /// <summary>
        /// Ports the reference <c>Screens.MainMenu.Draw</c> front end: the dark
        /// left command surface, the full-width accent bar, the top-left
        /// <c>Cfg.GameName</c> title block with the version rule line, the
        /// twelve setup rows rebuilt as <c>DrawRows</c> panels (fill + border
        /// highlight, label left / value right at 15 design units), the
        /// <c>DrawMatchBrief</c> LOCAL OPERATION card on the right, and the
        /// three footer hint lines. Every coordinate is the reference's
        /// 1280x720 design canvas converted at the canvas's fixed 1.5x, and
        /// the whole surface hides outside the Main screen.
        ///
        /// One deliberate divergence from the reference row set: row 10 is
        /// SETTINGS (which holds look sensitivity + invert-Y) where the
        /// reference carries MOUSE SENSITIVITY inline — invert-Y has no
        /// reference row, so the port keeps one settings screen for both.
        /// </summary>
        void BuildMainSurface(Transform parent)
        {
            GameObject rootGo = new GameObject("Main Menu Surface");
            rootGo.AddComponent<RectTransform>();
            rootGo.transform.SetParent(parent);
            ApplySkinRect(rootGo, Vector2.zero, new Vector2(1920f, 1080f));
            _mainRoot = rootGo;
            Transform root = rootGo.transform;

            MakeSkinImage(root, "Command Surface", 0f, 0f, 760f, 720f, CommandSurface);
            MakeSkinImage(root, "Accent Bar", 0f, 0f, 1280f, 6f, Accent);
            MakeSkinText(root, "Game Title", 60f, 48f, 700f, 84f, Cfg.GameName, 52f,
                Ink, TextAnchor.UpperLeft);
            MakeSkinText(root, "Version Line", 64f, 116f, 900f, 24f,
                $"EARLY ACCESS v{Cfg.Version}   -   OFFLINE BOT DEATHMATCH   -   GRAPPLE ARENA",
                13f, Dim, TextAnchor.UpperLeft);
            MakeSkinImage(root, "Title Rule", 64f, 145f, 460f, 1f, Rule);

            _rowLabels = new Text[12];
            _rowValues = new Text[12];
            _rowFills = new Image[12];
            _rowOutlines = new Image[12];
            for (int i = 0; i < 12; i++)
            {
                // Reference row geometry: x 64, width 460, height 34, a
                // 39-unit pitch from y 156 (row height 34 + gap 5), which
                // keeps the last row's border at 619 — above the first footer
                // hint at 624, like the reference's pitch note.
                float y = 156f + i * 39f;
                _rowOutlines[i] = MakeSkinImage(root, $"Row {i} Outline",
                    63f, y - 1f, 462f, 36f, RowBorder);
                _rowFills[i] = MakeSkinImage(root, $"Row {i} Fill", 64f, y, 460f, 34f, RowFill);
                _rowLabels[i] = MakeSkinText(root, $"Row {i} Label",
                    80f, y + 4f, 300f, 26f, "", 15f, Dim, TextAnchor.MiddleLeft);
                _rowValues[i] = MakeSkinText(root, $"Row {i} Value",
                    208f, y + 4f, 300f, 26f, "", 15f, Dim, TextAnchor.MiddleRight);
            }

            // Reference DrawMatchBrief — the LOCAL OPERATION card (770,202,
            // 420x232) to the right of the command surface.
            MakeSkinImage(root, "Brief Outline", 769f, 201f, 422f, 234f, BriefBorder);
            MakeSkinImage(root, "Brief Fill", 770f, 202f, 420f, 232f, BriefFill);
            MakeSkinImage(root, "Brief Accent Bar", 770f, 202f, 5f, 232f, Accent);
            MakeSkinText(root, "Brief Caption", 796f, 224f, 300f, 20f,
                "LOCAL OPERATION", 11f, Accent, TextAnchor.MiddleLeft);
            _briefMap = MakeSkinText(root, "Brief Map", 794f, 244f, 560f, 44f,
                "", 27f, Ink, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Mode Line", 795f, 282f, 300f, 20f,
                "OFFLINE FREE-FOR-ALL", 11f, Dim, TextAnchor.MiddleLeft);
            MakeSkinImage(root, "Brief Rule", 794f, 310f, 372f, 1f, BriefRule);
            MakeSkinText(root, "Brief Mode Caption", 794f, 328f, 140f, 18f,
                "MODE", 10f, Faint, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Mode Value", 794f, 346f, 140f, 24f,
                "DEATHMATCH", 16f, Ink, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Economy Caption", 944f, 328f, 130f, 18f,
                "ECONOMY", 10f, Faint, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Economy Value", 944f, 346f, 160f, 24f,
                "KILL UPGRADES", 16f, Ink, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Status Caption", 1076f, 328f, 110f, 18f,
                "STATUS", 10f, Faint, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Status Value", 1076f, 346f, 110f, 24f,
                "LOCAL", 16f, Good, TextAnchor.MiddleLeft);
            MakeSkinText(root, "Brief Hint", 794f, 388f, 580f, 30f,
                "Kills bank upgrade points. Spend them at a cabinet to grow health, damage, speed or jump.",
                10f, Dim, TextAnchor.MiddleLeft);

            MakeSkinText(root, "Hint Controls", 64f, 618f, 900f, 18f,
                "WASD move    MOUSE aim    LMB shoot    RMB aim / alt fire    R reload    1-4 weapons",
                10f, Faint, TextAnchor.UpperLeft);
            MakeSkinText(root, "Hint Grapple", 64f, 634f, 900f, 18f,
                "Q / E left & right grapple    V knife    F flashlight    SPACE jump (HOLD to bunny-hop)    TAB scores",
                10f, Faint, TextAnchor.UpperLeft);
            MakeSkinText(root, "Hint Tip", 64f, 658f, 900f, 18f,
                "Tip: hold jump and stay off WASD to keep speed. Grapple far and high, then slingshot.",
                10f, Dim, TextAnchor.UpperLeft);

            // Hidden until RefreshRows shows it on the Main screen.
            _mainRoot.SetActive(false);
        }

        void SetMainRow(int i, string label, string value)
        {
            if (_rowLabels == null || _rowValues == null ||
                _rowFills == null || _rowOutlines == null) return;
            bool selected = _selected == i;
            _rowLabels[i].text = label;
            _rowLabels[i].color = selected ? Ink : Dim;
            _rowValues[i].text = value;
            _rowValues[i].color = selected ? Accent : Dim;
            _rowFills[i].color = selected ? RowFillSelected : RowFill;
            _rowOutlines[i].color = selected ? Accent : RowBorder;
        }

        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        void ApplySkinRect(GameObject go, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null) rectTransform = go.AddComponent<RectTransform>();
            rectTransform.anchorMin = TopLeft;
            rectTransform.anchorMax = TopLeft;
            rectTransform.pivot = TopLeft;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        /// <summary>Design-canvas image (top-left origin, y grows downward).</summary>
        Image MakeSkinImage(Transform parent, string name, float x, float y,
            float w, float h, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplySkinRect(go, new Vector2(D(x), -D(y)), new Vector2(D(w), D(h)));

            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>Design-canvas text; size is in design units, like Ui.Text.</summary>
        Text MakeSkinText(Transform parent, string name, float x, float y,
            float w, float h, string text, float designSize, Color color, TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplySkinRect(go, new Vector2(D(x), -D(y)), new Vector2(D(w), D(h)));

            Text label = go.AddComponent<Text>();
            label.font = _font ?? Font.CreateDynamicFontFromOSFont("Arial", 24);
            label.fontSize = DSize(designSize);
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            label.color = color;
            return label;
        }

        // ---------------------------------- reference centred / crosshair screens

        /// <summary>
        /// The centred reference skin shared by pause / settings / match-over: a
        /// 40-unit title and two 15-unit subtitle lines, all centred on the
        /// 1280x720 design canvas. Positions and visibility are set per screen in
        /// <see cref="RefreshRows"/>; settings is port-only (the reference has no
        /// such screen) and reuses the pause geometry.
        /// </summary>
        void BuildCenterSurface(Transform parent)
        {
            GameObject rootGo = new GameObject("Centred Menu Surface");
            rootGo.AddComponent<RectTransform>();
            rootGo.transform.SetParent(parent);
            ApplySkinRect(rootGo, Vector2.zero, new Vector2(1920f, 1080f));
            _centerRoot = rootGo;
            Transform root = rootGo.transform;

            _centerTitle = MakeSkinText(root, "Title", 0f, 0f, 1280f, 60f, "", 40f, Ink, TextAnchor.MiddleCenter);
            _centerSub1 = MakeSkinText(root, "Subtitle 1", 0f, 0f, 1280f, 20f, "", 15f, Dim, TextAnchor.MiddleCenter);
            _centerSub2 = MakeSkinText(root, "Subtitle 2", 0f, 0f, 1280f, 20f, "", 15f, Ink, TextAnchor.MiddleCenter);
            _centerRoot.SetActive(false);
        }

        /// <summary>
        /// Ports the reference <c>Screens.CrosshairOptions.Draw</c> panel: the
        /// left-anchored 440x590 plate at x 24 / y 65, the title and its
        /// session-only line, the LIVE PREVIEW plate with the faint radius-30
        /// ring and 80-unit cross, the live 1.75x reticle preview, and the two
        /// footer hint lines. The nine setting rows are the shared reference row
        /// bank, laid out inside the panel by <see cref="RefreshRows"/>.
        /// </summary>
        void BuildCrosshairSurface(Transform parent)
        {
            GameObject rootGo = new GameObject("Crosshair Options Surface");
            rootGo.AddComponent<RectTransform>();
            rootGo.transform.SetParent(parent);
            ApplySkinRect(rootGo, Vector2.zero, new Vector2(1920f, 1080f));
            _crossRoot = rootGo;
            Transform root = rootGo.transform;

            // Reference panel: x 24, y = max(36, (720 - 590) / 2) = 65, 440x590,
            // rgba(11,13,18,232) fill with a rgba(255,255,255,46) border.
            MakeSkinImage(root, "Panel Border", 23f, 64f, 442f, 592f, PanelBorder);
            MakeSkinImage(root, "Panel Fill", 24f, 65f, 440f, 590f, PanelFill);
            MakeRefText(root, "Title", 44f, 83f, 400f, "CROSSHAIR OPTIONS", 26f, Ink, TextAnchor.UpperLeft);
            MakeRefText(root, "Subtitle", 45f, 115f, 400f, "Session-only visual settings", 11f, Dim, TextAnchor.UpperLeft);

            // Preview plate (42,143,404,102); the reticle sits at 62% of its
            // height like the reference preview.
            MakeSkinImage(root, "Preview Border", 41f, 142f, 406f, 104f, PreviewBorder);
            Image plate = MakeSkinImage(root, "Preview Fill", 42f, 143f, 404f, 102f, PreviewFill);
            MakeRefText(root, "Preview Caption", 54f, 153f, 300f, "LIVE PREVIEW", 10f, Faint, TextAnchor.UpperLeft);
            BuildPreviewGuides(plate.transform);
            _previewCrosshair = CrosshairGraphic.Create(plate.transform, "Preview Crosshair");
            ((RectTransform)_previewCrosshair.Root).anchoredPosition = PreviewCentre;

            MakeRefText(root, "Hint Rows", 44f, 621f, 500f,
                "UP/DOWN select    LEFT/RIGHT adjust    ENTER reset/back", 10f, Faint, TextAnchor.UpperLeft);
            MakeRefText(root, "Hint Note", 44f, 636f, 500f,
                "The centre reticle remains aligned to the shot path.", 10f, Dim, TextAnchor.UpperLeft);

            _crossRoot.SetActive(false);
        }

        /// <summary>The reticle point inside the preview plate: 62% down it.</summary>
        static Vector2 PreviewCentre => new Vector2(0f, -(0.12f * D(102f)));

        /// <summary>
        /// The reference preview's faint guides at <see cref="PreviewCentre"/>:
        /// the 80-unit cross and the radius-30 circle, both near-invisible whites
        /// (alpha 10 / 16 of 255). The ring is 16 tangent bars (chord = 2 r
        /// tan(pi/16)) so it reads as one circle.
        /// </summary>
        void BuildPreviewGuides(Transform plate)
        {
            MakePreviewBar(plate, "Preview Axis H", PreviewCentre, new Vector2(D(80f), D(1f)), 0f, PreviewAxis);
            MakePreviewBar(plate, "Preview Axis V", PreviewCentre, new Vector2(D(1f), D(80f)), 0f, PreviewAxis);

            const int segments = 16;
            float radius = D(30f);
            float chord = 2f * radius * (float)Math.Tan(Math.PI / segments);
            for (int i = 0; i < segments; i++)
            {
                float degrees = (i + 0.5f) * (360f / segments);
                double rad = degrees * Math.PI / 180.0;
                Vector2 at = new Vector2(
                    PreviewCentre.x + radius * (float)Math.Cos(rad),
                    PreviewCentre.y + radius * (float)Math.Sin(rad));
                MakePreviewBar(plate, $"Preview Ring {i}", at, new Vector2(chord, D(1f)),
                    degrees + 90f, PreviewGuide);
            }
        }

        /// <summary>
        /// The shared reference DrawRows bank for every screen except main: an
        /// outline + fill image pair and label/value texts per row, all 15-unit
        /// type like the reference. Rows are laid out per screen by
        /// <see cref="PositionRefRows"/> and hidden while unused.
        /// </summary>
        void BuildRefRows(Transform root)
        {
            _refLabels = new Text[RefRowCount];
            _refValues = new Text[RefRowCount];
            _refFills = new Image[RefRowCount];
            _refOutlines = new Image[RefRowCount];
            for (int i = 0; i < RefRowCount; i++)
            {
                _refOutlines[i] = MakeSkinImage(root, $"Ref Row {i} Outline", 0f, 0f, 1f, 1f, RowBorder);
                _refFills[i] = MakeSkinImage(root, $"Ref Row {i} Fill", 0f, 0f, 1f, 1f, RowFill);
                _refLabels[i] = MakeSkinText(root, $"Ref Row {i} Label", 0f, 0f, 1f, 1f, "", 15f, Dim, TextAnchor.MiddleLeft);
                _refValues[i] = MakeSkinText(root, $"Ref Row {i} Value", 0f, 0f, 1f, 1f, "", 15f, Dim, TextAnchor.MiddleRight);
                _refOutlines[i].gameObject.SetActive(false);
                _refFills[i].gameObject.SetActive(false);
                _refLabels[i].gameObject.SetActive(false);
                _refValues[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Applies one reference DrawRows row (label left, value right).</summary>
        void SetRefRow(int i, string label, string value)
        {
            if (_refLabels == null || _refValues == null || _refFills == null || _refOutlines == null) return;
            bool selected = _selected == i;
            _refLabels[i].text = label;
            _refLabels[i].color = selected ? Ink : Dim;
            _refValues[i].text = value;
            _refValues[i].color = selected ? Accent : Dim;
            _refFills[i].color = selected ? RowFillSelected : RowFill;
            _refOutlines[i].color = selected ? Accent : RowBorder;
        }

        /// <summary>
        /// Lays the bank out for one screen in reference design units: the
        /// outline sits 1 unit outside the fill, the label 16 units in from the
        /// left and the value 16 in from the right, both vertically centred
        /// across the row like DrawRows' <c>textY = y + h/2 - 8</c> at the
        /// port's 26-unit text box.
        /// </summary>
        void PositionRefRows(int count, float x, float y0, float w, float h, float pitch)
        {
            if (_refLabels == null || _refValues == null || _refFills == null || _refOutlines == null) return;
            for (int i = 0; i < RefRowCount; i++)
            {
                bool used = i < count;
                _refOutlines[i].gameObject.SetActive(used);
                _refFills[i].gameObject.SetActive(used);
                _refLabels[i].gameObject.SetActive(used);
                _refValues[i].gameObject.SetActive(used);
                if (!used) continue;

                float y = y0 + i * pitch;
                float textY = y + h * 0.5f - 13f;
                ApplySkinRect(_refOutlines[i].gameObject,
                    new Vector2(D(x - 1f), -D(y - 1f)), new Vector2(D(w + 2f), D(h + 2f)));
                ApplySkinRect(_refFills[i].gameObject,
                    new Vector2(D(x), -D(y)), new Vector2(D(w), D(h)));
                ApplySkinRect(_refLabels[i].gameObject,
                    new Vector2(D(x + 16f), -D(textY)), new Vector2(D(300f), D(26f)));
                ApplySkinRect(_refValues[i].gameObject,
                    new Vector2(D(x + w - 316f), -D(textY)), new Vector2(D(300f), D(26f)));
            }
        }

        void SetCenterTitle(string text, bool accent, float refY)
        {
            if (_centerTitle == null) return;
            _centerTitle.text = text;
            _centerTitle.color = accent ? Accent : Ink;
            PlaceCentered(_centerTitle, refY, 40f);
        }

        void SetCenterSub(Text? text, string value, float refY)
        {
            if (text == null) return;
            text.text = value;
            text.gameObject.SetActive(value.Length > 0);
            if (value.Length > 0) PlaceCentered(text, refY, 15f);
        }

        /// <summary>
        /// Places centred reference text: the glyphs' top ends up at the
        /// reference's y (the rect is lifted by the line's leading and the box
        /// centre carries the glyph centre), so the 40/15-unit lines land where
        /// the raylib build drew them.
        /// </summary>
        void PlaceCentered(Text text, float refY, float size)
        {
            ApplySkinRect(text.gameObject,
                new Vector2(0f, -D(refY - 0.12f * size)), new Vector2(D(1280f), D(1.24f * size)));
        }

        /// <summary>
        /// Reference-canvas text drawn from its top-left corner, lifted by the
        /// font's leading so the glyphs start at the reference's y.
        /// </summary>
        Text MakeRefText(Transform parent, string name, float x, float refY, float w,
            string text, float designSize, Color color, TextAnchor alignment)
        {
            float lead = 0.12f * designSize;
            return MakeSkinText(parent, name, x, refY - lead, w, 1.6f * designSize,
                text, designSize, color, alignment);
        }

        /// <summary>Centre-anchored preview stroke (uGUI has no line primitive).</summary>
        Image MakePreviewBar(Transform parent, string name, Vector2 anchoredPosition,
            Vector2 size, float rotationDegrees, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Center;
            rect.anchorMax = Center;
            rect.pivot = Center;
            go.transform.SetParent(parent);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            if (rotationDegrees != 0f) rect.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);

            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>
        /// Reference per-screen scrim. Match-over keeps the main screen's light
        /// veil on purpose: HudSync draws the forced scoreboard beneath this
        /// canvas, and the reference's rgba(6,8,12,200) would bury it (the
        /// reference draws its board above that scrim instead).
        /// </summary>
        static Color BackdropFor(Screen screen)
        {
            switch (screen)
            {
                case Screen.Paused:
                case Screen.Settings:
                    return PauseScrim;
                case Screen.Crosshair:
                    return CrosshairScrim;
                default:
                    return Scrim;
            }
        }
    }
}
