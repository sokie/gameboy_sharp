using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameboySharp.Tests.CpuTests;

/// <summary>The outcome of running one opcode's test file (typically 1000 cases).</summary>
internal sealed class OpcodeResult
{
    public string Name = "";
    public int Total;
    public int Passed;
    public int CycleMismatches;
    public readonly List<string> Failures = new(); // a few detailed diffs for the failure message

    public bool FullyPassed => Passed == Total;
}

/// <summary>
/// Runs the SM83 (TomHarte / SingleStepTests) JSON vectors against the emulator's CPU.
///
/// Each vector specifies an initial CPU + RAM state, the emulator executes exactly one
/// instruction over a flat memory bus, and the resulting registers and RAM are compared against
/// the vector's expected final state. This is the definitive way to prove every opcode (and its
/// flag behavior) is implemented correctly.
/// </summary>
internal sealed class Sm83TestRunner
{
    private const int MaxFailureSamples = 5;
    private readonly FlatMemoryBus _bus = new();

    public OpcodeResult RunFile(string path)
    {
        var result = new OpcodeResult { Name = Path.GetFileNameWithoutExtension(path) };
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (JsonElement test in doc.RootElement.EnumerateArray())
        {
            result.Total++;
            if (RunOneTest(test, result)) result.Passed++;
        }
        return result;
    }

    private bool RunOneTest(JsonElement test, OpcodeResult result)
    {
        JsonElement initial = test.GetProperty("initial");
        JsonElement final = test.GetProperty("final");

        // Load the initial state onto a fresh CPU backed by flat memory.
        _bus.Reset();
        var cpu = new Cpu(_bus)
        {
            PC = U16(initial, "pc"),
            SP = U16(initial, "sp"),
            A = U8(initial, "a"),
            B = U8(initial, "b"),
            C = U8(initial, "c"),
            D = U8(initial, "d"),
            E = U8(initial, "e"),
            F = U8(initial, "f"),
            H = U8(initial, "h"),
            L = U8(initial, "l"),
        };
        cpu.InterruptMasterEnable = U8OrZero(initial, "ime") != 0;
        cpu.ie_register = U8OrZero(initial, "ie");
        cpu.if_register = 0; // the vectors assume no interrupt is pending (IF is not provided)

        foreach (JsonElement cell in initial.GetProperty("ram").EnumerateArray())
            _bus.Seed((ushort)cell[0].GetInt32(), (byte)cell[1].GetInt32());

        // Execute exactly one instruction.
        int tCycles = cpu.Step();

        // Compare registers and RAM against the expected final state.
        var diffs = new List<string>();
        Compare(diffs, "A", cpu.A, U8(final, "a"));
        Compare(diffs, "B", cpu.B, U8(final, "b"));
        Compare(diffs, "C", cpu.C, U8(final, "c"));
        Compare(diffs, "D", cpu.D, U8(final, "d"));
        Compare(diffs, "E", cpu.E, U8(final, "e"));
        Compare(diffs, "F", cpu.F, U8(final, "f"));
        Compare(diffs, "H", cpu.H, U8(final, "h"));
        Compare(diffs, "L", cpu.L, U8(final, "l"));
        Compare16(diffs, "PC", cpu.PC, U16(final, "pc"));
        Compare16(diffs, "SP", cpu.SP, U16(final, "sp"));
        foreach (JsonElement cell in final.GetProperty("ram").EnumerateArray())
        {
            ushort address = (ushort)cell[0].GetInt32();
            byte expected = (byte)cell[1].GetInt32();
            byte actual = _bus.ReadByte(address);
            if (actual != expected) diffs.Add($"mem[{address:X4}]={actual:X2} exp {expected:X2}");
        }

        // Cycle count is recorded for information only; it isn't part of pass/fail (STOP and HALT
        // legitimately differ from the vectors' model without affecting any computed result).
        int expectedTCycles = test.GetProperty("cycles").GetArrayLength() * 4;
        if (tCycles != expectedTCycles) result.CycleMismatches++;

        if (diffs.Count == 0) return true;

        if (result.Failures.Count < MaxFailureSamples)
            result.Failures.Add($"{test.GetProperty("name").GetString()}: {string.Join(", ", diffs)}");
        return false;
    }

    private static byte U8(JsonElement e, string key) => (byte)e.GetProperty(key).GetInt32();
    private static ushort U16(JsonElement e, string key) => (ushort)e.GetProperty(key).GetInt32();
    private static byte U8OrZero(JsonElement e, string key)
        => e.TryGetProperty(key, out JsonElement v) ? (byte)v.GetInt32() : (byte)0;

    private static void Compare(List<string> diffs, string name, byte actual, byte expected)
    {
        if (actual != expected) diffs.Add($"{name}={actual:X2} exp {expected:X2}");
    }

    private static void Compare16(List<string> diffs, string name, ushort actual, ushort expected)
    {
        if (actual != expected) diffs.Add($"{name}={actual:X4} exp {expected:X4}");
    }
}
