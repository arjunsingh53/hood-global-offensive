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
        static readonly Color DamageInk = new Color(248f / 255f, 248f / 255f, 240f / 255f);
        static readonly Color HeadshotDamage = new Color(1f, 176f / 255f, 48f / 255f);
        static readonly Color Faint = new Color(0.38f, 0.40f, 0.42f);
        static readonly Color ScorePanel = new Color(8f / 255f, 10f / 255f, 14f / 255f, 225f / 255f);

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
        CrosshairGraphic _crosshair;
        Text _hitmarker;
        Text _deathNotice;
        Image _damageOverlay;
        Image _traumaOverlay;
        RectTransform _damageWheel;
        Text[] _killFeed;
        GameObject? _scoreboard;
        RectTransform? _scoreboardRect;
        Text? _scoreTitle;
        Text? _scoreFirstTo;
        Text? _scoreFooter;
        readonly List<ScoreRow> _scoreRows = new();

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

            // Crosshair -----------------------------------------------------
            SyncCrosshair(p, def);

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
            _canvasRoot = root;

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
            _crosshair = CrosshairGraphic.Create(root, "Crosshair");
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
