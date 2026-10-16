using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

public enum ScreenAction
{
    None,
    StartMatch,
    OpenCrosshairOptions,
    CloseCrosshairOptions,
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
    public string MapName = MapRegistry.Names[0];
    /// <summary>Slot-by-slot gun names (1-3). The knife always lives on 4.</summary>
    public readonly string[] Loadout = { "Rifle", "Shotgun", "Pistol" };
    public readonly CrosshairSettings Crosshair = new();
    public readonly CameraFeedbackSettings CameraFeedback = new();
}

/// <summary>
/// Preserves the opening E press while the upgrade overlay takes ownership of
/// the frame. The menu must see E released before E can buy again, so the
/// interact key that opened the cabinet cannot instantly purchase a stat.
/// Escape is intentionally never delayed.
/// </summary>
public sealed class UpgradeMenuKeyLatch
{
    private bool _awaitingRelease;

    public void ArmForOpen() => _awaitingRelease = true;

    public void Reset() => _awaitingRelease = false;

    /// <summary>True when E should count as a buy press this frame.</summary>
    public bool ShouldAcceptE(bool eDown, bool ePressed)
    {
        if (!_awaitingRelease) return ePressed;
        if (!eDown) _awaitingRelease = false;
        return false;
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

            // Keep the command surface quiet enough for the map to remain
            // visible, but give the text an intentional left-hand anchor.
            Raylib.DrawRectangle(0, 0, 760, h, U.Rgba(8, 11, 16, 214));
            Raylib.DrawRectangle(0, 0, w, 6, Ui.Accent);
            Raylib.DrawRectangle(64, 145, 460, 1, U.Rgba(255, 255, 255, 34));
            Ui.Text(Cfg.GameName, 60, 54, 52, Ui.Ink);
            Ui.Text($"EARLY ACCESS v{Cfg.Version}   -   OFFLINE BOT DEATHMATCH   -   GRAPPLE ARENA",
                64, 118, 13, Ui.Dim);

            const int panelW = 460;
            const int x = 64;

            // Twelve rows must clear the three footer hint lines at the bottom
            // of the 720-unit design canvas: a 39-unit pitch from 156 puts the
            // last row's border at 619, above the first hint line at 624.
            int y = 156;
            const int rowH = 34;
            const int gap = 5;

            Begin();
            Add("play", "START DEATHMATCH", "", x, y, panelW, rowH);
            y += rowH + gap;
            Add("gun1", "SLOT 1", settings.Loadout[0], x, y, panelW, rowH);
            y += rowH + gap;
            Add("gun2", "SLOT 2", settings.Loadout[1], x, y, panelW, rowH);
            y += rowH + gap;
            Add("gun3", "SLOT 3", settings.Loadout[2], x, y, panelW, rowH);
            y += rowH + gap;
            Add("map", "MAP", settings.MapName.ToUpperInvariant(), x, y, panelW, rowH);
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
                    case "gun1": CycleLoadoutSlot(settings, 0, axis); break;
                    case "gun2": CycleLoadoutSlot(settings, 1, axis); break;
                    case "gun3": CycleLoadoutSlot(settings, 2, axis); break;
                    case "map": CycleMap(settings, axis); break;
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

            DrawMatchBrief(settings);
            Ui.Text("WASD move    MOUSE aim    LMB shoot    RMB aim / alt fire    R reload    1-4 weapons",
                64, h - 96, 10, Ui.Faint);
            Ui.Text("Q / E left & right grapple    V knife    F flashlight    SPACE jump (HOLD to bunny-hop)    TAB scores",
                64, h - 80, 10, Ui.Faint);
            Ui.Text("Tip: hold jump and stay off WASD to keep speed. Grapple far and high, then slingshot.",
                64, h - 56, 10, Ui.Dim);

            return ScreenAction.None;
        }

        private static void CycleLoadoutSlot(MatchSettings settings, int slot, int axis)
        {
            WeaponDef current = WeaponRegistry.ByName(settings.Loadout[slot]);
            settings.Loadout[slot] = WeaponRegistry.CycleLoadout(current, axis).Name;
        }

        private static void CycleMap(MatchSettings settings, int axis)
        {
            int index = Array.IndexOf(MapRegistry.Names, settings.MapName);
            if (index < 0) index = 0;
            int next = ((index + axis) % MapRegistry.Names.Length + MapRegistry.Names.Length)
                % MapRegistry.Names.Length;
            settings.MapName = MapRegistry.Names[next];
        }

        private static void DrawMatchBrief(MatchSettings settings)
        {
            const int x = 770;
            const int y = 202;
            const int w = 420;
            const int h = 232;
            Ui.Panel(new Rectangle(x, y, w, h), U.Rgba(8, 11, 16, 196), U.Rgba(255, 255, 255, 42), 1.25f);
            Raylib.DrawRectangle(x, y, 5, h, Ui.Accent);
            Ui.Text("LOCAL OPERATION", x + 26, y + 24, 11, Ui.Accent);
            Ui.Text(settings.MapName.ToUpperInvariant(), x + 24, y + 48, 27, Ui.Ink);
            Ui.Text("OFFLINE FREE-FOR-ALL", x + 25, y + 82, 11, Ui.Dim);
            Raylib.DrawRectangle(x + 24, y + 108, w - 48, 1, U.Rgba(255, 255, 255, 28));

            Ui.Text("MODE", x + 24, y + 130, 10, Ui.Faint);
            Ui.Text("DEATHMATCH", x + 24, y + 148, 16, Ui.Ink);
            Ui.Text("ECONOMY", x + 174, y + 130, 10, Ui.Faint);
            Ui.Text("KILL UPGRADES", x + 174, y + 148, 16, Ui.Ink);
            Ui.Text("STATUS", x + 306, y + 130, 10, Ui.Faint);
            Ui.Text("LOCAL", x + 306, y + 148, 16, Ui.Good);

            Ui.Text("Kills bank upgrade points. Spend them at a cabinet to grow health, damage, speed or jump.",
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
            Ui.TextCenter(
                $"{sim.Player.Kills} kills   {sim.Player.Deaths} deaths   {sim.Player.KillPoints} upgrade points",
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
                    case "restart": return ScreenAction.Restart;
                    case "menu": return ScreenAction.QuitToMenu;
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return ScreenAction.Resume;
            return ScreenAction.None;
        }
    }

    // ========================================================== upgrade cabinet

    /// <summary>
    /// The kill-point upgrade cabinet. Four stats, three levels each: the same
    /// economy as the reference, where a match's maximum build costs twelve
    /// purchases and dying costs the highest stat one level.
    /// </summary>
    public sealed class UpgradeMenu
    {
        private readonly UpgradeMenuKeyLatch _latch = new();
        private int _selected;
        private float _messageUntil;
        private string _message = "";
        private bool _messageGood;

        public void Open() => _latch.ArmForOpen();

        /// <summary>True when the menu should stay open.</summary>
        public bool Draw(Simulation sim, int screenW, int screenH)
        {
            Player p = sim.Player;
            float now = sim.Time;

            Raylib.DrawRectangle(0, 0, screenW, screenH, U.Rgba(5, 8, 13, 160));
            using Ui.CanvasScope canvas = Ui.BeginCanvas(screenW, screenH);
            const int w = Ui.DesignWidth;
            const int h = Ui.DesignHeight;
            Raylib.DrawRectangle(0, 0, w, 5, Ui.Accent);

            int panelW = 560;
            int panelH = 430;
            int x = (w - panelW) / 2;
            int y = (h - panelH) / 2;
            const int headerH = 96;
            const int rowH = 56;
            const int gap = 8;

            Ui.Panel(new Rectangle(x, y, panelW, panelH), U.Rgba(13, 18, 26, 246), U.Rgba(255, 255, 255, 52), 1.5f);
            Raylib.DrawRectangle(x, y, panelW, 6, Ui.Accent);
            Raylib.DrawRectangle(x, y + headerH - 1, panelW, 1, U.Rgba(255, 255, 255, 28));

            Ui.Text("STATION", x + 26, y + 22, 12, Ui.Accent);
            Ui.Text("UPGRADE CABINET", x + 25, y + 38, 27, Ui.Ink);

            const int ptsW = 168;
            const int ptsH = 52;
            int ptsX = x + panelW - ptsW - 24;
            int ptsY = y + 22;
            Ui.Panel(new Rectangle(ptsX, ptsY, ptsW, ptsH), U.Rgba(255, 186, 56, 24), U.Rgba(255, 186, 56, 120));
            Ui.Text("KILL POINTS", ptsX + 14, ptsY + 8, 10, Ui.Dim);
            Ui.TextRight($"{p.KillPoints}", ptsX + ptsW - 14, ptsY + 22, 21, Ui.Accent);

            int rowY = y + headerH + 12;
            Begin();
            foreach (UpgradeStat stat in UpgradeStats)
            {
                Add(StatId(stat), StatLabel(stat), "", x + 22, rowY, panelW - 44, rowH);
                rowY += rowH + gap;
            }
            Add("close", "CLOSE", "", x + 22, rowY, panelW - 44, 42);

            Navigate(ref _selected);
            DrawUpgradeRows(sim, panelW);

            bool buyPressed = Raylib.IsKeyPressed(KeyboardKey.Space)
                || _latch.ShouldAcceptE(Raylib.IsKeyDown(KeyboardKey.E), Raylib.IsKeyPressed(KeyboardKey.E));

            bool clicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
            int chosen = -1;
            if (buyPressed) chosen = _selected;
            else if (clicked)
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

            bool stillOpen = true;
            if (chosen >= 0 && chosen < UpgradeStats.Length)
            {
                UpgradeStat stat = UpgradeStats[chosen];
                if (sim.TryBuyUpgrade(p, stat))
                {
                    Message($"{StatLabel(stat).ToLowerInvariant()} upgraded", true, now);
                }
                else
                {
                    bool maxed = Simulation.UpgradeLevel(p, stat) >= Cfg.UpgradeMaxLevel;
                    Message(maxed ? $"{StatLabel(stat).ToLowerInvariant()} is maxed" : "not enough kill points",
                        false, now);
                }
            }
            else if (chosen == Rows.Count - 1)
            {
                stillOpen = false;
            }

            if (now < _messageUntil)
            {
                Color c = _messageGood ? Ui.Good : Ui.Danger;
                Ui.TextCenter(_message.ToUpperInvariant(), w / 2, y + panelH - 78, 13, c);
            }

            Ui.Text("W/S SELECT   SPACE/E BUY   ESC CLOSE",
                x + 26, y + panelH - 40, 11, Ui.Faint);
            Ui.TextRight("MAX 3/STAT - DEATH COSTS 1", x + panelW - 26, y + panelH - 40, 11, Ui.Dim);

            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) stillOpen = false;
            return stillOpen;
        }

        private static readonly UpgradeStat[] UpgradeStats =
        {
            UpgradeStat.Health, UpgradeStat.Damage, UpgradeStat.Speed, UpgradeStat.Jump,
        };

        private static string StatId(UpgradeStat stat) => stat.ToString().ToLowerInvariant();

        private static string StatLabel(UpgradeStat stat) => Simulation.StatName(stat);

        private static string StatEffect(UpgradeStat stat) => stat switch
        {
            UpgradeStat.Health => $"+{Cfg.UpgradeHealthPerLevel:0} MAX HEALTH",
            UpgradeStat.Damage => $"+{Cfg.UpgradeDamagePerLevel * 100f:0}% WEAPON DAMAGE",
            UpgradeStat.Speed => $"+{Cfg.UpgradeSpeedPerLevel * 100f:0}% MOVE SPEED",
            _ => $"+{Cfg.UpgradeJumpPerLevel * 100f:0}% JUMP HEIGHT",
        };

        private void DrawUpgradeRows(Simulation sim, int panelW)
        {
            Player p = sim.Player;
            for (int i = 0; i < UpgradeStats.Length; i++)
            {
                UpgradeStat stat = UpgradeStats[i];
                Row row = Rows[i];
                bool selected = i == _selected;
                int level = Simulation.UpgradeLevel(p, stat);
                bool maxed = level >= Cfg.UpgradeMaxLevel;
                bool affordable = p.KillPoints >= Cfg.UpgradePointCost && !maxed;

                Color fill = selected ? U.Rgba(255, 186, 56, 42) : U.Rgba(255, 255, 255, 10);
                Color border = maxed ? U.Rgba(118, 206, 124, 94)
                    : selected ? Ui.Accent : U.Rgba(255, 255, 255, 32);
                Ui.Panel(row.Rect, fill, border, selected ? 1.5f : 1f);

                int textX = (int)row.Rect.X + 18;
                int nameY = (int)row.Rect.Y + 10;
                Ui.Text(StatLabel(stat), textX, nameY, 17, selected ? Ui.Ink : U.Rgba(236, 236, 234, 206));
                Ui.Text(StatEffect(stat), textX, nameY + 24, 10, selected ? Ui.Accent : Ui.Dim);

                // Three level pips, filled left to right.
                int pipW = 34;
                int pipsX = (int)(row.Rect.X + row.Rect.Width) - 18 - pipW * Cfg.UpgradeMaxLevel - 8 * (Cfg.UpgradeMaxLevel - 1);
                int pipsY = (int)row.Rect.Y + 18;
                for (int pip = 0; pip < Cfg.UpgradeMaxLevel; pip++)
                {
                    bool filled = pip < level;
                    var rect = new Rectangle(pipsX + pip * (pipW + 8), pipsY, pipW, 10);
                    Raylib.DrawRectangleRec(rect, filled ? Ui.Accent : U.Rgba(255, 255, 255, 22));
                    Raylib.DrawRectangleLinesEx(rect, 1f, U.Rgba(255, 255, 255, 40));
                }

                string status = maxed ? "MAX" : affordable ? $"COST {Cfg.UpgradePointCost}" : "NEEDS POINTS";
                Color statusColor = maxed ? Ui.Good : affordable ? Ui.Accent : Ui.Danger;
                Ui.TextRight(status, (int)(row.Rect.X + row.Rect.Width) - 18, (int)row.Rect.Y + 34, 10, statusColor);
            }
        }

        private void Message(string text, bool good, float now)
        {
            _message = text;
            _messageGood = good;
            _messageUntil = now + 1.6f;
        }
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
        Ui.TextRight("PTS", x + panelW - 20, hy, 10, Ui.Dim);

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

            float kd = actor.Deaths > 0 ? actor.Kills / (float)actor.Deaths : actor.Kills;

            Ui.TextRight($"{actor.Kills}", x + panelW - 210, ry, 10, Ui.Ink);
            Ui.TextRight($"{actor.Deaths}", x + panelW - 150, ry, 10, Ui.Dim);
            Ui.TextRight($"{kd:0.00}", x + panelW - 90, ry, 10, Ui.Dim);
            Ui.TextRight($"{actor.KillPoints}", x + panelW - 20, ry, 10, Ui.Accent);

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
