using System;
using HoodGlobalOffensive.Presentation;

namespace HoodGlobalOffensive.PresentationCheck
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("HOODMATCH presentation compile gate");
            Console.WriteLine("Compiles HGO.Presentation (MatchDriver) against HGO.Sim + Unity stubs.");
            Console.WriteLine("A clean -warnaserror build is the real gate; this marker is for CI logs.");
            Console.WriteLine("---------------------------------");

            // Touch the bridge types so the compiler cannot elide them; the assembly
            // compiling at all is the check. No engine is present to run them.
            Type driver = typeof(MatchDriver);
            Type audio = typeof(SimAudioPlayer);
            Type map = typeof(MapBlockout);
            Type actors = typeof(ActorViews);
            Type viewmodel = typeof(ViewModelSync);
            Type hud = typeof(HudSync);
            Type menu = typeof(MenuSync);
            Type effects = typeof(EffectsSync);
            Type crosshairSettings = typeof(CrosshairSettings);
            Type crosshairGraphic = typeof(CrosshairGraphic);
            Console.WriteLine($"MatchDriver type resolved: {driver.FullName}");
            Console.WriteLine($"SimAudioPlayer type resolved: {audio.FullName}");
            Console.WriteLine($"MapBlockout type resolved: {map.FullName}");
            Console.WriteLine($"ActorViews type resolved: {actors.FullName}");
            Console.WriteLine($"ViewModelSync type resolved: {viewmodel.FullName}");
            Console.WriteLine($"HudSync type resolved: {hud.FullName}");
            Console.WriteLine($"MenuSync type resolved: {menu.FullName}");
            Console.WriteLine($"EffectsSync type resolved: {effects.FullName}");
            Console.WriteLine($"CrosshairSettings type resolved: {crosshairSettings.FullName}");
            Console.WriteLine($"CrosshairGraphic type resolved: {crosshairGraphic.FullName}");
            Console.WriteLine("---------------------------------");
            Console.WriteLine("PRESENTATION: PASS");
            return 0;
        }
    }
}
