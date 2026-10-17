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

            // Touch the bridge type so the compiler cannot elide it; the assembly
            // compiling at all is the check. No engine is present to run it.
            Type driver = typeof(MatchDriver);
            Console.WriteLine($"MatchDriver type resolved: {driver.FullName}");
            Console.WriteLine("---------------------------------");
            Console.WriteLine("PRESENTATION: PASS");
            return 0;
        }
    }
}
