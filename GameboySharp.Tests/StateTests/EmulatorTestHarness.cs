using System;
using System.IO;

namespace GameboySharp.Tests.StateTests;

/// <summary>
/// Shared helpers for tests that drive a whole <see cref="Emulator"/>: locating a test ROM,
/// building a headless (audio-free) emulator, running frames, and fingerprinting the resulting
/// machine state. These power the reset- and save-state-determinism tests.
/// </summary>
internal static class EmulatorTestHarness
{
    /// <summary>
    /// The ROM to test with, taken solely from the <c>GBSHARP_TEST_ROM</c> environment variable. No
    /// ROM is bundled or auto-discovered, so the ROM-gated tests are strictly opt-in. Returns null
    /// when the variable is unset or points at a missing file, so callers skip rather than fail.
    /// </summary>
    public static string? FindTestRom()
    {
        string? path = Environment.GetEnvironmentVariable("GBSHARP_TEST_ROM");
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    /// <summary>Builds an emulator with audio disabled, so tests never depend on an audio device.</summary>
    public static Emulator CreateHeadless()
    {
        Environment.SetEnvironmentVariable("GBSHARP_AUDIO", "none");
        return new Emulator();
    }

    /// <summary>Runs the emulator for a fixed number of frames.</summary>
    public static void RunFrames(Emulator emulator, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            emulator.RunFrame();
        }
    }

    /// <summary>
    /// Produces a 64-bit FNV-1a fingerprint of the visible machine state — the full framebuffer plus
    /// the CPU register file. Two runs that fingerprint identically are producing identical output;
    /// any missed reset/save-state field shows up here as a mismatch.
    /// </summary>
    public static ulong Fingerprint(Emulator emulator)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong hash = fnvOffset;

        void MixByte(byte b) { hash ^= b; hash *= fnvPrime; }
        void MixValue(ulong value)
        {
            for (int i = 0; i < 8; i++) MixByte((byte)(value >> (i * 8)));
        }

        foreach (uint pixel in emulator.Ppu.GetFrameBuffer())
        {
            MixValue(pixel);
        }

        var cpu = emulator.Cpu;
        MixValue(cpu.PC);
        MixValue(cpu.SP);
        MixValue(cpu.AF);
        MixValue(cpu.BC);
        MixValue(cpu.DE);
        MixValue(cpu.HL);

        // Also fold in work RAM and high RAM so the fingerprint catches memory-state divergence — a
        // missed MMU/WRAM field would otherwise slip past a framebuffer+register-only check.
        for (int addr = 0xC000; addr <= 0xDFFF; addr++) MixByte(emulator.Mmu.ReadByte((ushort)addr));
        for (int addr = 0xFF80; addr <= 0xFFFE; addr++) MixByte(emulator.Mmu.ReadByte((ushort)addr));

        return hash;
    }
}
