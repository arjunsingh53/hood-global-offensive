using System;
using System.Collections.Generic;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace HoodGlobalOffensive.Presentation
{
    /// <summary>
    /// First-pass uGUI HUD driven entirely by sim state — no editor wiring needed:
    /// the canvas, scaler and every element are built in code. It mirrors the
    /// reference HUD's information layer (health card with build pips, weapon
    /// readout card with magazine bar + loadout slots,
    /// top bar with match clock + YOU/LEAD/FT line, compass tape, rotating radar
    /// scanner, kill feed, hitmarker, hook status squares, station prompt, damage
    /// flash + directional indicator, low-health vignette, scope overlay,
    /// match-over banner, Tab scoreboard) as one blockout; per-part colours
    /// follow the reference palette where the values were already captured. The
    /// peripheral anchors fade to 0.38 opacity behind a real scope / calibrated
    /// sight picture (reference Hud.PeripheralHudOpacity); the reticle, hitmarker
    /// and scope mask are never faded. Read-only view: it never writes to the
    /// simulation.
    /// </summary>
    public sealed class HudSync : MonoBehaviour
    {
        // Reference palette (src/UI/Ui.cs equivalents, captured values).
        static readonly Color Ink = new Color(0.93f, 0.95f, 0.96f);
        static readonly Color Dim = new Color(0.55f, 0.58f, 0.60f);
        static readonly Color Danger = new Color(0.86f, 0.28f, 0.26f);
        static readonly Color Good = new Color(0.36f, 0.78f, 0.47f);
        static readonly Color Amber = new Color(1.00f, 0.74f, 0.27f);
        static readonly Color Gold = new Color(0.94f, 0.78f, 0.35f);
        static readonly Color DamageTint = new Color(1.00f, 0.12f, 0.10f);
        static readonly Color DamageInk = new Color(248f / 255f, 248f / 255f, 240f / 255f);
        static readonly Color HeadshotDamage = new Color(1f, 176f / 255f, 48f / 255f);
        static readonly Color Faint = new Color(0.38f, 0.40f, 0.42f);
        static readonly Color ScorePanel = new Color(8f / 255f, 10f / 255f, 14f / 255f, 225f / 255f);
        static readonly Color DefaultBracketColor = new Color(232f / 255f, 240f / 255f, 244f / 255f, 130f / 255f);
        static readonly Color CardBorderColor = new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f);
        static readonly Color AccentInk = new Color(1f, 186f / 255f, 56f / 255f);
        static readonly Color RadarBracketColor = new Color(112f / 255f, 205f / 255f, 228f / 255f, 180f / 255f);

        const int FeedLines = 6;

        MatchDriver? _driver;
        Font? _font;

        VitalsReadout? _vitals;
        WeaponReadout? _weaponReadout;
        Text _timer;
        Text _matchResult;
        Image? _topBarBorder;
        Image? _topBarFill;
        CornerBrackets? _topBarBrackets;
        Text? _clockLabel;
        Text? _clockScore;
        Image? _compassBorder;
        Image? _compassFill;
        CornerBrackets? _compassBrackets;
        Image? _compassCaret;
        Text? _compassHeading;
        Image?[] _compassTicks = Array.Empty<Image?>();
        Text?[] _compassTickLabels = Array.Empty<Text?>();
        Image? _radarBorder;
        Image? _radarFill;
        CornerBrackets? _radarBrackets;
        RadarGraphic? _radar;
        Text? _radarTitle;
        Text? _radarRange;
        CrosshairGraphic _crosshair;
        Text _hitmarker;
        Text? _hitmarkerLabel;
        DeathOverlay? _deathOverlay;
        Image _damageOverlay;
        TraumaGraphic? _trauma;
        RectTransform _damageWheel;
        FeedRow[] _killFeed;
        HookSquare? _hookLeft;
        HookSquare? _hookRight;
        Text? _stationTitle;
        Text? _stationSub;
        ScopeOverlay? _scope;
        GameObject? _scoreboard;
        RectTransform? _scoreboardRect;
        Text? _scoreTitle;
        Text? _scoreFirstTo;
        Text? _scoreFooter;
        readonly List<ScoreRow> _scoreRows = new();

        static readonly UpgradeStat[] BuildStats =
        {
            UpgradeStat.Health, UpgradeStat.Damage, UpgradeStat.Speed, UpgradeStat.Jump,
        };

        /// <summary>
        /// One kill-feed line (reference <c>Hud.DrawKillFeed</c>): an arena panel behind the
        /// row whose brackets take the accent for player kills, then the killer name, the
        /// <c>&gt;</c> separator, the weapon (or HEAD) and the victim, each with the
        /// reference's own colour rule (killer amber for the local player, victim red when
        /// it is the local player, weapon red on a headshot). The reference measures each
        /// name and sizes the panel from the text; uGUI cannot measure before layout here,
        /// so the panel is a fixed 470x37.5 canvas-pixel row with fixed name slots — the
        /// documented blockout approximation for this row.
        /// </summary>
        sealed class FeedRow
        {
            public GameObject Root = null!;
            public Image Border = null!;
            public Image Fill = null!;
            public CornerBrackets Brackets = null!;
            public Text Killer = null!;
            public Text Separator = null!;
            public Text Weapon = null!;
            public Text Victim = null!;
        }

        sealed class ScoreRow
        {
            public GameObject Root = null!;
            public GameObject Fill = null!;
            public Text Name = null!;
            public Text Kills = null!;
            public Text Deaths = null!;
            public Text Kd = null!;
            public Text Pts = null!;
        }

        /// <summary>
        /// Quiet dual-hook readout under the reticle (reference
        /// <c>Hud.DrawHookStatus</c>): Q and E squares light gold while that hook is
        /// attached to a surface.
        /// </summary>
        sealed class HookSquare
        {
            public GameObject Root = null!;
            public Image Border = null!;
            public Image Fill = null!;
            public Text Key = null!;

            public void Set(bool attached, float opacity)
            {
                Color border = attached
                    ? Gold
                    : new Color(236f / 255f, 240f / 255f, 244f / 255f, 92f / 255f);
                Color fill = attached
                    ? new Color(Gold.r, Gold.g, Gold.b, 52f / 255f)
                    : new Color(5f / 255f, 8f / 255f, 13f / 255f, 130f / 255f);
                Border.color = Fade(border, opacity);
                Fill.color = Fade(fill, opacity);
                Key.color = Fade(attached ? Gold : Faint, opacity);
            }
        }

        /// <summary>
        /// Procedural corner bracket overlays (reference <c>Hud.DrawCornerBracket</c>):
        /// four L-shaped bracket overlays (eight line segments) reinforcing the corners
        /// of an arena-panel card with length min(12, min(w, h)*0.24) and thickness 1.5.
        /// </summary>
        sealed class CornerBrackets
        {
            public readonly Image[] Segments = new Image[8];

            public void Set(Color color, float opacity)
            {
                Color c = Fade(color, opacity);
                for (int i = 0; i < Segments.Length; i++)
                {
                    Segments[i].color = c;
                }
            }

            public static CornerBrackets Build(Transform parent, string prefix, float dx, float dy, float w, float h,
                Color initialColor, float scale = 1.5f)
            {
                CornerBrackets brackets = new CornerBrackets();
                float corner = MathF.Min(12f, MathF.Min(w, h) * 0.24f);
                const float t = 1.5f;

                float bx = dx - 1f;
                float by = dy - 1f;
                float bw = w + 2f;
                float bh = h + 2f;

                // Top-Left corner
                brackets.Segments[0] = Box(parent, $"{prefix} Bracket TL H", bx, by, corner, t, initialColor, scale);
                brackets.Segments[1] = Box(parent, $"{prefix} Bracket TL V", bx, by, t, corner, initialColor, scale);

                // Top-Right corner
                brackets.Segments[2] = Box(parent, $"{prefix} Bracket TR H", bx + bw - corner, by, corner, t, initialColor, scale);
                brackets.Segments[3] = Box(parent, $"{prefix} Bracket TR V", bx + bw - t, by, t, corner, initialColor, scale);

                // Bottom-Left corner
                brackets.Segments[4] = Box(parent, $"{prefix} Bracket BL H", bx, by + bh - t, corner, t, initialColor, scale);
                brackets.Segments[5] = Box(parent, $"{prefix} Bracket BL V", bx, by + bh - corner, t, corner, initialColor, scale);

                // Bottom-Right corner
                brackets.Segments[6] = Box(parent, $"{prefix} Bracket BR H", bx + bw - corner, by + bh - t, corner, t, initialColor, scale);
                brackets.Segments[7] = Box(parent, $"{prefix} Bracket BR V", bx + bw - t, by + bh - corner, t, corner, initialColor, scale);

                return brackets;
            }

            static Image Box(Transform parent, string name, float dx, float dy, float w, float h, Color color, float scale)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(dx * scale, -dy * scale);
                rect.sizeDelta = new Vector2(w * scale, h * scale);
                Image image = go.AddComponent<Image>();
                image.color = color;
                return image;
            }
        }

        /// <summary>
        /// The bottom-left biological status card (reference <c>Hud.DrawVitals</c>): the
        /// large health numeral with drop shadow, the left health-status rail, the
        /// HEALTH caption and CRITICAL / COMBAT READY status, the ten-segment health bar
        /// inside its 1 px outline, and the four build pips (HP, DMG, SPD, JMP) with
        /// active levels. Ported in the reference's 1280x720 logical units inside a 250x88
        /// card at the 1.5x canvas scale, perfectly mirroring the bottom-right weapon card.
        /// </summary>
        sealed class VitalsReadout
        {
            const float Scale = 1.5f;
            const float CardW = 250f;
            const float CardH = 88f;
            const float BarW = 148f;
            const float BarH = 8f;
            const int HealthSegments = 10;
            const float HealthGap = 2f;

            static readonly Color Accent = new Color(1f, 186f / 255f, 56f / 255f);

            public GameObject Root = null!;
            Image CardBorder = null!;
            Image CardFill = null!;
            CornerBrackets _brackets = null!;
            Image LeftRail = null!;
            Text ValueShadow = null!;
            Text Value = null!;
            Text Label = null!;
            Text Status = null!;
            readonly Image[] _segments = new Image[HealthSegments];
            readonly Image[] _barOutlines = new Image[4];
            Text BuildCaption = null!;
            readonly Pip[] _pips = new Pip[4];

            sealed class Pip
            {
                public Image Box = null!;
                public readonly Image[] Outlines = new Image[4];
                public Text Label = null!;
                public Text Value = null!;
            }

            public void Set(Player p, float op)
            {
                float health = U.Clamp01(p.Health / p.MaxHealth);
                bool low = p.Health < Cfg.LowHealthTraumaThreshold;
                Color healthColor = low
                    ? Danger
                    : (health > 0.6f ? Ink : Amber);

                // Reference DrawArenaPanel: the outline stays neutral ink and the health
                // colour lives in the corner brackets, rail, numeral and bar.
                CardBorder.color = Fade(CardBorderColor, op);
                CardFill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
                _brackets.Set(healthColor, op);
                LeftRail.color = Fade(healthColor, op);

                string valStr = $"{MathF.Ceiling(p.Health):0}";
                Value.text = valStr;
                Value.color = Fade(healthColor, op);
                ValueShadow.text = valStr;
                ValueShadow.color = Fade(new Color(0f, 0f, 0f, 215f / 255f), op);

                Label.color = Fade(low ? Danger : Dim, op);
                Status.text = low ? "CRITICAL" : "COMBAT READY";
                Status.color = Fade(low ? healthColor : Good, op);

                float active = health * HealthSegments;
                for (int i = 0; i < HealthSegments; i++)
                {
                    float intensity = U.Clamp01(active - i);
                    Color segment = intensity > 0.01f
                        ? new Color(healthColor.r, healthColor.g, healthColor.b,
                            healthColor.a * (0.48f + 0.52f * intensity))
                        : new Color(1f, 1f, 1f, 26f / 255f);
                    _segments[i].color = Fade(segment, op);
                }

                for (int i = 0; i < _barOutlines.Length; i++)
                {
                    _barOutlines[i].color = Fade(new Color(1f, 1f, 1f, 54f / 255f), op);
                }

                BuildCaption.color = Fade(Faint, op);
                for (int i = 0; i < _pips.Length; i++)
                {
                    int level = Simulation.UpgradeLevel(p, BuildStats[i]);
                    Color pipColor = level > 0 ? Accent : new Color(1f, 1f, 1f, 30f / 255f);
                    _pips[i].Box.color = Fade(pipColor, op);
                    for (int o = 0; o < _pips[i].Outlines.Length; o++)
                    {
                        _pips[i].Outlines[o].color = Fade(new Color(1f, 1f, 1f, 46f / 255f), op);
                    }
                    _pips[i].Value.text = level > 0 ? $"{level}" : "";
                    _pips[i].Value.color = Fade(Dim, op);
                    _pips[i].Label.color = Fade(Faint, op);
                }
            }

            public static VitalsReadout Build(Transform parent, Font font)
            {
                VitalsReadout view = new VitalsReadout();

                GameObject root = new GameObject("Vitals Readout");
                root.AddComponent<CanvasRenderer>();
                RectTransform rect = root.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(24f * Scale, (24f + CardH) * Scale);
                rect.sizeDelta = new Vector2(CardW * Scale, CardH * Scale);
                root.transform.SetParent(parent);
                view.Root = root;

                view.CardBorder = Box(root.transform, "Card Border", -1f, -1f, CardW + 2f, CardH + 2f,
                    new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f));
                view.CardFill = Box(root.transform, "Card Fill", 0f, 0f, CardW, CardH,
                    new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
                view._brackets = CornerBrackets.Build(root.transform, "Card", 0f, 0f, CardW, CardH,
                    DefaultBracketColor, Scale);
                view.LeftRail = Box(root.transform, "Left Rail", 0f, 0f, 5f, CardH, Danger);

                view.ValueShadow = TextEl(root.transform, font, "Value Shadow", 16f, 24f, 80f, 48f, 63,
                    TextAnchor.UpperLeft, new Color(0f, 0f, 0f, 1f));
                view.Value = TextEl(root.transform, font, "Value", 16f, 23f, 80f, 48f, 63,
                    TextAnchor.UpperLeft, Ink);

                view.Label = TextEl(root.transform, font, "Label", 78f, 16f, 120f, 16f, 15,
                    TextAnchor.UpperLeft, Dim);
                view.Label.text = "HEALTH";

                view.Status = TextEl(root.transform, font, "Status", 78f, 30f, 150f, 16f, 15,
                    TextAnchor.UpperLeft, Good);
                view.Status.text = "COMBAT READY";

                float segW = (BarW - (HealthSegments - 1) * HealthGap) / HealthSegments;
                for (int i = 0; i < HealthSegments; i++)
                {
                    view._segments[i] = Box(root.transform, $"Health Seg {i}", 78f + i * (segW + HealthGap), 46f,
                        segW, BarH, new Color(1f, 1f, 1f, 26f / 255f));
                }
                Color outline = new Color(1f, 1f, 1f, 54f / 255f);
                view._barOutlines[0] = Box(root.transform, "Health Outline Top", 77f, 45f, BarW + 2f, 1f, outline);
                view._barOutlines[1] = Box(root.transform, "Health Outline Bottom", 77f, 46f + BarH, BarW + 2f, 1f, outline);
                view._barOutlines[2] = Box(root.transform, "Health Outline Left", 77f, 45f, 1f, BarH + 2f, outline);
                view._barOutlines[3] = Box(root.transform, "Health Outline Right", 78f + BarW, 45f, 1f, BarH + 2f, outline);

                view.BuildCaption = TextEl(root.transform, font, "Build Caption", 78f, 61f, 40f, 14f, 14,
                    TextAnchor.UpperLeft, Faint);
                view.BuildCaption.text = "BUILD";

                string[] pipNames = { "HP", "DMG", "SPD", "JMP" };
                float[] pipXs = { 116f, 147f, 181f, 213f };
                for (int i = 0; i < 4; i++)
                {
                    float px = pipXs[i];
                    Pip pip = new Pip();
                    pip.Box = Box(root.transform, $"Pip {i} Box", px, 62f, 8f, 8f,
                        new Color(1f, 1f, 1f, 30f / 255f));
                    Color pipOutline = new Color(1f, 1f, 1f, 46f / 255f);
                    pip.Outlines[0] = Box(root.transform, $"Pip {i} Out Top", px - 1f, 61f, 10f, 1f, pipOutline);
                    pip.Outlines[1] = Box(root.transform, $"Pip {i} Out Bottom", px - 1f, 70f, 10f, 1f, pipOutline);
                    pip.Outlines[2] = Box(root.transform, $"Pip {i} Out Left", px - 1f, 61f, 1f, 10f, pipOutline);
                    pip.Outlines[3] = Box(root.transform, $"Pip {i} Out Right", px + 8f, 61f, 1f, 10f, pipOutline);

                    pip.Label = TextEl(root.transform, font, $"Pip {i} Label", px - 1f, 53f, 24f, 12f, 12,
                        TextAnchor.UpperLeft, Faint);
                    pip.Label.text = pipNames[i];

                    pip.Value = TextEl(root.transform, font, $"Pip {i} Value", px + 2f, 73f, 20f, 14f, 13,
                        TextAnchor.UpperLeft, Dim);

                    view._pips[i] = pip;
                }

                return view;
            }

            static Image Box(Transform parent, string name, float dx, float dy, float w, float h, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(dx * Scale, -dy * Scale);
                rect.sizeDelta = new Vector2(w * Scale, h * Scale);
                Image image = go.AddComponent<Image>();
                image.color = color;
                return image;
            }

            static Text TextEl(Transform parent, Font font, string name, float dx, float dy, float w, float h,
                int size, TextAnchor align, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = align switch
                {
                    TextAnchor.UpperRight => new Vector2(1f, 1f),
                    TextAnchor.UpperCenter => new Vector2(0.5f, 1f),
                    _ => new Vector2(0f, 1f),
                };
                rect.anchoredPosition = new Vector2(dx * Scale, -dy * Scale);
                rect.sizeDelta = new Vector2(w, h);
                Text text = go.AddComponent<Text>();
                text.font = font;
                text.fontSize = size;
                text.alignment = align;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = color;
                return text;
            }
        }

        /// <summary>
        /// The bottom-right weapon card (reference <c>Hud.DrawWeaponReadout</c>): the
        /// weapon name + state line, the oversized magazine numeral with the
        /// slash/reserve, an eight-segment magazine bar, the four loadout slot boxes
        /// above the card, the reload progress / PRESS R line, and the arena-panel
        /// corner brackets on both the card and the loadout slots. Ported in the
        /// reference's 1280x720 logical units with the 1.5x canvas scale applied, and
        /// it reuses the pointer positions the reference uses inside the 260x88 card.
        /// </summary>
        sealed class WeaponReadout
        {
            const float Scale = 1.5f;
            const float CardW = 260f;
            const float CardH = 88f;
            const float BarW = 111f;
            const float BarH = 8f;
            const float ReloadBarH = 5f;
            const int MagSegments = 8;
            const float MagGap = 2f;

            static readonly Color Accent = new Color(1f, 186f / 255f, 56f / 255f);

            public GameObject Root = null!;
            Image CardBorder = null!;
            Image CardFill = null!;
            CornerBrackets _brackets = null!;
            Text Name = null!;
            Text State = null!;
            Text Mag = null!;
            Text Slash = null!;
            Text Reserve = null!;
            Text MagCaption = null!;
            Text ResCaption = null!;
            readonly Image[] _magSegments = new Image[MagSegments];
            Image ReloadTrack = null!;
            Image ReloadFill = null!;
            Text ReloadLabel = null!;
            Slot[] _slots = Array.Empty<Slot>();

            sealed class Slot
            {
                public Image Border = null!;
                public Image Fill = null!;
                public CornerBrackets Brackets = null!;
                public Image Active = null!;
                public Text Label = null!;
                public Text Index = null!;
            }

            public void Set(Player p, Simulation sim, float op)
            {
                Weapon weapon = p.Current;
                WeaponDef def = weapon.Def;
                Color ammoColor = def.UsesAmmo && weapon.Mag == 0 ? Danger : Ink;

                CardBorder.color = Fade(new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f), op);
                CardFill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
                _brackets.Set(ammoColor, op);

                Name.text = def.Name.ToUpperInvariant();
                Name.color = Fade(Accent, op);
                State.text = WeaponState(def);
                State.color = Fade(Dim, op);

                Mag.text = def.UsesAmmo ? weapon.Mag.ToString("00") : "--";
                Mag.color = Fade(ammoColor, op);
                Slash.color = Fade(Faint, op);
                Reserve.color = Fade(new Color(238f / 255f, 242f / 255f, 244f / 255f, 210f / 255f), op);
                MagCaption.color = Fade(Faint, op);
                ResCaption.color = Fade(Faint, op);

                // Segmented magazine bar (reference DrawSegmentedBar): each of the
                // eight cells brightens as its share of the load is present.
                float magFill = def.Magazine <= 0 ? 0f : weapon.Mag / (float)def.Magazine;
                for (int i = 0; i < _magSegments.Length; i++)
                {
                    float intensity = U.Clamp01(magFill * MagSegments - i);
                    Color segment = intensity > 0.01f
                        ? new Color(ammoColor.r, ammoColor.g, ammoColor.b,
                            ammoColor.a * (0.48f + 0.52f * intensity))
                        : new Color(1f, 1f, 1f, 24f / 255f);
                    _magSegments[i].color = Fade(segment, op);
                }

                // Four loadout slots: primary, secondary, tertiary, then the knife.
                for (int slot = 1; slot <= 4; slot++)
                {
                    Slot view = _slots[slot - 1];
                    Weapon? owned = p.Weapons.Find(candidate => candidate.Slot == slot);
                    bool active = owned is not null && ReferenceEquals(owned, p.Current);
                    view.Border.color = Fade(
                        active ? Accent : new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f), op);
                    view.Fill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
                    view.Brackets.Set(active ? Accent : DefaultBracketColor, op);
                    view.Active.gameObject.SetActive(active);
                    if (active) view.Active.color = Fade(Accent, op);
                    view.Label.text = owned is null ? "---" : ShortName(owned.Def.Name);
                    view.Label.color = Fade(active ? Ink : Dim, op);
                    view.Index.color = Fade(Faint, op);
                }

                // Reload progress / PRESS R prompt (reference lines below the bar).
                bool reloading = weapon.Reloading && def.UsesAmmo;
                bool pressR = !reloading && def.UsesAmmo && weapon.Mag == 0;
                ReloadTrack.gameObject.SetActive(reloading);
                ReloadFill.gameObject.SetActive(reloading);
                ReloadLabel.gameObject.SetActive(reloading || pressR);
                if (reloading)
                {
                    float duration = MathF.Max(0.001f, def.ReloadTime);
                    float t = U.Clamp01(1f - (weapon.ReloadEndTime - sim.Time) / duration);
                    ReloadTrack.color = Fade(new Color(0f, 0f, 0f, 160f / 255f), op);
                    ReloadFill.rectTransform.sizeDelta = new Vector2(BarW * Scale * t, ReloadBarH * Scale);
                    ReloadFill.color = Fade(Accent, op);
                    ReloadLabel.text = weapon.ReloadStartedEmpty ? "EMPTY RELOAD" : "TACTICAL RELOAD";
                    ReloadLabel.color = Fade(Accent, op);
                }
                else if (pressR)
                {
                    ReloadLabel.text = "PRESS R";
                    ReloadLabel.color = Fade((int)(sim.Time * 3f) % 2 == 0 ? Danger : Dim, op);
                }
            }

            static string WeaponState(WeaponDef def) => def switch
            {
                { Melee: true } => "MELEE - V",
                { HasSecondary: true } => "ALT - DOUBLE BLAST",
                { HasScope: true } => "SCOPE - RMB",
                { HasIronSights: true } => "IRON SIGHTS - RMB",
                _ => "PRIMARY",
            };

            static string ShortName(string name) => name switch
            {
                "Double Barrel" => "DB",
                "Revolver" => "REV",
                _ => name.ToUpperInvariant(),
            };

            public static WeaponReadout Build(Transform parent, Font font)
            {
                WeaponReadout view = new WeaponReadout();

                // Card home: 24 logical from the right and bottom edges, pivot at its
                // top-left so every child uses the reference's card-local offsets.
                GameObject root = new GameObject("Weapon Readout");
                root.AddComponent<CanvasRenderer>();
                RectTransform rect = root.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(-(24f + CardW) * Scale, (24f + CardH) * Scale);
                rect.sizeDelta = new Vector2(CardW * Scale, CardH * Scale);
                root.transform.SetParent(parent);
                view.Root = root;

                view.CardBorder = Box(root.transform, "Card Border", -1f, -1f, CardW + 2f, CardH + 2f,
                    new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f));
                view.CardFill = Box(root.transform, "Card Fill", 0f, 0f, CardW, CardH,
                    new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
                view._brackets = CornerBrackets.Build(root.transform, "Card", 0f, 0f, CardW, CardH,
                    DefaultBracketColor, Scale);

                view.Name = TextEl(root.transform, font, "Name", 15f, 14f, 230f, 20f, 18,
                    TextAnchor.UpperLeft, Accent);
                view.State = TextEl(root.transform, font, "State", 15f, 30f, 220f, 16f, 15,
                    TextAnchor.UpperLeft, Dim);
                view.Mag = TextEl(root.transform, font, "Mag", 144f, 33f, 90f, 48f, 57,
                    TextAnchor.UpperLeft, Ink);
                view.Slash = TextEl(root.transform, font, "Slash", 197f, 55f, 20f, 22f, 24,
                    TextAnchor.UpperLeft, Faint);
                view.Reserve = TextEl(root.transform, font, "Reserve", 213f, 50f, 46f, 30f, 30,
                    TextAnchor.UpperLeft, Ink);
                view.MagCaption = TextEl(root.transform, font, "Mag Caption", 147f, 74f, 40f, 14f, 14,
                    TextAnchor.UpperLeft, Faint);
                view.ResCaption = TextEl(root.transform, font, "Res Caption", 218f, 74f, 40f, 14f, 14,
                    TextAnchor.UpperLeft, Faint);
                view.MagCaption.text = "MAG";
                view.ResCaption.text = "RES";

                // Magazine bar: eight cells inside a 1 px outline, like the health bar.
                float segW = (BarW - (MagSegments - 1) * MagGap) / MagSegments;
                for (int i = 0; i < MagSegments; i++)
                {
                    view._magSegments[i] = Box(root.transform, $"Mag {i}", 15f + i * (segW + MagGap), 51f,
                        segW, BarH, new Color(1f, 1f, 1f, 24f / 255f));
                }
                Color outline = new Color(1f, 1f, 1f, 48f / 255f);
                Box(root.transform, "Mag Outline Top", 14f, 50f, BarW + 2f, 1f, outline);
                Box(root.transform, "Mag Outline Bottom", 14f, 51f + BarH, BarW + 2f, 1f, outline);
                Box(root.transform, "Mag Outline Left", 14f, 50f, 1f, BarH + 2f, outline);
                Box(root.transform, "Mag Outline Right", 15f + BarW, 50f, 1f, BarH + 2f, outline);

                view.ReloadTrack = Box(root.transform, "Reload Track", 15f, 72f, BarW, ReloadBarH,
                    new Color(0f, 0f, 0f, 160f / 255f));
                view.ReloadFill = Box(root.transform, "Reload Fill", 15f, 72f, BarW, ReloadBarH, Accent);
                view.ReloadLabel = TextEl(root.transform, font, "Reload Label", 126f, 63f, 120f, 14f, 14,
                    TextAnchor.UpperRight, Accent);
                view.ReloadTrack.gameObject.SetActive(false);
                view.ReloadFill.gameObject.SetActive(false);
                view.ReloadLabel.gameObject.SetActive(false);

                // Loadout strip sits 26 logical above the card, right-aligned to the
                // card so the knife box overhangs the screen edge exactly like the
                // reference (its last box is clipped by the display).
                view._slots = new Slot[4];
                for (int slot = 1; slot <= 4; slot++)
                {
                    float boxX = CardW - (4 - slot) * 70f;
                    Slot slotView = new Slot();
                    slotView.Border = Box(root.transform, $"Slot {slot} Border", boxX - 1f, -27f, 64f, 23f,
                        new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f));
                    slotView.Fill = Box(root.transform, $"Slot {slot} Fill", boxX, -26f, 62f, 21f,
                        new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
                    slotView.Brackets = CornerBrackets.Build(root.transform, $"Slot {slot}", boxX, -26f, 62f, 21f,
                        DefaultBracketColor, Scale);
                    slotView.Active = Box(root.transform, $"Slot {slot} Active", boxX, -26f, 3f, 21f, Accent);
                    slotView.Label = TextEl(root.transform, font, $"Slot {slot} Label", boxX + 31f, -20f,
                        58f, 16f, 15, TextAnchor.UpperCenter, Dim);
                    slotView.Index = TextEl(root.transform, font, $"Slot {slot} Index", boxX + 3f, -24f,
                        20f, 16f, 15, TextAnchor.UpperLeft, Faint);
                    slotView.Index.text = $"{slot}";
                    slotView.Active.gameObject.SetActive(false);
                    view._slots[slot - 1] = slotView;
                }

                return view;
            }

            static Image Box(Transform parent, string name, float dx, float dy, float w, float h, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(dx * Scale, -dy * Scale);
                rect.sizeDelta = new Vector2(w * Scale, h * Scale);
                Image image = go.AddComponent<Image>();
                image.color = color;
                return image;
            }

            static Text TextEl(Transform parent, Font font, string name, float dx, float dy, float w, float h,
                int size, TextAnchor align, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = align switch
                {
                    TextAnchor.UpperRight => new Vector2(1f, 1f),
                    TextAnchor.UpperCenter => new Vector2(0.5f, 1f),
                    _ => new Vector2(0f, 1f),
                };
                rect.anchoredPosition = new Vector2(dx * Scale, -dy * Scale);
                rect.sizeDelta = new Vector2(w, h);
                Text text = go.AddComponent<Text>();
                text.font = font;
                text.fontSize = size;
                text.alignment = align;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = color;
                return text;
            }
        }

        /// <summary>
        /// The cosmetic scope picture (reference <c>Hud.DrawScopeOverlay</c>): full
        /// screen black with a 540-unit circular hole, thin cross lines, a faint
        /// inner ring and a centre dot, all fading in with the zoom blend. One
        /// canvas group drives the whole fade. The mask hole is approximated with
        /// 36 horizontal strips per side, which never over-mask into the circle.
        /// </summary>
        sealed class ScopeOverlay
        {
            public GameObject Root = null!;
            CanvasGroup Group = null!;

            public void Set(Player p)
            {
                bool visible = p.Alive && p.Current.Def.HasScope && p.ZoomBlend >= 0.25f;
                float t = U.Clamp01((p.ZoomBlend - 0.25f) / 0.75f);
                if (!visible || t <= 0.001f)
                {
                    if (Root.activeSelf) Root.SetActive(false);
                    return;
                }
                Root.SetActive(true);
                Group.alpha = t;
            }

            public static ScopeOverlay Create(Transform parent)
            {
                GameObject root = new GameObject("Scope Overlay");
                root.AddComponent<CanvasRenderer>();
                RectTransform rect = root.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                root.transform.SetParent(parent);

                ScopeOverlay overlay = new ScopeOverlay
                {
                    Root = root,
                    Group = root.AddComponent<CanvasGroup>(),
                };

                Color mask = new Color(0f, 0f, 0f, 230f / 255f);
                const float radius = 540f;
                const float halfWidth = 960f;
                const float stripHeight = 30f;
                for (int i = 0; i < 36; i++)
                {
                    float yA = -radius + i * stripHeight;
                    float yB = yA + stripHeight;
                    float dy = MathF.Max(MathF.Abs(yA), MathF.Abs(yB));
                    float dx = MathF.Sqrt(MathF.Max(0f, radius * radius - dy * dy));
                    float width = halfWidth - dx;
                    if (width <= 0.5f) continue;
                    float yCenter = (yA + yB) * 0.5f;
                    Rect(root.transform, $"Mask L{i}",
                        new Vector2(-(halfWidth + dx) * 0.5f, yCenter),
                        new Vector2(width, stripHeight + 0.5f), 0f, mask);
                    Rect(root.transform, $"Mask R{i}",
                        new Vector2((halfWidth + dx) * 0.5f, yCenter),
                        new Vector2(width, stripHeight + 0.5f), 0f, mask);
                }

                Color line = new Color(20f / 255f, 20f / 255f, 20f / 255f, 220f / 255f);
                Rect(root.transform, "Line H", Vector2.zero, new Vector2(1920f, 3f), 0f, line);
                Rect(root.transform, "Line V", Vector2.zero, new Vector2(3f, 1080f), 0f, line);

                const float ringRadius = 335f;
                const int ringSegments = 40;
                float segmentLength = 2f * MathF.PI * ringRadius / ringSegments + 2f;
                Color ring = new Color(30f / 255f, 30f / 255f, 30f / 255f, 140f / 255f);
                for (int i = 0; i < ringSegments; i++)
                {
                    float angle = i * (360f / ringSegments);
                    float radians = angle * MathF.PI / 180f;
                    Rect(root.transform, $"Ring {i}",
                        new Vector2(MathF.Cos(radians) * ringRadius, MathF.Sin(radians) * ringRadius),
                        new Vector2(3f, segmentLength), angle + 90f, ring);
                }

                Rect(root.transform, "Dot", Vector2.zero, new Vector2(5f, 5f), 0f,
                    new Color(240f / 255f, 240f / 255f, 240f / 255f, 230f / 255f));

                root.SetActive(false);
                return overlay;
            }

            static Image Rect(Transform parent, string name, Vector2 position, Vector2 size,
                float rotation, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
                go.transform.SetParent(parent);
                Image image = go.AddComponent<Image>();
                image.color = color;
                return image;
            }
        }

        /// <summary>
        /// The death / eliminated overlay (reference <c>Hud.DrawDeathOverlay</c>): full-screen
        /// dark-red scrim (90, 10, 8, 60), a dark horizontal banner at 34% screen height
        /// (8, 10, 14, 180) across the 1080p canvas, the red ELIMINATED title, "killed by {name}"
        /// subtitle derived from the newest player-victim kill feed entry, the amber
        /// "RESPAWNING IN 0.0" countdown, and the reminder hints ("death costs one upgrade level",
        /// "press TAB for scores"). Hidden while alive.
        /// </summary>
        sealed class DeathOverlay
        {
            const float Scale = 1.5f;

            public GameObject Root = null!;
            Image Scrim = null!;
            Image Banner = null!;
            Text Eliminated = null!;
            Text Killer = null!;
            Text Respawn = null!;
            Text CostHint = null!;
            Text ScoresHint = null!;

            public void Set(Simulation sim, Player p)
            {
                bool dead = !p.Alive;
                Root.SetActive(dead);
                if (!dead) return;

                string? killerName = null;
                for (int i = sim.KillFeed.Count - 1; i >= 0; i--)
                {
                    KillFeedEntry entry = sim.KillFeed[i];
                    if (entry.VictimIsPlayer)
                    {
                        killerName = entry.Killer;
                        break;
                    }
                }

                if (killerName != null)
                {
                    Killer.gameObject.SetActive(true);
                    Killer.text = $"killed by {killerName}";
                }
                else
                {
                    Killer.gameObject.SetActive(false);
                }

                float remaining = MathF.Max(0f, p.RespawnAt - sim.Time);
                Respawn.text = $"RESPAWNING IN {remaining:0.0}";
            }

            public static DeathOverlay Build(Transform parent, Font font)
            {
                DeathOverlay view = new DeathOverlay();
                GameObject root = new GameObject("Death Overlay");
                root.AddComponent<CanvasRenderer>();
                RectTransform rect = root.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                root.transform.SetParent(parent);
                view.Root = root;

                view.Scrim = Box(root.transform, "Death Scrim", 0f, 0f, 1920f, 1080f,
                    new Color(90f / 255f, 10f / 255f, 8f / 255f, 60f / 255f));
                RectTransform scrimRect = view.Scrim.rectTransform;
                scrimRect.anchorMin = Vector2.zero;
                scrimRect.anchorMax = new Vector2(1f, 1f);
                scrimRect.sizeDelta = Vector2.zero;
                scrimRect.anchoredPosition = Vector2.zero;

                const float bannerY = 367.2f;
                const float bannerH = 177f;
                view.Banner = Box(root.transform, "Death Banner", 0f, bannerY, 1920f, bannerH,
                    new Color(8f / 255f, 10f / 255f, 14f / 255f, 180f / 255f));
                RectTransform bannerRect = view.Banner.rectTransform;
                bannerRect.anchorMin = new Vector2(0f, 1f);
                bannerRect.anchorMax = new Vector2(1f, 1f);
                bannerRect.pivot = new Vector2(0.5f, 1f);
                bannerRect.anchoredPosition = new Vector2(0f, -bannerY);
                bannerRect.sizeDelta = new Vector2(0f, bannerH);

                view.Eliminated = TextCenter(root.transform, font, "Eliminated Title",
                    -(bannerY + 14f * Scale), 45, Danger);
                view.Eliminated.text = "ELIMINATED";

                view.Killer = TextCenter(root.transform, font, "Killer Subtitle",
                    -(bannerY + 52f * Scale), 15, Dim);

                view.Respawn = TextCenter(root.transform, font, "Respawn Countdown",
                    -(bannerY + 78f * Scale), 22, Amber);

                view.CostHint = TextCenter(root.transform, font, "Cost Hint",
                    -(bannerY + 98f * Scale), 15, Faint);
                view.CostHint.text = "death costs one upgrade level";

                view.ScoresHint = TextCenter(root.transform, font, "Scores Hint",
                    -(bannerY + 112f * Scale), 15, Faint);
                view.ScoresHint.text = "press TAB for scores";

                view.Root.SetActive(false);
                return view;
            }

            static Image Box(Transform parent, string name, float dx, float dy, float w, float h, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(dx, -dy);
                rect.sizeDelta = new Vector2(w, h);
                Image image = go.AddComponent<Image>();
                image.color = color;
                return image;
            }

            static Text TextCenter(Transform parent, Font font, string name, float topY, int size, Color color)
            {
                GameObject go = new GameObject(name);
                go.AddComponent<CanvasRenderer>();
                RectTransform rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent);
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, topY);
                rect.sizeDelta = new Vector2(1000f, size + 10f);
                Text text = go.AddComponent<Text>();
                text.font = font;
                text.fontSize = size;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = color;
                return text;
            }
        }

        Camera? _camera;
        Transform? _canvasRoot;
        readonly List<Text> _damageShadows = new();
        readonly List<Text> _damageLabels = new();
        int _damageShown;

        /// <summary>
        /// The active reticle profile. <see cref="MenuSync"/> owns persistence and pushes
        /// its loaded instance here (same object, so edits show live); without a menu the
        /// HUD keeps the default Classic profile.
        /// </summary>
        public CrosshairSettings Crosshair { get; set; } = new CrosshairSettings();

        /// <summary>
        /// Forces the scoreboard up outside the Tab-held path. The menu sets this while
        /// the match-over screen is up — the reference draws the same panel inside that
        /// screen (Screens.MatchOver). The board then sits under the menu backdrop; the
        /// reference draws it above. Acceptable blockout deviation, see HANDOFF.
        /// </summary>
        public bool ForceScoreboard { get; set; }

        void Awake()
        {
            _driver = GetComponent<MatchDriver>();
            if (_driver == null)
            {
                Debug.LogWarning("HudSync: no MatchDriver on this GameObject; disabling.");
                enabled = false;
                return;
            }

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            if (_font == null)
            {
                Debug.LogWarning("HudSync: no usable font found; disabling.");
                enabled = false;
                return;
            }

            BuildCanvas();
        }

        void LateUpdate()
        {
            Simulation? sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;
            Player p = sim.Player;
            WeaponDef def = p.Current.Def;

            // The peripheral anchors fade behind a real scope / calibrated sight
            // picture (reference Hud.PeripheralHudOpacity). Centre elements
            // (reticle, hitmarker, scope overlay) are never faded.
            float op = PeripheralOpacity(def, p.ZoomBlend);

            // Vitals card (reference DrawVitals) ---------------------------
            _vitals?.Set(p, op);

            // Weapon readout card (reference DrawWeaponReadout) -------------
            _weaponReadout?.Set(p, sim, op);

            // Top bar (reference DrawTopBar) ---------------------------------
            _matchResult.gameObject.SetActive(sim.MatchOver);
            _matchResult.text = sim.MatchResult;
            bool urgent = !sim.MatchOver && sim.TimeLeft < 30f;
            _timer.text = sim.MatchOver ? "" : FormatClock(sim.TimeLeft);
            _timer.color = Fade(urgent ? Danger : new Color(242f / 255f, 246f / 255f, 248f / 255f), op);
            if (_clockLabel != null) _clockLabel.color = Fade(Faint, op);
            if (_topBarBorder != null)
            {
                // Reference DrawArenaPanel: neutral outline, accent only on the brackets.
                _topBarBorder.color = Fade(CardBorderColor, op);
            }
            if (_topBarFill != null)
            {
                _topBarFill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
            }
            _topBarBrackets?.Set(urgent ? Danger : AccentInk, op);
            if (_clockScore != null)
            {
                int leadKills = 0;
                foreach (Actor leader in sim.Leaderboard())
                {
                    leadKills = leader.Kills;
                    break;
                }
                _clockScore.text = $"YOU {p.Kills:00}   -   LEAD {leadKills:00}   -   FT {sim.KillsToWin}";
                _clockScore.color = Fade(Dim, op);
            }

            // Compass tape (reference DrawCompassTape) ----------------------
            if (_compassFill != null && _compassCaret != null && _compassHeading != null)
            {
                _compassBorder!.color = Fade(new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f), op);
                _compassFill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
                _compassBrackets?.Set(DefaultBracketColor, op);
                _compassCaret.color = Fade(Gold, op);

                // Game yaw increases to the left; flip it for clockwise compass
                // degrees (N=000, E=090, S=180, W=270), like the reference.
                float heading = WrapHeading(-p.Yaw);
                const float pixelsPerDegree = 1.575f;
                const float halfWidth = 315f;
                float halfDegrees = halfWidth / pixelsPerDegree;
                int firstTick = (int)MathF.Floor((heading - halfDegrees) / 15f) * 15;
                int lastTick = (int)MathF.Ceiling((heading + halfDegrees) / 15f) * 15;
                int index = 0;
                for (int degree = firstTick;
                    degree <= lastTick && index < _compassTicks.Length;
                    degree += 15)
                {
                    float delta = SignedHeadingDelta(heading, degree);
                    float x = delta * pixelsPerDegree;
                    if (x < -halfWidth - 3f || x > halfWidth + 3f) continue;

                    int normalized = ((degree % 360) + 360) % 360;
                    bool labelled = normalized % 45 == 0;
                    float height = labelled ? 10.5f : 5.25f;
                    Image tick = _compassTicks[index]!;
                    Text label = _compassTickLabels[index]!;
                    tick.gameObject.SetActive(true);
                    tick.rectTransform.anchoredPosition = new Vector2(x, -(123f + height * 0.5f));
                    tick.rectTransform.sizeDelta = new Vector2(2f, height);
                    tick.color = Fade(labelled
                        ? new Color(236f / 255f, 240f / 255f, 244f / 255f, 188f / 255f)
                        : new Color(236f / 255f, 240f / 255f, 244f / 255f, 92f / 255f), op);
                    label.gameObject.SetActive(labelled);
                    if (labelled)
                    {
                        label.text = CompassLabel(normalized);
                        label.rectTransform.anchoredPosition = new Vector2(x, -142f);
                        label.color = Fade(Dim, op);
                    }
                    index++;
                }
                for (; index < _compassTicks.Length; index++)
                {
                    if (_compassTicks[index]!.gameObject.activeSelf) _compassTicks[index]!.gameObject.SetActive(false);
                    if (_compassTickLabels[index]!.gameObject.activeSelf) _compassTickLabels[index]!.gameObject.SetActive(false);
                }

                int headingDisplay = ((int)MathF.Round(heading)) % 360;
                _compassHeading.text = $"{headingDisplay:000}";
                _compassHeading.color = Fade(Faint, op);
            }

            // Radar (reference DrawRadar) -----------------------------------
            if (_radar != null && _radarBorder != null && _radarFill != null)
            {
                _radarBorder.color = Fade(new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f), op);
                _radarFill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), op);
                _radarBrackets?.Set(RadarBracketColor, op);
                _radar.Sync(sim, op);
                if (_radarTitle != null) _radarTitle.color = Fade(Faint, op);
                if (_radarRange != null) _radarRange.color = Fade(Faint, op);
            }

            // Kill feed (newest first, 6.5 s life like the reference) -------
            int line = 0;
            for (int i = sim.KillFeed.Count - 1; i >= 0 && line < FeedLines; i--)
            {
                KillFeedEntry entry = sim.KillFeed[i];
                float age = sim.Time - entry.SpawnedAt;
                if (age > 6.5f) continue;
                float alpha = age > 5.5f ? U.Clamp01(6.5f - age) : 1f;
                string weaponName = entry.Headshot ? "HEAD" : entry.Weapon.ToUpperInvariant();
                FeedRow row = _killFeed[line++];
                row.Root.SetActive(true);

                // Reference fades the whole row by its ageing alpha (opacity * alpha).
                float rowOpacity = op * alpha;
                row.Border.color = Fade(CardBorderColor, rowOpacity);
                row.Fill.color = Fade(new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f), rowOpacity);
                row.Brackets.Set(
                    entry.KillerIsPlayer
                        ? AccentInk
                        : new Color(236f / 255f, 240f / 255f, 244f / 255f, 120f / 255f),
                    rowOpacity);

                row.Killer.text = entry.Killer;
                row.Killer.color = entry.KillerIsPlayer
                    ? Fade(AccentInk, rowOpacity)
                    : Fade(new Color(240f / 255f, 240f / 255f, 240f / 255f), rowOpacity);
                row.Separator.color = Fade(new Color(236f / 255f, 240f / 255f, 244f / 255f), rowOpacity);
                row.Weapon.text = weaponName;
                row.Weapon.color = Fade(entry.Headshot ? Danger : AccentInk, rowOpacity);
                row.Victim.text = entry.Victim;
                row.Victim.color = entry.VictimIsPlayer
                    ? Fade(Danger, rowOpacity)
                    : Fade(new Color(200f / 255f, 200f / 255f, 200f / 255f), rowOpacity);
            }
            for (; line < FeedLines; line++)
            {
                if (_killFeed[line].Root.activeSelf) _killFeed[line].Root.SetActive(false);
            }

            // Hitmarker (reference life is 0.12 s) --------------------------
            // The reference drives both the marker and its ELIMINATED / HEADSHOT
            // label off one event end time, so the label rides the same envelope
            // instead of a separate render timer. attack/release shape the scale
            // (0.80 -> 1.18, settling back to 0.86) and fade is a 0.68 power
            // curve. Only a headshot tints the marker; a plain kill reads through
            // the ELIMINATED label below it.
            float hitAlpha = 0f;
            float hitScale = 1f;
            float hitElapsed = 0f;
            float hitFade = 0f;
            if (p.Alive && sim.Time <= sim.HitmarkerUntil && !sim.MatchOver)
            {
                hitElapsed = U.Clamp01(1f - (sim.HitmarkerUntil - sim.Time) / 0.12f);
                float attack = U.Clamp01(hitElapsed / 0.62f);
                float release = U.Clamp01((hitElapsed - 0.62f) / 0.38f);
                hitScale = U.Lerp(0.80f, 1.18f, attack) * U.Lerp(1f, 0.86f, release);
                hitFade = MathF.Pow(1f - hitElapsed, 0.68f);
                hitAlpha = hitFade;
            }
            _hitmarker.gameObject.SetActive(hitAlpha > 0f);
            if (hitAlpha > 0f)
            {
                Color hitColor = sim.LastHitWasHeadshot
                    ? new Color(255f / 255f, 86f / 255f, 70f / 255f)
                    : new Color(245f / 255f, 245f / 245f, 245f / 245f);
                _hitmarker.color = new Color(hitColor.r, hitColor.g, hitColor.b, hitAlpha);
                _hitmarker.transform.localScale = new Vector3(hitScale, hitScale, 1f);
            }
            if (_hitmarkerLabel != null)
            {
                bool showLabel = hitAlpha > 0f && (sim.LastHitWasKill || sim.LastHitWasHeadshot);
                _hitmarkerLabel.gameObject.SetActive(showLabel);
                if (showLabel)
                {
                    if (sim.LastHitWasKill)
                    {
                        // y = 26 reference units x scale x 1.5.
                        _hitmarkerLabel.text = "ELIMINATED";
                        _hitmarkerLabel.fontSize = 16;
                        _hitmarkerLabel.color = Fade(new Color(255f / 255f, 70f / 255f, 56f / 255f),
                            hitFade);
                        _hitmarkerLabel.rectTransform.anchoredPosition =
                            new Vector2(0f, 39f * hitScale);
                    }
                    else
                    {
                        // The headshot label eases in from 0.14 s, so it never
                        // flashes on a plain body hit that also registered a marker.
                        float labelAlpha = hitFade * U.Clamp01((hitElapsed - 0.14f) / 0.28f);
                        _hitmarkerLabel.text = "HEADSHOT";
                        _hitmarkerLabel.fontSize = 15;
                        _hitmarkerLabel.color = Fade(new Color(255f / 255f, 104f / 255f, 82f / 255f),
                            labelAlpha);
                        _hitmarkerLabel.rectTransform.anchoredPosition =
                            new Vector2(0f, 36f * hitScale);
                    }
                }
            }

            // Damage feedback ----------------------------------------------
            float killedFlash = sim.Time < sim.KilledFlashUntil ? 0.45f : 0f;
            float flashAlpha = MathF.Max(U.Clamp(sim.DamageFlash, 0f, 1f) * 0.35f, killedFlash);
            _damageOverlay.color = new Color(DamageTint.r, DamageTint.g, DamageTint.b, flashAlpha);
            _damageOverlay.gameObject.SetActive(flashAlpha > 0.001f);

            // Critical-health trauma vignette (reference DrawHealthVignette) -
            _trauma?.Sync(sim);

            // Directional damage arrow: the wheel rotates so the arrow points at
            // the source on screen (relative yaw: + is to the player's left).
            if (flashAlpha > 0.001f)
            {
                float relative = U.AngleDelta(p.Yaw, sim.DamageFlashDirection);
                _damageWheel.localRotation = Quaternion.Euler(0f, 0f, -relative);
            }

            // Death overlay (reference DrawDeathOverlay) --------------------
            _deathOverlay?.Set(sim, p);

            // Crosshair -----------------------------------------------------
            SyncCrosshair(p, def);

            // Hook status / station prompt / scope (reference centre zone) --
            bool alive = p.Alive;
            if (_hookLeft != null && _hookRight != null)
            {
                _hookLeft.Root.SetActive(alive);
                _hookRight.Root.SetActive(alive);
                if (alive)
                {
                    _hookLeft.Set(p.HookLeft.Attached, op);
                    _hookRight.Set(p.HookRight.Attached, op);
                }
            }

            bool nearStation = alive && sim.NearbyStation(p) != null;
            if (_stationTitle != null && _stationSub != null)
            {
                bool wasActive = _stationTitle.gameObject.activeSelf;
                if (wasActive != nearStation)
                {
                    _stationTitle.gameObject.SetActive(nearStation);
                    _stationSub.gameObject.SetActive(nearStation);
                }
                if (nearStation)
                {
                    _stationTitle.color = Fade(Ink, op);
                    _stationSub.text = $"{p.KillPoints} KILL POINTS   -   1 POINT PER LEVEL";
                    _stationSub.color = Fade(Gold, op);
                }
            }

            _scope?.Set(p);

            // Floating damage numbers --------------------------------------
            SyncDamageNumbers(sim);

            // Scoreboard (reference DrawScoreboard): Tab-held during play, or forced
            // while the match-over screen is up. It never shows while paused, because
            // the driver is disabled outside the playing screen.
            bool showScores = ForceScoreboard ||
                (_driver != null && _driver.enabled && Input.GetKey(KeyCode.Tab));
            if (_scoreboard != null)
            {
                _scoreboard.SetActive(showScores);
                if (showScores) SyncScoreboard(sim);
            }
        }

        /// <summary>
        /// The settings-driven reticle (reference <c>Hud.DrawCrosshair</c>): dynamic gap
        /// follows the weapon's spread projected through the camera's vertical FOV so the
        /// reticle stays an honest pixel representation of the shot cone; the reference's
        /// suppression rule hides it behind a real scope / calibrated sight picture, and
        /// it only draws while alive.
        /// </summary>
        void SyncCrosshair(Player p, WeaponDef def)
        {
            bool suppressed = p.ZoomBlend > 0.75f && (def.HasScope || def.HasCalibratedIronSights);
            bool visible = p.Alive && !suppressed;
            _crosshair.Root.gameObject.SetActive(visible);
            if (!visible) return;

            // The canvas is 1080 units tall, matching the reference's 1080p screen
            // height in the projection formula.
            float spread = p.Current.SpreadFor(p.HorizontalSpeed, p.OnGround, p.Crouching, p.ZoomBlend);
            Camera? cam = PlayerCamera();
            float fovY = cam != null ? cam.fieldOfView : p.CurrentFov;
            float pixelsPerRadian = 540f / MathF.Tan(fovY * 0.5f * MathF.PI / 180f);
            float spreadPixels = MathF.Tan(spread * MathF.PI / 180f) * pixelsPerRadian;
            _crosshair.Update(Crosshair, spreadPixels, 1f, 1080f * 0.32f);
        }

        /// <summary>
        /// Floating damage the local player dealt (reference <c>Hud.DrawDamageNumbers</c>):
        /// the world hit point is projected through the attached camera and positioned in
        /// 1080p canvas units. Unity viewports are y-up from the bottom-left while the
        /// reference screen is y-down from the top-left, so the flip lives here — do not
        /// "fix" a sign elsewhere. Headshots use the reference's larger amber
        /// "!"-suffixed style; both styles keep its fade curve, min(1, t * 2.2), and a
        /// half-alpha black drop shadow one pixel below.
        /// </summary>
        void SyncDamageNumbers(Simulation sim)
        {
            if (_canvasRoot == null) return;
            Camera? cam = PlayerCamera();

            int shown = 0;
            if (cam != null)
            {
                // Reference cull margin: 40 pixels outside the 1920×1080 frame.
                const float padX = 40f / 1920f;
                const float padY = 40f / 1080f;

                foreach (FloatingDamage d in sim.DamageNumbers)
                {
                    Vector3 viewport = cam.WorldToViewportPoint(MatchDriver.ToUnity(d.Position));
                    if (viewport.z <= 0f) continue; // behind the camera
                    if (viewport.x < -padX || viewport.x > 1f + padX ||
                        viewport.y < -padY || viewport.y > 1f + padY) continue;

                    float t = U.Clamp01(d.Life / d.MaxLife);
                    float alpha = MathF.Min(1f, t * 2.2f);
                    if (alpha <= 0.004f) continue;

                    int size = d.Headshot ? 20 : 14;
                    string text = d.Headshot ? $"{d.Amount:0}!" : $"{d.Amount:0}";
                    Color colour = d.Headshot ? HeadshotDamage : DamageInk;
                    Vector2 position = new Vector2(viewport.x * 1920f, viewport.y * 1080f);

                    Text shadow = GetDamageText(_damageShadows, shown, "Damage Shadow");
                    Text label = GetDamageText(_damageLabels, shown, "Damage Number");
                    shadow.text = text;
                    label.text = text;
                    shadow.fontSize = size;
                    label.fontSize = size;
                    shadow.rectTransform.anchoredPosition = new Vector2(position.x, position.y - 1f);
                    label.rectTransform.anchoredPosition = position;
                    shadow.color = new Color(0f, 0f, 0f, alpha * 0.5f);
                    label.color = new Color(colour.r, colour.g, colour.b, alpha);
                    shadow.gameObject.SetActive(true);
                    label.gameObject.SetActive(true);
                    shown++;
                }
            }

            for (int i = shown; i < _damageShown; i++)
            {
                _damageShadows[i].gameObject.SetActive(false);
                _damageLabels[i].gameObject.SetActive(false);
            }
            _damageShown = shown;
        }

        Text GetDamageText(List<Text> pool, int index, string baseName)
        {
            while (pool.Count <= index)
            {
                Text text = MakeText(_canvasRoot!, $"{baseName} {pool.Count}", AnchorBottomLeft,
                    Vector2.zero, new Vector2(160f, 36f), 14, TextAnchor.MiddleCenter, Ink);
                pool.Add(text);
            }
            return pool[index];
        }

        HookSquare BuildHookSquare(Transform parent, string name, Vector2 position, string key)
        {
            GameObject rootGo = new GameObject(name);
            rootGo.AddComponent<CanvasRenderer>();
            rootGo.AddComponent<RectTransform>();
            rootGo.transform.SetParent(parent);
            ApplyRect(rootGo, AnchorCenter, position, new Vector2(24f, 24f));

            HookSquare square = new HookSquare();
            square.Root = rootGo;
            square.Border = MakeImage(rootGo.transform, "Border", AnchorCenter,
                Vector2.zero, new Vector2(24f, 24f), new Color(1f, 1f, 1f, 0.36f));
            square.Fill = MakeImage(rootGo.transform, "Fill", AnchorCenter,
                Vector2.zero, new Vector2(20f, 20f),
                new Color(5f / 255f, 8f / 255f, 13f / 255f, 130f / 255f));
            square.Key = MakeText(rootGo.transform, "Key", AnchorCenter,
                Vector2.zero, new Vector2(24f, 24f), 15, TextAnchor.MiddleCenter, Faint);
            square.Key.text = key;
            return square;
        }

        /// <summary>
        /// Tab-held scoreboard mirroring the reference <c>Screens.DrawScoreboardContent</c>
        /// (1280x720 logical metrics scaled 1.5x onto the 1080p canvas): map header,
        /// "FIRST TO" line, K / D / K-D / PTS columns, ranked leaderboard and the gold
        /// highlight row behind the local player. "TAB to close" footer included; the
        /// reference always draws it, including inside the match-over screen.
        /// </summary>
        void SyncScoreboard(Simulation sim)
        {
            if (_scoreboard == null || _scoreboardRect == null || _scoreFooter == null) return;

            int rows = sim.Actors.Count + 2;
            float panelH = 84f + rows * 39f;
            _scoreboardRect.sizeDelta = new Vector2(1140f, panelH);
            _scoreboardRect.anchoredPosition = new Vector2(0f, -(150f + panelH * 0.5f));
            if (_scoreTitle != null) _scoreTitle.text = $"{sim.Map.Name.ToUpperInvariant()}  -  DEATHMATCH";
            if (_scoreFirstTo != null) _scoreFirstTo.text = $"FIRST TO {sim.KillsToWin}";
            _scoreFooter.rectTransform.anchoredPosition = new Vector2(280f, -(panelH - 24f));

            int index = 0;
            int rank = 1;
            foreach (Actor actor in sim.Leaderboard())
            {
                ScoreRow row = EnsureScoreRow(index);
                row.Root.SetActive(true);
                row.Fill.SetActive(actor.IsPlayer);
                row.Name.text = $"{rank}.  {actor.Name}{(actor.IsPlayer ? "  (you)" : "")}";
                row.Name.color = actor.IsPlayer ? Gold : (actor.Alive ? Ink : Dim);
                float kd = actor.Deaths > 0 ? actor.Kills / (float)actor.Deaths : actor.Kills;
                row.Kills.text = $"{actor.Kills}";
                row.Deaths.text = $"{actor.Deaths}";
                row.Kd.text = $"{kd:0.00}";
                row.Pts.text = $"{actor.KillPoints}";
                index++;
                rank++;
            }
            for (int i = index; i < _scoreRows.Count; i++)
            {
                if (_scoreRows[i].Root.activeSelf) _scoreRows[i].Root.SetActive(false);
            }
        }

        ScoreRow EnsureScoreRow(int index)
        {
            while (_scoreRows.Count <= index)
            {
                int i = _scoreRows.Count;
                GameObject root = new GameObject($"Score Row {i}");
                RectTransform rect = root.AddComponent<RectTransform>();
                root.transform.SetParent(_scoreboard!.transform);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(1140f, 39f);
                rect.anchoredPosition = new Vector2(570f, -(99f + i * 39f + 19.5f));

                ScoreRow row = new ScoreRow();
                row.Root = root;
                row.Fill = MakeImage(root.transform, "Highlight", AnchorTopLeft,
                    new Vector2(570f, -24f), new Vector2(1116f, 36f),
                    new Color(Gold.r, Gold.g, Gold.b, 38f / 255f)).gameObject;
                row.Name = MakeText(root.transform, "Name", AnchorTopLeft,
                    new Vector2(380f, -19.5f), new Vector2(700f, 30f), 15, TextAnchor.MiddleLeft, Ink);
                row.Kills = MakeText(root.transform, "K", AnchorTopRight,
                    new Vector2(-355f, -19.5f), new Vector2(80f, 30f), 15, TextAnchor.MiddleRight, Ink);
                row.Deaths = MakeText(root.transform, "D", AnchorTopRight,
                    new Vector2(-265f, -19.5f), new Vector2(80f, 30f), 15, TextAnchor.MiddleRight, Dim);
                row.Kd = MakeText(root.transform, "K/D", AnchorTopRight,
                    new Vector2(-175f, -19.5f), new Vector2(80f, 30f), 15, TextAnchor.MiddleRight, Dim);
                row.Pts = MakeText(root.transform, "PTS", AnchorTopRight,
                    new Vector2(-70f, -19.5f), new Vector2(80f, 30f), 15, TextAnchor.MiddleRight, Gold);
                row.Root.SetActive(false);
                _scoreRows.Add(row);
            }
            return _scoreRows[index];
        }

        Camera? PlayerCamera()
        {
            if (_camera == null && _driver != null && _driver.PlayerCamera != null)
            {
                _camera = _driver.PlayerCamera.GetComponent<Camera>();
            }
            return _camera;
        }

        static Color Fade(Color color, float opacity) =>
            new Color(color.r, color.g, color.b, color.a * U.Clamp01(opacity));

        /// <summary>
        /// Local HUD fade behind a real scope / calibrated sight picture (reference
        /// <c>Hud.PeripheralHudOpacity</c>): the peripheral anchors drop to 0.38
        /// opacity so the protected centre stays the only combat read. An ordinary
        /// narrowed FOV never fades the HUD.
        /// </summary>
        static float PeripheralOpacity(WeaponDef def, float zoomBlend)
        {
            if (!def.HasScope && !def.HasCalibratedIronSights) return 1f;
            float t = U.Clamp01((zoomBlend - 0.16f) / 0.68f);
            return U.Lerp(1f, 0.38f, t);
        }

        static float WrapHeading(float degrees)
        {
            float wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        static float SignedHeadingDelta(float current, float candidate)
        {
            float delta = (candidate - current) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }

        static string CompassLabel(int degrees) => degrees switch
        {
            0 => "N",
            45 => "NE",
            90 => "E",
            135 => "SE",
            180 => "S",
            225 => "SW",
            270 => "W",
            315 => "NW",
            _ => $"{degrees:000}",
        };

        static string FormatClock(float seconds)
        {
            float clamped = MathF.Max(0f, seconds);
            int minutes = (int)(clamped / 60f);
            int secs = (int)(clamped % 60f);
            return $"{minutes:00}:{secs:00}";
        }

        // ------------------------------------------------------------------ build

        void BuildCanvas()
        {
            GameObject canvasGo = new GameObject("HudSync Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.transform.SetParent(transform);
            Transform root = canvasGo.transform;
            _canvasRoot = root;

            // Scope overlay first so its corner masks sit beneath the info cards,
            // like the reference draw order (peripheral HUD drawn after the scope).
            _scope = ScopeOverlay.Create(root);

            // Vitals card (bottom-left, reference DrawVitals) ---------------
            _vitals = VitalsReadout.Build(root, _font!);

            // Weapon readout card (bottom-right, reference DrawWeaponReadout) -
            _weaponReadout = WeaponReadout.Build(root, _font!);

            // Top bar (reference DrawTopBar; panel 220x58 at 1.5x) ------------
            _topBarBorder = MakeImage(root, "Top Bar Border", AnchorTopCenter,
                new Vector2(0f, -65.5f), new Vector2(332f, 89f), new Color(1f, 1f, 1f, 0.29f));
            _topBarFill = MakeImage(root, "Top Bar Fill", AnchorTopCenter,
                new Vector2(0f, -65.5f), new Vector2(330f, 87f),
                new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
            // Reference DrawArenaPanel(panel, opacity, urgent ? Danger : Accent): the
            // 330x87 fill's canvas-space top-left is (795, 22) at 1920x1080.
            _topBarBrackets = CornerBrackets.Build(root, "Top Bar", 795f, 22f, 330f, 87f,
                DefaultBracketColor, 1f);
            _clockLabel = MakeText(root, "Match Clock Label", AnchorTopCenter,
                new Vector2(0f, -40f), new Vector2(220f, 20f), 14, TextAnchor.MiddleCenter, Faint);
            _clockLabel.text = "MATCH CLOCK";
            _timer = MakeText(root, "Timer", AnchorTopCenter,
                new Vector2(0f, -68.5f), new Vector2(300f, 50f), 42, TextAnchor.MiddleCenter, Ink);
            _clockScore = MakeText(root, "Clock Score", AnchorTopCenter,
                new Vector2(0f, -95.5f), new Vector2(400f, 20f), 14, TextAnchor.MiddleCenter, Dim);

            // Match result banner -------------------------------------------
            _matchResult = MakeText(root, "Match Result", AnchorCenter,
                new Vector2(0f, 40f), new Vector2(1440f, 120f), 56, TextAnchor.MiddleCenter, Gold);
            _matchResult.gameObject.SetActive(false);

            // Compass tape (reference DrawCompassTape; 420x21 at 1.5x) -------
            _compassBorder = MakeImage(root, "Compass Border", AnchorTopCenter,
                new Vector2(0f, -135.75f), new Vector2(632f, 33f), new Color(1f, 1f, 1f, 0.29f));
            _compassFill = MakeImage(root, "Compass Fill", AnchorTopCenter,
                new Vector2(0f, -135.75f), new Vector2(630f, 31f),
                new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
            // Reference DrawArenaPanel(strip, opacity): default bracket ink.
            _compassBrackets = CornerBrackets.Build(root, "Compass", 645f, 120.25f, 630f, 31f,
                DefaultBracketColor, 1f);
            _compassCaret = MakeImage(root, "Compass Caret", AnchorTopCenter,
                new Vector2(0f, -124f), new Vector2(7f, 7f), Gold);
            _compassCaret.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _compassHeading = MakeText(root, "Compass Heading", AnchorTopCenter,
                new Vector2(0f, -161.5f), new Vector2(120f, 14f), 14, TextAnchor.MiddleCenter, Faint);
            _compassTicks = new Image?[30];
            _compassTickLabels = new Text?[30];
            for (int i = 0; i < _compassTicks.Length; i++)
            {
                _compassTicks[i] = MakeImage(root, $"Compass Tick {i}", AnchorTopCenter,
                    Vector2.zero, new Vector2(2f, 8f), new Color(1f, 1f, 1f, 0.5f));
                _compassTickLabels[i] = MakeText(root, $"Compass Label {i}", AnchorTopCenter,
                    new Vector2(0f, -142f), new Vector2(40f, 14f), 14, TextAnchor.MiddleCenter, Dim);
                _compassTicks[i]!.gameObject.SetActive(false);
                _compassTickLabels[i]!.gameObject.SetActive(false);
            }

            // Radar (reference DrawRadar; player-centred, rotates with yaw) --
            _radarBorder = MakeImage(root, "Radar Border", AnchorTopLeft,
                new Vector2(136.5f, -133.5f), new Vector2(216f, 216f),
                new Color(232f / 255f, 240f / 255f, 244f / 255f, 74f / 255f));
            _radarFill = MakeImage(root, "Radar Fill", AnchorTopLeft,
                new Vector2(136.5f, -133.5f), new Vector2(214f, 214f),
                new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
            // Reference DrawArenaPanel(radarBounds, opacity, U.Rgba(112,205,228,180)).
            _radarBrackets = CornerBrackets.Build(root, "Radar", 29.5f, 26.5f, 214f, 214f,
                DefaultBracketColor, 1f);
            GameObject radarGo = new GameObject("Radar");
            radarGo.AddComponent<CanvasRenderer>();
            radarGo.AddComponent<RectTransform>();
            radarGo.transform.SetParent(root);
            ApplyRect(radarGo, AnchorTopLeft, new Vector2(136.5f, -133.5f), new Vector2(216f, 216f));
            _radar = radarGo.AddComponent<RadarGraphic>();
            _radarTitle = MakeText(root, "Radar Title", AnchorTopLeft,
                new Vector2(126.5f, -43.5f), new Vector2(160f, 18f), 14, TextAnchor.MiddleLeft, Faint);
            _radarTitle.text = "FIELD SCANNER";
            _radarRange = MakeText(root, "Radar Range", AnchorTopLeft,
                new Vector2(146.5f, -210f), new Vector2(160f, 16f), 12, TextAnchor.MiddleRight, Faint);
            _radarRange.text = "RANGE 72";

            // Kill feed (top-right, reference DrawKillFeed; 27-unit rows at 1.5x) ---
            _killFeed = new FeedRow[FeedLines];
            for (int i = 0; i < FeedLines; i++)
            {
                _killFeed[i] = BuildFeedRow(root, i);
            }

            // Crosshair + hitmarker (centre) ---------------------------------
            _crosshair = CrosshairGraphic.Create(root, "Crosshair");
            _hitmarker = MakeText(root, "Hitmarker", AnchorCenter,
                Vector2.zero, new Vector2(48f, 56f), 34, TextAnchor.MiddleCenter, Ink);
            _hitmarker.text = "×";
            _hitmarker.gameObject.SetActive(false);
            // Reference DrawHitmarker labels: ELIMINATED on a kill (11px) or HEADSHOT
            // on a headshot (10px), drawn under the marker on the same envelope.
            // y = 26 / 24 reference units x 1.5, font 11 / 10 x 1.5.
            _hitmarkerLabel = MakeText(root, "Hitmarker Label", AnchorCenter,
                new Vector2(0f, 39f), new Vector2(360f, 26f), 16, TextAnchor.MiddleCenter, Danger);
            _hitmarkerLabel.gameObject.SetActive(false);

            // Damage / trauma overlays (full screen) --------------------------
            _damageOverlay = MakeImage(root, "Damage Overlay", AnchorBottomLeft,
                Vector2.zero, new Vector2(1920f, 1080f), new Color(0f, 0f, 0f, 0f));
            _damageOverlay.gameObject.SetActive(false);

            // Directional damage arrow (rotating wheel, arrow above centre) ----
            GameObject wheelGo = new GameObject("Damage Wheel");
            _damageWheel = wheelGo.AddComponent<RectTransform>();
            ApplyRect(wheelGo, AnchorCenter, Vector2.zero, Vector2.zero);
            Text arrow = MakeText(_damageWheel.transform, "Damage Arrow", AnchorCenter,
                new Vector2(0f, 120f), new Vector2(60f, 40f), 26, TextAnchor.MiddleCenter, Danger);
            arrow.text = "^";

            // Hook status squares (reference DrawHookStatus; 16px at 1.5x) -----
            _hookLeft = BuildHookSquare(root, "Hook Q", new Vector2(-60f, -51f), "Q");
            _hookRight = BuildHookSquare(root, "Hook E", new Vector2(36f, -51f), "E");

            // Station prompt (reference DrawStationPrompt) ----------------------
            _stationTitle = MakeText(root, "Station Title", AnchorCenter,
                new Vector2(0f, -180f), new Vector2(760f, 30f), 22, TextAnchor.MiddleCenter, Ink);
            _stationTitle.text = "[ E ]  UPGRADE CABINET";
            _stationSub = MakeText(root, "Station Sub", AnchorCenter,
                new Vector2(0f, -212f), new Vector2(760f, 24f), 15, TextAnchor.MiddleCenter, Gold);
            _stationTitle.gameObject.SetActive(false);
            _stationSub.gameObject.SetActive(false);

            // Death overlay (reference DrawDeathOverlay) --------------------
            _deathOverlay = DeathOverlay.Build(root, _font!);

            // Trauma vignette (reference DrawHealthVignette) ------------------
            // Built last so it stacks over every HUD element like the reference,
            // which draws the vignette after the whole match layer; only the Tab
            // scoreboard (drawn after Hud.Draw in the reference) stays above it.
            GameObject traumaGo = new GameObject("Trauma Graphic");
            traumaGo.AddComponent<CanvasRenderer>();
            traumaGo.AddComponent<RectTransform>();
            traumaGo.transform.SetParent(root);
            ApplyRect(traumaGo, AnchorCenter, Vector2.zero, new Vector2(1920f, 1080f));
            _trauma = traumaGo.AddComponent<TraumaGraphic>();

            // Scoreboard overlay (hidden; reference DrawScoreboardContent at 1.5x on
            // the 1080p canvas: panel 1140 wide, row pitch 39, top offset 150).
            Image scorePanel = MakeImage(root, "Scoreboard", AnchorTopCenter,
                Vector2.zero, new Vector2(1140f, 400f), ScorePanel);
            _scoreboard = scorePanel.gameObject;
            _scoreboardRect = scorePanel.rectTransform;
            Transform scoreRoot = _scoreboard.transform;

            _scoreTitle = MakeText(scoreRoot, "Scoreboard Title", AnchorTopLeft,
                new Vector2(380f, -41f), new Vector2(700f, 40f), 30, TextAnchor.MiddleLeft, Ink);
            _scoreFirstTo = MakeText(scoreRoot, "Scoreboard First To", AnchorTopRight,
                new Vector2(-230f, -42f), new Vector2(400f, 30f), 22, TextAnchor.MiddleRight, Gold);
            MakeText(scoreRoot, "Scoreboard Header Player", AnchorTopLeft,
                new Vector2(130f, -84f), new Vector2(200f, 24f), 15, TextAnchor.MiddleLeft, Dim).text = "PLAYER";
            MakeText(scoreRoot, "Scoreboard Header K", AnchorTopRight,
                new Vector2(-355f, -84f), new Vector2(80f, 24f), 15, TextAnchor.MiddleRight, Dim).text = "K";
            MakeText(scoreRoot, "Scoreboard Header D", AnchorTopRight,
                new Vector2(-265f, -84f), new Vector2(80f, 24f), 15, TextAnchor.MiddleRight, Dim).text = "D";
            MakeText(scoreRoot, "Scoreboard Header KD", AnchorTopRight,
                new Vector2(-175f, -84f), new Vector2(80f, 24f), 15, TextAnchor.MiddleRight, Dim).text = "K/D";
            MakeText(scoreRoot, "Scoreboard Header PTS", AnchorTopRight,
                new Vector2(-70f, -84f), new Vector2(80f, 24f), 15, TextAnchor.MiddleRight, Dim).text = "PTS";
            _scoreFooter = MakeText(scoreRoot, "Scoreboard Footer", AnchorTopLeft,
                new Vector2(280f, -300f), new Vector2(500f, 30f), 15, TextAnchor.MiddleLeft, Faint);
            _scoreFooter.text = "TAB to close";
            _scoreboard.SetActive(false);
        }

        static readonly Vector2 AnchorBottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 AnchorBottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 AnchorTopLeft = new Vector2(0f, 1f);
        static readonly Vector2 AnchorTopRight = new Vector2(1f, 1f);
        static readonly Vector2 AnchorTopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 AnchorCenter = new Vector2(0.5f, 0.5f);

        FeedRow BuildFeedRow(Transform parent, int index)
        {
            // Reference row pitch is 27 logical units at the 1.5x canvas scale; the panel
            // is the reference's (content width + 18) x (rowHeight - 2) with a fixed
            // width because uGUI cannot measure the names before layout.
            const float PanelW = 470f;
            const float PanelH = 37.5f;
            const float Pitch = 40.5f;
            FeedRow row = new FeedRow();

            GameObject go = new GameObject($"Feed {index}");
            go.AddComponent<CanvasRenderer>();
            RectTransform rect = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-22.5f, -(22.5f + index * Pitch));
            rect.sizeDelta = new Vector2(PanelW, PanelH);
            row.Root = go;

            row.Border = FeedBox(go.transform, $"Feed {index} Border", -1f, -1f, PanelW + 2f, PanelH + 2f,
                CardBorderColor);
            row.Fill = FeedBox(go.transform, $"Feed {index} Fill", 0f, 0f, PanelW, PanelH,
                new Color(5f / 255f, 8f / 255f, 13f / 255f, 206f / 255f));
            row.Brackets = CornerBrackets.Build(go.transform, $"Feed {index}", 0f, 0f, PanelW, PanelH,
                DefaultBracketColor, 1f);

            row.Killer = FeedText(go.transform, $"Feed {index} Killer", 13.5f, 4.5f, 120f, 28.5f, 18,
                TextAnchor.MiddleLeft, Ink);
            row.Separator = FeedText(go.transform, $"Feed {index} Separator", 139.5f, 4.5f, 12f, 28.5f, 18,
                TextAnchor.MiddleLeft, Ink);
            row.Separator.text = ">";
            row.Weapon = FeedText(go.transform, $"Feed {index} Weapon", 154.5f, 7.5f, 95f, 24f, 14,
                TextAnchor.MiddleLeft, AccentInk);
            row.Victim = FeedText(go.transform, $"Feed {index} Victim", 296.5f, 4.5f, 160f, 28.5f, 18,
                TextAnchor.MiddleRight, Ink);

            go.SetActive(false);
            return row;
        }

        static Image FeedBox(Transform parent, string name, float dx, float dy, float w, float h, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            RectTransform rect = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(dx, -dy);
            rect.sizeDelta = new Vector2(w, h);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        Text FeedText(Transform parent, string name, float dx, float dy, float w, float h,
            int fontSize, TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            RectTransform rect = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(dx, -dy);
            rect.sizeDelta = new Vector2(w, h);

            Text text = go.AddComponent<Text>();
            text.font = _font ?? Font.CreateDynamicFontFromOSFont("Arial", 24);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            return text;
        }

        void ApplyRect(GameObject go, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                rectTransform = go.AddComponent<RectTransform>();
            }
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        Text MakeText(Transform parent, string name, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, int fontSize,
            TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplyRect(go, anchor, anchoredPosition, size);

            Text text = go.AddComponent<Text>();
            text.font = _font ?? Font.CreateDynamicFontFromOSFont("Arial", 24);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            return text;
        }

        Image MakeImage(Transform parent, string name, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<CanvasRenderer>();
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent);
            ApplyRect(go, anchor, anchoredPosition, size);

            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }
    }
}
