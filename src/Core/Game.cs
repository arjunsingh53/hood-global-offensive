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
    private bool _exit;

    public Simulation? Simulation => _sim;

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(DefaultWidth, DefaultHeight, Cfg.Title);
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

        switch (action)
        {
            case ScreenAction.StartMatch:
                _audio.Play("buy", 0.5f);
                StartMatch();
                break;
            case ScreenAction.QuitApp:
                _exit = true;
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
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _state = GameState.Paused;
            Raylib.EnableCursor();
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.B) && sim.Player.Alive)
        {
            _buyMenu.Refresh();
            _state = GameState.BuyMenu;
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
        }
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

    private static void RenderFrame(Simulation sim, int w, int h)
    {
        Raylib.BeginDrawing();
        Renderer.DrawSky(w, h);

        Camera3D cam = Renderer.BuildCamera(sim);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();

        Renderer.DrawViewModel(sim, w, h);
        Renderer.BlitViewModel(w, h);

        Hud.Draw(sim, cam, w, h);

        if (Raylib.IsKeyDown(KeyboardKey.Tab)) Screens.DrawScoreboard(sim, w, h);

        Raylib.EndDrawing();
    }

    private static void RenderWorldOnly(Simulation sim, int w, int h)
    {
        Renderer.DrawSky(w, h);

        Camera3D cam = Renderer.BuildCamera(sim);
        Rlgl.SetClipPlanes(1.0, 9000.0);
        Raylib.BeginMode3D(cam);
        Renderer.DrawWorld(sim, cam);
        Raylib.EndMode3D();

        Renderer.DrawViewModel(sim, w, h);
        Renderer.BlitViewModel(w, h);

        Hud.Draw(sim, cam, w, h);
    }

    private void PlaySimulationSounds(Simulation sim)
    {
        foreach (SoundEvent evt in sim.Sounds)
        {
            _audio.Play(evt, sim.Player.EyePosition);
        }
    }
}
