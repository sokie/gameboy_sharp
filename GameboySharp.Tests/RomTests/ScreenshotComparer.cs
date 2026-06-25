using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameboySharp.Tests.RomTests;

/// <summary>
/// Compares an emulator framebuffer against a reference PNG screenshot, pixel for pixel. Pure and
/// emulator-agnostic: it takes the raw framebuffer (as produced by <c>Ppu.GetFrameBuffer()</c>) plus
/// a PNG path, so the same routine validates both PPU and audio test ROMs, which are all checked by
/// rendering a frame and diffing it against a known-good image.
/// </summary>
internal static class ScreenshotComparer
{
    /// <summary>The outcome of a framebuffer-vs-reference comparison.</summary>
    internal readonly struct Result
    {
        /// <summary>True when every pixel in the framebuffer equals the reference.</summary>
        public bool Matches { get; init; }

        /// <summary>How many pixels differ.</summary>
        public int MismatchCount { get; init; }

        /// <summary>Total pixels compared (width × height).</summary>
        public int TotalPixels { get; init; }

        /// <summary>Percentage of pixels that matched, 0–100.</summary>
        public double MatchPercent { get; init; }

        /// <summary>The first differing pixel in row-major order, or null when none differ.</summary>
        public (int X, int Y)? FirstMismatch { get; init; }

        public override string ToString() =>
            Matches
                ? $"exact match ({TotalPixels} px)"
                : $"{MismatchCount}/{TotalPixels} px differ ({MatchPercent:F2}% match)" +
                  (FirstMismatch is { } p ? $", first at ({p.X},{p.Y})" : "");
    }

    /// <summary>
    /// Compares <paramref name="frameBuffer"/> (row-major, one packed pixel per entry) to the PNG at
    /// <paramref name="referencePngPath"/>. A length or dimension mismatch throws, because that points
    /// at a wrong reference image or a test wiring bug rather than an emulator inaccuracy.
    /// </summary>
    public static Result Compare(uint[] frameBuffer, int width, int height, string referencePngPath)
    {
        int total = width * height;
        if (frameBuffer.Length != total)
        {
            throw new ArgumentException(
                $"Framebuffer has {frameBuffer.Length} pixels, expected {total} ({width}x{height}).",
                nameof(frameBuffer));
        }

        using Image<Rgba32> reference = Image.Load<Rgba32>(referencePngPath);
        if (reference.Width != width || reference.Height != height)
        {
            throw new ArgumentException(
                $"Reference image is {reference.Width}x{reference.Height}, expected {width}x{height}: {referencePngPath}",
                nameof(referencePngPath));
        }

        // Pull the whole reference into a flat, row-major array so it indexes the same way the
        // framebuffer does ([y * width + x]) — the two are then a straight element-by-element diff.
        var expected = new Rgba32[total];
        reference.CopyPixelDataTo(expected);

        int mismatches = 0;
        (int X, int Y)? firstMismatch = null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // The framebuffer packs each pixel as AABBGGRR in a uint (the PPU's native layout);
                // pull the three colour channels back out. Alpha is ignored: pixels are always opaque
                // on both sides, and the references carry no meaningful transparency.
                uint packed = frameBuffer[y * width + x];
                byte r = (byte)(packed & 0xFF);
                byte g = (byte)((packed >> 8) & 0xFF);
                byte b = (byte)((packed >> 16) & 0xFF);

                Rgba32 want = expected[y * width + x];
                if (r != want.R || g != want.G || b != want.B)
                {
                    mismatches++;
                    firstMismatch ??= (x, y);
                }
            }
        }

        return new Result
        {
            Matches = mismatches == 0,
            MismatchCount = mismatches,
            TotalPixels = total,
            MatchPercent = 100.0 * (total - mismatches) / total,
            FirstMismatch = firstMismatch,
        };
    }
}
