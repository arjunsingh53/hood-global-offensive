using System;
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
    /// reference HUD's information layer (health card, ammo card, timer, score,
    /// kill feed, hitmarker, damage flash + directional indicator, low-health
    /// vignette, match-over banner) as one blockout; per-part colours follow the
    /// reference palette where the values were already captured.
    /// Read-only view: it never writes to the simulation.
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

        const int FeedLines = 6;

        MatchDriver? _driver;
        Font? _font;

        Text _healthValue;
        Text _healthLabel;
        Image _healthTrack;
        Image _healthFill;
        Text _ammoWeapon;
        Text _ammoMag;
        Text _ammoReserve;
        Text _timer;
        Text _matchResult;
        Text _score;
        Text _crosshair;
        Text _hitmarker;
        Text _deathNotice;
        Image _damageOverlay;
        Image _traumaOverlay;
        RectTransform _damageWheel;
        Text[] _killFeed;

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
            Weapon weapon = p.Current;
            WeaponDef def = weapon.Def;

            // Health card --------------------------------------------------
            float health = U.Clamp01(p.Health / p.MaxHealth);
            bool lowHealth = p.Health < Cfg.LowHealthTraumaThreshold;
            Color healthColor = lowHealth ? Danger : (health > 0.6f ? Ink : Amber);
            _healthValue.text = $"{MathF.Ceiling(p.Health):0}";
            _healthValue.color = healthColor;
            _healthLabel.color = lowHealth ? Danger : Dim;
            float fillWidth = _healthTrack.rectTransform.sizeDelta.x * health;
            _healthFill.rectTransform.sizeDelta =
                new Vector2(fillWidth, _healthFill.rectTransform.sizeDelta.y);
            _healthFill.color = lowHealth ? Danger : Good;

            // Ammo card ----------------------------------------------------
            string mag = def.UsesAmmo ? weapon.Mag.ToString("00") : "--";
            _ammoMag.text = mag;
            _ammoMag.color = def.UsesAmmo && weapon.Mag == 0 ? Danger : Ink;
            _ammoWeapon.text = def.Name.ToUpperInvariant();
            _ammoReserve.text = def.UsesAmmo
                ? (def.InfiniteReserve ? "RESERVE INF" : $"RESERVE {weapon.Reserve}")
                : "MELEE";

            // Timer / match state ------------------------------------------
            _timer.text = sim.MatchOver ? "" : FormatClock(sim.TimeLeft);
            _matchResult.gameObject.SetActive(sim.MatchOver);
            _matchResult.text = sim.MatchResult;

            // Score --------------------------------------------------------
            _score.text = $"K {p.Kills}   D {p.Deaths}   PTS {p.KillPoints}   #{sim.PlayerRank() + 1}";

            // Kill feed (newest first, 6.5 s life like the reference) -------
            int line = 0;
            for (int i = sim.KillFeed.Count - 1; i >= 0 && line < FeedLines; i--)
            {
                KillFeedEntry entry = sim.KillFeed[i];
                float age = sim.Time - entry.SpawnedAt;
                if (age > 6.5f) continue;
                float alpha = age > 5.5f ? U.Clamp01(6.5f - age) : 1f;
                string weaponName = entry.Headshot ? "HEAD" : entry.Weapon.ToUpperInvariant();
                Text row = _killFeed[line++];
                row.gameObject.SetActive(true);
                row.text = $"{entry.Killer}  [{weaponName}]  {entry.Victim}";
                Color baseColor = entry.KillerIsPlayer ? Gold : Ink;
                row.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            }
            for (; line < FeedLines; line++)
            {
                if (_killFeed[line].gameObject.activeSelf) _killFeed[line].gameObject.SetActive(false);
            }

            // Hitmarker (reference life is 0.12 s) --------------------------
            float hitAlpha = 0f;
            if (sim.Time <= sim.HitmarkerUntil && !sim.MatchOver)
            {
                float elapsed = U.Clamp01(1f - (sim.HitmarkerUntil - sim.Time) / 0.12f);
                hitAlpha = 1f - U.Lerp(0.35f, 1f, elapsed);
            }
            _hitmarker.gameObject.SetActive(hitAlpha > 0f);
            if (hitAlpha > 0f)
            {
                Color hitColor = sim.LastHitWasKill || sim.LastHitWasHeadshot ? Danger : Ink;
                _hitmarker.color = new Color(hitColor.r, hitColor.g, hitColor.b, hitAlpha);
            }

            // Damage feedback ----------------------------------------------
            float killedFlash = sim.Time < sim.KilledFlashUntil ? 0.45f : 0f;
            float flashAlpha = MathF.Max(U.Clamp(sim.DamageFlash, 0f, 1f) * 0.35f, killedFlash);
            _damageOverlay.color = new Color(DamageTint.r, DamageTint.g, DamageTint.b, flashAlpha);
            _damageOverlay.gameObject.SetActive(flashAlpha > 0.001f);

            float traumaAlpha = lowHealth
                ? p.LowHealthTrauma * (0.5f + 0.5f * p.TraumaPulse) * 0.45f
                : 0f;
            _traumaOverlay.color = new Color(DamageTint.r, DamageTint.g, DamageTint.b, traumaAlpha);
            _traumaOverlay.gameObject.SetActive(traumaAlpha > 0.001f);

            // Directional damage arrow: the wheel rotates so the arrow points at
            // the source on screen (relative yaw: + is to the player's left).
            if (flashAlpha > 0.001f)
            {
                float relative = U.AngleDelta(p.Yaw, sim.DamageFlashDirection);
                _damageWheel.localRotation = Quaternion.Euler(0f, 0f, -relative);
            }

            // Death / respawn ----------------------------------------------
            bool deathVisible = !p.Alive;
            _deathNotice.gameObject.SetActive(deathVisible);
            if (deathVisible)
            {
                _deathNotice.text = $"RESPAWNING IN {MathF.Max(0f, p.RespawnAt - sim.Time):0.0}s";
            }
        }

        static string FormatClock(float seconds)
        {
            float clamped = MathF.Max(0f, seconds);
            int minutes = (int)(clamped / 60f);
            int secs = (int)(clamped % 60f);
            return $"{minutes:0}:{secs:00}";
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

            // Health card (bottom-left, mirrors the reference card stack) ----
            _healthValue = MakeText(root, "Health", AnchorBottomLeft,
                new Vector2(24f, 60f), new Vector2(240f, 60f), 44, TextAnchor.LowerLeft, Ink);
            _healthLabel = MakeText(root, "Health Label", AnchorBottomLeft,
                new Vector2(26f, 40f), new Vector2(240f, 20f), 10, TextAnchor.LowerLeft, Dim);
            _healthLabel.text = "HEALTH";
            _healthTrack = MakeImage(root, "Health Track", AnchorBottomLeft,
                new Vector2(24f, 18f), new Vector2(180f, 8f), new Color(1f, 1f, 1f, 0.10f));
            _healthFill = MakeImage(root, "Health Fill", AnchorBottomLeft,
                new Vector2(24f, 18f), new Vector2(180f, 8f), Good);

            // Ammo card (bottom-right) --------------------------------------
            _ammoWeapon = MakeText(root, "Ammo Weapon", AnchorBottomRight,
                new Vector2(-24f, 96f), new Vector2(400f, 24f), 20, TextAnchor.UpperRight, Dim);
            _ammoMag = MakeText(root, "Ammo Mag", AnchorBottomRight,
                new Vector2(-24f, 24f), new Vector2(300f, 60f), 46, TextAnchor.UpperRight, Ink);
            _ammoReserve = MakeText(root, "Ammo Reserve", AnchorBottomRight,
                new Vector2(-24f, 2f), new Vector2(400f, 20f), 13, TextAnchor.UpperRight, Dim);

            // Timer / match result ------------------------------------------
            _timer = MakeText(root, "Timer", AnchorTopCenter,
                new Vector2(0f, -16f), new Vector2(720f, 40f), 30, TextAnchor.UpperCenter, Ink);
            _matchResult = MakeText(root, "Match Result", AnchorCenter,
                new Vector2(0f, 40f), new Vector2(1440f, 120f), 56, TextAnchor.MiddleCenter, Gold);
            _matchResult.gameObject.SetActive(false);

            // Score (top-left) ----------------------------------------------
            _score = MakeText(root, "Score", AnchorTopLeft,
                new Vector2(20f, -14f), new Vector2(320f, 26f), 18, TextAnchor.UpperLeft, Ink);

            // Kill feed (top-right) ------------------------------------------
            _killFeed = new Text[FeedLines];
            for (int i = 0; i < FeedLines; i++)
            {
                Text row = MakeText(root, $"Feed {i}", AnchorTopRight,
                    new Vector2(-20f, -20 - i * 27), new Vector2(540f, 26f), 18, TextAnchor.UpperRight, Ink);
                row.gameObject.SetActive(false);
                _killFeed[i] = row;
            }

            // Crosshair + hitmarker (centre) ---------------------------------
            _crosshair = MakeText(root, "Crosshair", AnchorCenter,
                Vector2.zero, new Vector2(48f, 56f), 30, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.85f));
            _crosshair.text = "+";
            _hitmarker = MakeText(root, "Hitmarker", AnchorCenter,
                Vector2.zero, new Vector2(48f, 56f), 34, TextAnchor.MiddleCenter, Ink);
            _hitmarker.text = "×";
            _hitmarker.gameObject.SetActive(false);

            // Damage / trauma overlays (full screen) --------------------------
            _damageOverlay = MakeImage(root, "Damage Overlay", AnchorBottomLeft,
                Vector2.zero, new Vector2(1920f, 1080f), new Color(0f, 0f, 0f, 0f));
            _damageOverlay.gameObject.SetActive(false);
            _traumaOverlay = MakeImage(root, "Trauma Overlay", AnchorBottomLeft,
                Vector2.zero, new Vector2(1920f, 1080f), new Color(0f, 0f, 0f, 0f));
            _traumaOverlay.gameObject.SetActive(false);

            // Directional damage arrow (rotating wheel, arrow above centre) ----
            GameObject wheelGo = new GameObject("Damage Wheel");
            _damageWheel = wheelGo.AddComponent<RectTransform>();
            ApplyRect(wheelGo, AnchorCenter, Vector2.zero, Vector2.zero);
            Text arrow = MakeText(_damageWheel.transform, "Damage Arrow", AnchorCenter,
                new Vector2(0f, 120f), new Vector2(60f, 40f), 26, TextAnchor.MiddleCenter, Danger);
            arrow.text = "^";

            // Death / respawn notice (lower-centre) ---------------------------
            _deathNotice = MakeText(root, "Death Notice", AnchorBottomCenter,
                new Vector2(0f, 120f), new Vector2(1200f, 40f), 30, TextAnchor.MiddleCenter, Danger);
            _deathNotice.gameObject.SetActive(false);
        }

        static readonly Vector2 AnchorBottomLeft = new Vector2(0f, 0f);
        static readonly Vector2 AnchorBottomCenter = new Vector2(0.5f, 0f);
        static readonly Vector2 AnchorBottomRight = new Vector2(1f, 0f);
        static readonly Vector2 AnchorTopLeft = new Vector2(0f, 1f);
        static readonly Vector2 AnchorTopRight = new Vector2(1f, 1f);
        static readonly Vector2 AnchorTopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 AnchorCenter = new Vector2(0.5f, 0.5f);

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
