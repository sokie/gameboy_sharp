using Xunit;

namespace GameboySharp.Tests.StateTests;

/// <summary>
/// Guards the machine-reset path. A reset must be a true power cycle: every emulated component has
/// to return to its boot state, or the game will misbehave after a runtime reset/reload. A missed
/// field shows up here as a fingerprint mismatch.
///
/// These tests need a real ROM. They are skipped (not failed) when none is available — see
/// <see cref="EmulatorTestHarness.FindTestRom"/>.
/// </summary>
public class ResetDeterminismTests
{
    private const int Frames = 120; // ~2 seconds of emulation; enough to get past boot into the game.

    [SkippableFact]
    public void Reset_ReproducesFreshBoot()
    {
        string? rom = EmulatorTestHarness.FindTestRom();
        Skip.If(rom == null, "Set GBSHARP_TEST_ROM to a .gb/.gbc ROM to run this test.");

        using var emulator = EmulatorTestHarness.CreateHeadless();

        emulator.LoadRom(rom!);
        EmulatorTestHarness.RunFrames(emulator, Frames);
        ulong afterFreshBoot = EmulatorTestHarness.Fingerprint(emulator);

        // A reset is a power cycle, so re-running the same number of frames must land in exactly the
        // same place the fresh boot did.
        emulator.Reset();
        EmulatorTestHarness.RunFrames(emulator, Frames);
        ulong afterReset = EmulatorTestHarness.Fingerprint(emulator);

        Assert.Equal(afterFreshBoot, afterReset);
    }

    [SkippableFact]
    public void TwoFreshBoots_AreIdentical()
    {
        // Baseline determinism check: with no input, two independent runs of the same ROM must match.
        // If this ever fails, the emulator has hidden non-determinism and the reset test above is moot.
        string? rom = EmulatorTestHarness.FindTestRom();
        Skip.If(rom == null, "Set GBSHARP_TEST_ROM to a .gb/.gbc ROM to run this test.");

        using var first = EmulatorTestHarness.CreateHeadless();
        first.LoadRom(rom!);
        EmulatorTestHarness.RunFrames(first, Frames);

        using var second = EmulatorTestHarness.CreateHeadless();
        second.LoadRom(rom!);
        EmulatorTestHarness.RunFrames(second, Frames);

        Assert.Equal(EmulatorTestHarness.Fingerprint(first), EmulatorTestHarness.Fingerprint(second));
    }
}
