using System;
using System.IO;
using System.Linq;
using CpuTestHarness;
using Xunit;

namespace GameboySharp.Tests.CpuTests;

/// <summary>
/// Runs the full SM83 (TomHarte / SingleStepTests) CPU vector suite — one file per opcode,
/// 1000 single-instruction tests each — when the vectors are available locally.
///
/// The ~160 MB of vectors are not committed, so the test is <b>skipped</b> unless the
/// <c>SM83_TEST_DIR</c> environment variable points at the folder of opcode JSON files. That
/// keeps CI green by default while letting anyone run a full CPU-accuracy check on demand.
///
/// Download: https://github.com/SingleStepTests/sm83  (the v1/ folder)
/// Run:      SM83_TEST_DIR=/path/to/sm83/v1 dotnet test
/// </summary>
public class Sm83VectorTests
{
    [SkippableFact]
    public void AllOpcodes_MatchHardwareVectors()
    {
        string? directory = Environment.GetEnvironmentVariable("SM83_TEST_DIR");
        Skip.If(string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory),
            "Set SM83_TEST_DIR to the SM83 vector folder (the v1/ folder of SingleStepTests/sm83) to run this test.");

        var files = Directory.GetFiles(directory!, "*.json")
                             .OrderBy(f => f, StringComparer.Ordinal)
                             .ToList();
        Skip.If(files.Count == 0, $"No .json vector files found in {directory}.");

        var runner = new Sm83TestRunner();
        var results = files.Select(runner.RunFile).ToList();

        int totalTests = results.Sum(r => r.Total);
        int totalPassed = results.Sum(r => r.Passed);
        var failures = results.Where(r => !r.FullyPassed).ToList();

        Assert.True(failures.Count == 0,
            $"{totalPassed}/{totalTests} SM83 register/RAM tests passed across {results.Count} opcodes. " +
            $"Failing opcodes: {string.Join("; ", failures.Select(f => $"{f.Name} ({f.Passed}/{f.Total}) e.g. {f.Failures.FirstOrDefault()}"))}");
    }
}
