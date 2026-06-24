using System.IO;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// Simple ROM-only cartridge (no MBC)
    /// </summary>
    public class RomOnly : IMbc
    {
        private readonly byte[] _romData;

        public bool IsRamEnabled => false;
        public int CurrentRomBank => 0;
        public int CurrentRamBank => 0;
        public bool HasBattery => false;

        public RomOnly(byte[] romData)
        {
            _romData = romData ?? throw new ArgumentNullException(nameof(romData));
            Log.Information($"ROM-only cartridge initialized: {_romData.Length / 1024}KB");
        }

        public byte ReadRom(ushort address)
        {
            if (address < _romData.Length)
            {
                return _romData[address];
            }
            else
            {
                Log.Warning($"ROM-only: Read out of bounds at address 0x{address:X4}");
                return 0xFF;
            }
        }

        public void WriteRom(ushort address, byte value)
        {
            // ROM-only cartridges ignore writes to ROM area
            Log.Debug($"ROM-only: Write to ROM address 0x{address:X4} ignored (value: 0x{value:X2})");
        }

        public byte ReadRam(ushort address)
        {
            return 0xFF; // No RAM present
        }

        public void WriteRam(ushort address, byte value)
        {
            // No RAM present, ignore writes
        }

        // A ROM-only cartridge has no battery RAM and no banking registers, so there is nothing
        // to expose or to persist. These are empty by design (see IMbc for the contract).
        public byte[] GetRam() => System.Array.Empty<byte>();

        public void SetRam(byte[] data) { /* No RAM to restore. */ }

        public void SaveState(BinaryWriter writer) { /* No mutable state. */ }

        public void LoadState(BinaryReader reader) { /* No mutable state. */ }
    }
}