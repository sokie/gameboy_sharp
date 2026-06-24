using System.IO;

namespace GameboySharp
{
    /// <summary>
    /// Interface for Memory Bank Controllers (MBCs)
    /// </summary>
    public interface IMbc
    {
        /// <summary>
        /// Reads a byte from the cartridge ROM
        /// </summary>
        /// <param name="address">The address to read from (0x0000-0x7FFF)</param>
        /// <returns>The byte value at the specified address</returns>
        byte ReadRom(ushort address);

        /// <summary>
        /// Writes a byte to the cartridge (used for MBC register writes)
        /// </summary>
        /// <param name="address">The address to write to (0x0000-0x7FFF)</param>
        /// <param name="value">The value to write</param>
        void WriteRom(ushort address, byte value);

        /// <summary>
        /// Reads a byte from external RAM
        /// </summary>
        /// <param name="address">The address to read from (0xA000-0xBFFF)</param>
        /// <returns>The byte value at the specified address</returns>
        byte ReadRam(ushort address);

        /// <summary>
        /// Writes a byte to external RAM
        /// </summary>
        /// <param name="address">The address to write to (0xA000-0xBFFF)</param>
        /// <param name="value">The value to write</param>
        void WriteRam(ushort address, byte value);

        /// <summary>
        /// Gets whether external RAM is enabled
        /// </summary>
        bool IsRamEnabled { get; }

        /// <summary>
        /// Gets the current ROM bank number
        /// </summary>
        int CurrentRomBank { get; }

        /// <summary>
        /// Gets the current RAM bank number
        /// </summary>
        int CurrentRamBank { get; }

        /// <summary>
        /// Gets whether this cartridge has battery-backed RAM. When true, the cartridge's RAM is
        /// meant to survive a power-off, so the emulator persists it to a <c>.sav</c> file.
        /// </summary>
        bool HasBattery { get; }

        /// <summary>
        /// Returns the cartridge's external RAM (the bytes a real battery would retain), or an empty
        /// array if the cartridge has none. This is the live backing buffer: treat it as read-only.
        /// </summary>
        byte[] GetRam();

        /// <summary>
        /// Replaces the cartridge's external RAM, e.g. when restoring a <c>.sav</c> file. Only as many
        /// bytes as the cartridge actually has are copied; extra bytes are ignored.
        /// </summary>
        void SetRam(byte[] data);

        /// <summary>
        /// Writes the controller's mutable state (banking registers, RAM, and any clock) to a save
        /// state. The reader side is <see cref="LoadState"/>; the two must stay in lockstep.
        /// </summary>
        void SaveState(BinaryWriter writer);

        /// <summary>
        /// Restores the controller's mutable state previously written by <see cref="SaveState"/>.
        /// </summary>
        void LoadState(BinaryReader reader);
    }
}