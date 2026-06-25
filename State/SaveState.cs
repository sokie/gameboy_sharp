using System;
using System.IO;
using System.Text;
using Serilog;

namespace GameboySharp
{
    /// <summary>A decoded save-state thumbnail: raw RGBA pixels plus its dimensions.</summary>
    internal readonly struct SaveStateThumbnail
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Rgba;

        public SaveStateThumbnail(int width, int height, byte[] rgba)
        {
            Width = width;
            Height = height;
            Rgba = rgba;
        }
    }

    /// <summary>
    /// The save-state container format and (de)serialiser. A state file is:
    ///
    ///   magic "GBSS" · version · ROM guard (title + header checksum) · thumbnail · machine blocks
    ///
    /// The ROM guard makes sure a state can only be restored into the same game, and the version lets
    /// us reject states written by an incompatible build. The thumbnail sits right after the guard so a
    /// preview can be read cheaply without parsing the (much larger) machine state. The machine blocks
    /// are written and read in a fixed component order; each component owns its own block layout.
    /// </summary>
    internal static class SaveState
    {
        private static readonly byte[] Magic = { (byte)'G', (byte)'B', (byte)'S', (byte)'S' };
        // Version 2 added APU accuracy state (the sweep "negate used" latch); v1 states are rejected.
        private const int Version = 2;

        // Thumbnails are the framebuffer downscaled by 2 (160x144 → 80x72) with nearest sampling.
        private const int ThumbnailWidth = GameboyConstants.ScreenWidth / 2;   // 80
        private const int ThumbnailHeight = GameboyConstants.ScreenHeight / 2; // 72

        /// <summary>Serialises the entire emulator state (plus a thumbnail) to the stream.</summary>
        public static void Save(Emulator emulator, Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Magic);
            writer.Write(Version);
            WriteRomGuard(writer, emulator);
            WriteThumbnail(writer, emulator);

            // Machine blocks, in a fixed order that Load mirrors exactly.
            emulator.Cpu.SaveState(writer);
            emulator.Mmu.SaveState(writer);
            emulator.Ppu.SaveState(writer);
            emulator.Timer.SaveState(writer);
            emulator.Apu.SaveState(writer);
            emulator.Joypad.SaveState(writer);
        }

        /// <summary>
        /// Restores emulator state from the stream. Returns false (without modifying the emulator) if
        /// the data isn't a save state, is from an incompatible version, or belongs to a different ROM.
        /// </summary>
        public static bool TryLoad(Emulator emulator, Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (!ReadAndCheckHeader(reader, out string title, out byte checksum))
            {
                return false;
            }

            if (title != emulator.RomTitle || checksum != emulator.RomHeaderChecksum)
            {
                Log.Warning("Save state belongs to a different ROM ('{StateTitle}' vs '{RomTitle}'); ignoring.",
                            title, emulator.RomTitle);
                return false;
            }

            SkipThumbnail(reader);

            emulator.Cpu.LoadState(reader);
            emulator.Mmu.LoadState(reader);
            emulator.Ppu.LoadState(reader);
            emulator.Timer.LoadState(reader);
            emulator.Apu.LoadState(reader);
            emulator.Joypad.LoadState(reader);
            return true;
        }

        /// <summary>
        /// Reads just the thumbnail from a state file for previews, skipping the machine state. Returns
        /// null if the file isn't a readable save state.
        /// </summary>
        public static SaveStateThumbnail? TryReadThumbnail(Stream stream)
        {
            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                if (!ReadAndCheckHeader(reader, out _, out _))
                {
                    return null;
                }
                return ReadThumbnail(reader);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read save-state thumbnail.");
                return null;
            }
        }

        private static void WriteRomGuard(BinaryWriter writer, Emulator emulator)
        {
            writer.Write(emulator.RomTitle);
            writer.Write(emulator.RomHeaderChecksum);
        }

        /// <summary>Reads and validates the magic + version, then returns the ROM guard fields.</summary>
        private static bool ReadAndCheckHeader(BinaryReader reader, out string title, out byte checksum)
        {
            title = "";
            checksum = 0;

            byte[] magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
            {
                return false;
            }
            for (int i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i]) return false;
            }

            int version = reader.ReadInt32();
            if (version != Version)
            {
                Log.Warning("Save state version {Version} is not supported (expected {Expected}).", version, Version);
                return false;
            }

            title = reader.ReadString();
            checksum = reader.ReadByte();
            return true;
        }

        private static void WriteThumbnail(BinaryWriter writer, Emulator emulator)
        {
            writer.Write(ThumbnailWidth);
            writer.Write(ThumbnailHeight);

            uint[] frame = emulator.Ppu.GetFrameBuffer();
            // Nearest-neighbour downscale by 2. Framebuffer pixels are AABBGGRR, i.e. memory bytes
            // [R, G, B, A] — exactly the RGBA order we want to store.
            for (int y = 0; y < ThumbnailHeight; y++)
            {
                int srcY = y * 2;
                for (int x = 0; x < ThumbnailWidth; x++)
                {
                    int srcX = x * 2;
                    uint pixel = frame[srcY * GameboyConstants.ScreenWidth + srcX];
                    writer.Write((byte)(pixel & 0xFF));         // R
                    writer.Write((byte)((pixel >> 8) & 0xFF));  // G
                    writer.Write((byte)((pixel >> 16) & 0xFF)); // B
                    writer.Write((byte)((pixel >> 24) & 0xFF)); // A
                }
            }
        }

        private static SaveStateThumbnail ReadThumbnail(BinaryReader reader)
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            byte[] rgba = reader.ReadBytes(width * height * 4);
            return new SaveStateThumbnail(width, height, rgba);
        }

        private static void SkipThumbnail(BinaryReader reader)
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            // ReadBytes (rather than a seek) keeps this correct even on non-seekable streams.
            reader.ReadBytes(width * height * 4);
        }
    }
}
