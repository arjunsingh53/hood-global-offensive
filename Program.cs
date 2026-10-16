using HoodGlobalOffensive.Audio;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Render;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

// The game ships as a GUI-subsystem exe so double-clicking it opens the window
// with no console riding along. CLI modes still need their output to appear.
ConsoleBridge.EnsureConsole();

if (HasFlag("--help") || HasFlag("-h")) return PrintHelp();

if (HasFlag("--maps"))
{
    foreach (string name in MapRegistry.Names)
    {
        Console.WriteLine(name);
    }
    return 0;
}

if (HasFlag("--sfx-dump"))
{
    return AudioAudit.Dump(StringArg("--sfx-dump", "sfx-dump"));
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
    bool reticle = HasFlag("--reticle");
    string anim = StringArg("--anim", "");

    return ScreenshotCapture.Run(path, seconds, bots, skill, seed, width, height, weapon, screen, zoom, map, reticle, anim);
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
          dotnet run -- --sfx-dump <dir>           write the generated sound cues to WAV files

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
          --reticle        screenshot: mark the exact centre-ray pixel for sight calibration
          --anim <a[:t]>   screenshot: pin a viewmodel action (DRAW|IDLE|INSPEC|RELOAD1|RELOAD2|SHOOT) at clip time t
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

/// <summary>
/// A GUI-subsystem process launched from a terminal inherits valid standard
/// handles, so its output prints as usual. Some launchers pass none; for those
/// the bridge attaches to the parent console and rebinds the streams. A
/// double-clicked game has no parent console, so nothing is printed and no
/// window appears.
/// </summary>
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    public static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (GetConsoleWindow() != IntPtr.Zero) return;
        // Never disturb an already-usable stdout: it may be an inherited
        // console handle or a caller's pipe that a rebind would break.
        if (StdHandleValid(StdOutputHandle)) return;

        if (!AttachConsole(AttachParentProcess)) return;

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private static bool StdHandleValid(int handleKind)
    {
        IntPtr handle = GetStdHandle(handleKind);
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handleKind);
}
