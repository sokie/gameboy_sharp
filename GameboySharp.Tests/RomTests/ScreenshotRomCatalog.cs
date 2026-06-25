using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameboySharp.Tests.RomTests;

/// <summary>Which hardware a screenshot case runs as — picks the palette/colour path and reference image.</summary>
internal enum GbMode
{
    Dmg,
    Cgb,
}

/// <summary>One screenshot test case: a ROM, its candidate reference images, and how to run it.</summary>
internal sealed class ScreenshotRomCase
{
    /// <summary>Short identifier, used as the test's display label.</summary>
    public string Name { get; }

    /// <summary>ROM location relative to the c-sp release root.</summary>
    public string RomRelPath { get; }

    /// <summary>Reference-image candidates relative to the release root, tried in order (see <see cref="Create"/>).</summary>
    public IReadOnlyList<string> ReferenceRelPaths { get; }

    /// <summary>Whether to run the ROM as a monochrome DMG or a colour CGB.</summary>
    public GbMode Mode { get; }

    /// <summary>How many frames to render before capturing the framebuffer.</summary>
    public int Frames { get; }

    /// <summary>
    /// True if the framebuffer is expected to match the reference exactly (a mismatch then fails the
    /// test as a regression). False marks a documented accuracy gap: the test skips while it mismatches,
    /// but stops skipping — flagging itself for promotion — once the emulator improves enough to match.
    /// </summary>
    public bool ExpectedToPass { get; }

    private ScreenshotRomCase(string name, string romRelPath, IReadOnlyList<string> referenceRelPaths,
                              GbMode mode, int frames, bool expectedToPass)
    {
        Name = name;
        RomRelPath = romRelPath;
        ReferenceRelPaths = referenceRelPaths;
        Mode = mode;
        Frames = frames;
        ExpectedToPass = expectedToPass;
    }

    /// <summary>
    /// Builds a case, deriving the ordered list of reference-image candidates from a base
    /// <paramref name="referenceStem"/> (directory + file name without extension). The c-sp release
    /// names screenshots a few different ways, so we try, in order: <c>&lt;stem&gt;-&lt;mode&gt;.png</c>
    /// (a per-model image), <c>&lt;stem&gt;-dmg-cgb.png</c> (one image shared by both models), then plain
    /// <c>&lt;stem&gt;.png</c>. The first that exists on disk wins.
    /// </summary>
    public static ScreenshotRomCase Create(string name, string romRelPath, string referenceStem,
                                           GbMode mode, int frames, bool expectedToPass)
    {
        string modeSuffix = mode == GbMode.Cgb ? "cgb" : "dmg";
        var candidates = new[]
        {
            $"{referenceStem}-{modeSuffix}.png",
            $"{referenceStem}-dmg-cgb.png",
            $"{referenceStem}.png",
        };
        return new ScreenshotRomCase(name, romRelPath, candidates, mode, frames, expectedToPass);
    }

    public override string ToString() => Name; // shown as the case label in the test runner
}

/// <summary>
/// The catalog of audio/PPU screenshot test cases, plus path resolution against the
/// <c>GBSHARP_TEST_ROMS_DIR</c> directory — an extracted
/// <see href="https://github.com/c-sp/game-boy-test-roms">c-sp/game-boy-test-roms</see> v7.0 release,
/// which co-locates each ROM with its reference screenshot.
/// </summary>
internal static class ScreenshotRomCatalog
{
    /// <summary>
    /// The release root from <c>GBSHARP_TEST_ROMS_DIR</c>, or null when it is unset or points at a
    /// missing folder — so the tests skip rather than fail, exactly like the SM83 vector suite.
    /// </summary>
    public static string? RomsDir()
    {
        string? dir = Environment.GetEnvironmentVariable("GBSHARP_TEST_ROMS_DIR");
        return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// The four flagship + CGB cases. Pass/fail status below is the measured result as of writing;
    /// the <c>ExpectedToPass: false</c> cases are documented accuracy gaps that skip while they
    /// mismatch and self-promote once fixed (see <see cref="ScreenshotRomCase.ExpectedToPass"/>):
    /// <list type="bullet">
    /// <item><b>dmg-acid2</b> / <b>cgb-acid2</b> — both pass pixel-exact, covering the mono and colour
    /// PPU render paths.</item>
    /// <item><b>dmg_sound</b> / <b>cgb_sound</b> — Blargg APU suites captured at a fixed duration (they
    /// loop forever). The frequency-sweep and length/power sub-tests now pass (dmg_sound 9/12,
    /// cgb_sound 11/12); the holdouts are the "wave RAM access while on" tests (09/10/12), which need
    /// cycle-accurate wave-position timing this line-based APU doesn't model — so the combined ROMs
    /// still don't fully pass.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<ScreenshotRomCase> Cases { get; } = new[]
    {
        ScreenshotRomCase.Create("dmg-acid2", "dmg-acid2/dmg-acid2.gb",
            "dmg-acid2/dmg-acid2", GbMode.Dmg, frames: 60, expectedToPass: true),
        ScreenshotRomCase.Create("cgb-acid2", "cgb-acid2/cgb-acid2.gbc",
            "cgb-acid2/cgb-acid2", GbMode.Cgb, frames: 60, expectedToPass: true),
        ScreenshotRomCase.Create("dmg_sound", "blargg/dmg_sound/dmg_sound.gb",
            "blargg/dmg_sound/dmg_sound", GbMode.Dmg, frames: 2200, expectedToPass: false),
        ScreenshotRomCase.Create("cgb_sound", "blargg/cgb_sound/cgb_sound.gb",
            "blargg/cgb_sound/cgb_sound", GbMode.Cgb, frames: 2200, expectedToPass: false),
    };

    /// <summary>Absolute path to a case's ROM under the given release root.</summary>
    public static string RomPath(string romsDir, ScreenshotRomCase testCase) =>
        Path.Combine(romsDir, testCase.RomRelPath);

    /// <summary>
    /// The first existing reference-image candidate for a case under the given release root, or null
    /// if none of them exist (e.g. an unexpected release layout).
    /// </summary>
    public static string? ResolveReferencePath(string romsDir, ScreenshotRomCase testCase) =>
        testCase.ReferenceRelPaths
            .Select(rel => Path.Combine(romsDir, rel))
            .FirstOrDefault(File.Exists);
}
