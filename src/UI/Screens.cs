using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

public enum ScreenAction
{
    None,
    StartMatch,
    OpenCrosshairOptions,
    CloseCrosshairOptions,
    OpenWeaponDossier,
    CloseWeaponDossier,
    Resume,
    Restart,
    QuitToMenu,
    QuitApp,
}

/// <summary>Options chosen on the front end and used to build a match.</summary>
public sealed class MatchSettings
{
    public int BotCount = Cfg.DefaultBotCount;
    public BotSkill Skill = BotSkill.Normal;
    public float Sensitivity = Cfg.Sensitivity;
    public int KillsToWin = Cfg.KillsToWin;
    public ulong Seed = 20260114;
    public readonly CrosshairSettings Crosshair = new();
    public readonly CameraFeedbackSettings CameraFeedback = new();
}

/// <summary>
/// Preserves the opening B press while the buy overlay takes ownership of the
/// frame. The menu must see a release, then a new B press, before B can close
/// it again. Escape is intentionally never delayed.
/// </summary>
public sealed class BuyMenuKeyLatch
{
    private bool _awaitingBuyRelease;

    public void ArmForOpen() => _awaitingBuyRelease = true;

    public void Reset() => _awaitingBuyRelease = false;

    public bool ShouldClose(bool buyDown, bool buyPressed, bool escapePressed)
    {
        if (escapePressed)
        {
            _awaitingBuyRelease = false;
            return true;
        }

        if (_awaitingBuyRelease)
        {
            if (!buyDown) _awaitingBuyRelease = false;
            return false;
        }

        return buyPressed;
    }
}

/// <summary>Interactive text menus built from a small immediate-mode list.</summary>
public static class Screens
{
    private readonly record struct Row(string Id, string Label, string Value, Rectangle Rect);

    private static bool Hit(Vector2 p, Rectangle r)
        => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    private static readonly List<Row> Rows = new();

    private static void Begin() => Rows.Clear();

    private static Rectangle Add(string id, string label, string value, int x, int y, int w, int rowH)
    {
        var r = new Rectangle(x, y, w, rowH);
        Rows.Add(new Row(id, label, value, r));
        return r;
    }

    private static void DrawRows(int selected)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Row row = Rows[i];
            bool isSelected = i == selected;
            Ui.Panel(row.Rect,
                isSelected ? U.Rgba(255, 186, 56, 46) : U.Rgba(255, 255, 255, 10),
                isSelected ? Ui.Accent : U.Rgba(255, 255, 255, 22));

            int textY = (int)row.Rect.Y + (int)(row.Rect.Height * 0.5f) - 8;
            Ui.Text(row.Label, (int)row.Rect.X + 16, textY, 15, isSelected ? Ui.Ink : Ui.Dim);
            if (row.Value.Length > 0)
            {
                Ui.TextRight(row.Value, (int)(row.Rect.X + row.Rect.Width) - 16, textY, 15,
                    isSelected ? Ui.Accent : Ui.Dim);
            }
        }
    }

    /// <summary>Keyboard + mouse navigation over the rows built this frame.</summary>
    private static void Navigate(ref int selected)
    {
        if (Rows.Count == 0) return;

        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) selected++;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) selected--;
        selected = ((selected % Rows.Count) + Rows.Count) % Rows.Count;

        // Rows live in Ui's 1280×720 logical canvas, so hit testing must use
        // the inverse-mapped cursor after a resized or ultrawide window.
        Vector2 mouse = Ui.GetMousePosition();
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Hit(mouse, Rows[i].Rect)) selected = i;
        }
    }

    private static bool Activated() =>
        Raylib.IsKeyPressed(KeyboardKey.Enter) ||
        Raylib.IsKeyPressed(KeyboardKey.KpEnter) ||
        Raylib.IsMouseButtonPressed(MouseButton.Left);

    private static int AxisPress()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D)) return 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A)) return -1;
        return 0;
    }

    // ================================================================ main menu

    public static class MainMenu
    {
        private static int _selected;

        public static ScreenAction Draw(MatchSettings settings, int screenW, int screenH)
        {
            // A restrained live map backdrop gives the front end a sense of place
            // without adding replacement props or changing the supplied scene.
            Renderer.DrawMenuBackdrop(screenW, screenH);
            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(4, 7, 11, 94));

            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;

            // Keep the command surface quiet enough for the Industrial Zone to
            // remain visible, but give the text an intentional left-hand anchor.
            Raylib.DrawRectangle(0, 0, 760, h, U.Rgba(8, 11, 16, 214));
            Raylib.DrawRectangle(0, 0, w, 6, Ui.Accent);
            Raylib.DrawRectangle(64, 145, 460, 1, U.Rgba(255, 255, 255, 34));
            Ui.Text(Cfg.GameName, 60, 54, 52, Ui.Ink);
            Ui.Text($"EARLY ACCESS v{Cfg.Version}   •   {Cfg.MapName.ToUpperInvariant()}   •   OFFLINE BOT DEATHMATCH",
                64, 118, 13, Ui.Dim);

            const int panelW = 460;
            const int x = 64;
            int y = 174;
            const int rowH = 44;
            const int gap = 8;

            Begin();
            Add("play", "START DEATHMATCH", "", x, y, panelW, rowH);
            y += rowH + gap;
            Add("bots", "BOTS", $"{settings.BotCount}", x, y, panelW, rowH);
            y += rowH + gap;
            Add("skill", "BOT SKILL", settings.Skill.ToString().ToUpperInvariant(), x, y, panelW, rowH);
            y += rowH + gap;
            Add("kill", "KILL LIMIT", $"{settings.KillsToWin}", x, y, panelW, rowH);
            y += rowH + gap;
            Add("sens", "MOUSE SENSITIVITY", $"{settings.Sensitivity:0.0}", x, y, panelW, rowH);
            y += rowH + gap;
            Add("crosshair", "CROSSHAIR OPTIONS", settings.Crosshair.PresetLabel, x, y, panelW, rowH);
            y += rowH + gap;
            Add("feedback", "CAMERA FEEDBACK", settings.CameraFeedback.Label, x, y, panelW, rowH);
            y += rowH + gap;
            Add("quit", "QUIT", "", x, y, panelW, rowH);

            Navigate(ref _selected);
            DrawRows(_selected);

            int axis = AxisPress();
            if (axis != 0)
            {
                switch (Rows[_selected].Id)
                {
                    case "bots":
                        settings.BotCount = U.ClampInt(settings.BotCount + axis, 1, Cfg.MaxBots);
                        break;
                    case "skill":
                        settings.Skill = (BotSkill)U.ClampInt((int)settings.Skill + axis, 0, 3);
                        break;
                    case "kill":
                        settings.KillsToWin = U.ClampInt(settings.KillsToWin + axis * 5, 5, 100);
                        break;
                    case "sens":
                        settings.Sensitivity = U.Clamp(settings.Sensitivity + axis * 0.1f, 0.3f, 8f);
                        break;
                    case "feedback":
                        settings.CameraFeedback.Cycle(axis);
                        break;
                }
            }

            bool clicked = Activated();
            if (clicked)
            {
                switch (Rows[_selected].Id)
                {
                    case "play": return ScreenAction.StartMatch;
                    case "crosshair": return ScreenAction.OpenCrosshairOptions;
                    case "quit": return ScreenAction.QuitApp;
                }
            }

            DrawMatchBrief();
            Ui.Text("WASD move    MOUSE aim    CLICK fire    RMB aim    R reload    1 / 2 / 3 weapon slots",
                64, h - 96, 10, Ui.Faint);
            Ui.Text("SHIFT sprint cycle    ALT walk    CTRL/C crouch    SPACE jump    B buy    F2 crosshair    TAB scores    ESC pause",
                64, h - 80, 10, Ui.Faint);
            Ui.Text("Tip: crouch and tap-fire for control. AK sights align to the centre ray.",
                64, h - 56, 10, Ui.Dim);

            return ScreenAction.None;
        }

        private static void DrawMatchBrief()
        {
            const int x = 770;
            const int y = 202;
            const int w = 420;
            const int h = 232;
            Ui.Panel(new Rectangle(x, y, w, h), U.Rgba(8, 11, 16, 196), U.Rgba(255, 255, 255, 42), 1.25f);
            Raylib.DrawRectangle(x, y, 5, h, Ui.Accent);
            Ui.Text("LOCAL OPERATION", x + 26, y + 24, 11, Ui.Accent);
            Ui.Text(Cfg.MapName.ToUpperInvariant(), x + 24, y + 48, 27, Ui.Ink);
            Ui.Text("OFFLINE FREE-FOR-ALL", x + 25, y + 82, 11, Ui.Dim);
            Raylib.DrawRectangle(x + 24, y + 108, w - 48, 1, U.Rgba(255, 255, 255, 28));

            Ui.Text("MODE", x + 24, y + 130, 10, Ui.Faint);
            Ui.Text("DEATHMATCH", x + 24, y + 148, 16, Ui.Ink);
            Ui.Text("LOADOUT", x + 174, y + 130, 10, Ui.Faint);
            Ui.Text("GUN GAME", x + 174, y + 148, 16, Ui.Ink);
            Ui.Text("STATUS", x + 306, y + 130, 10, Ui.Faint);
            Ui.Text("LOCAL", x + 306, y + 148, 16, Ui.Good);

            Ui.Text("Use the menu to tune bots, sensitivity, reticle and camera feedback before deployment.",
                x + 24, y + 194, 10, Ui.Dim);
        }
    }

    // ================================================================ pause

    public static class Pause
    {
        private static int _selected;

        public static ScreenAction Draw(MatchSettings settings, Simulation sim, int screenW, int screenH)
        {
            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(6, 8, 12, 175));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;

            const int panelW = 420;
            int x = (w - panelW) / 2;
            int y = (int)(h * 0.25f);
            const int rowH = 42;
            const int gap = 8;

            Ui.TextCenter("PAUSED", w / 2, y - 72, 40, Ui.Ink);
            Ui.TextCenter($"{sim.Player.Kills} kills   {sim.Player.Deaths} deaths   ${sim.Player.Money}",
                w / 2, y - 26, 15, Ui.Dim);

            Begin();
            Add("resume", "RESUME", "", x, y, panelW, rowH);
            y += rowH + gap;
            Add("sens", "MOUSE SENSITIVITY", $"{settings.Sensitivity:0.0}", x, y, panelW, rowH);
            y += rowH + gap;
            Add("crosshair", "CROSSHAIR OPTIONS", settings.Crosshair.PresetLabel, x, y, panelW, rowH);
            y += rowH + gap;
            Add("feedback", "CAMERA FEEDBACK", settings.CameraFeedback.Label, x, y, panelW, rowH);
            y += rowH + gap;
            Add("dossier", "WEAPON DOSSIER", sim.Player.Current.Def.Name, x, y, panelW, rowH);
            y += rowH + gap;
            Add("restart", "RESTART MATCH", "", x, y, panelW, rowH);
            y += rowH + gap;
            Add("menu", "QUIT TO MENU", "", x, y, panelW, rowH);

            Navigate(ref _selected);
            DrawRows(_selected);

            int axis = AxisPress();
            if (axis != 0 && Rows[_selected].Id == "sens")
            {
                settings.Sensitivity = U.Clamp(settings.Sensitivity + axis * 0.1f, 0.3f, 8f);
                sim.Player.Sensitivity = settings.Sensitivity;
            }
            else if (axis != 0 && Rows[_selected].Id == "feedback")
            {
                settings.CameraFeedback.Cycle(axis);
            }

            if (Activated())
            {
                switch (Rows[_selected].Id)
                {
                    case "resume": return ScreenAction.Resume;
                    case "crosshair": return ScreenAction.OpenCrosshairOptions;
                    case "dossier": return ScreenAction.OpenWeaponDossier;
                    case "restart": return ScreenAction.Restart;
                    case "menu": return ScreenAction.QuitToMenu;
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return ScreenAction.Resume;
            return ScreenAction.None;
        }
    }

    // ========================================================== weapon dossier

    /// <summary>
    /// Read-only HGO weapon readiness information. It deliberately reads the
    /// current local weapon state rather than emulating an external attachment
    /// system or changing balance from inside the UI.
    /// </summary>
    public static class WeaponDossier
    {
        public static ScreenAction Draw(Simulation sim, int screenW, int screenH)
        {
            Weapon weapon = sim.Player.Current;
            WeaponDef def = weapon.Def;

            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(6, 8, 12, 132));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;

            int panelW = Math.Min(660, Math.Max(480, w - 48));
            int panelH = Math.Min(548, h - 48);
            int x = (w - panelW) / 2;
            int y = (h - panelH) / 2;
            Ui.Panel(new Rectangle(x, y, panelW, panelH), U.Rgba(11, 13, 18, 242), U.Rgba(255, 255, 255, 52), 1.5f);

            Ui.Text("WEAPON DOSSIER", x + 24, y + 22, 28, Ui.Ink);
            Ui.Text("CURRENT HGO CONFIGURATION", x + 25, y + 54, 10, Ui.Accent);
            Ui.Text(def.Name.ToUpperInvariant(), x + 24, y + 86, 25, Ui.Ink);
            Ui.Text($"{def.Class.ToUpperInvariant()}   |   {SlotLabel(def.Slot)}", x + 25, y + 116, 11, Ui.Dim);

            var modePanel = new Rectangle(x + panelW - 200, y + 26, 170, 72);
            Ui.Panel(modePanel, U.Rgba(255, 186, 56, 24), U.Rgba(255, 186, 56, 92));
            Ui.TextCenter("FIRE CONTROL", (int)(modePanel.X + modePanel.Width * 0.5f), (int)modePanel.Y + 12, 10, Ui.Dim);
            Ui.TextCenter(weapon.FireModeLabel, (int)(modePanel.X + modePanel.Width * 0.5f), (int)modePanel.Y + 31, 21, Ui.Accent);
            // The dossier freezes simulation, so keep this informational rather
            // than advertising an input it cannot process while open.
            Ui.TextCenter(weapon.HasSelectableFireMode ? "CLOSE THEN [V]" : "FIXED", (int)(modePanel.X + modePanel.Width * 0.5f), (int)modePanel.Y + 57, 9, Ui.Faint);

            int statX = x + 24;
            int statY = y + 158;
            int statW = panelW - 48;
            DrawStat("DAMAGE", $"{def.Damage:0}", U.Clamp01(def.Damage / 120f), statX, statY, statW, Ui.Danger);
            DrawStat("REACH", $"{def.Range:0}", U.Clamp01(def.Range / 6000f), statX, statY + 44, statW, Ui.Accent);
            DrawStat("HANDLING", $"{def.MoveScale * 100f:0}%", U.Clamp01(def.MoveScale), statX, statY + 88, statW, Ui.Good);
            DrawStat("CONTROL", $"{MathF.Max(0f, 100f - def.VisualKick * 3f):0}%", U.Clamp01(1f - def.VisualKick / 34f), statX, statY + 132, statW, U.Rgb(130, 190, 245));

            int detailsY = statY + 194;
            Ui.Panel(new Rectangle(x + 24, detailsY, panelW - 48, 104), U.Rgba(3, 7, 11, 156), U.Rgba(255, 255, 255, 26));
            DrawDetail("AMMO", $"{weapon.Mag} / {weapon.Reserve}  |  {def.Magazine}-ROUND MAG", x + 40, detailsY + 16, panelW - 80);
            DrawDetail("RELOAD", $"{def.ReloadTime:0.00}s  |  {(weapon.ReloadStartedEmpty ? "EMPTY CYCLE" : "TACTICAL CYCLE")}", x + 40, detailsY + 40, panelW - 80);
            DrawDetail("PRESENTATION", "[X] INSPECT   |   SPRINT, FIRE, ADS OR RELOAD CANCELS", x + 40, detailsY + 64, panelW - 80);

            Ui.Text("Read-only readiness data. Weapon balance, models and the Gun Game pipeline remain unchanged.",
                x + 24, y + panelH - 44, 10, Ui.Faint);
            Ui.Text("G / ESC  CLOSE", x + 24, y + panelH - 25, 10, Ui.Dim);

            if (Raylib.IsKeyPressed(KeyboardKey.G) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                return ScreenAction.CloseWeaponDossier;

            return ScreenAction.None;
        }

        private static void DrawStat(string label, string value, float fraction, int x, int y, int width, Color color)
        {
            Ui.Text(label, x, y, 10, Ui.Dim);
            Ui.TextRight(value, x + width, y, 12, Ui.Ink);
            var bar = new Rectangle(x, y + 18, width, 8);
            Ui.Bar(bar, fraction, color, U.Rgba(0, 0, 0, 150));
            Raylib.DrawRectangleLinesEx(bar, 1f, U.Rgba(255, 255, 255, 28));
        }

        private static void DrawDetail(string label, string value, int x, int y, int width)
        {
            Ui.Text(label, x, y, 10, Ui.Accent);
            Ui.TextRight(value, x + width, y, 10, Ui.Dim);
        }

        private static string SlotLabel(WeaponSlot slot) => slot switch
        {
            WeaponSlot.Knife => "MELEE SLOT",
            WeaponSlot.Secondary => "SECONDARY SLOT",
            _ => "PRIMARY SLOT",
        };
    }

    // ==================================================== crosshair options

    /// <summary>
    /// Session-local reticle settings. The menu is deliberately presentation-only:
    /// the player can see the reticle update without touching aim or weapon state.
    /// </summary>
    public static class CrosshairOptions
    {
        private static int _selected;

        public static ScreenAction Draw(CrosshairSettings settings, int screenW, int screenH)
        {
            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(6, 8, 12, 88));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;

            int panelW = Math.Min(440, Math.Max(340, w - 48));
            const int x = 24;
            int y = Math.Max(36, (h - 590) / 2);
            const int rowH = 35;
            const int gap = 5;

            var panel = new Rectangle(x, y, panelW, 590);
            Ui.Panel(panel, U.Rgba(11, 13, 18, 232), U.Rgba(255, 255, 255, 46));
            Ui.Text("CROSSHAIR OPTIONS", x + 20, y + 18, 26, Ui.Ink);
            Ui.Text("Session-only visual settings", x + 21, y + 50, 11, Ui.Dim);

            int previewY = y + 78;
            var preview = new Rectangle(x + 18, previewY, panelW - 36, 102);
            Ui.Panel(preview, U.Rgba(3, 7, 11, 196), U.Rgba(255, 255, 255, 25));
            Ui.Text("LIVE PREVIEW", (int)preview.X + 12, (int)preview.Y + 10, 10, Ui.Faint);
            int previewCx = (int)(preview.X + preview.Width * 0.5f);
            int previewCy = (int)(preview.Y + preview.Height * 0.62f);
            Raylib.DrawCircleLines(previewCx, previewCy, 30f, U.Rgba(255, 255, 255, 16));
            Raylib.DrawLine(previewCx - 40, previewCy, previewCx + 40, previewCy, U.Rgba(255, 255, 255, 10));
            Raylib.DrawLine(previewCx, previewCy - 40, previewCx, previewCy + 40, U.Rgba(255, 255, 255, 10));
            CrosshairRenderer.Draw(settings, previewCx, previewCy, settings.DynamicGap ? 5f : 0f, 1.75f);

            int rowY = previewY + 118;
            Begin();
            Add("preset", "PRESET", settings.PresetLabel, x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("color", "COLOR", settings.ColorLabel, x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("dynamic", "DYNAMIC GAP", settings.DynamicGap ? "ON" : "OFF", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("dot", "CENTRE DOT", settings.CentreDot ? "ON" : "OFF", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("gap", "BASE GAP", $"{settings.BaseGap} px", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("length", "ARM LENGTH", $"{settings.ArmLength} px", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("thickness", "THICKNESS", $"{settings.Thickness} px", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("reset", "RESET DEFAULT", "", x + 18, rowY, panelW - 36, rowH);
            rowY += rowH + gap;
            Add("back", "BACK", "", x + 18, rowY, panelW - 36, rowH);

            Navigate(ref _selected);
            DrawRows(_selected);

            int axis = AxisPress();
            if (axis != 0)
            {
                switch (Rows[_selected].Id)
                {
                    case "preset": settings.CyclePreset(axis); break;
                    case "color": settings.CycleColor(axis); break;
                    case "dynamic": settings.ToggleDynamicGap(); break;
                    case "dot": settings.ToggleCentreDot(); break;
                    case "gap": settings.AdjustGap(axis); break;
                    case "length": settings.AdjustLength(axis); break;
                    case "thickness": settings.AdjustThickness(axis); break;
                }
            }

            if (Activated())
            {
                switch (Rows[_selected].Id)
                {
                    case "reset": settings.Reset(); break;
                    case "back": return ScreenAction.CloseCrosshairOptions;
                }
            }

            Ui.Text("UP/DOWN select    LEFT/RIGHT adjust    ENTER reset/back", x + 20, y + 556, 10, Ui.Faint);
            Ui.Text("The centre reticle remains aligned to the shot path.", x + 20, y + 571, 10, Ui.Dim);

            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.F2))
                return ScreenAction.CloseCrosshairOptions;

            return ScreenAction.None;
        }
    }

    // ================================================================ buy menu

    public sealed class BuyMenu
    {
        internal enum ProtectionPurchaseOutcome
        {
            Purchased,
            AlreadyEquipped,
            InsufficientFunds,
        }

        private readonly List<(string Name, int Price, string Kind)> _items = new();
        private readonly BuyMenuKeyLatch _closeLatch = new();
        private int _selected;
        private float _messageUntil;
        private string _message = "";
        private bool _messageGood;

        private void Rebuild()
        {
            _items.Clear();
            _items.Add(("Kevlar + Helmet", 1000, "gear"));
            foreach (WeaponDef def in WeaponRegistry.All)
            {
                if (def.Slot == WeaponSlot.Knife) continue;
                _items.Add((def.Name, def.Price, def.Slot == WeaponSlot.Primary ? "primary" : "secondary"));
            }
        }

        public void Refresh()
        {
            Rebuild();
            _selected = 0;
            _closeLatch.Reset();
        }

        /// <summary>
        /// Begins an interactive buy session. Unlike <see cref="Refresh"/>,
        /// this retains the B edge that opened the menu until it is released.
        /// </summary>
        public void Open()
        {
            Refresh();
            _closeLatch.ArmForOpen();
        }

        /// <summary>True when the menu should stay open.</summary>
        public bool Draw(Simulation sim, int screenW, int screenH)
        {
            Player p = sim.Player;
            if (_items.Count == 0) Rebuild();

            // The world remains visible enough to retain spatial context, while the
            // matte isolates this from the combat HUD instead of reading as a debug list.
            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(5, 8, 13, 178));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;
            Raylib.DrawRectangle(0, 0, w, 5, Ui.Accent);

            int outer = Math.Clamp(w / 28, 24, 56);
            int panelW = Math.Min(1_020, w - outer * 2);
            int panelH = Math.Min(500, h - outer * 2);
            int x = (w - panelW) / 2;
            int y = (h - panelH) / 2;
            const int headerH = 106;
            const int footerH = 54;

            // An intentionally restrained command-board treatment: a clear title,
            // live credits, then eight large click targets rather than tiny rows.
            Ui.Panel(new Rectangle(x, y, panelW, panelH), U.Rgba(13, 18, 26, 246), U.Rgba(255, 255, 255, 52), 1.5f);
            Raylib.DrawRectangle(x, y, panelW, 6, Ui.Accent);
            Raylib.DrawRectangle(x, y + headerH - 1, panelW, 1, U.Rgba(255, 255, 255, 28));

            Ui.Text("LOADOUT", x + 26, y + 25, 12, Ui.Accent);
            Ui.Text("BUY STATION", x + 25, y + 41, 29, Ui.Ink);
            Ui.Text("SELECT A WEAPON OR PROTECTION", x + 27, y + 76, 11, Ui.Dim);

            const int moneyW = 184;
            const int moneyH = 58;
            int moneyX = x + panelW - moneyW - 26;
            int moneyY = y + 24;
            Ui.Panel(new Rectangle(moneyX, moneyY, moneyW, moneyH), U.Rgba(255, 186, 56, 24), U.Rgba(255, 186, 56, 120));
            Ui.Text("AVAILABLE", moneyX + 14, moneyY + 10, 10, Ui.Dim);
            Ui.TextRight($"${p.Money:N0}", moneyX + moneyW - 14, moneyY + 25, 22, Ui.Accent);

            const int columns = 2;
            int contentX = x + 22;
            int contentY = y + headerH + 16;
            int contentW = panelW - 44;
            const int columnGap = 12;
            const int rowGap = 10;
            int rows = (_items.Count + columns - 1) / columns;
            int cardW = (contentW - columnGap) / columns;
            int usableHeight = panelH - headerH - footerH - 16;
            int cardH = Math.Max(44, (usableHeight - rowGap * Math.Max(0, rows - 1)) / Math.Max(1, rows));

            Begin();
            for (int i = 0; i < _items.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                int cardX = contentX + column * (cardW + columnGap);
                int cardY = contentY + row * (cardH + rowGap);
                Add(i.ToString(), _items[i].Name, "", cardX, cardY, cardW, cardH);
            }

            Navigate(ref _selected);
            DrawBuyCards(p);

            int chosen = -1;

            // Unlike list-style menus, this surface must never buy the selected
            // item when the player clicks decorative chrome, the message area or
            // the dimmed world. Mouse activation is valid only over a card.
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                chosen = _selected;
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Vector2 pointer = Ui.GetMousePosition();
                for (int i = 0; i < Rows.Count; i++)
                {
                    if (Hit(pointer, Rows[i].Rect))
                    {
                        chosen = i;
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _items.Count && i < 9; i++)
                {
                    var key = (KeyboardKey)((int)KeyboardKey.One + i);
                    if (Raylib.IsKeyPressed(key)) chosen = i;
                }
            }

            if (chosen >= 0 && chosen < _items.Count) Purchase(sim, chosen);

            if (sim.Time < _messageUntil)
            {
                Color c = _messageGood ? Ui.Good : Ui.Danger;
                Ui.TextCenter(_message.ToUpperInvariant(), w / 2, y + panelH - 42, 13, c);
            }

            Ui.Text("1-8 QUICK BUY", x + 26, y + panelH - 25, 11, Ui.Faint);
            Ui.TextRight("CLICK TO BUY     B / ESC CLOSE", x + panelW - 26, y + panelH - 25, 11, Ui.Dim);

            if (_closeLatch.ShouldClose(
                    Raylib.IsKeyDown(KeyboardKey.B),
                    Raylib.IsKeyPressed(KeyboardKey.B),
                    Raylib.IsKeyPressed(KeyboardKey.Escape)))
            {
                return false;
            }

            return true;
        }

        private void DrawBuyCards(Player player)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Row row = Rows[i];
                (string name, int price, string kind) = _items[i];
                bool selected = i == _selected;
                bool equipped = kind == "gear" && HasFullProtection(player);
                bool affordable = player.Money >= price;
                Color fill = equipped
                    ? (selected ? U.Rgba(118, 206, 124, 42) : U.Rgba(118, 206, 124, 18))
                    : selected ? U.Rgba(255, 186, 56, 42) : U.Rgba(255, 255, 255, 10);
                Color border = equipped
                    ? (selected ? Ui.Good : U.Rgba(118, 206, 124, 82))
                    : selected ? Ui.Accent : U.Rgba(255, 255, 255, 32);
                Color nameColor = selected ? Ui.Ink : U.Rgba(236, 236, 234, 206);
                Color priceColor = equipped ? Ui.Good : affordable ? (selected ? Ui.Accent : Ui.Ink) : Ui.Danger;

                Ui.Panel(row.Rect, fill, border, selected ? 1.5f : 1f);

                int keySize = Math.Min(34, Math.Max(26, (int)row.Rect.Height - 22));
                int keyX = (int)row.Rect.X + 14;
                int keyY = (int)row.Rect.Y + ((int)row.Rect.Height - keySize) / 2;
                Ui.Panel(new Rectangle(keyX, keyY, keySize, keySize),
                    selected ? U.Rgba(255, 186, 56, 70) : U.Rgba(255, 255, 255, 16),
                    selected ? U.Rgba(255, 186, 56, 180) : U.Rgba(255, 255, 255, 42));
                Ui.TextCenter((i + 1).ToString(), keyX + keySize / 2, keyY + 7, 15, selected ? Ui.Accent : Ui.Dim);

                int textX = keyX + keySize + 13;
                int nameY = (int)row.Rect.Y + 13;
                Ui.Text(name, textX, nameY, 17, nameColor);
                Ui.Text(KindLabel(kind), textX, nameY + 22, 10, selected ? Ui.Accent : Ui.Dim);
                Ui.TextRight($"${price:N0}", (int)(row.Rect.X + row.Rect.Width) - 16, nameY + 5, 17, priceColor);
                if (equipped)
                    Ui.TextRight("EQUIPPED", (int)(row.Rect.X + row.Rect.Width) - 16, nameY + 26, 9, Ui.Good);
                else if (!affordable)
                    Ui.TextRight("INSUFFICIENT", (int)(row.Rect.X + row.Rect.Width) - 16, nameY + 26, 9, Ui.Danger);
            }
        }

        /// <summary>True when the protected loadout cannot provide any further value.</summary>
        internal static bool HasFullProtection(Player player) =>
            player.Armor >= Cfg.MaxArmor - 0.01f && player.HasHelmet;

        /// <summary>
        /// Applies the one meaningful armour transaction in the current free-respawn
        /// deathmatch rules. Keeping this small branch independent of Raylib input
        /// makes it impossible for the UI to charge a player for no improvement.
        /// </summary>
        internal static ProtectionPurchaseOutcome TryPurchaseProtection(Player player, int price)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentOutOfRangeException.ThrowIfNegative(price);

            if (HasFullProtection(player)) return ProtectionPurchaseOutcome.AlreadyEquipped;
            if (player.Money < price) return ProtectionPurchaseOutcome.InsufficientFunds;

            player.Armor = Cfg.MaxArmor;
            player.HasHelmet = true;
            player.Money -= price;
            return ProtectionPurchaseOutcome.Purchased;
        }

        private static string KindLabel(string kind) => kind switch
        {
            "gear" => "PROTECTION",
            "secondary" => "SIDEARM",
            _ => "PRIMARY WEAPON",
        };

        private void Purchase(Simulation sim, int index)
        {
            Player p = sim.Player;
            (string name, int price, string kind) = _items[index];

            if (kind == "gear")
            {
                switch (TryPurchaseProtection(p, price))
                {
                    case ProtectionPurchaseOutcome.AlreadyEquipped:
                        Message("protection is already equipped", false, sim.Time);
                        break;
                    case ProtectionPurchaseOutcome.InsufficientFunds:
                        Message($"not enough money for {name}", false, sim.Time);
                        break;
                    case ProtectionPurchaseOutcome.Purchased:
                        Message("kevlar + helmet purchased", true, sim.Time);
                        break;
                }
                return;
            }

            if (p.Money < price)
            {
                Message($"not enough money for {name}", false, sim.Time);
                return;
            }

            WeaponDef def = WeaponRegistry.ByName(name);
            Weapon? existing = p.Weapons.Find(x => x.Def == def);
            if (existing is not null && existing.Mag >= def.Magazine && existing.Reserve >= def.Reserve)
            {
                Message($"{name} is already full", false, sim.Time);
                return;
            }

            if (existing is not null)
            {
                existing.Mag = def.Magazine;
                existing.Reserve = def.Reserve;
                existing.Reloading = false;
            }
            else
            {
                p.GiveWeapon(def);
            }

            p.Money -= price;
            p.SelectWeapon(def.Slot);
            Message($"{name} purchased", true, sim.Time);
            sim.Emit("buy", sim.Player.Position, 0.6f, 1f, SoundEventScope.Local);
        }

        private void Message(string text, bool good, float now)
        {
            _message = text;
            _messageGood = good;
            _messageUntil = now + 1.8f;
        }
    }

    // ================================================================ scoreboard

    public static void DrawScoreboard(Simulation sim, int screenW, int screenH)
    {
        using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
        DrawScoreboardContent(sim, Ui.DesignWidth, Ui.DesignHeight);
    }

    /// <summary>Draws the logical scoreboard while an owning screen holds the UI canvas.</summary>
    private static void DrawScoreboardContent(Simulation sim, int w, int h)
    {
        int panelW = Math.Min(760, w - 80);
        int x = (w - panelW) / 2;
        int y = (int)(h * 0.14f);
        int rows = sim.Actors.Count + 2;
        int panelH = 56 + rows * 26;

        Ui.Panel(new Rectangle(x, y, panelW, panelH), U.Rgba(8, 10, 14, 225), Ui.PanelBorder, 1.5f);

        Ui.Text($"{sim.Map.Name.ToUpperInvariant()}  -  DEATHMATCH", x + 20, y + 14, 20, Ui.Ink);
        Ui.TextRight($"FIRST TO {sim.KillsToWin}", x + panelW - 20, y + 18, 15, Ui.Accent);

        int hy = y + 48;
        Ui.Text("PLAYER", x + 20, hy, 10, Ui.Dim);
        Ui.TextRight("K", x + panelW - 210, hy, 10, Ui.Dim);
        Ui.TextRight("D", x + panelW - 150, hy, 10, Ui.Dim);
        Ui.TextRight("K/D", x + panelW - 90, hy, 10, Ui.Dim);
        Ui.TextRight("ACC", x + panelW - 20, hy, 10, Ui.Dim);

        int ry = hy + 18;
        int rank = 1;
        foreach (Actor actor in sim.Leaderboard())
        {
            bool me = actor.IsPlayer;
            if (me)
            {
                Ui.Panel(new Rectangle(x + 8, ry - 4, panelW - 16, 24), U.Rgba(255, 186, 56, 38), U.Rgba(0, 0, 0, 0), 0f);
            }

            Color nameColor = me ? Ui.Accent : (actor.Alive ? Ui.Ink : Ui.Dim);
            Ui.Text($"{rank}.  {actor.Name}{(me ? "  (you)" : "")}", x + 20, ry, 10, nameColor);

            sim.ShotsFired.TryGetValue(actor.Id, out int fired);
            sim.ShotsHit.TryGetValue(actor.Id, out int hits);
            float accuracy = fired > 0 ? hits * 100f / fired : 0f;
            float kd = actor.Deaths > 0 ? actor.Kills / (float)actor.Deaths : actor.Kills;

            Ui.TextRight($"{actor.Kills}", x + panelW - 210, ry, 10, Ui.Ink);
            Ui.TextRight($"{actor.Deaths}", x + panelW - 150, ry, 10, Ui.Dim);
            Ui.TextRight($"{kd:0.00}", x + panelW - 90, ry, 10, Ui.Dim);
            Ui.TextRight($"{accuracy:0}%", x + panelW - 20, ry, 10, Ui.Dim);

            int ping = 12 + Math.Abs(actor.Id * 17) % 46;
            Ui.Text($"{ping} ms", x + 20 + Ui.Width($"{rank}.  {actor.Name}", 10) + 100, ry, 10, Ui.Faint);

            ry += 26;
            rank++;
        }

        Ui.Text("TAB to close", x + 20, y + panelH - 26, 10, Ui.Faint);
    }

    // ================================================================ match over

    public static class MatchOver
    {
        private static int _selected;

        public static ScreenAction Draw(Simulation sim, MatchSettings settings, int screenW, int screenH)
        {
            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(6, 8, 12, 200));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;
            DrawScoreboardContent(sim, w, h);

            int panelW = 420;
            int x = (w - panelW) / 2;
            int y = (int)(h * 0.62f);
            const int rowH = 42;
            const int gap = 8;

            Ui.TextCenter("MATCH OVER", w / 2, y - 74, 40, Ui.Accent);
            Ui.TextCenter(sim.MatchResult, w / 2, y - 30, 15, Ui.Dim);
            Ui.TextCenter($"you finished rank {sim.PlayerRank()} of {sim.Actors.Count}",
                w / 2, y - 10, 15, Ui.Ink);

            Begin();
            Add("again", "PLAY AGAIN", "", x, y, panelW, rowH);
            y += rowH + gap;
            Add("menu", "BACK TO MENU", "", x, y, panelW, rowH);

            Navigate(ref _selected);
            DrawRows(_selected);

            if (Activated() || Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                return Rows[_selected].Id == "again" ? ScreenAction.Restart : ScreenAction.QuitToMenu;
            }

            return ScreenAction.None;
        }
    }
}
