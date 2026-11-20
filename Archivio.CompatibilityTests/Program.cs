using Archivio.CompatibilityTests;

namespace Archivio.CompatibilityTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;

    private static int Main(string[] args)
    {
        if (args.Contains("--ffmpeg-process-runner", StringComparer.Ordinal))
        {
            Run("FFmpeg process runner lifecycle", TestProcessRunner.RunProcessRunnerLifecycle);
            return _failed == 0 ? 0 : 1;
        }

        var ffmpegAvailable = TestProcessRunner.DetectFfmpeg();
        TestFixture.Initialize(ffmpegAvailable);
        Console.WriteLine($"FFmpeg integration: {(ffmpegAvailable ? "available" : "not available; dependent tests will be skipped")}");
        try
        {
            foreach (var test in CompatibilityTestCatalog.All)
            {
                if (test.RequiresFfmpeg && !ffmpegAvailable)
                {
                    Skip(test.DisplayName, "ffmpeg executable was not found within the 5 second discovery timeout");
                    continue;
                }

                Run(test.DisplayName, test.Action);
            }
        }
        finally
        {
            if (!TestFixture.Cleanup())
            {
                _failed++;
                Console.WriteLine($"FAIL test cleanup: temporary directory remained: {TestFixture.Root}");
            }
        }

        Console.WriteLine($"\nCompatibility checks: {_passed} passed, {_failed} failed, {_skipped} skipped.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Skip(string name, string reason)
    {
        _skipped++;
        Console.WriteLine($"SKIP {name}: {reason}");
    }

    private static void Run(string name, Action check)
    {
        try
        {
            check();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}");
        }
    }
}
