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
    // Stable, edge-only marks give the critical-condition layer material without
    // importing a lens texture or polluting the protected aim area at screen centre.
    private readonly record struct TraumaMark(float X, float Y, float Radius);
    private static readonly TraumaMark[] TraumaMarks =
    {
        new(0.055f, 0.115f, 0.013f), new(0.118f, 0.060f, 0.008f),
        new(0.215f, 0.145f, 0.010f), new(0.932f, 0.095f, 0.012f),
        new(0.840f, 0.055f, 0.007f), new(0.962f, 0.225f, 0.010f),
        new(0.075f, 0.805f, 0.011f), new(0.188f, 0.930f, 0.009f),
        new(0.880f, 0.890f, 0.013f), new(0.955f, 0.765f, 0.008f),
    };

    public static void Draw(Simulation sim, Camera3D cam, int w, int h, CrosshairSettings crosshair)
    {
        // The scope tunnel owns the world frame before any HUD layer draws:
        // peripheral anchors then fade over black instead of ghosting through a
        // translucent mask that was painted on top of them.
        DrawScopeMask(sim, w, h);

        // Keep the reticle/impact layer completely independent of the corner
        // presentation.  A real calibrated sight or scope owns the protected
        // centre of the screen, while the player can still recover essential
        // state from deliberately quiet peripheral anchors.
        float peripheralOpacity = PeripheralHudOpacity(sim.Player.Current.Def, sim.Player.ZoomBlend);
        DrawRadar(sim, w, h, peripheralOpacity);
        DrawTopBar(sim, w, h, peripheralOpacity);
        DrawCompassTape(sim, w, h, peripheralOpacity);
        DrawKillFeed(sim, w, h, peripheralOpacity);
        DrawDamageNumbers(sim, cam, w, h);
        DrawVitals(sim, w, h, peripheralOpacity);
        DrawWeaponReadout(sim, w, h, peripheralOpacity);

        if (sim.Player.Alive)
        {
            DrawCrosshair(sim, cam, w, h, crosshair);
            DrawHitmarker(sim, w, h);
            DrawHookStatus(sim, w, h, peripheralOpacity);
            DrawStationPrompt(sim, w, h, peripheralOpacity);
            DrawDamageIndicators(sim, w, h);
            DrawScopeReticle(sim, w, h);
        }
        else
        {
            DrawDeathOverlay(sim, w, h);
        }

        DrawHealthVignette(sim, w, h);
    }

    /// <summary>
    /// The local HUD fades only for a sight picture which has been authored for
    /// ADS (or a real scope). A weapon with an ordinary narrowed FOV keeps the
    /// normal HUD and reticle so it never loses usability while its model is
    /// being calibrated.
    /// </summary>
    internal static float PeripheralHudOpacity(WeaponDef def, float zoomBlend)
    {
        if (!def.HasScope && !def.HasCalibratedIronSights) return 1f;

        float t = U.Clamp01((zoomBlend - 0.16f) / 0.68f);
        return U.Lerp(1f, 0.38f, t);
    }

    private static Color Fade(Color color, float opacity) =>
        U.Rgba(color.R, color.G, color.B, (int)(color.A * U.Clamp01(opacity)));

    // The combat interface uses deliberately simple, procedural shapes instead
    // of imported UI art.  That keeps the look coherent at every resolution and
    // gives the match layer a single visual language: a dark tactical scrim,
    // a bright information edge, and small amber state accents.
    private static void DrawArenaPanel(Rectangle rect, float opacity, Color? accent = null)
    {
        Color border = Fade(U.Rgba(232, 240, 244, 74), opacity);
        Raylib.DrawRectangleRec(rect, Fade(U.Rgba(5, 8, 13, 206), opacity));
        Raylib.DrawRectangleLinesEx(rect, 1f, border);

        float corner = MathF.Min(12f, MathF.Min(rect.Width, rect.Height) * 0.24f);
        Color cornerColor = accent is Color value
            ? Fade(value, opacity)
            : Fade(U.Rgba(232, 240, 244, 130), opacity);
        DrawCornerBracket(rect.X, rect.Y, 1f, 1f, corner, cornerColor);
        DrawCornerBracket(rect.X + rect.Width, rect.Y, -1f, 1f, corner, cornerColor);
        DrawCornerBracket(rect.X, rect.Y + rect.Height, 1f, -1f, corner, cornerColor);
        DrawCornerBracket(rect.X + rect.Width, rect.Y + rect.Height, -1f, -1f, corner, cornerColor);
    }

    private static void DrawCornerBracket(float x, float y, float horizontal, float vertical, float length, Color color)
    {
        Raylib.DrawLineEx(new Vector2(x, y), new Vector2(x + horizontal * length, y), 1.5f, color);
        Raylib.DrawLineEx(new Vector2(x, y), new Vector2(x, y + vertical * length), 1.5f, color);
    }

    private static void DrawArenaText(string text, int x, int y, int size, Color color, float opacity,
        bool centered = false)
    {
        int drawX = centered ? x - Raylib.MeasureText(text, size) / 2 : x;
        Color outline = Fade(U.Rgba(0, 0, 0, 215), opacity);
        Raylib.DrawText(text, drawX - 1, y, size, outline);
        Raylib.DrawText(text, drawX + 1, y, size, outline);
        Raylib.DrawText(text, drawX, y - 1, size, outline);
        Raylib.DrawText(text, drawX, y + 1, size, outline);
        Raylib.DrawText(text, drawX, y, size, Fade(color, opacity));
    }

    private static void DrawSegmentedBar(Rectangle bar, float amount, int segments, Color fill, Color empty, float opacity)
    {
        float gap = 2f;
        float segmentWidth = (bar.Width - (segments - 1) * gap) / segments;
        float active = U.Clamp01(amount) * segments;
        for (int i = 0; i < segments; i++)
        {
            float intensity = U.Clamp01(active - i);
            Color segment = intensity > 0.01f
                ? U.Rgba(fill.R, fill.G, fill.B, (int)(fill.A * (0.48f + 0.52f * intensity)))
                : empty;
            Raylib.DrawRectangleRec(new Rectangle(bar.X + i * (segmentWidth + gap), bar.Y, segmentWidth, bar.Height),
                Fade(segment, opacity));
        }
    }

    // ---------------------------------------------------------------- crosshair

    private static void DrawCrosshair(Simulation sim, Camera3D cam, int w, int h, CrosshairSettings settings)
    {
        Player p = sim.Player;
        if (IsCrosshairSuppressed(p.Current.Def, p.ZoomBlend)) return;

        float spread = p.Current.SpreadFor(p.HorizontalSpeed, p.OnGround, p.Crouching, p.ZoomBlend);
        // The render camera can receive a tiny presentation-only FOV response.
        // Scale the visual gap with that exact projection FOV so the custom
        // reticle remains an honest pixel representation of weapon spread.
        float pixelsPerRadian = h * 0.5f / MathF.Tan(cam.FovY * 0.5f * U.Deg2Rad);
        int cx = w / 2, cy = h / 2;
        float spreadPixels = MathF.Tan(spread * U.Deg2Rad) * pixelsPerRadian;
        CrosshairRenderer.Draw(settings, cx, cy, spreadPixels, maxGap: h * 0.32f);
    }

    /// <summary>
    /// The reticle only disappears behind a real scope or a model with a
    /// calibrated rear/front iron-sight picture. This keeps fallback weapons
    /// playable while their ordinary aim view narrows the FOV.
    /// </summary>
    internal static bool IsCrosshairSuppressed(WeaponDef def, float zoomBlend) =>
        zoomBlend > 0.75f && (def.HasScope || def.HasCalibratedIronSights);

    private static void DrawHitmarker(Simulation sim, int w, int h)
    {
        if (sim.Time > sim.HitmarkerUntil) return;

        int cx = w / 2, cy = h / 2;
        // Simulation retains an event end time rather than allocating a UI
        // animation object. The compact attack/release envelope therefore stays
        // deterministic with the gameplay event: it expands on confirmation,
        // then settles and fades instead of holding a static X.
        const float hitmarkerLife = 0.12f;
        float elapsed = U.Clamp01(1f - (sim.HitmarkerUntil - sim.Time) / hitmarkerLife);
        float attack = U.Clamp01(elapsed / 0.62f);
        float release = U.Clamp01((elapsed - 0.62f) / 0.38f);
        float scale = U.Lerp(0.80f, 1.18f, attack) * U.Lerp(1f, 0.86f, release);
        float fade = MathF.Pow(1f - elapsed, 0.68f);

        Color c = sim.LastHitWasHeadshot
            ? U.Rgba(255, 86, 70, (int)(245 * fade))
            : U.Rgba(245, 245, 245, (int)(230 * fade));
        float inner = 6f * scale;
        float outer = 14f * scale;
        float lineWidth = MathF.Max(1f, 1.35f * scale);

        Raylib.DrawLineEx(new Vector2(cx - outer, cy - outer), new Vector2(cx - inner, cy - inner), lineWidth, c);
        Raylib.DrawLineEx(new Vector2(cx + inner, cy + inner), new Vector2(cx + outer, cy + outer), lineWidth, c);
        Raylib.DrawLineEx(new Vector2(cx - outer, cy + outer), new Vector2(cx - inner, cy + inner), lineWidth, c);
        Raylib.DrawLineEx(new Vector2(cx + inner, cy - outer), new Vector2(cx + outer, cy - inner), lineWidth, c);

        if (sim.LastHitWasKill)
        {
            int killAlpha = (int)(230 * fade);
            Ui.TextCenter("ELIMINATED", cx, cy + (int)(26f * scale), 11, U.Rgba(255, 70, 56, killAlpha));
        }
        else if (sim.LastHitWasHeadshot)
        {
            int labelAlpha = (int)(190 * fade * U.Clamp01((elapsed - 0.14f) / 0.28f));
            Ui.TextCenter("HEADSHOT", cx, cy + (int)(24f * scale), 10, U.Rgba(255, 104, 82, labelAlpha));
        }
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

    /// <summary>
    /// Quiet dual-hook readout under the reticle: Q and E squares light amber
    /// while that hook is attached to a surface.
    /// </summary>
    private static void DrawHookStatus(Simulation sim, int w, int h, float opacity)
    {
        Player p = sim.Player;
        int cx = w / 2, cy = h / 2;
        DrawHookSquare(cx - 40, cy + 34, "Q", p.HookLeft.Attached, opacity);
        DrawHookSquare(cx + 24, cy + 34, "E", p.HookRight.Attached, opacity);
    }

    private static void DrawHookSquare(int x, int y, string key, bool attached, float opacity)
    {
        Color border = attached ? Ui.Accent : U.Rgba(236, 240, 244, 92);
        Color fill = attached ? U.Rgba(255, 186, 56, 52) : U.Rgba(5, 8, 13, 130);
        Raylib.DrawRectangle(x, y, 16, 16, Fade(fill, opacity));
        Raylib.DrawRectangleLines(x, y, 16, 16, Fade(border, opacity));
        Ui.TextCenter(key, x + 8, y + 3, 10, Fade(attached ? Ui.Accent : Ui.Faint, opacity));
    }

    private static void DrawStationPrompt(Simulation sim, int w, int h, float opacity)
    {
        if (sim.NearbyStation(sim.Player) is null) return;

        const string prompt = "[ E ]  UPGRADE CABINET";
        int y = (int)(h * 0.66f);
        Ui.TextCenter(prompt, w / 2, y, 15, Fade(Ui.Ink, opacity));
        Ui.TextCenter($"{sim.Player.KillPoints} KILL POINTS   -   1 POINT PER LEVEL",
            w / 2, y + 20, 10, Fade(Ui.Accent, opacity));
    }

    /// <summary>Blend of the magnified optic, 0 while stowed and 1 at full zoom.</summary>
    private static float ScopeBlend(Player p) => U.Clamp01((p.ZoomBlend - 0.25f) / 0.75f);

    /// <summary>
    /// Blacks the world outside the scope circle. Drawn before the peripheral
    /// HUD so those anchors fade over the tunnel instead of ghosting through it.
    /// </summary>
    private static void DrawScopeMask(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        if (!p.Alive || !p.Current.Def.HasScope) return;
        float t = ScopeBlend(p);
        if (t <= 0f) return;

        int cx = w / 2, cy = h / 2;
        float radius = h * 0.5f;
        int alpha = (int)(232 * t);

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
    }

    /// <summary>
    /// The duplex sight picture: a soft tube shadow inside the rim, heavy posts
    /// on all four cardinals, a fine crosshair with an open centre, a mil-dot
    /// drop ladder and the exact centre pixel the hitscan ray resolves through.
    /// </summary>
    private static void DrawScopeReticle(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        if (!p.Current.Def.HasScope) return;
        float t = ScopeBlend(p);
        if (t <= 0f) return;

        int cx = w / 2, cy = h / 2;
        float radius = h * 0.5f;
        float scale = radius / 540f;
        var centre = new Vector2(cx, cy);

        // Stacked translucent rings deepen the edge so the glass reads as a
        // tube rather than a paper cut-out, then a crisp dark lip closes it.
        for (int i = 0; i < 8; i++)
        {
            float inner = radius * (0.84f + i * 0.02f);
            Raylib.DrawRing(centre, inner, radius, 0f, 360f, 96, U.Rgba(4, 5, 7, (int)(13 * t)));
        }
        Raylib.DrawRing(centre, radius - 1.5f * scale, radius, 0f, 360f, 96, U.Rgba(6, 7, 9, (int)(150 * t)));
        Raylib.DrawCircleLines(cx, cy, radius * 0.995f, U.Rgba(236, 226, 205, (int)(26 * t)));

        Color post = U.Rgba(10, 11, 13, (int)(225 * t));
        Color fine = U.Rgba(14, 15, 18, (int)(210 * t));
        float gap = 9f * scale;
        float thinStart = radius * 0.44f;
        float postStart = radius * 0.97f;

        // Heavy duplex posts on the horizontals and verticals.
        Raylib.DrawLineEx(new Vector2(cx - postStart, cy), new Vector2(cx - thinStart, cy), 6.5f * scale, post);
        Raylib.DrawLineEx(new Vector2(cx + postStart, cy), new Vector2(cx + thinStart, cy), 6.5f * scale, post);
        Raylib.DrawLineEx(new Vector2(cx, cy - postStart), new Vector2(cx, cy - thinStart), 6.5f * scale, post);
        Raylib.DrawLineEx(new Vector2(cx, cy + postStart), new Vector2(cx, cy + thinStart), 6.5f * scale, post);

        // Fine crosshair, deliberately open at the centre.
        Raylib.DrawLineEx(new Vector2(cx - thinStart, cy), new Vector2(cx - gap, cy), 1.5f, fine);
        Raylib.DrawLineEx(new Vector2(cx + gap, cy), new Vector2(cx + thinStart, cy), 1.5f, fine);
        Raylib.DrawLineEx(new Vector2(cx, cy - thinStart), new Vector2(cx, cy - gap), 1.5f, fine);
        Raylib.DrawLineEx(new Vector2(cx, cy + gap), new Vector2(cx, cy + thinStart), 1.5f, fine);

        // Mil-dot drop ladder under the centre.
        float step = radius * 0.085f;
        for (int i = 1; i <= 4; i++)
        {
            float y = cy + gap + step * i;
            if (y > cy + thinStart) break;
            Raylib.DrawCircleV(new Vector2(cx, y), 2.2f * MathF.Max(1f, scale),
                U.Rgba(14, 15, 18, (int)(195 * t)));
        }

        // Exact centre pixel: the hitscan ray always resolves through it.
        Raylib.DrawRectangle(cx, cy, 1, 1, U.Rgba(240, 240, 240, (int)(235 * t)));
    }

    // ---------------------------------------------------------------- vitals

    private static void DrawVitals(Simulation sim, int w, int h, float opacity)
    {
        _ = w;
        Player p = sim.Player;
        float health = U.Clamp01(p.Health / p.MaxHealth);
        bool low = p.Health < Cfg.LowHealthTraumaThreshold;
        Color healthColor = low
            ? U.Rgb(246, 78, 66)
            : health > 0.6f ? U.Rgb(238, 242, 244) : U.Rgb(255, 188, 70);

        // The biological status card is intentionally large enough to read in a
        // glance, but it is anchored below the active combat area rather than
        // occupying it.  The left red rail acts as a non-text health cue.
        const int margin = 24;
        const int cardW = 250;
        const int cardH = 88;
        int cardY = h - margin - cardH;
        var card = new Rectangle(margin, cardY, cardW, cardH);
        DrawArenaPanel(card, opacity, healthColor);
        Raylib.DrawRectangle(margin, cardY, 5, cardH, Fade(healthColor, opacity));

        DrawArenaText($"{MathF.Ceiling(p.Health):0}", margin + 16, cardY + 23, 42, healthColor, opacity);
        Ui.Text("HEALTH", margin + 78, cardY + 16, 10, Fade(Ui.Dim, opacity));
        Ui.Text(low ? "CRITICAL" : "COMBAT READY", margin + 78, cardY + 30, 10,
            Fade(low ? healthColor : Ui.Good, opacity));

        var healthBar = new Rectangle(margin + 78, cardY + 46, 148, 8);
        DrawSegmentedBar(healthBar, health, 10, healthColor, U.Rgba(255, 255, 255, 26), opacity);
        Raylib.DrawRectangleLinesEx(healthBar, 1f, Fade(U.Rgba(255, 255, 255, 54), opacity));

        // The upgrade build lives here as four compact pips. Each pip is one
        // purchased level of that stat, so the card doubles as the build readout.
        Ui.Text("BUILD", margin + 78, cardY + 61, 9, Fade(Ui.Faint, opacity));
        DrawUpgradePip(margin + 116, cardY + 62, "HP", p.UpgradeHealth, opacity);
        DrawUpgradePip(margin + 147, cardY + 62, "DMG", p.UpgradeDamage, opacity);
        DrawUpgradePip(margin + 181, cardY + 62, "SPD", p.UpgradeSpeed, opacity);
        DrawUpgradePip(margin + 213, cardY + 62, "JMP", p.UpgradeJump, opacity);
    }

    private static void DrawUpgradePip(int x, int y, string label, int level, float opacity)
    {
        Color color = level > 0 ? Ui.Accent : U.Rgba(255, 255, 255, 30);
        Raylib.DrawRectangle(x, y, 8, 8, Fade(color, opacity));
        Raylib.DrawRectangleLines(x, y, 8, 8, Fade(U.Rgba(255, 255, 255, 46), opacity));
        Ui.Text(level > 0 ? level.ToString() : "", x + 2, y + 11, 9, Fade(Ui.Dim, opacity));
        Ui.Text(label, x - 1, y - 9, 8, Fade(Ui.Faint, opacity));
    }

    private static void DrawWeaponReadout(Simulation sim, int w, int h, float opacity)
    {
        Player p = sim.Player;
        Weapon weapon = p.Current;
        const int margin = 24;
        const int cardW = 260;
        const int cardH = 88;
        int cardX = w - margin - cardW;
        int cardY = h - margin - cardH;
        var card = new Rectangle(cardX, cardY, cardW, cardH);
        Color ammoColor = weapon.Def.UsesAmmo && weapon.Mag == 0 ? Ui.Danger : Ui.Ink;
        DrawArenaPanel(card, opacity, ammoColor);

        Ui.Text(weapon.Def.Name.ToUpperInvariant(), cardX + 15, cardY + 14, 12, Fade(Ui.Accent, opacity));
        Ui.Text(weaponState(weapon), cardX + 15, cardY + 30, 10, Fade(Ui.Dim, opacity));

        // The magazine uses a much larger numeral than reserve ammunition. It
        // stays stable and legible while firing without turning the whole corner
        // into a menu-like panel. Reserve is unlimited, exactly like the source.
        string mag = weapon.Def.UsesAmmo ? weapon.Mag.ToString("00") : "--";
        DrawArenaText(mag, cardX + 144, cardY + 33, 38, ammoColor, opacity);
        Ui.Text("/", cardX + 197, cardY + 55, 16, Fade(Ui.Faint, opacity));
        DrawArenaText("INF", cardX + 213, cardY + 50, 20, U.Rgba(238, 242, 244, 210), opacity);
        Ui.Text("MAG", cardX + 147, cardY + 74, 9, Fade(Ui.Faint, opacity));
        Ui.Text("RES", cardX + 218, cardY + 74, 9, Fade(Ui.Faint, opacity));

        float magFill = weapon.Def.Magazine <= 0 ? 0f : weapon.Mag / (float)weapon.Def.Magazine;
        var magazineBar = new Rectangle(cardX + 15, cardY + 51, 111, 8);
        DrawSegmentedBar(magazineBar, magFill, 8, ammoColor, U.Rgba(255, 255, 255, 24), opacity);
        Raylib.DrawRectangleLinesEx(magazineBar, 1f, Fade(U.Rgba(255, 255, 255, 48), opacity));

        // Four loadout slots: primary, secondary, tertiary, then the knife.
        int stripY = cardY - 26;
        for (int slot = 1; slot <= 4; slot++)
        {
            Weapon? owned = p.Weapons.Find(candidate => candidate.Slot == slot);
            var box = new Rectangle(w - margin - (4 - slot) * 70, stripY, 62, 21);
            bool active = owned is not null && ReferenceEquals(owned, p.Current);

            DrawArenaPanel(box, opacity, active ? Ui.Accent : null);
            if (active)
                Raylib.DrawRectangle((int)box.X, (int)box.Y, 3, (int)box.Height, Fade(Ui.Accent, opacity));

            string label = owned is null ? "---" : ShortName(owned.Def.Name);
            Ui.TextCenter(label, (int)box.X + 31, (int)box.Y + 6, 10, Fade(active ? Ui.Ink : Ui.Dim, opacity));
            Ui.Text($"{slot}", (int)box.X + 3, (int)box.Y + 2, 10, Fade(Ui.Faint, opacity));
        }

        if (weapon.Reloading && weapon.Def.UsesAmmo)
        {
            float duration = MathF.Max(0.001f, weapon.Def.ReloadTime);
            float t = U.Clamp01(1f - (weapon.ReloadEndTime - sim.Time) / duration);
            var bar = new Rectangle(cardX + 15, cardY + 72, 111, 5);
            Ui.Bar(bar, t, Fade(Ui.Accent, opacity), Fade(U.Rgba(0, 0, 0, 160), opacity));
            Ui.TextRight(weapon.ReloadStartedEmpty ? "EMPTY RELOAD" : "TACTICAL RELOAD",
                cardX + 126, cardY + 63, 9, Fade(Ui.Accent, opacity));
        }
        else if (weapon.Def.UsesAmmo && weapon.Mag == 0)
        {
            Ui.TextRight("PRESS R", cardX + 126, cardY + 63, 9,
                Fade((int)(sim.Time * 3f) % 2 == 0 ? Ui.Danger : Ui.Dim, opacity));
        }

        static string weaponState(Weapon w) => w.Def switch
        {
            { Melee: true } => "MELEE - V",
            { HasSecondary: true } => "ALT - DOUBLE BLAST",
            { HasScope: true } => "SCOPE - RMB",
            { HasIronSights: true } => "IRON SIGHTS - RMB",
            _ => "PRIMARY",
        };
    }

    private static void DrawTopBar(Simulation sim, int w, int h, float opacity)
    {
        _ = h;
        int minutes = (int)(sim.TimeLeft / 60f);
        int seconds = (int)(sim.TimeLeft % 60f);
        bool urgent = sim.TimeLeft < 30f;
        Color timerColor = urgent ? Ui.Danger : U.Rgb(242, 246, 248);

        var panel = new Rectangle(w / 2f - 110f, 14f, 220f, 58f);
        DrawArenaPanel(panel, opacity, urgent ? Ui.Danger : Ui.Accent);
        Ui.TextCenter("MATCH CLOCK", w / 2, 20, 9, Fade(Ui.Faint, opacity));
        DrawArenaText($"{minutes:00}:{seconds:00}", w / 2, 29, 28, timerColor, opacity, centered: true);

        Actor? leader = sim.Leaderboard().FirstOrDefault();
        int leadKills = leader?.Kills ?? 0;
        string score = $"YOU {sim.Player.Kills:00}   -   LEAD {leadKills:00}   -   FT {sim.KillsToWin}";
        Ui.TextCenter(score, w / 2, 57, 9, Fade(Ui.Dim, opacity));
    }

    /// <summary>
    /// A fixed screen-space heading tape. The world camera and the radar both
    /// read Player.Yaw, but the tape keeps conventional compass headings so
    /// spoken callouts remain stable while the radar terrain rotates.
    /// </summary>
    private static void DrawCompassTape(Simulation sim, int w, int h, float opacity)
    {
        _ = h;
        int tapeWidth = Math.Min(420, Math.Max(280, w / 3));
        const int tapeHeight = 21;
        int cx = w / 2;
        const int y = 80;
        int left = cx - tapeWidth / 2;
        var strip = new Rectangle(left, y, tapeWidth, tapeHeight);

        DrawArenaPanel(strip, opacity);

        // Game yaw increases to the left. Flip it to report conventional
        // clockwise compass degrees: N=000, E=090, S=180, W=270.
        float heading = WrapHeading(-sim.Player.Yaw);
        const float pixelsPerDegree = 1.05f;
        float halfDegrees = tapeWidth * 0.5f / pixelsPerDegree;
        int firstTick = (int)MathF.Floor((heading - halfDegrees) / 15f) * 15;
        int lastTick = (int)MathF.Ceiling((heading + halfDegrees) / 15f) * 15;

        for (int degree = firstTick; degree <= lastTick; degree += 15)
        {
            float delta = SignedHeadingDelta(heading, degree);
            float x = cx + delta * pixelsPerDegree;
            if (x < left - 2f || x > left + tapeWidth + 2f) continue;

            int normalized = ((degree % 360) + 360) % 360;
            bool labelled = normalized % 45 == 0;
            float tickHeight = labelled ? 7f : 3.5f;
            Color tickColor = Fade(labelled ? U.Rgba(236, 240, 244, 188) : U.Rgba(236, 240, 244, 92), opacity);
            Raylib.DrawLineEx(new Vector2(x, y + 2f), new Vector2(x, y + 2f + tickHeight), 1f, tickColor);

            if (labelled)
            {
                string label = CompassLabel(normalized);
                Ui.TextCenter(label, (int)x, y + 10, 9, Fade(Ui.Dim, opacity));
            }
        }

        // The centre caret identifies the exact current bearing while the
        // heading number remains readable even when a cardinal label scrolls.
        Raylib.DrawTriangle(new Vector2(cx, y), new Vector2(cx - 4f, y + 5f), new Vector2(cx + 4f, y + 5f),
            Fade(Ui.Accent, opacity));
        int headingDisplay = ((int)MathF.Round(heading)) % 360;
        Ui.TextCenter($"{headingDisplay:000}", cx, y + tapeHeight + 2, 9, Fade(Ui.Faint, opacity));
    }

    private static float WrapHeading(float degrees)
    {
        float wrapped = degrees % 360f;
        return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    private static float SignedHeadingDelta(float current, float candidate)
    {
        float delta = (candidate - current) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return delta;
    }

    private static string CompassLabel(int degrees) => degrees switch
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

    private static void DrawKillFeed(Simulation sim, int w, int h, float opacity)
    {
        _ = h;
        const int rowHeight = 27;
        int y = 20;
        foreach (KillFeedEntry entry in Enumerable.Reverse(sim.KillFeed))
        {
            float age = sim.Time - entry.SpawnedAt;
            float alpha = age > 5.5f ? U.Clamp01((6.5f - age) / 1f) : 1f;
            int a = (int)(255 * alpha);

            string weapon = entry.Headshot ? "HEAD" : entry.Weapon.ToUpperInvariant();

            Color killerBase = entry.KillerIsPlayer
                ? U.Rgba(Ui.Accent.R, Ui.Accent.G, Ui.Accent.B, a)
                : U.Rgba(240, 240, 240, a);
            Color victimBase = entry.VictimIsPlayer
                ? U.Rgba(Ui.Danger.R, Ui.Danger.G, Ui.Danger.B, a)
                : U.Rgba(200, 200, 200, a);
            Color killerColor = Fade(killerBase, opacity);
            Color victimColor = Fade(victimBase, opacity);

            int killerW = Ui.Width(entry.Killer, 12);
            int weaponW = Ui.Width(weapon, 9);
            int victimW = Ui.Width(entry.Victim, 12);
            int total = killerW + weaponW + victimW + 62;
            int x = w - 24 - total;

            var bg = new Rectangle(x - 9, y - 5, total + 18, rowHeight - 2);
            DrawArenaPanel(bg, opacity * alpha, entry.KillerIsPlayer ? Ui.Accent : U.Rgba(236, 240, 244, 120));

            Ui.Text(entry.Killer, x, y, 12, killerColor);
            int weaponX = x + killerW + 12;
            Ui.Text(">", weaponX, y + 1, 12, Fade(U.Rgba(236, 240, 244, a), opacity));
            Ui.Text(weapon, weaponX + 13, y + 3, 9,
                Fade(entry.Headshot ? Ui.Danger : Ui.Accent, opacity * alpha));
            Ui.Text(entry.Victim, weaponX + 18 + weaponW, y, 12, victimColor);

            y += rowHeight;
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

    private static void DrawRadar(Simulation sim, int w, int h, float opacity)
    {
        // The scanner is player-centred and rotates the world under a stable
        // up-facing player marker. It has a proper top-left home so it does not
        // compete with the high-value ammunition card in the opposite corner.
        // All geometry is clipped to the true circular boundary rather than a
        // hidden square, keeping the scan readable while the player is moving.
        const float radius = 67f;
        const float worldRange = 720f;
        _ = h;
        Vector2 center = new(24f + radius, 22f + radius);
        float scale = radius / worldRange;

        var radarBounds = new Rectangle(center.X - radius - 5f, center.Y - radius - 5f,
            (radius + 5f) * 2f, (radius + 5f) * 2f);
        DrawArenaPanel(radarBounds, opacity, U.Rgba(112, 205, 228, 180));
        Raylib.DrawCircleV(center, radius + 2f, Fade(U.Rgba(0, 0, 0, 168), opacity));
        Raylib.DrawCircleV(center, radius, Fade(U.Rgba(9, 16, 23, 226), opacity));
        DrawRadarGrid(center, radius, opacity);

        Vector2 ToRadar(Vector3 world) => WorldToRadar(world, sim.Player.Position, sim.Player.Yaw, center, scale);

        foreach (Brush brush in sim.Map.Brushes)
        {
            if (brush.Kind == SurfaceKind.Floor) continue;

            Vector2 a = ToRadar(new Vector3(brush.Box.Min.X, 0f, brush.Box.Min.Z));
            Vector2 b = ToRadar(new Vector3(brush.Box.Max.X, 0f, brush.Box.Min.Z));
            Vector2 c = ToRadar(new Vector3(brush.Box.Max.X, 0f, brush.Box.Max.Z));
            Vector2 d = ToRadar(new Vector3(brush.Box.Min.X, 0f, brush.Box.Max.Z));
            Color geometry = brush.Kind == SurfaceKind.Crate
                ? U.Rgba(226, 190, 108, 116)
                : brush.Kind == SurfaceKind.Metal
                    ? U.Rgba(114, 194, 226, 105)
                    : U.Rgba(204, 212, 216, 80);
            geometry = Fade(geometry, opacity);

            DrawRadarSegment(a, b, center, radius - 1f, geometry);
            DrawRadarSegment(b, c, center, radius - 1f, geometry);
            DrawRadarSegment(c, d, center, radius - 1f, geometry);
            DrawRadarSegment(d, a, center, radius - 1f, geometry);
        }

        // Cabinets are fixed, high-value map objectives: always shown.
        foreach (UpgradeStation station in sim.Stations)
        {
            Vector2 point = ToRadar(station.Position);
            if ((point - center).Length() <= radius - 6f)
            {
                Raylib.DrawCircleV(point, 3.2f, Fade(U.Rgba(255, 186, 56, 235), opacity));
                Raylib.DrawCircleLines((int)point.X, (int)point.Y, 6f, Fade(U.Rgba(255, 186, 56, 110), opacity));
            }
        }

        Vector3 eye = sim.Player.EyePosition;
        foreach (Actor actor in sim.Actors)
        {
            if (actor.IsPlayer || !actor.Alive) continue;

            Vector3 delta = actor.Position - sim.Player.Position;
            float distance = U.FlatLength(delta);
            if (distance > 1500f) continue;

            bool visible = sim.Map.LineOfSight(eye, actor.EyePosition) && distance < 1100f;
            float contactFade = U.Clamp01(1f - distance / 1500f);
            Color contact = visible
                ? U.Rgba(236, 74, 62, (int)(120 + 135 * contactFade))
                : U.Rgba(236, 74, 62, (int)(70 * contactFade));
            contact = Fade(contact, opacity);

            Vector2 contactPoint = ToRadar(actor.Position);
            Vector2 fromCenter = contactPoint - center;
            float contactRadius = fromCenter.Length();
            if (contactRadius <= radius - 7f)
            {
                Raylib.DrawCircleV(contactPoint, visible ? 3.4f : 2.5f, contact);
                if (visible)
                    Raylib.DrawCircleLines((int)contactPoint.X, (int)contactPoint.Y, 6f,
                        Fade(U.Rgba(236, 74, 62, 90), opacity));
            }
            else if (contactRadius > 1e-4f)
            {
                // Preserve the old map-wide awareness without filling the small
                // radar with distant dots: an edge chevron identifies direction.
                Vector2 direction = fromCenter / contactRadius;
                DrawRadarEdgeChevron(center + direction * (radius - 7f), direction, contact);
            }
        }

        // The local player always points up because the terrain rotates under
        // the radar. This stays quick to parse while swinging on hooks.
        Raylib.DrawTriangle(
            center + new Vector2(0f, -7.5f),
            center + new Vector2(4.75f, 4.5f),
            center + new Vector2(-4.75f, 4.5f),
            Fade(U.Rgba(120, 255, 140, 235), opacity));
        Raylib.DrawCircleLines((int)center.X, (int)center.Y, 9f, Fade(U.Rgba(120, 255, 140, 70), opacity));
        Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius, Fade(U.Rgba(230, 240, 244, 102), opacity));
        Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius + 2f, Fade(U.Rgba(112, 205, 228, 92), opacity));

        Ui.Text("FIELD SCANNER", (int)(center.X - radius + 7f), (int)(center.Y - radius + 7f), 9,
            Fade(Ui.Faint, opacity));
        Ui.TextRight("RANGE 72", (int)(center.X + radius - 7f), (int)(center.Y + radius - 16f), 8,
            Fade(Ui.Faint, opacity));
    }

    private static void DrawRadarGrid(Vector2 center, float radius, float opacity)
    {
        Color grid = Fade(U.Rgba(184, 202, 206, 24), opacity);
        Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius * 0.50f, grid);
        Raylib.DrawCircleLines((int)center.X, (int)center.Y, radius * 0.75f, grid);
        Raylib.DrawLineEx(new Vector2(center.X - radius, center.Y), new Vector2(center.X + radius, center.Y), 1f, grid);
        Raylib.DrawLineEx(new Vector2(center.X, center.Y - radius), new Vector2(center.X, center.Y + radius), 1f, grid);
    }

    private static Vector2 WorldToRadar(Vector3 world, Vector3 playerPosition, float playerYaw,
        Vector2 center, float scale)
    {
        Vector3 delta = world - playerPosition;
        Vector3 right = U.RightFromYaw(playerYaw);
        Vector3 forward = U.DirFromAngles(playerYaw, 0f);
        float localRight = delta.X * right.X + delta.Z * right.Z;
        float localForward = delta.X * forward.X + delta.Z * forward.Z;
        return center + new Vector2(localRight * scale, -localForward * scale);
    }

    private static void DrawRadarSegment(Vector2 a, Vector2 b, Vector2 center, float radius, Color color)
    {
        if (!TryClipSegmentToCircle(a, b, center, radius, out Vector2 clippedA, out Vector2 clippedB)) return;
        Raylib.DrawLineEx(clippedA, clippedB, 1f, color);
    }

    private static bool TryClipSegmentToCircle(Vector2 a, Vector2 b, Vector2 center, float radius,
        out Vector2 clippedA, out Vector2 clippedB)
    {
        Vector2 delta = b - a;
        Vector2 relative = a - center;
        float aa = Vector2.Dot(delta, delta);
        if (aa < 1e-6f)
        {
            bool inside = relative.LengthSquared() <= radius * radius;
            clippedA = a;
            clippedB = b;
            return inside;
        }

        float bb = 2f * Vector2.Dot(relative, delta);
        float cc = Vector2.Dot(relative, relative) - radius * radius;
        float discriminant = bb * bb - 4f * aa * cc;
        if (discriminant < 0f)
        {
            clippedA = default;
            clippedB = default;
            return false;
        }

        float root = MathF.Sqrt(discriminant);
        float enter = MathF.Max(0f, (-bb - root) / (2f * aa));
        float exit = MathF.Min(1f, (-bb + root) / (2f * aa));
        if (enter > exit)
        {
            clippedA = default;
            clippedB = default;
            return false;
        }

        clippedA = a + delta * enter;
        clippedB = a + delta * exit;
        return true;
    }

    private static void DrawRadarEdgeChevron(Vector2 point, Vector2 direction, Color color)
    {
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Raylib.DrawTriangle(point + direction * 3.5f,
            point - direction * 2.5f + perpendicular * 2.8f,
            point - direction * 2.5f - perpendicular * 2.8f,
            color);
    }

    // ---------------------------------------------------------------- feedback

    private static void DrawHealthVignette(Simulation sim, int w, int h)
    {
        Player p = sim.Player;
        if (!p.Alive) return;

        float healthTrauma = p.LowHealthTrauma;
        float transientDamage = U.Clamp01(sim.DamageFlash) * 0.58f;
        float intensity = MathF.Max(healthTrauma, transientDamage);
        if (intensity <= 0.01f) return;

        // The short event envelope comes from Player's simulation-owned heartbeat
        // state, rather than from a render-frame timer, so the sound and visuals
        // stay aligned and screenshot/headless paths cannot mutate it.
        float pulse = 0.66f + 0.34f * p.TraumaPulse;
        float vignette = U.Clamp01(intensity * pulse);

        // A restrained neutral wash is a low-cost desaturation cue. A true colour
        // grading pass would require routing the whole world and UI through a
        // post-process target; this preserves the existing HUD draw contract.
        int washAlpha = (int)(healthTrauma * Cfg.LowHealthNeutralWashAlpha);
        if (washAlpha > 0)
            Raylib.DrawRectangle(0, 0, w, h, U.Rgba(142, 148, 154, washAlpha));

        DrawPeripheralFringe(w, h, healthTrauma, p.TraumaPulse);
        DrawTraumaMarks(w, h, healthTrauma, p.TraumaPulse);

        int alpha = (int)(vignette * 164f);
        int bandHeight = (int)(h * 0.255f);
        int bandWidth = (int)(w * 0.225f);
        // GPU interpolated bands produce a continuous edge falloff. The four
        // gradients meet outside the central aim cone, preserving clear target
        // acquisition while avoiding the striped artefacts a ring of 2D lines
        // creates at different display resolutions.
        Color outer = U.Rgba(147, 18, 17, alpha);
        Color clear = U.Rgba(147, 18, 17, 0);
        Raylib.DrawRectangleGradientV(0, 0, w, bandHeight, outer, clear);
        Raylib.DrawRectangleGradientV(0, h - bandHeight, w, bandHeight, clear, outer);
        Raylib.DrawRectangleGradientH(0, 0, bandWidth, h, outer, clear);
        Raylib.DrawRectangleGradientH(w - bandWidth, 0, bandWidth, h, clear, outer);
    }

    private static void DrawPeripheralFringe(int w, int h, float healthTrauma, float pulse)
    {
        if (healthTrauma <= 0.01f) return;

        int layers = 4;
        for (int i = 0; i < layers; i++)
        {
            float t = i / (float)layers;
            int alpha = (int)(healthTrauma * (0.34f + pulse * 0.22f) * 42f * (1f - t));
            int width = Math.Max(1, (int)(2f + t * 4f));
            int inset = (int)(t * 5f);
            Raylib.DrawRectangle(inset, 0, width, h, U.Rgba(176, 34, 30, alpha));
            Raylib.DrawRectangle(w - inset - width, 0, width, h, U.Rgba(42, 74, 140, alpha));
        }
    }

    private static void DrawTraumaMarks(int w, int h, float healthTrauma, float pulse)
    {
        if (healthTrauma <= 0.025f) return;

        int alpha = (int)(healthTrauma * (0.48f + pulse * 0.32f) * 118f);
        foreach (TraumaMark mark in TraumaMarks)
        {
            float radius = MathF.Max(2f, mark.Radius * MathF.Min(w, h));
            Raylib.DrawCircleV(new Vector2(mark.X * w, mark.Y * h), radius, U.Rgba(102, 15, 16, alpha));
            Raylib.DrawCircleLines((int)(mark.X * w), (int)(mark.Y * h), radius + 1f,
                U.Rgba(185, 58, 48, alpha / 3));
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
        Ui.TextCenter("death costs one upgrade level", w / 2, (int)(h * 0.34f) + 98, 10, Ui.Faint);
        Ui.TextCenter("press TAB for scores", w / 2, (int)(h * 0.34f) + 112, 10, Ui.Faint);
    }

    private static string ShortName(string name) => name switch
    {
        "Double Barrel" => "DB",
        "Revolver" => "REV",
        _ => name.ToUpperInvariant(),
    };
}
