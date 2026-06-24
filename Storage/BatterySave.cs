using System;
using System.IO;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// Reads and writes the cartridge's battery-backed RAM as a <c>.sav</c> file — the same format
    /// other emulators use: the raw external-RAM bytes, optionally followed by an MBC3 real-time-clock
    /// footer in the BGB/VBA layout. Keeping raw RAM at the front means <c>.sav</c> files interoperate
    /// with other emulators for the common (no-RTC) case.
    /// </summary>
    internal static class BatterySave
    {
        /// <summary>Writes the cartridge's RAM (and RTC, if present) to <paramref name="path"/>.</summary>
        public static void Save(string path, IMbc mbc)
        {
            byte[] ram = mbc.GetRam();
            if (ram.Length == 0 && mbc is not Mbc3 { HasRtc: true })
            {
                return; // nothing to persist
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write to a temp file then move it into place, so a crash mid-write can't corrupt an
            // existing save.
            string tempPath = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(tempPath)))
            {
                writer.Write(ram);
                if (mbc is Mbc3 { HasRtc: true } mbc3)
                {
                    mbc3.WriteRtcSave(writer);
                }
            }
            File.Move(tempPath, path, overwrite: true);
        }

        /// <summary>
        /// Loads RAM (and RTC, if present) from <paramref name="path"/> into the cartridge. Extra or
        /// missing bytes are tolerated so partial / foreign saves still load as far as they can.
        /// </summary>
        public static void Load(string path, IMbc mbc)
        {
            byte[] data = File.ReadAllBytes(path);
            int ramLength = mbc.GetRam().Length;

            // SetRam copies only as many bytes as the cartridge has, so an appended RTC footer is left
            // untouched here.
            mbc.SetRam(data);

            if (mbc is Mbc3 { HasRtc: true } mbc3 && data.Length >= ramLength + RtcFooterSize)
            {
                using var stream = new MemoryStream(data, ramLength, data.Length - ramLength, writable: false);
                using var reader = new BinaryReader(stream);
                mbc3.ReadRtcSave(reader);
            }

            Log.Information("Loaded battery save: {Path} ({Bytes} bytes)", path, data.Length);
        }

        // Ten 32-bit registers plus a 64-bit timestamp (see Mbc3.WriteRtcSave).
        private const int RtcFooterSize = 10 * 4 + 8;
    }
}
