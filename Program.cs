using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

if (HasFlag("--help") || HasFlag("-h")) return PrintHelp();

if (HasFlag("--maps"))
{
    foreach (string name in MapRegistry.Names)
    {
        Console.WriteLine(name);
    }
    return 0;
}

if (HasFlag("--headless"))
{
    float seconds = FloatArg("--seconds", 90f);
    int bots = IntArg("--bots", Cfg.DefaultBotCount);
    BotSkill skill = SkillArg("--skill", BotSkill.Normal);
    ulong seed = (ulong)IntArg("--seed", 20260114);
    string map = MapRegistry.Normalize(StringArg("--map", MapRegistry.Default));
    bool verbose = HasFlag("--verbose");

    Console.WriteLine($"{Cfg.GameName} v{Cfg.Version} headless run: {seconds:0}s, {bots} bots, skill {skill}, map {map}, seed {seed}");
    return HeadlessRunner.Run(seconds, bots, skill, seed, verbose, map);
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
    string map = MapRegistry.Normalize(StringArg("--map", MapRegistry.Default));
    bool zoom = HasFlag("--zoom");

    return ScreenshotCapture.Run(path, seconds, bots, skill, seed, width, height, weapon, screen, zoom, map);
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
    Console.WriteLine($"""
        {Cfg.GameName} v{Cfg.Version} - an early access fast-movement arena FPS with dual grappling hooks.

        Usage:
          dotnet run                              play the game
          dotnet run -- --headless [options]       run a full match with no window and report
          dotnet run -- --screenshot <file>        render one gameplay frame to a PNG
          dotnet run -- --maps                     list the map catalog

        Options:
          --seconds <n>    headless: simulated seconds (default 90)
                           screenshot: seconds of gameplay before capture (default 4)
          --bots <n>       number of bots (default {Cfg.DefaultBotCount})
          --skill <level>  Easy | Normal | Hard | Expert (default Normal)
          --seed <n>       deterministic match seed
          --map <name>     {string.Join(" | ", MapRegistry.Names)} (default {MapRegistry.Default})
          --width <n>      screenshot: window width (default 1280)
          --height <n>     screenshot: window height (default 720)
          --weapon <name>  screenshot: equip a weapon by its catalog name
          --screen <name>  screenshot: play | scoreboard | upgrade | menu | crosshair | trauma | impact
          --zoom           screenshot: hold aim / scope before capturing
          --verbose        headless: also dump the kill feed

        Controls:
          WASD move      mouse aim      left click fire      R reload      1-4 weapons / wheel
          SPACE jump     hold SPACE with no WASD to bunny hop for speed
          Q / E grapple: Q fires the left hook, E the right hook (E also opens cabinets you stand beside)
          right click aim (Double Barrel: point-blank double blast)
          V quick knife   F flashlight   TAB scoreboard   F2 crosshair   ESC pause
        """);
    return 0;
}
