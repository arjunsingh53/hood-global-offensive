// Minimal stand-ins for the NUnit.Framework and UnityEngine APIs used by the
// edit-mode parity tests (unity/Assets/Tests/EditMode/**), so the HGO.Sim.Tests
// assembly can be type-checked headlessly by EditModeCheck.csproj. Extend when
// the tests start using more Test Runner APIs.

using System;

namespace UnityEngine
{
    public static class Application
    {
        // In Unity this is "<project>/Assets"; the test resolves the repo root as
        // dataPath/../../, i.e. two levels above Assets.
        public static string dataPath => "Assets";
    }
}

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
    }

    public static class Assert
    {
        public static void Fail(string message)
        {
            throw new NUnitAssertionException(message);
        }
    }

    public static class TestContext
    {
        public static readonly TextWriterProxy Progress = new TextWriterProxy();

        public sealed class TextWriterProxy
        {
            public void WriteLine(string value)
            {
            }
        }
    }

    public sealed class NUnitAssertionException : System.Exception
    {
        public NUnitAssertionException(string message) : base(message)
        {
        }
    }
}
