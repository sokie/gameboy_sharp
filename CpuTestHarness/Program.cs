using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CpuTestHarness
{
    // Validates the emulator's SM83/LR35902 CPU against the SingleStepTests (TomHarte) JSON
    // vectors: one file per opcode, 1000 single-instruction tests each.
    //
    // Get the vectors (the v1/ folder) from https://github.com/SingleStepTests/sm83 and point
    // the harness at the directory:
    //   dotnet run --project CpuTestHarness -- <dir-of-json> [opcode-filter]
    //   e.g. dotnet run --project CpuTestHarness -- ~/sm83/v1
    //        dotnet run --project CpuTestHarness -- ~/sm83/v1 cb   (only CB-prefixed opcodes)
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string directory = args.Length > 0 ? args[0] : "/tmp/sm83tests";
            string? filter = args.Length > 1 ? args[1] : null;

            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"Test directory not found: {directory}");
                Console.WriteLine("Download the vectors from https://github.com/SingleStepTests/sm83 (the v1/ folder).");
                Console.WriteLine("Usage: dotnet run --project CpuTestHarness -- <dir-of-json> [opcode-filter]");
                return 2;
            }

            var files = Directory.GetFiles(directory, "*.json")
                                 .OrderBy(f => f, StringComparer.Ordinal)
                                 .ToList();
            if (filter is not null)
            {
                files = files.Where(f => Path.GetFileNameWithoutExtension(f)
                                             .Contains(filter, StringComparison.OrdinalIgnoreCase))
                             .ToList();
            }

            if (files.Count == 0)
            {
                Console.WriteLine("No matching .json test files found.");
                return 2;
            }

            Console.WriteLine($"Running SM83 CPU tests from {directory} ({files.Count} opcode file(s))...");

            var runner = new Sm83TestRunner();
            var results = new List<OpcodeResult>(files.Count);
            var stopwatch = Stopwatch.StartNew();
            foreach (string file in files) results.Add(runner.RunFile(file));
            stopwatch.Stop();

            return Report(results, stopwatch.ElapsedMilliseconds);
        }

        private static int Report(List<OpcodeResult> results, long elapsedMs)
        {
            int totalTests = results.Sum(r => r.Total);
            int totalPassed = results.Sum(r => r.Passed);
            int fullyPassed = results.Count(r => r.FullyPassed);
            int cycleMismatches = results.Sum(r => r.CycleMismatches);
            int imeMismatches = results.Sum(r => r.ImeMismatches);

            Console.WriteLine();
            Console.WriteLine("=== SM83 CPU test results ===");
            Console.WriteLine($"opcodes fully passing : {fullyPassed}/{results.Count}");
            Console.WriteLine($"individual tests      : {totalPassed}/{totalTests} ({100.0 * totalPassed / Math.Max(1, totalTests):F2}%)");
            Console.WriteLine($"cycle mismatches      : {cycleMismatches} (informational)");
            Console.WriteLine($"IME mismatches        : {imeMismatches} (informational; SM83 docs say ignore)");
            Console.WriteLine($"elapsed               : {elapsedMs} ms");

            var failed = results.Where(r => !r.FullyPassed).ToList();
            if (failed.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"--- {failed.Count} opcode(s) with register/RAM failures ---");
                foreach (OpcodeResult r in failed)
                {
                    string sample = r.Failures.FirstOrDefault() ?? "";
                    Console.WriteLine($"  {r.Name}: {r.Passed}/{r.Total}   e.g. {sample}");
                }
            }

            // Cycle counts are reported separately because they don't affect register correctness.
            var cycleOffenders = results.Where(r => r.CycleMismatches > 0).ToList();
            if (cycleOffenders.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"--- {cycleOffenders.Count} opcode(s) with cycle-count mismatches (informational) ---");
                foreach (OpcodeResult r in cycleOffenders)
                    Console.WriteLine($"  {r.Name}: {r.CycleMismatches}/{r.Total} cases off");
            }

            if (failed.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("ALL REGISTER/RAM TESTS PASSED ✓");
                return 0;
            }
            return 1;
        }
    }
}
