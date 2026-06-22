using System.Collections.Generic;

namespace GameboySharp.Tests.CpuTests;

/// <summary>
/// A plain flat 64 KB address space with no I/O, banking, or PPU side effects — the memory
/// model the SM83 single-step tests assume. Every write is recorded so the bus can be reset to
/// zero between tests cheaply (only clearing the handful of addresses each test touched, rather
/// than wiping all 64 KB 500,000+ times).
/// </summary>
internal sealed class FlatMemoryBus : IMemoryBus
{
    private readonly byte[] _memory = new byte[0x10000];
    private readonly List<int> _touched = new();

    // The CPU is exercised in DMG mode, so there is no speed switching to model.
    public bool IsGameBoyColor => false;
    public bool IsSpeedSwitchRequested() => false;
    public void PerformSpeedSwitch() { }

    public byte ReadByte(ushort address) => _memory[address];

    public void WriteByte(ushort address, byte value)
    {
        _memory[address] = value;
        _touched.Add(address);
    }

    public ushort ReadWord(ushort address)
        => (ushort)(_memory[address] | (_memory[(ushort)(address + 1)] << 8));

    public void WriteWord(ushort address, ushort value)
    {
        WriteByte(address, (byte)(value & 0xFF));
        WriteByte((ushort)(address + 1), (byte)(value >> 8));
    }

    /// <summary>Seeds a memory location (used to load a test's initial RAM).</summary>
    public void Seed(ushort address, byte value)
    {
        _memory[address] = value;
        _touched.Add(address);
    }

    /// <summary>Zeroes every address touched since the last reset.</summary>
    public void Reset()
    {
        foreach (int address in _touched) _memory[address] = 0;
        _touched.Clear();
    }
}
