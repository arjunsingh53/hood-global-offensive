using System;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// First-pass front end: a code-built uGUI menu stack (main / pause / settings /
    /// match-over) that owns game-flow state. It never touches the simulation's rules —
    /// leaving <see cref="Screen.Playing"/> simply disables the <see cref="MatchDriver"/>
    /// (the fixed tick stops), releases the cursor and freezes time; entering it
    /// re-enables the driver, locks the cursor and restores time. Player settings (look
    /// sensitivity, Y inversion) persist through <c>PlayerPrefs</c> and are pushed onto
    /// the driver.
    ///
    /// Keyboard-driven on purpose so it needs no EventSystem/Button wiring: Up/Down move
    /// the selection, Enter activates, Esc pauses / backs out, Left/Right adjust the
    /// focused setting. Attach it to the MatchDriver GameObject. The driver may still
    /// lock the cursor in its own <c>Awake</c>: this component applies menu state in
    /// <c>Start</c>, which is guaranteed to run after every <c>Awake</c>.
    /// </summary>
    public sealed class MenuSync : MonoBehaviour
    {
        enum Screen { Main, Playing, Paused, Settings, MatchOver }

        const string SensitivityKey = "HGO.LookSensitivity";
        const string InvertYKey = "HGO.InvertLookY";
        const float MinSensitivity = 0.2f;
        const float MaxSensitivity = 3.0f;
        const float SensitivityStep = 0.1f;

        static readonly Color Backdrop = new Color(0.04f, 0.05f, 0.06f, 0.82f);
        static readonly Color Ink = new Color(0.93f, 0.95f, 0.96f);
        static readonly Color Dim = new Color(0.55f, 0.58f, 0.60f);
        static readonly Color Gold = new Color(0.94f, 0.78f, 0.35f);

        readonly string[] _rows = new string[6];

        MatchDriver? _driver;
        Font? _font;
        GameObject? _panel;
        Text _title;
        Text _subtitle;
        Text _hint;
        Text[] _rowTexts;

        Screen _screen = Screen.Main;
        Screen _settingsReturn = Screen.Main;
        int _selected;

        float _sensitivity = 1f;
        bool _invertY;

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
            ApplySettings();
        }

        void Start()
        {
            if (_driver == null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            if (_font == null) Debug.LogWarning("MenuSync: no usable font; menu text will not draw.");

            BuildPanel();
            SetScreen(Screen.Main);
        }

        void Update()
        {
            if (_driver == null) return;

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

            if (enter) Activate();
            RefreshRows();
        }

        // ------------------------------------------------------------- state machine

        int RowCount()
        {
            switch (_screen)
            {
                case Screen.Main: return 3;
                case Screen.Paused: return 4;
                case Screen.MatchOver: return 2;
                case Screen.Settings: return 3;
                default: return 0;
            }
        }

        void Activate()
        {
            switch (_screen)
            {
                case Screen.Main:
                    if (_selected == 0) BeginPlay();
                    else if (_selected == 1) OpenSettings(Screen.Main);
                    else Quit();
                    break;
                case Screen.Paused:
                    if (_selected == 0) SetScreen(Screen.Playing);
                    else if (_selected == 1) OpenSettings(Screen.Paused);
                    else if (_selected == 2) BeginPlay();
                    else Quit();
                    break;
                case Screen.MatchOver:
                    if (_selected == 0) BeginPlay();
                    else Quit();
                    break;
                case Screen.Settings:
                    if (_selected == 1) ToggleInvert();
                    else if (_selected == 2) SetScreen(_settingsReturn);
                    // Row 0 (sensitivity) is adjusted with Left/Right; Enter is a no-op.
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

        void SetScreen(Screen screen)
        {
            _screen = screen;
            _selected = 0;

            bool playing = screen == Screen.Playing;
            if (_driver != null) _driver.enabled = playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Time.timeScale = playing ? 1f : 0f;
            if (_panel != null) _panel.SetActive(!playing);

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
        }

        // --------------------------------------------------------------------- draw

        void RefreshRows()
        {
            switch (_screen)
            {
                case Screen.Main:
                    _title.text = "HOODMATCH";
                    _subtitle.text = "FIRST-PERSON BLOCKOUT";
                    _rows[0] = "PLAY"; _rows[1] = "SETTINGS"; _rows[2] = "QUIT";
                    break;
                case Screen.Paused:
                    _title.text = "PAUSED";
                    _subtitle.text = "";
                    _rows[0] = "RESUME"; _rows[1] = "SETTINGS"; _rows[2] = "RESTART"; _rows[3] = "QUIT";
                    break;
                case Screen.MatchOver:
                    _title.text = "MATCH OVER";
                    _subtitle.text = _driver != null && _driver.Sim != null ? _driver.Sim.MatchResult : "";
                    _rows[0] = "PLAY AGAIN"; _rows[1] = "QUIT";
                    break;
                case Screen.Settings:
                    _title.text = "SETTINGS";
                    _subtitle.text = "LEFT / RIGHT adjusts the focused setting";
                    _rows[0] = $"LOOK SENSITIVITY   {_sensitivity:0.0}";
                    _rows[1] = $"INVERT Y   {(_invertY ? "ON" : "OFF")}";
                    _rows[2] = "BACK";
                    break;
               default:
                    break;
            }

            if (_rowTexts == null) return;
            int count = RowCount();
            for (int i = 0; i < _rowTexts.Length; i++)
            {
                bool used = i < count;
                _rowTexts[i].gameObject.SetActive(used);
                if (!used) continue;
                _rowTexts[i].text = (_selected == i ? "> " : "  ") + _rows[i];
                _rowTexts[i].color = _selected == i ? Gold : Ink;
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
            MakeImage(root, "Backdrop", Vector2.zero, new Vector2(1920f, 1080f), Backdrop);
            _title = MakeText(root, "Title", new Vector2(0f, 150f), new Vector2(1400f, 90f), 64, Ink);
            _subtitle = MakeText(root, "Subtitle", new Vector2(0f, 70f), new Vector2(1400f, 40f), 22, Dim);
            _hint = MakeText(root, "Hint", new Vector2(0f, -330f), new Vector2(1400f, 30f), 16, Dim);
            _hint.text = "UP / DOWN move     ENTER select     ESC back";

            _rowTexts = new Text[6];
            for (int i = 0; i < _rowTexts.Length; i++)
            {
                _rowTexts[i] = MakeText(root, $"Row {i}",
                    new Vector2(0f, -10f - i * 52f), new Vector2(900f, 48f), 34, Ink);
            }
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
    }
}
