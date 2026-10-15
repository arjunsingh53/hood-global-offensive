using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

public enum ScreenAction
{
    None,
    StartMatch,
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

        Vector2 mouse = Raylib.GetMousePosition();
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

        public static ScreenAction Draw(MatchSettings settings, int w, int h)
        {
            Raylib.ClearBackground(U.Rgb(16, 18, 24));

            // Backdrop framing
            Raylib.DrawRectangle(0, 0, w, 6, U.Rgb(255, 186, 56));
            Ui.Text(Cfg.GameName, 60, 54, 52, Ui.Ink);
            Ui.Text($"early access v{Cfg.Version}  -  map: {Cfg.MapName}  -  a Counter-Strike style deathmatch in C#", 64, 118, 15, Ui.Dim);

            int panelW = 460;
            int x = 64;
            int y = 200;
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
                }
            }

            bool clicked = Activated();
            if (clicked)
            {
                switch (Rows[_selected].Id)
                {
                    case "play": return ScreenAction.StartMatch;
                    case "quit": return ScreenAction.QuitApp;
                }
            }

            Ui.Text("WASD to move    MOUSE to aim    CLICK to fire    R reload    1/2/3 weapons",
                64, h - 96, 10, Ui.Faint);
            Ui.Text("SHIFT walk    CTRL crouch    SPACE jump    B buy menu    TAB scores    ESC pause",
                64, h - 80, 10, Ui.Faint);
            Ui.Text("Tip: crouch and tap-fire for accuracy, and drag down to fight AK recoil.",
                64, h - 56, 10, Ui.Dim);

            return ScreenAction.None;
        }
    }

    // ================================================================ pause

    public static class Pause
    {
        private static int _selected;

        public static ScreenAction Draw(MatchSettings settings, Simulation sim, int w, int h)
        {
            Raylib.DrawRectangle(0, 0, w, h, U.Rgba(6, 8, 12, 175));

            const int panelW = 420;
            int x = (w - panelW) / 2;
            int y = (int)(h * 0.3f);
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

            if (Activated())
            {
                switch (Rows[_selected].Id)
                {
                    case "resume": return ScreenAction.Resume;
                    case "restart": return ScreenAction.Restart;
                    case "menu": return ScreenAction.QuitToMenu;
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return ScreenAction.Resume;
            return ScreenAction.None;
        }
    }

    // ================================================================ buy menu

    public sealed class BuyMenu
    {
        private readonly List<(string Name, int Price, string Kind)> _items = new();
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
        }

        /// <summary>True when the menu should stay open.</summary>
        public bool Draw(Simulation sim, int w, int h)
        {
            Player p = sim.Player;
            if (_items.Count == 0) Rebuild();

            Raylib.DrawRectangle(0, 0, w, h, U.Rgba(6, 8, 12, 190));

            const int panelW = 560;
            int x = (w - panelW) / 2;
            int y = (int)(h * 0.16f);

            Ui.TextCenter("BUY MENU", w / 2, y - 60, 40, Ui.Ink);
            Ui.TextCenter($"${p.Money}   -   press the number key or click to buy   -   B or ESC to close",
                w / 2, y - 20, 15, Ui.Dim);

            Begin();
            const int rowH = 34;
            const int gap = 4;
            int rowY = y;
            for (int i = 0; i < _items.Count; i++)
            {
                (string name, int price, string kind) = _items[i];
                string value = $"${price}";
                if (kind == "primary") value = $"PRIMARY   {value}";
                else if (kind == "secondary") value = $"PISTOL   {value}";
                else value = $"GEAR   {value}";
                Add(i.ToString(), $"{i + 1}. {name}", value, x, rowY, panelW, rowH);
                rowY += rowH + gap;
            }

            Navigate(ref _selected);
            DrawRows(_selected);

            bool clicked = Activated();
            int chosen = -1;

            if (clicked) chosen = _selected;
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
                Ui.TextCenter(_message, w / 2, rowY + 16, 15, c);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.B)) return false;
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return false;
            return true;
        }

        private void Purchase(Simulation sim, int index)
        {
            Player p = sim.Player;
            (string name, int price, string kind) = _items[index];

            if (p.Money < price)
            {
                Message($"not enough money for {name}", false, sim.Time);
                return;
            }

            if (kind == "gear")
            {
                p.Armor = Cfg.MaxArmor;
                p.HasHelmet = true;
                p.Money -= price;
                Message("kevlar + helmet purchased", true, sim.Time);
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
            sim.Emit("buy", sim.Player.Position, 0.6f, 1f);
        }

        private void Message(string text, bool good, float now)
        {
            _message = text;
            _messageGood = good;
            _messageUntil = now + 1.8f;
        }
    }

    // ================================================================ scoreboard

    public static void DrawScoreboard(Simulation sim, int w, int h)
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

        public static ScreenAction Draw(Simulation sim, MatchSettings settings, int w, int h)
        {
            Raylib.DrawRectangle(0, 0, w, h, U.Rgba(6, 8, 12, 200));
            DrawScoreboard(sim, w, h);

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
