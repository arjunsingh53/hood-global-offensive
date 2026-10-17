using System;

namespace HoodGlobalOffensive.EditModeCheck
{
    internal static class Program
    {
        private static int Main()
        {
            // Referencing the test assembly's types proves the edit-mode test sources
            // were compiled, not skipped.
            GC.KeepAlive(typeof(HoodGlobalOffensive.Parity.ParityTests));
            GC.KeepAlive(typeof(HoodGlobalOffensive.Parity.ParityChecks));
            Console.WriteLine("EDITMODE: PASS");
            return 0;
        }
    }
}
