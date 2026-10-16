using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.UI;
using HoodGlobalOffensive.World;
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
        int width = 1280, int height = 720, string weapon = "", string screen = "play", bool zoom = false,
        string mapName = MapRegistry.Default, bool reticle = false, string anim = "")
    {
        try
        {
            return Capture(path, warmupSeconds, botCount, skill, seed, width, height, weapon, screen, zoom, mapName, reticle, anim);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"screenshot capture failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Capture(string path, float warmupSeconds, int botCount, BotSkill skill, ulong seed,
        int width, int height, string weaponName, string screen, bool zoom, string mapName, bool reticle, string anim)
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(width, height, Cfg.Title);
        Raylib.SetTargetFPS(120);

        var sim = new Simulation(botCount, skill, seed, mapName: mapName);
        var rng = new Rng(seed ^ 0x5EED_1234UL);

        // "menu" renders the title screen instead of a gameplay frame. Nothing has to
        // be simulated for it, which also makes it the fastest capture.
        var settings = new MatchSettings { BotCount = botCount, Skill = skill, Seed = seed, MapName = sim.Map.Name };
        bool menuScreen = screen.Equals("menu", StringComparison.OrdinalIgnoreCase);
        bool crosshairScreen = screen.Equals("crosshair", StringComparison.OrdinalIgnoreCase);
        bool upgradeScreen = screen.Equals("upgrade", StringComparison.OrdinalIgnoreCase);
        bool traumaScreen = screen.Equals("trauma", StringComparison.OrdinalIgnoreCase);
        bool impactScreen = screen.Equals("impact", StringComparison.OrdinalIgnoreCase);
        bool stagedFeedbackScreen = traumaScreen || impactScreen;

        WeaponDef? pinned = null;
        if (!string.IsNullOrWhiteSpace(weaponName) && !weaponName.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            pinned = WeaponRegistry.ByName(weaponName);
            sim.Player.GiveWeapon(pinned, SlotFor(pinned));
            sim.Player.SelectWeaponNumber(SlotFor(pinned));
            Console.WriteLine($"equipped {pinned.Name}");
        }

        const float dt = 1f / 60f;
        int ticks = menuScreen || stagedFeedbackScreen ? 0 : (int)(warmupSeconds / dt);
        if (!menuScreen && !stagedFeedbackScreen)
        {
            for (int t = 0; t < ticks; t++)
            {
                InputFrame warmupInput = ScriptedPlay.Build(t, sim, ref rng);
                PinRequestedWeapon(sim, pinned, ref warmupInput);
                sim.Update(warmupInput, dt);
                if (sim.MatchOver) break;
            }
        }

        // Keep stepping until the frame is worth capturing: the player is alive and
        // healthy, an enemy is genuinely in view, and (when asked) ADS or the
        // scope has fully come up. Iron sights use a held intent while scopes keep
        // their existing toggle state, so the screenshot follows production input.
        int extra = 0;
        const int maxExtra = 12000;
        while (extra < maxExtra && !menuScreen && !stagedFeedbackScreen)
        {
            InputFrame captureInput = ScriptedPlay.Build(ticks + extra, sim, ref rng);
            PinRequestedWeapon(sim, pinned, ref captureInput);
            if (zoom)
            {
                if (sim.Player.Current.Def.HasScope) sim.Player.ZoomHeld = true;
                else captureInput.ZoomDown = true;
            }

            bool ready = sim.Player.Alive
                && sim.Player.Health > 25f
                && EnemyInView(sim)
                && (!zoom || sim.Player.ZoomBlend > 0.97f);
            if (ready) break;

            sim.Update(captureInput, dt);
            extra++;
        }

        if (traumaScreen) StageTraumaPreview(sim, dt);
        if (impactScreen) StageImpactPreview(sim, dt);

        // Capture-only: pin one viewmodel action clip so DRAW / RELOAD / SHOOT
        // poses can be verified deterministically instead of hoping the live
        // state machine happens to be mid-clip when the frame is taken.
        if (!string.IsNullOrWhiteSpace(anim))
        {
            string name = anim;
            float at = 0f;
            int sep = anim.IndexOf(':');
            if (sep >= 0)
            {
                name = anim[..sep];
                float.TryParse(anim[(sep + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out at);
            }
            AnimatedViewmodels.ForceAction(name, at);
            Console.WriteLine($"forced viewmodel action {name} at {at:0.###}s");
        }

        // Let the viewmodel springs, sway and movement transients settle so the
        // captured frame shows the steady-state weapon pose (and a true sight
        // picture on ADS) instead of a mid-swing transient.
        for (int t = 0; t < 45 && !menuScreen && !stagedFeedbackScreen; t++)
        {
            InputFrame settleInput = InputFrame.Neutral;
            PinRequestedWeapon(sim, pinned, ref settleInput);
            if (zoom)
            {
                if (sim.Player.Current.Def.HasScope) sim.Player.ZoomHeld = true;
                else settleInput.ZoomDown = true;
            }
            sim.Update(settleInput, dt);
        }

        int w = Raylib.GetScreenWidth();
        int h = Raylib.GetScreenHeight();
        var upgradeMenu = new Screens.UpgradeMenu();
        if (upgradeScreen) StageUpgradePreview(sim);

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
                // Match the production Game path exactly: only the world is
                // rendered to and colour-graded from the private target. The
                // viewmodel and HUD remain outside it for trustworthy captures.
                Camera3D cam = Renderer.BuildCamera(sim, settings.CameraFeedback.Strength);
                Renderer.BeginWorldPass(cam, w, h);
                Rlgl.SetClipPlanes(1.0, 9000.0);
                Raylib.BeginMode3D(cam);
                Renderer.DrawWorld(sim, cam);
                Raylib.EndMode3D();
                Renderer.EndWorldPass(w, h);

                Renderer.DrawViewModel(sim, w, h, settings.CameraFeedback.Strength);
                Renderer.BlitViewModel(w, h);

                Hud.Draw(sim, cam, w, h, settings.Crosshair);

                if (screen.Equals("scoreboard", StringComparison.OrdinalIgnoreCase))
                {
                    Screens.DrawScoreboard(sim, w, h);
                }
                else if (upgradeScreen)
                {
                    upgradeMenu.Draw(sim, w, h);
                }
                else if (crosshairScreen)
                {
                    Screens.CrosshairOptions.Draw(settings.Crosshair, w, h);
                }
            }

            // Calibration aid: marks the exact pixel the hitscan centre ray
            // resolves to, so a sight picture can be verified against ground
            // truth instead of an eyeball estimate.
            if (reticle)
            {
                int cx = w / 2, cy = h / 2;
                Color mark = new(255, 0, 255, 255);
                Raylib.DrawLine(cx - 16, cy, cx + 16, cy, mark);
                Raylib.DrawLine(cx, cy - 16, cx, cy + 16, mark);
                Raylib.DrawCircleLines(cx, cy, 3f, mark);
            }

            Raylib.EndDrawing();

            if (frame == 1) Raylib.TakeScreenshot(path);
        }

        Renderer.Shutdown();
        Raylib.CloseWindow();

        var file = new FileInfo(path);
        Console.WriteLine($"wrote {file.FullName} ({(file.Exists ? file.Length : 0)} bytes)");
        Console.WriteLine($"captured frame: {w}x{h}, {ticks + extra} simulated ticks "
            + $"({(ticks + extra) * dt:0.0}s), skill {skill}, {botCount} bots, map {sim.Map.Name}");
        Console.WriteLine($"player: {sim.Player.Kills} kills, {sim.Player.Deaths} deaths, points {sim.Player.KillPoints}, "
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
            + $"hooks left {sim.Player.HookLeft.Attached} right {sim.Player.HookRight.Attached}, "
            + $"yaw {sim.Player.Yaw:0.0}, pitch {sim.Player.Pitch:0.0}, "
            + $"punch ({sim.Player.Punch.X:0.0},{sim.Player.Punch.Y:0.0}), fov {sim.Player.CurrentFov:0.0}");

        return file.Exists && file.Length > 0 ? 0 : 1;
    }

    private static int SlotFor(WeaponDef def)
    {
        if (def.Melee) return 4;
        if (WeaponRegistry.SecondaryPool.Contains(def)) return 3;
        return 1;
    }

    /// <summary>
    /// The scripted robot cycles slots for coverage and a respawn rebuilds the
    /// settings loadout, both of which would silently replace an explicitly
    /// requested capture weapon. Pinning suppresses the cycle and re-issues the
    /// weapon after a respawn so the frame always shows what was asked for.
    /// </summary>
    private static void PinRequestedWeapon(Simulation sim, WeaponDef? pinned, ref InputFrame input)
    {
        if (pinned is null) return;

        input.SwitchCycle = 0;
        input.SwitchSlot = 0;
        if (sim.Player.Alive && !ReferenceEquals(sim.Player.Current.Def, pinned))
        {
            sim.Player.GiveWeapon(pinned, SlotFor(pinned));
            sim.Player.SelectWeaponNumber(SlotFor(pinned));
        }
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
    /// Capture-only upgrade-cabinet frame: parks the player beside a cabinet
    /// and stages a few kill points so the menu shows real affordability states.
    /// It changes only this private capture simulation, never the live game.
    /// </summary>
    private static void StageUpgradePreview(Simulation sim)
    {
        Player player = sim.Player;
        UpgradeStation station = sim.Stations[0];
        Vector3 toStation = station.Position - player.Position;
        float yaw = U.YawFromDirection(toStation);
        player.Position = station.Position - U.DirFromAngles(yaw, 0f) * (Cfg.StationInteractRadius * 0.6f);
        player.Position = new Vector3(player.Position.X, 0f, player.Position.Z);
        player.Velocity = Vector3.Zero;
        player.Yaw = yaw;
        player.Pitch = 0f;
        player.Punch = Vector2.Zero;
        player.OnGround = true;
        player.Alive = true;
        player.KillPoints = 2;
        player.Health = player.MaxHealth;
        player.ResetMovementState();
        player.ResetPresentationFeedback();
    }

    /// <summary>
    /// Capture-only critical-condition frame. It exercises the actual Player
    /// update and HUD state rather than painting a screenshot-only overlay.
    /// </summary>
    private static void StageTraumaPreview(Simulation sim, float dt)
    {
        PrepareFeedbackPreview(sim);
        Player player = sim.Player;
        player.Health = 8f;

        // Let the real low-health state converge and start a real local pulse.
        for (int i = 0; i < 8; i++) sim.Update(InputFrame.Neutral, dt);
    }

    /// <summary>
    /// Capture-only impact frame using the same simulation-owned mist, droplet,
    /// and splat emitter that resolved actor hits use in normal play.
    /// </summary>
    private static void StageImpactPreview(Simulation sim, float dt)
    {
        PrepareFeedbackPreview(sim);
        Player player = sim.Player;
        player.Pitch = -14f;

        // Put a normal world-model target behind the burst so the preview reads
        // as an impact on a combatant, not as an abstract red particle test.
        // It is capture-only; no bot AI or gameplay rules run in this setup.
        var target = new Bot(88, "impact target", BotSkill.Easy)
        {
            Position = new Vector3(0f, 0f, 125f),
            Velocity = Vector3.Zero,
            Yaw = 180f,
            Pitch = 0f,
            OnGround = true,
            Alive = true,
        };
        sim.Actors.Add(target);

        // Match the entry-side hit point a real ray from the local player would
        // resolve on this target's front collision face.
        Vector3 impactPoint = target.Position + new Vector3(0f, Cfg.StandHeight * 0.52f, 14f);
        sim.SpawnActorHitVfx(impactPoint, -Vector3.UnitZ, HitGroup.Chest);

        // Leave the burst live for the capture rather than advancing it until it
        // has faded or landed. The emitter itself is the production code path.
        _ = dt;
    }

    private static void PrepareFeedbackPreview(Simulation sim)
    {
        Player player = sim.Player;
        player.Position = new Vector3(0f, 0f, 230f);
        player.Velocity = Vector3.Zero;
        player.Yaw = 0f;
        player.Pitch = 0f;
        player.Punch = Vector2.Zero;
        player.OnGround = true;
        player.Alive = true;
        player.Health = player.MaxHealth;
        player.ResetMovementState();
        player.ResetPresentationFeedback();

        // Keep screenshot staging isolated: no live bot can alter the player
        // between the deterministic setup and the captured feedback frame.
        foreach (Actor actor in sim.Actors)
        {
            if (actor.IsPlayer) continue;
            actor.Alive = false;
            actor.RespawnAt = float.MaxValue;
        }
    }
}
