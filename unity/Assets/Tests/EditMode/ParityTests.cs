using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace HoodGlobalOffensive.Parity
{
    /// <summary>
    /// Edit-mode gate for the Unity sim port. Runs the shared golden suite compiled from
    /// ParityChecks.cs (the exact code the console harness in tools/parity executes) and fails
    /// with the emitted mismatch lines. Run via Window > General > Test Runner > EditMode,
    /// or headless: Unity -batchmode -projectPath unity -runTests -testPlatform EditMode
    /// -testResults results.xml.
    /// </summary>
    public sealed class ParityTests
    {
        private static string LoadGolden(string name)
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            return File.ReadAllText(Path.Combine(repoRoot, "tools", "parity", "goldens", name));
        }

        [Test]
        public void SimGoldenValues_MatchReference()
        {
            AssertParity(ParityChecks.RunSimChecks());
        }

        [Test]
        public void HeadlessReports_MatchReferenceCaptures()
        {
            AssertParity(ParityChecks.RunHeadlessReportChecks(LoadGolden));
        }

        private static void AssertParity(ParityChecks.ParityReport report)
        {
            if (report.Failures == 0)
            {
                TestContext.Progress.WriteLine($"parity ok: {report.Lines.Count} result lines emitted");
                return;
            }

            var failures = new StringBuilder();
            failures.AppendLine($"{report.Failures} parity mismatch(es):");
            foreach (string line in report.Lines)
            {
                if (line.StartsWith("FAIL", System.StringComparison.Ordinal))
                {
                    failures.AppendLine(line);
                }
            }

            Assert.Fail(failures.ToString());
        }
    }
}
