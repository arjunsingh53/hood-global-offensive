using System.Numerics;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.UI;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>
/// Runs a match headlessly for a few seconds with the scripted player, then draws
/// a single real gameplay frame and writes it to disk. Lets the game be verified
/// visually without asking anyone to play it.
/// </summary>
public static class ScreenshotCapture
{
    public static int Run(string path, float warmupSeconds, int botCount, BotSkill skill, ulong seed,
        int width = 1280, int height = 720, string weapon = "", string screen = "play", bool zoom = false)
    {
        try
        {
            return Capture(path, warmupSeconds, botCount, skill, seed, width, height, weapon, screen, zoom);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"screenshot capture failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Capture(string path, float warmupSeconds, int botCount, BotSkill skill, ulong seed,
        int width, int height, string weaponName, string screen, bool zoom)
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(width, height, Cfg.Title);
        Raylib.SetTargetFPS(120);

        var sim = new Simulation(botCount, skill, seed);
        var rng = new Rng(seed ^ 0x5EED_1234UL);
        ScriptedPlay.Reset();

        // "menu" renders the title screen instead of a gameplay frame. Nothing has to
        // be simulated for it, which also makes it the fastest capture.
        var settings = new MatchSettings { BotCount = botCount, Skill = skill, Seed = seed };
        bool menuScreen = screen.Equals("menu", StringComparison.OrdinalIgnoreCase);
        bool characterScreen = screen.StartsWith("character", StringComparison.OrdinalIgnoreCase);
        bool crosshairScreen = screen.Equals("crosshair", StringComparison.OrdinalIgnoreCase);
        bool dossierScreen = screen.Equals("dossier", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(weaponName) && !weaponName.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            Combat.WeaponDef def = Combat.WeaponRegistry.ByName(weaponName);
            sim.Player.GiveWeapon(def);
            sim.Player.SelectWeapon(def.Slot);
            Console.WriteLine($"equipped {def.Name}");
        }

        const float dt = 1f / 60f;
        int ticks = menuScreen || characterScreen ? 0 : (int)(warmupSeconds / dt);
        if (!menuScreen && !characterScreen)
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.Update(ScriptedPlay.Build(t, sim, ref rng), dt);
                if (sim.MatchOver) break;
            }
        }

        // Keep stepping until the frame is worth capturing: the player is alive and
        // healthy, an enemy is genuinely in view, and (when asked) the scope has
        // fully come up. Zoom has to be re-asserted every step because dying resets it.
        int extra = 0;
        const int maxExtra = 12000;
        while (extra < maxExtra && !menuScreen && !characterScreen)
        {
            if (zoom) sim.Player.ZoomHeld = true;

            bool ready = sim.Player.Alive
                && sim.Player.Health > 25f
                && EnemyInView(sim)
                && (!zoom || sim.Player.ZoomBlend > 0.97f);
            if (ready) break;

            sim.Update(ScriptedPlay.Build(ticks + extra, sim, ref rng), dt);
            extra++;
        }

        if (characterScreen) StageCharacterPreview(sim, screen);

        int w = Raylib.GetScreenWidth();
        int h = Raylib.GetScreenHeight();
        var buyMenu = new Screens.BuyMenu();
        if (screen.Equals("buy", StringComparison.OrdinalIgnoreCase)) buyMenu.Refresh();

        // Two frames: the first settles the window and the buffer, the second is captured.
        for (int frame = 0; frame < 2; frame++)
        {
            Raylib.BeginDrawing();

            if (menuScreen)
            {
                Screens.MainMenu.Draw(settings, w, h);
            }
            else
            {
                Renderer.DrawSky(w, h);

                Camera3D cam = Renderer.BuildCamera(sim, settings.CameraFeedback.Strength);
                Rlgl.SetClipPlanes(1.0, 9000.0);
                Raylib.BeginMode3D(cam);
                Renderer.DrawWorld(sim, cam);
                Raylib.EndMode3D();

                // Character-review frames isolate the bot asset. The normal game
                // path (including Gun Game's first-person model) is untouched.
                if (!characterScreen)
                {
                    Renderer.DrawViewModel(sim, w, h, settings.CameraFeedback.Strength);
                    Renderer.BlitViewModel(w, h);
                }

                Hud.Draw(sim, cam, w, h, settings.Crosshair);

                if (screen.Equals("scoreboard", StringComparison.OrdinalIgnoreCase))
                {
                    Screens.DrawScoreboard(sim, w, h);
                }
                else if (screen.Equals("buy", StringComparison.OrdinalIgnoreCase))
                {
                    buyMenu.Draw(sim, w, h);
                }
                else if (crosshairScreen)
                {
                    Screens.CrosshairOptions.Draw(settings.Crosshair, w, h);
                }
                else if (dossierScreen)
                {
                    Screens.WeaponDossier.Draw(sim, w, h);
                }
            }

            Raylib.EndDrawing();

            if (frame == 1) Raylib.TakeScreenshot(path);
        }

        Renderer.Shutdown();
        Raylib.CloseWindow();

        var file = new FileInfo(path);
        Console.WriteLine($"wrote {file.FullName} ({(file.Exists ? file.Length : 0)} bytes)");
        Console.WriteLine($"captured frame: {w}x{h}, {ticks + extra} simulated ticks "
            + $"({(ticks + extra) * dt:0.0}s), skill {skill}, {botCount} bots");
        Console.WriteLine($"player: {sim.Player.Kills} kills, {sim.Player.Deaths} deaths, "
            + $"health {sim.Player.Health:0}, weapon {sim.Player.Current.Def.Name}, "
            + $"reloading {sim.Player.Current.Reloading}, zoom {sim.Player.ZoomBlend:0.00}, "
            + $"kick {sim.Player.ViewKick:0.0}, landDip {sim.Player.LandDip:0.00}");
        bool wallHit = sim.Map.RayHit(sim.Player.EyePosition, sim.Player.AimDirection, 5000f,
            out float wallDistance, out _, out _, out _);
        Console.WriteLine($"wall ahead of muzzle: {(wallHit ? $"{wallDistance:0.0} units" : "none")}, "
            + $"position ({sim.Player.Position.X:0.0},{sim.Player.Position.Y:0.0},{sim.Player.Position.Z:0.0}), "
            + $"embedded {sim.Map.BoxBlocked(sim.Player.Bounds)}");
        Console.WriteLine($"state: alive {sim.Player.Alive}, bobAmount {sim.Player.BobAmount:0.00}, "
            + $"bobPhase {sim.Player.BobPhase:0.00}, sway ({sim.Player.Sway.X:0.00},{sim.Player.Sway.Y:0.00}), "
            + $"feedback step {sim.Player.FootstepFeedback:0.00}, land {sim.Player.LandingFeedback:0.00}, "
            + $"damage {sim.Player.DamageFeedback:0.00}, "
            + $"sprint {sim.Player.SprintState}, tacCharge {sim.Player.TacticalSprintCharge:0.00}, "
            + $"yaw {sim.Player.Yaw:0.0}, pitch {sim.Player.Pitch:0.0}, "
            + $"punch ({sim.Player.Punch.X:0.0},{sim.Player.Punch.Y:0.0}), fov {sim.Player.CurrentFov:0.0}");

        return file.Exists && file.Length > 0 ? 0 : 1;
    }

    private static bool EnemyInView(Simulation sim)
    {
        if (!sim.Player.Alive) return false;

        Vector3 eye = sim.Player.EyePosition;
        Vector3 forward = sim.Player.AimDirection;

        foreach (Actor actor in sim.Actors)
        {
            if (actor.IsPlayer || !actor.Alive) continue;

            Vector3 to = actor.EyePosition - eye;
            float distance = to.Length();
            if (distance > 1700f) continue;
            if (Vector3.Dot(forward, to / distance) < 0.55f) continue;
            if (!sim.Map.LineOfSight(eye, actor.EyePosition)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A stable art-review frame for the character asset. It changes only the
    /// screenshot capture's private simulation after normal match setup; it never
    /// runs in the game loop or affects gameplay rules.
    /// </summary>
    private static void StageCharacterPreview(Simulation sim, string screen)
    {
        Player player = sim.Player;
        player.Position = new Vector3(0f, 0f, -1050f);
        player.Velocity = Vector3.Zero;
        player.Yaw = 180f;
        player.Pitch = 0f;
        player.Punch = Vector2.Zero;
        player.CrouchAmount = 0f;
        player.OnGround = true;
        player.Alive = true;
        player.ResetMovementState();
        player.BobAmount = 0f;
        player.LandDip = 0f;

        Actor? bot = sim.Actors.FirstOrDefault(actor => !actor.IsPlayer);
        if (bot is null) return;

        // This sightline passes through the central opening in the north divider,
        // placing the Soldier close enough to inspect texture, orientation and pose.
        // Close enough to inspect the weapon-ready silhouette rather than merely
        // prove the asset loaded; this capture-only staging never runs in play.
        bool closeReview = screen.Equals("character-close", StringComparison.OrdinalIgnoreCase)
            || screen.Equals("character-close-side", StringComparison.OrdinalIgnoreCase);
        bot.Position = new Vector3(0f, 0f, closeReview ? -990f : -965f);
        bot.Velocity = PreviewVelocity(screen);
        // A three-quarter inspection frame makes it possible to confirm that a
        // hand-skinned world weapon has depth and is not merely a front-facing
        // silhouette.  It is capture-only and never affects a live match.
        bot.Yaw = screen.Equals("character-side", StringComparison.OrdinalIgnoreCase)
            || screen.Equals("character-close-side", StringComparison.OrdinalIgnoreCase) ? 34f : 0f;
        bot.Pitch = 0f;
        bot.CrouchAmount = screen.Equals("character-crouch", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        bot.OnGround = true;
        bot.Alive = true;
        sim.Time = 0.6f;
    }

    private static Vector3 PreviewVelocity(string screen) => screen.ToLowerInvariant() switch
    {
        "character-walk" => new Vector3(Cfg.WalkSpeed, 0f, 0f),
        "character-run" => new Vector3(Cfg.RunSpeed, 0f, 0f),
        "character-crouch" => new Vector3(Cfg.CrouchSpeed, 0f, 0f),
        _ => Vector3.Zero,
    };
}
