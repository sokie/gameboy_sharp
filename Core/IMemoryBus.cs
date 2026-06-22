namespace GameboySharp
{
    /// <summary>
    /// The view of the system that the CPU talks to: a 16-bit memory bus plus the few
    /// Game Boy Color speed-switch hooks the CPU needs.
    ///
    /// The real <see cref="Mmu"/> implements this for normal emulation. Decoupling the CPU
    /// from the concrete MMU lets test harnesses supply a plain flat-memory implementation,
    /// which is exactly what the SM83 single-step test vectors expect (a 64 KB RAM with no
    /// I/O, banking, or PPU side effects).
    /// </summary>
    internal interface IMemoryBus
    {
        byte ReadByte(ushort address);
        void WriteByte(ushort address, byte value);
        ushort ReadWord(ushort address);
        void WriteWord(ushort address, ushort value);

        bool IsGameBoyColor { get; }
        bool IsSpeedSwitchRequested();
        void PerformSpeedSwitch();
    }
}
