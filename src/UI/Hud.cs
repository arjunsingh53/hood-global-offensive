using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using Raylib_cs;

namespace HoodGlobalOffensive.UI;

/// <summary>Draws everything the player sees while alive.</summary>
public static class Hud
{
    public static void Draw(Simulation sim, Camera3D cam, int w, int h)
    {
        DrawRadar(sim, w, h);
        DrawTopBar(sim, w, h);
        DrawKillFeed(sim, w, h);
        DrawDamageNumbers(sim, cam, w, h);
        DrawVitals(sim, w, h);
        DrawWeaponReadout(sim, w, h);

        if (sim.Player.Alive)
        {
            DrawCrosshair(sim, w, h);
            DrawHitmarker(sim, w, h);
            DrawDamageIndicators(sim, w, h);
            DrawScopeOverlay(sim, w, h);
        }
        else
        {
            DrawDeathOverlay(sim, w, h);
        }

        DrawHealthVignette(sim, w, h);
    }

    // ---------------------------------------------------------------- crosshair

    private static void DrawCrosshair(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        if (p.ZoomBlend > 0.75f) return;

        float spread = p.Current.SpreadFor(p.HorizontalSpeed, p.OnGround, p.Crouching);
        float pixelsPerRadian = h * 0.5f / MathF.Tan(p.CurrentFov * 0.5f * U.Deg2Rad);
        float gap = 4f + MathF.Tan(spread * U.Deg2Rad) * pixelsPerRadian;
        gap = MathF.Min(gap, h * 0.32f);

        int cx = w / 2, cy = h / 2;
        float len = 7f;
        Color c = U.Rgba(120, 255, 140, 225);

        Raylib.DrawLine((int)(cx - gap - len), cy, (int)(cx - gap), cy, c);
        Raylib.DrawLine((int)(cx + gap), cy, (int)(cx + gap + len), cy, c);
        Raylib.DrawLine(cx, (int)(cy - gap - len), cx, (int)(cy - gap), c);
        Raylib.DrawLine(cx, (int)(cy + gap), cx, (int)(cy + gap + len), c);
        Raylib.DrawPixel(cx, cy, c);
    }

    private static void DrawHitmarker(Simulation sim, int w, int h)
    {
        if (sim.Time > sim.HitmarkerUntil) return;

        int cx = w / 2, cy = h / 2;
        Color c = sim.LastHitWasHeadshot
            ? U.Rgba(255, 180, 60, 235)
            : U.Rgba(245, 245, 245, 220);
        int inner = 6, outer = 14;

        Raylib.DrawLine(cx - outer, cy - outer, cx - inner, cy - inner, c);
        Raylib.DrawLine(cx + inner, cy + inner, cx + outer, cy + outer, c);
        Raylib.DrawLine(cx - outer, cy + outer, cx - inner, cy + inner, c);
        Raylib.DrawLine(cx + inner, cy - inner, cx + outer, cy - outer, c);
    }

    private static void DrawDamageIndicators(Simulation sim, int w, int h)
    {
        if (sim.DamageFlash <= 0.01f) return;

        float relative = U.Clamp(sim.DamageFlash, 0f, 1f);
        int cx = w / 2, cy = h / 2;
        float radius = h * 0.16f;
        float angle = (sim.DamageFlashDirection - sim.Player.Yaw) * U.Deg2Rad;

        // Draw an arc segment pointing at the damage source.
        int segments = 14;
        float span = 0.55f;
        for (int i = 0; i < segments; i++)
        {
            float t0 = angle - span + (2f * span) * (i / (float)segments);
            float t1 = angle - span + (2f * span) * ((i + 1) / (float)segments);
            Vector2 p0 = new(cx + MathF.Sin(t0) * radius, cy - MathF.Cos(t0) * radius);
            Vector2 p1 = new(cx + MathF.Sin(t1) * radius, cy - MathF.Cos(t1) * radius);
            Vector2 inner0 = new(cx + MathF.Sin(t0) * (radius - 9f), cy - MathF.Cos(t0) * (radius - 9f));
            Vector2 inner1 = new(cx + MathF.Sin(t1) * (radius - 9f), cy - MathF.Cos(t1) * (radius - 9f));
            Color c = U.Rgba(235, 60, 50, (int)(210 * relative));
            Raylib.DrawTriangle(p0, p1, inner1, c);
            Raylib.DrawTriangle(p0, inner1, inner0, c);
        }
    }

    private static void DrawScopeOverlay(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        if (p.ZoomBlend < 0.25f) return;

        float t = U.Clamp01((p.ZoomBlend - 0.25f) / 0.75f);
        int cx = w / 2, cy = h / 2;
        float radius = h * 0.5f;
        int alpha = (int)(230 * t);

        // Black the corners to leave a circular scope.
        Raylib.DrawRectangle(0, 0, w, (int)(cy - radius), U.Rgba(0, 0, 0, alpha));
        Raylib.DrawRectangle(0, (int)(cy + radius), w, h, U.Rgba(0, 0, 0, alpha));
        for (int y = (int)(cy - radius); y < cy + radius; y++)
        {
            float dy = y - cy;
            float dx = MathF.Sqrt(MathF.Max(0f, radius * radius - dy * dy));
            int edge = (int)(cx - dx);
            if (edge > 0) Raylib.DrawRectangle(0, y, edge, 1, U.Rgba(0, 0, 0, alpha));
            int rightEdge = (int)(cx + dx);
            if (rightEdge < w) Raylib.DrawRectangle(rightEdge, y, w - rightEdge, 1, U.Rgba(0, 0, 0, alpha));
        }

        Color line = U.Rgba(20, 20, 20, (int)(220 * t));
        Raylib.DrawLine(0, cy, w, cy, line);
        Raylib.DrawLine(cx, 0, cx, h, line);
        Raylib.DrawCircleLines(cx, cy, radius * 0.62f, U.Rgba(30, 30, 30, (int)(140 * t)));
        Raylib.DrawCircle(cx, cy, 2f, U.Rgba(240, 240, 240, (int)(230 * t)));
    }

    // ---------------------------------------------------------------- vitals

    private static void DrawVitals(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        int baseY = h - 84;
        float health = U.Clamp01(p.Health / Cfg.MaxHealth);
        bool low = p.Health <= 30f;

        Color healthColor = low
            ? U.Rgb(226, 78, 62)
            : health > 0.6f ? U.Rgb(214, 214, 210) : U.Rgb(232, 176, 72);

        var healthBar = new Rectangle(30, baseY + 42, 220, 7);
        Ui.Bar(healthBar, health, healthColor, U.Rgba(0, 0, 0, 150));
        Raylib.DrawRectangleLinesEx(healthBar, 1f, U.Rgba(255, 255, 255, 55));

        Ui.ShadowText($"{MathF.Ceiling(p.Health):0}", 28, baseY, 40, healthColor);

        var armorBar = new Rectangle(30 + 74, baseY + 42, 146, 7);
        Ui.Bar(armorBar, U.Clamp01(p.Armor / Cfg.MaxArmor), U.Rgb(120, 186, 232), U.Rgba(0, 0, 0, 150));
        Raylib.DrawRectangleLinesEx(armorBar, 1f, U.Rgba(255, 255, 255, 55));

        Ui.ShadowText($"{MathF.Ceiling(p.Armor):0}", 30 + 76, baseY + 14, 20,
            p.Armor > 0f ? U.Rgb(150, 200, 240) : Ui.Faint);
        Ui.ShadowText(p.HasHelmet ? "HELMET" : "NO HELMET", 30 + 152, baseY + 2, 10, Ui.Dim);
    }

    private static void DrawWeaponReadout(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        Weapon weapon = p.Current;
        int baseY = h - 84;

        string ammo = $"{weapon.Mag} / {weapon.Reserve}";
        Color ammoColor = weapon.Mag == 0 ? Ui.Danger : Ui.Ink;
        Ui.ShadowText(ammo, w - 30 - Ui.Width(ammo, 30), baseY, 30, ammoColor);
        Ui.TextRight(weapon.Def.Name.ToUpperInvariant(), w - 30, baseY - 16, 10, Ui.Accent);

        // Weapon slot strip
        int y = baseY + 40;
        for (int i = 0; i < 3; i++)
        {
            WeaponSlot slot = (WeaponSlot)i;
            Weapon? owned = p.Weapons.Find(x => x.Def.Slot == slot);
            var box = new Rectangle(w - 30 - (3 - i) * 66, y, 58, 22);
            bool active = owned is not null && p.Current.Def == owned.Def;

            Ui.Panel(box,
                active ? U.Rgba(255, 186, 56, 60) : U.Rgba(10, 12, 16, 140),
                active ? Ui.Accent : Ui.PanelBorder);

            string label = owned is null ? "---" : ShortName(owned.Def.Name);
            Ui.TextCenter(label, (int)box.X + 29, (int)box.Y + 7, 10, active ? Ui.Ink : Ui.Dim);
            Ui.Text($"{i + 1}", (int)box.X + 3, (int)box.Y + 2, 10, Ui.Faint);
        }

        if (weapon.Reloading)
        {
            float duration = MathF.Max(0.001f, weapon.Def.ReloadTime);
            float t = U.Clamp01(1f - (weapon.ReloadEndTime - sim.Time) / duration);
            var bar = new Rectangle(w - 200, baseY + 22, 170, 5);
            Ui.Bar(bar, t, Ui.Accent, U.Rgba(0, 0, 0, 160));
            Ui.TextRight("RELOADING", w - 30, baseY + 22 - 14, 10, Ui.Accent);
        }
        else if (weapon.Mag == 0)
        {
            Ui.TextRight("PRESS R TO RELOAD", w - 30, baseY + 22 - 14, 10,
                (int)(sim.Time * 3f) % 2 == 0 ? Ui.Danger : Ui.Dim);
        }
    }

    private static void DrawTopBar(Simulation sim, int w, int h)
    {
        string money = $"${sim.Player.Money}";
        Ui.ShadowText(money, 30, 22, 20, Ui.Good);
        Ui.Text("DEATHMATCH", 30, 46, 10, Ui.Dim);

        int minutes = (int)(sim.TimeLeft / 60f);
        int seconds = (int)(sim.TimeLeft % 60f);
        bool urgent = sim.TimeLeft < 30f;
        Color timerColor = urgent ? Ui.Danger : Ui.Ink;

        var panel = new Rectangle(w / 2f - 86f, 14f, 172f, 46f);
        Ui.Panel(panel, Ui.PanelBg, Ui.PanelBorder);
        Ui.TextCenter($"{minutes:00}:{seconds:00}", w / 2, 20, 20, timerColor);
        Ui.TextCenter($"FIRST TO {sim.KillsToWin}", w / 2, 42, 10, Ui.Dim);

        Actor? leader = sim.Leaderboard().FirstOrDefault();
        if (leader is not null)
        {
            string label = $"{leader.Name}  {leader.Kills}";
            Ui.TextRight(label, w / 2 - 100, 30, 10, Ui.Dim);
        }
    }

    private static void DrawKillFeed(Simulation sim, int w, int h)
    {
        int y = 20;
        foreach (KillFeedEntry entry in Enumerable.Reverse(sim.KillFeed))
        {
            float age = sim.Time - entry.SpawnedAt;
            float alpha = age > 5.5f ? U.Clamp01((6.5f - age) / 1f) : 1f;
            int a = (int)(255 * alpha);

            string weapon = entry.Headshot ? $"* {entry.Weapon}" : entry.Weapon;
            string text = $"{entry.Killer}   [{weapon}]   {entry.Victim}";

            Color killerColor = entry.KillerIsPlayer ? Ui.Accent : U.Rgba(240, 240, 240, a);
            Color victimColor = entry.VictimIsPlayer ? Ui.Danger : U.Rgba(200, 200, 200, a);
            if (entry.KillerIsPlayer) killerColor = U.Rgba(killerColor.R, killerColor.G, killerColor.B, a);
            if (entry.VictimIsPlayer) victimColor = U.Rgba(victimColor.R, victimColor.G, victimColor.B, a);

            int killerW = Ui.Width(entry.Killer, 10);
            int weaponW = Ui.Width($"[{weapon}]", 10);
            int total = killerW + weaponW + 34 + Ui.Width(entry.Victim, 10);
            int x = w - 24 - total;

            var bg = new Rectangle(x - 8, y - 4, total + 16, 20);
            Ui.Panel(bg, U.Rgba(8, 10, 14, (int)(150 * alpha)), U.Rgba(255, 255, 255, 0), 0f);

            Ui.Text(entry.Killer, x, y, 10, killerColor);
            Ui.Text($"[{weapon}]", x + killerW + 8, y, 10, entry.Headshot ? Ui.Accent : U.Rgba(220, 190, 140, a));
            Ui.Text(entry.Victim, x + killerW + weaponW + 26, y, 10, victimColor);

            y += 22;
        }
    }

    private static void DrawDamageNumbers(Simulation sim, Camera3D cam, int w, int h)
    {
        foreach (FloatingDamage d in sim.DamageNumbers)
        {
            Vector2 screen = Raylib.GetWorldToScreen(d.Position, cam);
            if (screen.X < -40 || screen.Y < -40 || screen.X > w + 40 || screen.Y > h + 40) continue;

            float t = U.Clamp01(d.Life / d.MaxLife);
            int alpha = (int)(255 * MathF.Min(1f, t * 2.2f));
            int size = d.Headshot ? 20 : 14;
            Color c = d.Headshot
                ? U.Rgba(255, 176, 48, alpha)
                : U.Rgba(248, 248, 240, alpha);

            string text = d.Headshot ? $"{d.Amount:0}!" : $"{d.Amount:0}";
            Ui.TextCenter(text, (int)screen.X, (int)screen.Y, size, U.Rgba(0, 0, 0, alpha / 2));
            Ui.TextCenter(text, (int)screen.X, (int)screen.Y - 1, size, c);
        }
    }

    // ---------------------------------------------------------------- radar

    private static void DrawRadar(Simulation sim, int w, int h)
    {
        const int size = 168;
        int x0 = w - size - 24;
        int y0 = h - size - 130;

        var rect = new Rectangle(x0, y0, size, size);
        Ui.Panel(rect, U.Rgba(8, 10, 14, 175), Ui.PanelBorder);

        float scale = size / (Cfg.MapHalf * 2f + 80f);
        Vector2 ToMap(Vector3 p) => new(
            x0 + size * 0.5f + p.X * scale,
            y0 + size * 0.5f + p.Z * scale);

        // Static geometry so the layout is readable.
        foreach (var brush in sim.Map.Brushes)
        {
            if (brush.Kind == SurfaceKind.Floor) continue;
            Vector2 a = ToMap(brush.Box.Min);
            Vector2 b = ToMap(brush.Box.Max);
            var r = new Rectangle(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(b.X - a.X), MathF.Abs(b.Y - a.Y));
            if (r.Width < 0.7f) r.Width = 0.7f;
            if (r.Height < 0.7f) r.Height = 0.7f;
            Raylib.DrawRectangleRec(r, U.Rgba(150, 150, 145, 52));
        }

        Vector3 eye = sim.Player.EyePosition;

        foreach (Actor actor in sim.Actors)
        {
            if (actor.IsPlayer || !actor.Alive) continue;
            Vector3 delta = actor.Position - sim.Player.Position;
            float distance = U.FlatLength(delta);
            if (distance > 1500f) continue;

            bool visible = sim.Map.LineOfSight(eye, actor.EyePosition)
                && distance < 1100f;
            float fade = U.Clamp01(1f - distance / 1500f);
            Color c = visible
                ? U.Rgba(236, 74, 62, (int)(120 + 135 * fade))
                : U.Rgba(236, 74, 62, (int)(70 * fade));

            Vector2 p = ToMap(actor.Position);
            Raylib.DrawCircleV(p, visible ? 3.4f : 2.6f, c);
            if (visible) Raylib.DrawCircleLines((int)p.X, (int)p.Y, 6f, U.Rgba(236, 74, 62, 90));
        }

        // Player arrow
        Vector2 me = ToMap(sim.Player.Position);
        float yaw = sim.Player.Yaw * U.Deg2Rad;
        Vector2 forward = new(-MathF.Sin(yaw), -MathF.Cos(yaw));
        Vector2 right = new(MathF.Cos(yaw), -MathF.Sin(yaw));
        Raylib.DrawTriangle(
            me + forward * 7f,
            me - forward * 3f + right * 4.5f,
            me - forward * 3f - right * 4.5f,
            U.Rgba(120, 255, 140, 235));

        Ui.Text("RADAR", x0 + 8, y0 + size - 14, 10, Ui.Faint);
    }

    // ---------------------------------------------------------------- feedback

    private static void DrawHealthVignette(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        float intensity = 0f;

        if (p.Alive && p.Health < 45f) intensity = (45f - p.Health) / 45f;
        if (!p.Alive) intensity = 0.85f;
        if (sim.DamageFlash > 0.02f) intensity = MathF.Max(intensity, sim.DamageFlash * 0.7f);

        if (intensity <= 0.01f) return;

        float pulse = 0.72f + 0.28f * MathF.Sin(sim.Time * 5.2f);
        int alpha = (int)(U.Clamp01(intensity) * 120 * pulse);
        int band = (int)(h * 0.22f);

        for (int i = 0; i < 26; i++)
        {
            float t = i / 26f;
            int a = (int)(alpha * (1f - t));
            Raylib.DrawRectangle(0, (int)(t * band), w, 2, U.Rgba(150, 20, 16, a));
            Raylib.DrawRectangle(0, h - (int)(t * band) - 2, w, 2, U.Rgba(150, 20, 16, a));
            Raylib.DrawRectangle((int)(t * band), 0, 2, h, U.Rgba(150, 20, 16, a));
            Raylib.DrawRectangle(w - (int)(t * band) - 2, 0, 2, h, U.Rgba(150, 20, 16, a));
        }
    }

    private static void DrawDeathOverlay(Simulation sim, int w, int h)
    {
        Raylib.DrawRectangle(0, 0, w, h, U.Rgba(90, 10, 8, 60));
        Raylib.DrawRectangle(0, (int)(h * 0.34f), w, 118, U.Rgba(8, 10, 14, 180));

        Ui.TextCenter("ELIMINATED", w / 2, (int)(h * 0.34f) + 14, 30, Ui.Danger);

        string? killerName = null;
        foreach (KillFeedEntry entry in Enumerable.Reverse(sim.KillFeed))
        {
            if (entry.VictimIsPlayer)
            {
                killerName = entry.Killer;
                break;
            }
        }

        if (killerName is not null)
        {
            Ui.TextCenter($"killed by {killerName}", w / 2, (int)(h * 0.34f) + 52, 10, Ui.Dim);
        }

        float remaining = MathF.Max(0f, sim.Player.RespawnAt - sim.Time);
        Ui.TextCenter($"RESPAWNING IN {remaining:0.0}", w / 2, (int)(h * 0.34f) + 78, 15, Ui.Accent);
        Ui.TextCenter("press TAB for scores", w / 2, (int)(h * 0.34f) + 100, 10, Ui.Faint);
    }

    private static string ShortName(string name) => name switch
    {
        "Desert Eagle" => "DEAGLE",
        "Glock-18" => "GLOCK",
        "MP5-SD" => "MP5",
        _ => name.ToUpperInvariant(),
    };
}
