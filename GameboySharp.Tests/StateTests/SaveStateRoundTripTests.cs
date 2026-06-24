using System.IO;
using Xunit;

namespace GameboySharp.Tests.StateTests;

/// <summary>
/// The core save-state correctness guard. A save state must capture enough of the machine that
/// loading it and continuing produces exactly the same output as if no save/load had happened. This
/// is what catches the "easy-to-miss" latches (HALT bug, EI delay, PPU dot/window counters, timer
/// accumulators, MBC/bank state, …): forget one and the fingerprints diverge.
///
/// Skipped (not failed) when no ROM is available — see <see cref="EmulatorTestHarness.FindTestRom"/>.
/// </summary>
public class SaveStateRoundTripTests
{
    private const int WarmupFrames = 120;     // run into the game before snapshotting
    private const int ContinuationFrames = 90; // how far to run after the snapshot when comparing

    [SkippableFact]
    public void LoadingASavedState_ResumesIdentically()
    {
        string? rom = EmulatorTestHarness.FindTestRom();
        Skip.If(rom == null, "Set GBSHARP_TEST_ROM to a .gb/.gbc ROM to run this test.");

        using var emulator = EmulatorTestHarness.CreateHeadless();
        emulator.LoadRom(rom!);
        EmulatorTestHarness.RunFrames(emulator, WarmupFrames);

        // Snapshot here.
        using var stateStream = new MemoryStream();
        SaveState.Save(emulator, stateStream);

        // Reference continuation: keep running from the live machine.
        EmulatorTestHarness.RunFrames(emulator, ContinuationFrames);
        ulong reference = EmulatorTestHarness.Fingerprint(emulator);

        // Now rewind by loading the snapshot back in, then run the same number of frames.
        stateStream.Position = 0;
        bool loaded = SaveState.TryLoad(emulator, stateStream);
        Assert.True(loaded, "Save state failed to load back into the same ROM.");

        EmulatorTestHarness.RunFrames(emulator, ContinuationFrames);
        ulong afterReload = EmulatorTestHarness.Fingerprint(emulator);

        Assert.Equal(reference, afterReload);
    }

    [SkippableFact]
    public void SaveState_RejectsAMismatchedRom()
    {
        // The ROM guard must refuse a state whose title/checksum doesn't match the loaded game, so a
        // state can't be loaded into the wrong cartridge and corrupt it.
        string? rom = EmulatorTestHarness.FindTestRom();
        Skip.If(rom == null, "Set GBSHARP_TEST_ROM to a .gb/.gbc ROM to run this test.");

        using var emulator = EmulatorTestHarness.CreateHeadless();
        emulator.LoadRom(rom!);
        EmulatorTestHarness.RunFrames(emulator, 10);

        using var stateStream = new MemoryStream();
        SaveState.Save(emulator, stateStream);

        // Corrupt the stored ROM title so the guard should reject it. The title is written as a
        // length-prefixed UTF-8 string just after the 4-byte magic and 4-byte version, so the first
        // title character lives at offset 9.
        byte[] bytes = stateStream.ToArray();
        bytes[9] ^= 0xFF;

        using var tampered = new MemoryStream(bytes);
        bool loaded = SaveState.TryLoad(emulator, tampered);
        Assert.False(loaded, "A state with a mismatched ROM guard should be rejected.");
    }
}
