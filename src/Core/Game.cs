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
    CrosshairOptions,
    WeaponDossier,
    BuyMenu,
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
    private readonly AudioBank _audio = new();
    private readonly Screens.BuyMenu _buyMenu = new();
    private GameState _crosshairReturnState = GameState.MainMenu;
    private GameState _dossierReturnState = GameState.Playing;
    private bool _exit;

    public Simulation? Simulation => _sim;

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(DefaultWidth, DefaultHeight, Cfg.Title);
        // Raylib maps Escape to WindowShouldClose by default. HGO owns Escape as
        // a pause/resume control, so leave closing the window to its title-bar
        // control or an explicit menu action instead.
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

        while (!Raylib.WindowShouldClose() && !_exit)
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);

            switch (_state)
            {
                case GameState.MainMenu: StepMainMenu(w, h); break;
                case GameState.Playing: StepPlaying(w, h, dt); break;
                case GameState.Paused: StepPaused(w, h); break;
                case GameState.CrosshairOptions: StepCrosshairOptions(w, h); break;
                case GameState.WeaponDossier: StepWeaponDossier(w, h); break;
                case GameState.BuyMenu: StepBuyMenu(w, h); break;
                default: StepMatchOver(w, h); break;
            }
        }

        Renderer.Shutdown();
        _audio.Dispose();
        Raylib.CloseWindow();
    }

    // ---------------------------------------------------------------- states

    private void StepMainMenu(int w, int h)
    {
        Raylib.BeginDrawing();
        ScreenAction action = Screens.MainMenu.Draw(_settings, w, h);
        Raylib.EndDrawing();

        if (Raylib.IsKeyPressed(KeyboardKey.F2)) action = ScreenAction.OpenCrosshairOptions;

        switch (action)
        {
            case ScreenAction.StartMatch:
                _audio.Play("buy", 0.5f);
                StartMatch();
                break;
            case ScreenAction.QuitApp:
                _exit = true;
                break;
            case ScreenAction.OpenCrosshairOptions:
                OpenCrosshairOptions(GameState.MainMenu);
                break;
        }
    }

    private void StartMatch()
    {
        _sim = new Simulation(_settings.BotCount, _settings.Skill, _settings.Seed, _settings.KillsToWin);
        _sim.Player.Sensitivity = _settings.Sensitivity;
        _settings.Seed = _settings.Seed * 6364136223846793005UL + 1442695040888963407UL;
        ScriptedPlay.Reset();
        _buyMenu.Refresh();
        _state = GameState.Playing;
        Raylib.DisableCursor();
        _audio.Play("spawn", 0.5f);
    }

    private void StepPlaying(int w, int h, float dt)
    {
        Simulation sim = _sim!;

        // Route full-screen controls before gameplay input. This prevents a
        // final movement/fire tick under the menu and keeps B available even
        // while the player is waiting to respawn.
        GameplayOverlayAction overlay = GameplayOverlayInput.Resolve(
            Raylib.IsKeyPressed(KeyboardKey.Escape),
            Raylib.IsKeyPressed(KeyboardKey.B),
            sim.MatchOver);
        if (overlay == GameplayOverlayAction.Pause)
        {
            _state = GameState.Paused;
            Raylib.EnableCursor();
            return;
        }
        if (overlay == GameplayOverlayAction.Buy)
        {
            _buyMenu.Refresh();
            _state = GameState.BuyMenu;
            Raylib.EnableCursor();
            return;
        }

        // The dossier is informational and freezes the game exactly like the
        // existing menu states. Open it before polling gameplay input so G never
        // becomes a stray movement/fire frame.
        if (Raylib.IsKeyPressed(KeyboardKey.G))
        {
            OpenWeaponDossier(GameState.Playing);
            return;
        }

        // Open before polling gameplay input so the shortcut cannot produce a
        // stray fire/movement tick while the user is changing a visual setting.
        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            OpenCrosshairOptions(GameState.Playing);
            return;
        }

        InputFrame input = RealInput.Poll(Raylib.IsWindowFocused());
        sim.Update(input, dt);
        sim.Player.Sensitivity = _settings.Sensitivity;

        PlaySimulationSounds(sim);
        RenderFrame(sim, w, h);

        if (sim.MatchOver)
        {
            _audio.Play("match_end", 0.8f);
            _state = GameState.MatchOver;
            Raylib.EnableCursor();
        }
    }

    private void StepPaused(int w, int h)
    {
        Simulation sim = _sim!;

        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        ScreenAction action = Screens.Pause.Draw(_settings, sim, w, h);
        Raylib.EndDrawing();

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
                break;
            case ScreenAction.OpenWeaponDossier:
                OpenWeaponDossier(GameState.Paused);
                break;
        }
    }

    private void StepCrosshairOptions(int w, int h)
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

        if (action != ScreenAction.CloseCrosshairOptions) return;

        _state = _crosshairReturnState;
        if (_state == GameState.Playing) Raylib.DisableCursor();
        else Raylib.EnableCursor();
    }

    private void StepWeaponDossier(int w, int h)
    {
        Simulation sim = _sim!;

        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        ScreenAction action = Screens.WeaponDossier.Draw(sim, w, h);
        Raylib.EndDrawing();

        if (action != ScreenAction.CloseWeaponDossier) return;

        _state = _dossierReturnState;
        if (_state == GameState.Playing) Raylib.DisableCursor();
        else Raylib.EnableCursor();
    }

    private void StepBuyMenu(int w, int h)
    {
        Simulation sim = _sim!;

        Raylib.BeginDrawing();
        RenderWorldOnly(sim, w, h);
        bool stillOpen = _buyMenu.Draw(sim, w, h);
        Raylib.EndDrawing();

        if (!stillOpen)
        {
            _state = GameState.Playing;
            Raylib.DisableCursor();
        }
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
                _audio.Play("buy", 0.5f);
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
        Renderer.DrawSky(w, h);

        Camera3D cam = Renderer.BuildCamera(sim, _settings.CameraFeedback.Strength);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();

        Renderer.DrawViewModel(sim, w, h, _settings.CameraFeedback.Strength);
        Renderer.BlitViewModel(w, h);

        Hud.Draw(sim, cam, w, h, _settings.Crosshair);

        if (Raylib.IsKeyDown(KeyboardKey.Tab)) Screens.DrawScoreboard(sim, w, h);

        Raylib.EndDrawing();
    }

    private void RenderWorldOnly(Simulation sim, int w, int h)
    {
        Renderer.DrawSky(w, h);

        Camera3D cam = Renderer.BuildCamera(sim, _settings.CameraFeedback.Strength);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();

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

    private void OpenWeaponDossier(GameState returnState)
    {
        _dossierReturnState = returnState;
        _state = GameState.WeaponDossier;
        Raylib.EnableCursor();
    }

    private void PlaySimulationSounds(Simulation sim)
    {
        foreach (SoundEvent evt in sim.Sounds)
        {
            _audio.Play(evt, sim.Player.EyePosition);
        }
    }
}
