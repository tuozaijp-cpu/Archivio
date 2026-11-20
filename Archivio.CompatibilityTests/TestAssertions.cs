namespace Archivio.CompatibilityTests;

internal static class TestAssertions
{
    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

