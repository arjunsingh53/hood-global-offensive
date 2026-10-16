using HoodGlobalOffensive.Audio;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.UI;
using Raylib_cs;

namespace HoodGlobalOffensive.Core;

public enum GameState
{
    MainMenu,
    Playing,
    Paused,
    UpgradeMenu,
    CrosshairOptions,
    MatchOver,
}

/// <summary>Owns the window, the state machine and the frame loop.</summary>
public sealed class Game
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 720;

    private GameState _state = GameState.MainMenu;
    private Simulation? _sim;
    private readonly MatchSettings _settings = new();
    private readonly UserSettingsStore _userSettings = new();
    private readonly AudioBank _audio = new();
    private readonly Screens.UpgradeMenu _upgradeMenu = new();
    private GameState _crosshairReturnState = GameState.MainMenu;
    private bool _exit;

    public Simulation? Simulation => _sim;

    public void Run()
    {
        // GUI preferences are local-only. Command-line checks construct no Game,
        // so deterministic simulation and screenshot tools never touch profiles.
        _userSettings.LoadInto(_settings);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(DefaultWidth, DefaultHeight, Cfg.Title);
        // Menus are rendered on a responsive 1280×720 logical canvas. Keep a
        // readable physical floor rather than allowing system resize to crush
        // click targets and option text into an unusable surface.
        Raylib.SetWindowMinSize(960, 540);
        // Raylib maps Escape to WindowShouldClose by default. HOODMATCH owns
        // Escape as a pause/resume control, so leave closing the window to its
        // title-bar control or an explicit menu action instead.
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(144);

        try
        {
            _audio.Load();
        }
        catch
        {
            // Audio is optional; the game runs fine without a device.
        }

        try
        {
            while (!Raylib.WindowShouldClose() && !_exit)
            {
                // GLFW can report a zero-sized framebuffer while the window is
                // minimized. Pause the presentation loop briefly rather than asking
                // the logical UI canvas to render at a zero zoom.
                int w = Raylib.GetScreenWidth();
                int h = Raylib.GetScreenHeight();
                if (Raylib.IsWindowMinimized() || w <= 0 || h <= 0)
                {
                    Raylib.WaitTime(0.05);
                    continue;
                }

                float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);

                switch (_state)
                {
                    case GameState.MainMenu: StepMainMenu(w, h); break;
                    case GameState.Playing: StepPlaying(w, h, dt); break;
                    case GameState.Paused: StepPaused(w, h); break;
                    case GameState.UpgradeMenu: StepUpgradeMenu(w, h); break;
                    case GameState.CrosshairOptions: StepCrosshairOptions(w, h); break;
                    default: StepMatchOver(w, h); break;
                }
            }
        }
        finally
        {
            _userSettings.SaveIfChanged(_settings);
            Renderer.Shutdown();
            _audio.Dispose();
            Raylib.CloseWindow();
        }
    }

    // ---------------------------------------------------------------- states

    private void StepMainMenu(int w, int h)
    {
        Raylib.BeginDrawing();
        ScreenAction action = Screens.MainMenu.Draw(_settings, w, h);
        Raylib.EndDrawing();
        _userSettings.SaveIfChanged(_settings);

        if (Raylib.IsKeyPressed(KeyboardKey.F2)) action = ScreenAction.OpenCrosshairOptions;

        switch (action)
        {
            case ScreenAction.StartMatch:
                _audio.Play("spawn", 0.5f);
                StartMatch();
                break;
            case ScreenAction.QuitApp:
                _exit = true;
                break;
            case ScreenAction.OpenCrosshairOptions:
                OpenCrosshairOptions(GameState.MainMenu);
                StepCrosshairOptions(w, h, acceptInput: false);
                break;
        }
    }

    private void StartMatch()
    {
        _sim = new Simulation(_settings.BotCount, _settings.Skill, _settings.Seed,
            _settings.KillsToWin, _settings.Loadout, _settings.MapName);
        _sim.Player.Sensitivity = _settings.Sensitivity;
        _settings.Seed = _settings.Seed * 6364136223846793005UL + 1442695040888963407UL;
        _state = GameState.Playing;
        Raylib.DisableCursor();
        _audio.Play("spawn", 0.5f);
    }

    private void StepPlaying(int w, int h, float dt)
    {
        Simulation sim = _sim!;

        // Route the full-screen control before gameplay input. This prevents a
        // final movement/fire tick under the menu and keeps Escape available
        // even while the player is waiting to respawn.
        GameplayOverlayAction overlay = GameplayOverlayInput.Resolve(
            Raylib.IsKeyPressed(KeyboardKey.Escape), sim.MatchOver);
        if (overlay == GameplayOverlayAction.Pause)
        {
            _state = GameState.Paused;
            Raylib.EnableCursor();
            // Present the pause screen immediately. Returning before Begin/EndDrawing
            // leaves the Escape press that opened the menu still live, so the new
            // screen used to close itself on its very first drawn frame.
            StepPaused(w, h, acceptInput: false);
            return;
        }

        // Open before polling gameplay input so the shortcut cannot produce a
        // stray fire/movement tick while the user is changing a visual setting.
        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            OpenCrosshairOptions(GameState.Playing);
            // Same input-ownership rule as the upgrade menu: present one frame of
            // the new screen so this F2 press cannot also close it.
            StepCrosshairOptions(w, h, acceptInput: false);
            return;
        }

        InputFrame input = RealInput.Poll(Raylib.IsWindowFocused());

        // The cabinet key (E) is resolved inside the simulation against hook
        // input, so the player only leaves gameplay when actually at a station.
        sim.Update(input, dt);
        sim.Player.Sensitivity = _settings.Sensitivity;

        PlaySimulationSounds(sim);
        RenderFrame(sim, w, h);

        if (sim.Player.WantsOpenUpgrade && sim.Player.Alive && !sim.MatchOver)
        {
            _upgradeMenu.Open();
            _state = GameState.UpgradeMenu;
            Raylib.EnableCursor();
            // Present the new state immediately. Returning before Begin/EndDrawing
            // leaves Raylib on the opening input frame, which used to hand that
            // same E press to the menu before it could be seen.
            PresentUpgradeMenu(sim, w, h);
            return;
        }

        if (sim.MatchOver)
        {
            _audio.Play("match_end", 0.8f);
            _state = GameState.MatchOver;
            Raylib.EnableCursor();
        }
    }

    private void StepPaused(int w, int h, bool acceptInput = true)
    {
        Simulation sim = _sim!;

        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        ScreenAction action = Screens.Pause.Draw(_settings, sim, w, h);
        Raylib.EndDrawing();
        _userSettings.SaveIfChanged(_settings);

        // A transition frame only presents the menu. Raylib polls input inside
        // EndDrawing, so the press that opened this screen is still live until
        // this frame completes; acting on it here would instantly undo the open.
        if (!acceptInput) return;

        if (Raylib.IsKeyPressed(KeyboardKey.F2)) action = ScreenAction.OpenCrosshairOptions;

        switch (action)
        {
            case ScreenAction.Resume:
                _state = GameState.Playing;
                Raylib.DisableCursor();
                break;
            case ScreenAction.Restart:
                StartMatch();
                break;
            case ScreenAction.QuitToMenu:
                _state = GameState.MainMenu;
                break;
            case ScreenAction.OpenCrosshairOptions:
                OpenCrosshairOptions(GameState.Paused);
                StepCrosshairOptions(w, h, acceptInput: false);
                break;
        }
    }

    private void StepUpgradeMenu(int w, int h)
    {
        Simulation sim = _sim!;

        if (!PresentUpgradeMenu(sim, w, h))
        {
            _state = GameState.Playing;
            Raylib.DisableCursor();
        }
    }

    private bool PresentUpgradeMenu(Simulation sim, int w, int h)
    {
        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        bool stillOpen = _upgradeMenu.Draw(sim, w, h);
        Raylib.EndDrawing();
        return stillOpen;
    }

    private void StepCrosshairOptions(int w, int h, bool acceptInput = true)
    {
        Raylib.BeginDrawing();
        if (_crosshairReturnState == GameState.MainMenu || _sim is null)
        {
            Raylib.ClearBackground(U.Rgb(16, 18, 24));
        }
        else
        {
            RenderWorldOnly(_sim, w, h);
        }

        ScreenAction action = Screens.CrosshairOptions.Draw(_settings.Crosshair, w, h);
        Raylib.EndDrawing();
        _userSettings.SaveIfChanged(_settings);

        // Transition frame: present only. The shortcut press that opened this
        // screen must not also close it before it can be seen.
        if (!acceptInput) return;

        if (action != ScreenAction.CloseCrosshairOptions) return;

        _state = _crosshairReturnState;
        if (_state == GameState.Playing) Raylib.DisableCursor();
        else Raylib.EnableCursor();
    }

    private void StepMatchOver(int w, int h)
    {
        Simulation sim = _sim!;

        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        ScreenAction action = Screens.MatchOver.Draw(sim, _settings, w, h);
        Raylib.EndDrawing();

        switch (action)
        {
            case ScreenAction.Restart:
                _audio.Play("spawn", 0.5f);
                StartMatch();
                break;
            case ScreenAction.QuitToMenu:
                _state = GameState.MainMenu;
                break;
        }
    }

    // ---------------------------------------------------------------- rendering

    private void RenderFrame(Simulation sim, int w, int h)
    {
        Raylib.BeginDrawing();
        Camera3D cam = Renderer.BuildCamera(sim, _settings.CameraFeedback.Strength);
        Renderer.BeginWorldPass(cam, w, h);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();
        Renderer.EndWorldPass(w, h);

        // World-only colour grade has completed above.  The separate weapon
        // pass and all HUD information remain ungraded for a stable sight and
        // clear competitive read.
        Renderer.DrawViewModel(sim, w, h, _settings.CameraFeedback.Strength);
        Renderer.BlitViewModel(w, h);

        Hud.Draw(sim, cam, w, h, _settings.Crosshair);

        if (Raylib.IsKeyDown(KeyboardKey.Tab)) Screens.DrawScoreboard(sim, w, h);

        Raylib.EndDrawing();
    }

    private void RenderWorldOnly(Simulation sim, int w, int h)
    {
        Camera3D cam = Renderer.BuildCamera(sim, _settings.CameraFeedback.Strength);
        Renderer.BeginWorldPass(cam, w, h);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();
        Renderer.EndWorldPass(w, h);

        Renderer.DrawViewModel(sim, w, h, _settings.CameraFeedback.Strength);
        Renderer.BlitViewModel(w, h);

        Hud.Draw(sim, cam, w, h, _settings.Crosshair);
    }

    private void OpenCrosshairOptions(GameState returnState)
    {
        _crosshairReturnState = returnState;
        _state = GameState.CrosshairOptions;
        Raylib.EnableCursor();
    }

    private void PlaySimulationSounds(Simulation sim)
    {
        foreach (SoundEvent evt in sim.Sounds)
        {
            _audio.Play(evt, sim.Player.EyePosition, sim.Player.Yaw, sim.Player.LowHealthTrauma, sim.Map);
        }
    }
}
