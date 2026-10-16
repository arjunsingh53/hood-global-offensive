using System;
using System.IO;
using HoodGlobalOffensive.Parity;

namespace HoodGlobalOffensive.Parity
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("HOODMATCH sim-port parity harness");
            Console.WriteLine("Golden values captured from the raylib reference (main @ 6a6fbdd, v0.2.2).");
            Console.WriteLine("---------------------------------");

            ParityChecks.ParityReport report = ParityChecks.RunAll(LoadGolden);
            foreach (string line in report.Lines)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine("---------------------------------");
            if (report.Failures == 0)
            {
                Console.WriteLine("PARITY: PASS");
                return 0;
            }

            Console.WriteLine($"PARITY: FAIL ({report.Failures} mismatches)");
            return 1;
        }

        private static string LoadGolden(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "goldens", name);
            return File.ReadAllText(path);
        }
    }
}
