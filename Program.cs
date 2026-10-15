using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.UI;
using Raylib_cs;

if (HasFlag("--help") || HasFlag("-h")) return PrintHelp();

if (HasFlag("--movement-check"))
{
    ulong seed = (ulong)IntArg("--seed", 20260114);
    return MovementVerifier.Run(seed);
}

if (HasFlag("--weapon-check"))
{
    ulong seed = (ulong)IntArg("--seed", 20260114);
    return WeaponReadinessVerifier.Run(seed);
}

if (HasFlag("--gameplay-check"))
{
    return GameplayInputAndReachVerifier.Run();
}

if (HasFlag("--headless"))
{
    float seconds = FloatArg("--seconds", 90f);
    int bots = IntArg("--bots", Cfg.DefaultBotCount);
    BotSkill skill = SkillArg("--skill", BotSkill.Normal);
    ulong seed = (ulong)IntArg("--seed", 20260114);
    bool verbose = HasFlag("--verbose");

    Console.WriteLine($"{Cfg.GameName} v{Cfg.Version} headless run: {seconds:0}s, {bots} bots, skill {skill}, seed {seed}");
    return HeadlessRunner.Run(seconds, bots, skill, seed, verbose);
}

if (HasFlag("--screenshot"))
{
    string path = StringArg("--screenshot", "screenshot.png");
    float seconds = FloatArg("--seconds", 4f);
    int bots = IntArg("--bots", Cfg.DefaultBotCount);
    BotSkill skill = SkillArg("--skill", BotSkill.Normal);
    ulong seed = (ulong)IntArg("--seed", 20260114);
    int width = IntArg("--width", 1280);
    int height = IntArg("--height", 720);
    string weapon = StringArg("--weapon", "");
    string screen = StringArg("--screen", "play");
    bool zoom = HasFlag("--zoom");

    return ScreenshotCapture.Run(path, seconds, bots, skill, seed, width, height, weapon, screen, zoom);
}

new Game().Run();
return 0;

bool HasFlag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

string StringArg(string name, string fallback)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    }
    return fallback;
}

int IntArg(string name, int fallback)
    => int.TryParse(StringArg(name, fallback.ToString()), out int value) ? value : fallback;

float FloatArg(string name, float fallback)
    => float.TryParse(StringArg(name, fallback.ToString("0.###")), out float value) ? value : fallback;

BotSkill SkillArg(string name, BotSkill fallback)
    => Enum.TryParse(StringArg(name, fallback.ToString()), ignoreCase: true, out BotSkill value) ? value : fallback;

int PrintHelp()
{
    Console.WriteLine("""
        HOOD GLOBAL OFFENSIVE - an early access Counter-Strike style FPS written in C#.

        Usage:
          dotnet run                              play the game
          dotnet run -- --headless [options]       run a full match with no window and report
          dotnet run -- --movement-check           run deterministic movement and visual-aim contract checks
          dotnet run -- --weapon-check             run deterministic weapon-readiness contract checks
          dotnet run -- --gameplay-check           run input-routing and firearm-reach regression checks
          dotnet run -- --screenshot <file>        render one gameplay frame to a PNG

        Options:
          --seconds <n>    headless: simulated seconds (default 90)
                           screenshot: seconds of gameplay before capture (default 4)
          --bots <n>       number of bots (default 9)
          --skill <level>  Easy | Normal | Hard | Expert (default Normal)
          --seed <n>       deterministic match seed
          --width <n>      screenshot: window width (default 1280)
          --height <n>     screenshot: window height (default 720)
          --weapon <name>  screenshot: equip a weapon, e.g. "AK-47", "AWP", "Desert Eagle"
          --screen <name>  screenshot: play | scoreboard | buy | menu | character[-idle|-walk|-run|-crouch] | crosshair | dossier
          --zoom           screenshot: hold the scope before capturing
          --verbose        headless: also dump the kill feed

        Controls:
          WASD move      mouse aim      left click fire      R reload      1/2/3 weapons
          SHIFT cycle sprint/tac/off    ALT walk             CTRL hold-crouch / C toggle crouch
          SPACE jump     right click zoom (AWP)
          B buy menu     X inspect weapon    V fire mode     G weapon dossier
          F2 crosshair options                              TAB scoreboard ESC pause
        """);
    return 0;
}
