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
    /// The main screen renders the reference <c>Screens.MainMenu.Draw</c> skin (left command
    /// surface, panelled label/value setup rows, LOCAL OPERATION match brief, footer hints)
    /// in the reference's 1280x720 design canvas mapped at the canvas's fixed 1.5x; the
    /// pause / settings / crosshair-options / match-over screens are still centred blockout
    /// rows awaiting their own skin pass.
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
        // Blockout screens only — the reference skin uses Accent above.
        static readonly Color Gold = new Color(0.94f, 0.78f, 0.35f);
        static readonly Color PreviewBackdrop = new Color(0.02f, 0.03f, 0.045f, 0.92f);

        // The reference front end is authored in Ui's 1280x720 design canvas
        // and letterboxed to the window; the menu canvas is a fixed
        // 1920x1080 CanvasScaler, so one design unit maps to 1.5 canvas px.
        const float UiScale = 1.5f;

        static float D(float v) => v * UiScale;

        static int DSize(float design) => (int)Math.Round(design * UiScale);

        readonly string[] _rows = new string[12];
        readonly CrosshairSettings _crosshair = new();
        readonly CameraFeedbackSettings _cameraFeedback = new();
        readonly string[] _loadout = (string[])WeaponRegistry.DefaultLoadout.Clone();

        MatchDriver? _driver;
        HudSync? _hud;
        UpgradeSync? _upgradeSync;
        Font? _font;
        GameObject? _panel;
        GameObject? _mainRoot;
        GameObject? _previewPanel;
        CrosshairGraphic? _previewCrosshair;
        Text _title;
        Text _subtitle;
        Text _hint;
        Text[] _rowTexts;
        // Reference-skin main menu (Screens.MainMenu.Draw): label/value row
        // panels with the selection carried by the panel highlight, plus the
        // LOCAL OPERATION match-brief card whose map name updates live.
        Text[]? _rowLabels;
        Text[]? _rowValues;
        Image[]? _rowFills;
        Image[]? _rowOutlines;
        Text? _briefMap;

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
                    // Reference preview: scale 1.75 and a fixed 5 px spread so the
                    // dynamic-gap setting is visible at a glance.
                    _previewCrosshair.Update(_crosshair, _crosshair.DynamicGap ? 5f : 0f, 1.75f);
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
            if (_previewPanel != null) _previewPanel.SetActive(screen == Screen.Crosshair);
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
                    break;
                case Screen.Paused:
                    _title.text = "PAUSED";
                    _subtitle.text = sim != null
                        ? $"{sim.Player.Kills} kills   {sim.Player.Deaths} deaths   {sim.Player.KillPoints} upgrade points"
                        : "";
                    _rows[0] = "RESUME";
                    _rows[1] = $"CROSSHAIR OPTIONS   {_crosshair.PresetLabel}";
                    _rows[2] = $"CAMERA FEEDBACK   {_cameraFeedback.Label}";
                    _rows[3] = "SETTINGS";
                    _rows[4] = "RESTART MATCH";
                    _rows[5] = "QUIT TO MENU";
                    break;
                case Screen.MatchOver:
                    _title.text = "MATCH OVER";
                    _subtitle.text = sim != null
                        ? $"{sim.MatchResult}   -   you finished rank {sim.PlayerRank() + 1} of {sim.Actors.Count}"
                        : "";
                    _rows[0] = "PLAY AGAIN";
                    _rows[1] = "BACK TO MENU";
                    break;
                case Screen.Settings:
                    _title.text = "SETTINGS";
                    _subtitle.text = "LEFT / RIGHT adjusts the focused setting";
                    _rows[0] = $"LOOK SENSITIVITY   {_sensitivity:0.0}";
                    _rows[1] = $"INVERT Y   {(_invertY ? "ON" : "OFF")}";
                    _rows[2] = "BACK";
                    break;
                case Screen.Crosshair:
                    _title.text = "CROSSHAIR OPTIONS";
                    _subtitle.text = "LEFT / RIGHT adjusts the focused setting";
                    _rows[0] = $"PRESET   {_crosshair.PresetLabel}";
                    _rows[1] = $"COLOR   {_crosshair.ColorLabel}";
                    _rows[2] = $"DYNAMIC GAP   {(_crosshair.DynamicGap ? "ON" : "OFF")}";
                    _rows[3] = $"CENTRE DOT   {(_crosshair.CentreDot ? "ON" : "OFF")}";
                    _rows[4] = $"BASE GAP   {_crosshair.BaseGap} px";
                    _rows[5] = $"ARM LENGTH   {_crosshair.ArmLength} px";
                    _rows[6] = $"THICKNESS   {_crosshair.Thickness} px";
                    _rows[7] = "RESET DEFAULT";
                    _rows[8] = "BACK";
                    break;
               default:
                    break;
            }

            if (_rowTexts == null) return;

            // The main screen draws its own reference skin (command surface,
            // top-left title block, panelled rows, match brief); the centred
            // blockout title/subtitle/hint and text rows belong to the
            // remaining screens. BuildPanel assigns all of them together, so
            // the _rowTexts guard above implies the rest exist.
            bool main = _screen == Screen.Main;
            if (_mainRoot != null) _mainRoot.SetActive(main);
            _title.gameObject.SetActive(!main);
            _subtitle.gameObject.SetActive(!main);
            _hint.gameObject.SetActive(!main);
            if (main)
            {
                // The centred blockout rows would otherwise show through the
                // reference skin from the startup state (they default active).
                for (int i = 0; i < _rowTexts.Length; i++) _rowTexts[i].gameObject.SetActive(false);
                return;
            }

            // The crosshair screen runs nine tighter rows under the live
            // preview, so it lifts the title and uses a reduced pitch and
            // font; the shorter screens keep the roomy original.
            bool listScreen = _screen == Screen.Crosshair;
            _title.rectTransform.anchoredPosition = new Vector2(0f, listScreen ? 300f : 150f);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, listScreen ? 240f : 70f);

            float top = _screen == Screen.Main ? 195f : _screen == Screen.Crosshair ? 55f : -10f;
            float pitch = listScreen ? 42f : 52f;
            int fontSize = listScreen ? 26 : 34;
            Vector2 rowSize = new Vector2(900f, listScreen ? 38f : 48f);

            int count = RowCount();
            for (int i = 0; i < _rowTexts.Length; i++)
            {
                bool used = i < count;
                _rowTexts[i].gameObject.SetActive(used);
                if (!used) continue;
                _rowTexts[i].text = (_selected == i ? "> " : "  ") + _rows[i];
                _rowTexts[i].color = _selected == i ? Gold : Ink;
                _rowTexts[i].fontSize = fontSize;
                _rowTexts[i].rectTransform.anchoredPosition = new Vector2(0f, top - i * pitch);
                _rowTexts[i].rectTransform.sizeDelta = rowSize;
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
            // The reference scrim is rgba(4,7,11,94) over the parked scenic
            // backdrop, so the map stays visible behind the front end; only
            // the command surface (760 design units) is heavily darkened.
            MakeImage(root, "Backdrop", Vector2.zero, new Vector2(1920f, 1080f), Scrim);
            _title = MakeText(root, "Title", new Vector2(0f, 150f), new Vector2(1400f, 90f), 64, Ink);
            _subtitle = MakeText(root, "Subtitle", new Vector2(0f, 70f), new Vector2(1400f, 40f), 22, Dim);
            _hint = MakeText(root, "Hint", new Vector2(0f, -330f), new Vector2(1400f, 30f), 16, Dim);
            _hint.text = "UP / DOWN move     ENTER select     ESC back";

            Image previewImage = MakeImage(root, "Crosshair Preview",
                new Vector2(0f, 150f), new Vector2(640f, 110f), PreviewBackdrop);
            _previewPanel = previewImage.gameObject;
            _previewCrosshair = CrosshairGraphic.Create(_previewPanel.transform, "Preview Crosshair");
            _previewPanel.SetActive(false);

            _rowTexts = new Text[12];
            for (int i = 0; i < _rowTexts.Length; i++)
            {
                _rowTexts[i] = MakeText(root, $"Row {i}",
                    new Vector2(0f, -10f - i * 52f), new Vector2(900f, 48f), 34, Ink);
            }

            BuildMainSurface(root);
        }

        void ApplyRect(GameObject go, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null) rectTransform = go.AddComponent<RectTransform>();
            rectTransform.anchorMin = Center;
            rectTransform.anchorMax = Center;
            rectTransform.pivot = Center;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        Text MakeText(Transform parent, string name, Vector2 anchoredPosition,
            Vector2 size, int fontSize, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplyRect(go, anchoredPosition, size);

            Text text = go.AddComponent<Text>();
            text.font = _font ?? Font.CreateDynamicFontFromOSFont("Arial", 24);
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            return text;
        }

        Image MakeImage(Transform parent, string name, Vector2 anchoredPosition,
            Vector2 size, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplyRect(go, anchoredPosition, size);

            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

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
    }
}
