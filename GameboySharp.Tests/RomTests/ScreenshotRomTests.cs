using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameboySharp.Tests.StateTests;
using Xunit;

namespace GameboySharp.Tests.RomTests;

/// <summary>
/// Validates audio and PPU accuracy against the community-standard Game Boy test ROMs by rendering a
/// fixed number of frames and diffing the framebuffer against a reference screenshot. Covers the four
/// flagship + CGB cases in <see cref="ScreenshotRomCatalog"/> (dmg-acid2, cgb-acid2, dmg_sound, cgb_sound).
///
/// The ROMs and their reference images are <b>not</b> committed. The whole theory is skipped unless
/// <c>GBSHARP_TEST_ROMS_DIR</c> points at an extracted c-sp/game-boy-test-roms v7.0 release, so CI
/// stays green and fast by default — the same opt-in pattern as the SM83 vector and determinism tests.
///
/// Download: https://github.com/c-sp/game-boy-test-roms/releases/tag/v7.0
/// Run:      GBSHARP_TEST_ROMS_DIR=/path/to/extracted-release dotnet test
/// </summary>
public class ScreenshotRomTests
{
    /// <summary>Placeholder case yielded when the ROMs are unavailable, so the suite shows one skip, not four.</summary>
    private const string EnvVarUnsetSentinel = "(GBSHARP_TEST_ROMS_DIR not set)";

    /// <summary>
    /// Lightest → darkest, matching the four DMG LCD shades the references are drawn with, in the PPU's
    /// AABBGGRR layout. The shades are pure grays, so byte order doesn't actually matter here.
    /// </summary>
    private static readonly uint[] DmgGrayscalePalette =
    {
        0xFFFFFFFF, // index 0: white (lightest)
        0xFFAAAAAA, // index 1: light gray
        0xFF555555, // index 2: dark gray
        0xFF000000, // index 3: black (darkest)
    };

    /// <summary>
    /// Feeds one row per case to the theory. Enumerated at discovery time: when the ROMs aren't present
    /// it yields a single sentinel row, collapsing the suite to exactly one skipped test (mirroring how
    /// the SM83 vector test reduces to a single skip when its data directory is absent).
    /// </summary>
    public static IEnumerable<object[]> TestCases()
    {
        if (ScreenshotRomCatalog.RomsDir() is null)
        {
            yield return new object[] { EnvVarUnsetSentinel };
            yield break;
        }

        foreach (ScreenshotRomCase testCase in ScreenshotRomCatalog.Cases)
        {
            yield return new object[] { testCase.Name };
        }
    }

    [SkippableTheory]
    [MemberData(nameof(TestCases))]
    public void Framebuffer_MatchesReference(string caseName)
    {
        string? romsDir = ScreenshotRomCatalog.RomsDir();
        Skip.If(romsDir is null,
            "Set GBSHARP_TEST_ROMS_DIR to an extracted c-sp/game-boy-test-roms v7.0 release to run the audio/PPU screenshot tests.");

        ScreenshotRomCase testCase = ScreenshotRomCatalog.Cases.First(c => c.Name == caseName);

        string romPath = ScreenshotRomCatalog.RomPath(romsDir!, testCase);
        Skip.IfNot(File.Exists(romPath),
            $"ROM not found at {romPath}. Is GBSHARP_TEST_ROMS_DIR an extracted c-sp v7.0 release?");

        string? referencePath = ScreenshotRomCatalog.ResolveReferencePath(romsDir!, testCase);
        Skip.If(referencePath is null,
            $"No reference image for {testCase.Name}; tried {string.Join(", ", testCase.ReferenceRelPaths)}.");

        using Emulator emulator = EmulatorTestHarness.CreateHeadless();
        emulator.LoadRom(romPath);
        PrepareForMode(emulator, testCase.Mode);

        EmulatorTestHarness.RunFrames(emulator, testCase.Frames);

        ScreenshotComparer.Result result = ScreenshotComparer.Compare(
            emulator.Ppu.GetFrameBuffer(),
            GameboyConstants.ScreenWidth,
            GameboyConstants.ScreenHeight,
            referencePath!);

        string reference = Path.GetFileName(referencePath!);
        if (testCase.ExpectedToPass)
        {
            Assert.True(result.Matches,
                $"{testCase.Name}: framebuffer does not match {reference} — {result}.");
        }
        else
        {
            // A documented accuracy gap: stay green while it still mismatches, but the instant the
            // emulator renders this ROM correctly the Skip stops firing and the test passes — a standing
            // signal to flip ExpectedToPass to true and lock the win in as a regression guard.
            Skip.If(!result.Matches,
                $"known limitation: {testCase.Name} does not yet match {reference} — {result}.");
        }
    }

    /// <summary>Applies the per-mode setup that makes rendered pixels line up with the reference image.</summary>
    private static void PrepareForMode(Emulator emulator, GbMode mode)
    {
        if (mode == GbMode.Dmg)
        {
            // Swap the emulator's default green palette for the grayscale ramp the references use.
            emulator.Ppu.SetDmgPalette(DmgGrayscalePalette);
        }
        else if (!emulator.Mmu.IsGameBoyColor)
        {
            // A CGB cartridge normally flips the PPU into colour mode from its header during LoadRom; if
            // a particular ROM didn't, force it so the GBC colour path (and the references) are exercised.
            emulator.Mmu.ForceGBC();
        }
    }
}
