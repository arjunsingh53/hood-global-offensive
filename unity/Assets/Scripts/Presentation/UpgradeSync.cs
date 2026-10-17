using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// The kill-point upgrade cabinet overlay (reference <c>Screens.UpgradeMenu</c>).
    /// The sim raises <c>Player.WantsOpenUpgrade</c> for one frame when E is pressed
    /// next to a station; this component opens a code-built uGUI panel and freezes the
    /// match while it is up — <see cref="MatchDriver"/> skips its tick, exactly like the
    /// reference never steps the sim from its UpgradeMenu state. Up/Down (or W/S) move,
    /// Space or E buys, Esc closes; the E press that opened the cabinet must be released
    /// once before it can buy (reference <c>UpgradeMenuKeyLatch</c>). Keyboard-only like
    /// the rest of the blockout UI, so the cursor stays locked. Attach it to the
    /// MatchDriver GameObject; without it the cabinet simply never opens.
    ///
    /// Layout numbers are the reference's 1280×720 design canvas scaled ×1.5 onto the
    /// 1920×1080 uGUI canvas; colours convert the reference <c>Ui</c> tokens exactly.
    /// Two documented deviations: the CLOSE row is drawn like every other row (the
    /// reference leaves it invisible), and the footer key hint sits below it instead
    /// of inside it for 1080p legibility.
    /// </summary>
    public sealed class UpgradeSync : MonoBehaviour
    {
        static readonly UpgradeStat[] Stats =
        {
            UpgradeStat.Health, UpgradeStat.Damage, UpgradeStat.Speed, UpgradeStat.Jump,
        };

        /// <summary>Rows are the four stats followed by CLOSE.</summary>
        const int RowCount = 5;

        const float PanelW = 840f;
        const float PanelH = 645f;
        const float RowW = PanelW - 66f;
        const float RowX = 33f;
        const float RowH = 84f;
        const float RowGap = 12f;
        const float FirstRowY = 162f;

        // Reference Ui tokens (src/UI/Ui.cs), converted from 0-255 to 0-1.
        static readonly Color Veil = new Color(5f / 255f, 8f / 255f, 13f / 255f, 160f / 255f);
        static readonly Color PanelFill = new Color(13f / 255f, 18f / 255f, 26f / 255f, 246f / 255f);
        static readonly Color Ink = new Color(236f / 255f, 236f / 255f, 234f / 255f);
        static readonly Color InkSoft = new Color(236f / 255f, 236f / 255f, 234f / 255f, 206f / 255f);
        static readonly Color Dim = new Color(236f / 255f, 236f / 255f, 234f / 255f, 140f / 255f);
        static readonly Color Faint = new Color(236f / 255f, 236f / 255f, 234f / 255f, 70f / 255f);
        static readonly Color Accent = new Color(1f, 186f / 255f, 56f / 255f);
        static readonly Color Good = new Color(118f / 255f, 206f / 255f, 124f / 255f);
        static readonly Color Danger = new Color(226f / 255f, 74f / 255f, 62f / 255f);
        static readonly Color RowFill = new Color(1f, 1f, 1f, 10f / 255f);
        static readonly Color SelectedFill = new Color(1f, 186f / 255f, 56f / 255f, 42f / 255f);
        static readonly Color PipBack = new Color(1f, 1f, 1f, 22f / 255f);
        static readonly Color PointsFill = new Color(1f, 186f / 255f, 56f / 255f, 24f / 255f);
        static readonly Color Divider = new Color(1f, 1f, 1f, 28f / 255f);

        MatchDriver? _driver;
        Font? _font;
        GameObject? _panel;
        Text? _pointsValue;
        Text? _message;
        Text? _closeLabel;
        Image[]? _rowFills;
        Image[,]? _pips;
        Text[]? _rowNameTexts;
        Text[]? _rowEffects;
        Text[]? _rowStatus;

        int _selected;
        bool _eLatched;
        bool _justClosed;
        string _messageText = "";
        bool _messageGood;
        float _messageUntil;

        /// <summary>True while the cabinet panel is up.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// True while the cabinet is open and on the frame it closed, so the menu
        /// never treats the closing Esc (or Enter) as its own press.
        /// </summary>
        public bool BlocksMenuInput => IsOpen || _justClosed;

        void Start()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("UpgradeSync: no MatchDriver on this GameObject; disabling.");
                enabled = false;
                return;
            }

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            if (_font == null) Debug.LogWarning("UpgradeSync: no usable font; cabinet text will not draw.");

            BuildPanel();
            if (_panel != null) _panel.SetActive(false);
        }

        void Update()
        {
            _justClosed = false;

            if (_driver == null || _driver.Sim == null) return;
            Simulation sim = _driver.Sim;
            bool driverLive = _driver.enabled;

            if (IsOpen)
            {
                // A pause, match-over transition or torn-down driver closes the cabinet.
                if (!driverLive) { Close(); return; }
                StepOverlay();
                return;
            }

            if (!driverLive) return;
            Player p = sim.Player;
            if (p.WantsOpenUpgrade && p.Alive && !sim.MatchOver) Open();
        }

        void StepOverlay()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            if (_eLatched && !Input.GetKey(KeyCode.E)) _eLatched = false;

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                _selected = (_selected + RowCount - 1) % RowCount;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                _selected = (_selected + 1) % RowCount;

            bool buy = Input.GetKeyDown(KeyCode.Space) || (!_eLatched && Input.GetKeyDown(KeyCode.E));
            if (buy) Choose(_selected);

            if (IsOpen) RefreshRows();
        }

        void Open()
        {
            IsOpen = true;
            _selected = 0;
            // The E that opened the cabinet is still held: latch it so it cannot
            // also buy a stat (reference UpgradeMenuKeyLatch).
            _eLatched = Input.GetKey(KeyCode.E);
            if (_panel != null) _panel.SetActive(true);
            RefreshRows();
        }

        void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _justClosed = true;
            if (_panel != null) _panel.SetActive(false);
        }

        void Choose(int index)
        {
            if (_driver == null || _driver.Sim == null) return;
            Simulation sim = _driver.Sim;
            if (index >= Stats.Length) { Close(); return; }

            UpgradeStat stat = Stats[index];
            if (sim.TryBuyUpgrade(sim.Player, stat))
            {
                Message($"{Simulation.StatName(stat).ToLowerInvariant()} upgraded", true);
            }
            else
            {
                bool maxed = Simulation.UpgradeLevel(sim.Player, stat) >= Cfg.UpgradeMaxLevel;
                Message(maxed ? $"{Simulation.StatName(stat).ToLowerInvariant()} is maxed" : "not enough kill points",
                    false);
            }
        }

        void Message(string text, bool good)
        {
            _messageText = text;
            _messageGood = good;
            _messageUntil = Time.unscaledTime + 1.6f;
        }

        static string StatEffect(UpgradeStat stat) => stat switch
        {
            UpgradeStat.Health => $"+{Cfg.UpgradeHealthPerLevel:0} MAX HEALTH",
            UpgradeStat.Damage => $"+{Cfg.UpgradeDamagePerLevel * 100f:0}% WEAPON DAMAGE",
            UpgradeStat.Speed => $"+{Cfg.UpgradeSpeedPerLevel * 100f:0}% MOVE SPEED",
            _ => $"+{Cfg.UpgradeJumpPerLevel * 100f:0}% JUMP HEIGHT",
        };

        // --------------------------------------------------------------------- draw

        void RefreshRows()
        {
            if (_driver == null || _driver.Sim == null || _panel == null) return;
            if (_rowFills == null || _rowNameTexts == null
                || _rowEffects == null || _rowStatus == null || _pips == null) return;

            Player p = _driver.Sim.Player;
            if (_pointsValue != null) _pointsValue.text = $"{p.KillPoints}";

            for (int i = 0; i < Stats.Length; i++)
            {
                bool selected = _selected == i;
                int level = Simulation.UpgradeLevel(p, Stats[i]);
                bool maxed = level >= Cfg.UpgradeMaxLevel;
                bool affordable = p.KillPoints >= Cfg.UpgradePointCost && !maxed;

                _rowFills[i].color = selected ? SelectedFill : RowFill;
                _rowNameTexts[i].color = selected ? Ink : InkSoft;
                _rowEffects[i].color = selected ? Accent : Dim;
                for (int pip = 0; pip < Cfg.UpgradeMaxLevel; pip++)
                    _pips[i, pip].color = pip < level ? Accent : PipBack;

                _rowStatus[i].text = maxed ? "MAX" : affordable ? $"COST {Cfg.UpgradePointCost}" : "NEEDS POINTS";
                _rowStatus[i].color = maxed ? Good : affordable ? Accent : Danger;
            }

            bool closeSelected = _selected == Stats.Length;
            _rowFills[Stats.Length].color = closeSelected ? SelectedFill : RowFill;
            if (_closeLabel != null) _closeLabel.color = closeSelected ? Ink : Dim;

            bool showMessage = Time.unscaledTime < _messageUntil;
            if (_message != null)
            {
                _message.gameObject.SetActive(showMessage);
                if (showMessage)
                {
                    _message.text = _messageText.ToUpperInvariant();
                    _message.color = _messageGood ? Good : Danger;
                }
            }
        }

        void BuildPanel()
        {
            GameObject canvasGo = new GameObject("UpgradeSync Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.transform.SetParent(transform);
            _panel = canvasGo;

            Transform root = canvasGo.transform;
            MakeImage(root, "Veil", Vector2.zero, new Vector2(1920f, 1080f), Veil);
            MakeImage(root, "Top Strip", new Vector2(0f, 536f), new Vector2(1920f, 8f), Accent);

            GameObject cabinetGo = new GameObject("Cabinet");
            cabinetGo.AddComponent<CanvasRenderer>();
            cabinetGo.AddComponent<RectTransform>();
            cabinetGo.transform.SetParent(root);
            ApplyRect(cabinetGo, Vector2.zero, new Vector2(PanelW, PanelH));
            Image cabinet = cabinetGo.AddComponent<Image>();
            cabinet.color = PanelFill;
            Transform panel = cabinetGo.transform;

            MakePanelImage(panel, "Header Divider", 0f, 143f, PanelW, 2f, Divider);
            MakePanelText(panel, "Station", 39f, 33f, 300f, 18, Accent, TextAnchor.MiddleLeft).text = "STATION";
            MakePanelText(panel, "Title", 38f, 57f, 520f, 40, Ink, TextAnchor.MiddleLeft).text =
                "UPGRADE CABINET";

            const float ptsX = PanelW - 252f - 36f;
            MakePanelImage(panel, "Points Badge", ptsX, 33f, 252f, 78f, PointsFill);
            MakePanelText(panel, "Points Label", ptsX + 21f, 45f, 220f, 15, Dim, TextAnchor.MiddleLeft).text =
                "KILL POINTS";
            _pointsValue = MakePanelText(panel, "Points Value", ptsX + 153f, 66f, 78f, 32, Accent,
                TextAnchor.MiddleRight);

            _rowFills = new Image[RowCount];
            _pips = new Image[Stats.Length, Cfg.UpgradeMaxLevel];
            _rowNameTexts = new Text[Stats.Length];
            _rowEffects = new Text[Stats.Length];
            _rowStatus = new Text[Stats.Length];

            for (int i = 0; i < Stats.Length; i++)
            {
                float rowY = FirstRowY + i * (RowH + RowGap);
                _rowFills[i] = MakePanelImage(panel, $"Row {i}", RowX, rowY, RowW, RowH, RowFill);
                _rowNameTexts[i] = MakePanelText(panel, $"Row {i} Name", RowX + 27f, rowY + 15f, 340f, 26,
                    Ink, TextAnchor.MiddleLeft);
                _rowNameTexts[i].text = Simulation.StatName(Stats[i]);
                _rowEffects[i] = MakePanelText(panel, $"Row {i} Effect", RowX + 27f, rowY + 51f, 420f, 15,
                    Dim, TextAnchor.MiddleLeft);
                _rowEffects[i].text = StatEffect(Stats[i]);
                for (int pip = 0; pip < Cfg.UpgradeMaxLevel; pip++)
                {
                    float pipX = RowX + RowW - 27f - Cfg.UpgradeMaxLevel * 51f
                        - (Cfg.UpgradeMaxLevel - 1) * 12f + pip * (51f + 12f);
                    _pips[i, pip] = MakePanelImage(panel, $"Row {i} Pip {pip}", pipX, rowY + 27f, 51f, 15f,
                        PipBack);
                }
                _rowStatus[i] = MakePanelText(panel, $"Row {i} Status", PanelW - 260f, rowY + 51f, 200f, 15,
                    Dim, TextAnchor.MiddleRight);
            }

            float closeY = FirstRowY + Stats.Length * (RowH + RowGap);
            _rowFills[Stats.Length] = MakePanelImage(panel, "Close Row", RowX, closeY, RowW, 63f, RowFill);
            _closeLabel = MakePanelText(panel, "Close Label", RowX, closeY, RowW, 26, Dim, TextAnchor.MiddleCenter);
            _closeLabel.text = "CLOSE";

            _message = MakePanelText(panel, "Message", 0f, 523f, PanelW, 20, Good, TextAnchor.MiddleCenter);
            _message.gameObject.SetActive(false);

            MakePanelText(panel, "Key Hint", 39f, 613f, 760f, 17, Faint, TextAnchor.MiddleLeft).text =
                "W/S SELECT   SPACE/E BUY   ESC CLOSE";
            MakePanelText(panel, "Rule Hint", PanelW - 359f, 615f, 320f, 15, Dim, TextAnchor.MiddleRight).text =
                "MAX 3/STAT - DEATH COSTS 1";
        }

        Text MakePanelText(Transform panel, string name, float left, float top, float width,
            int fontSize, Color color, TextAnchor align)
        {
            float height = fontSize + 12f;
            Text text = MakeText(panel, name,
                new Vector2(left + width * 0.5f - PanelW * 0.5f, PanelH * 0.5f - top - height * 0.5f),
                new Vector2(width, height), fontSize, color);
            text.alignment = align;
            return text;
        }

        Image MakePanelImage(Transform panel, string name, float left, float top, float width, float height,
            Color color)
        {
            return MakeImage(panel, name,
                new Vector2(left + width * 0.5f - PanelW * 0.5f, PanelH * 0.5f - top - height * 0.5f),
                new Vector2(width, height), color);
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
