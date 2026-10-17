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
            Console.WriteLine($"MatchDriver type resolved: {driver.FullName}");
            Console.WriteLine($"SimAudioPlayer type resolved: {audio.FullName}");
            Console.WriteLine("---------------------------------");
            Console.WriteLine("PRESENTATION: PASS");
            return 0;
        }
    }
}
