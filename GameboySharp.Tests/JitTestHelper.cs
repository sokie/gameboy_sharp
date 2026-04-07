using GameboySharp.Jit;

namespace GameboySharp.Tests;

internal record CpuSnapshot(
    byte A, byte F, byte B, byte C, byte D, byte E, byte H, byte L,
    ushort SP, ushort PC, int TotalCycles);

internal class JitTestHelper
{
    public Cpu Cpu { get; }
    public Mmu Mmu { get; }
    public Ppu Ppu { get; }
    public Timer Timer { get; }
    public Apu Apu { get; }
    public Joypad Joypad { get; }
    public JitCpu JitCpu { get; }

    private readonly Action<int>? _onSync;

    public JitTestHelper(Action<int>? onSync = null)
    {
        _onSync = onSync;
        Cpu = new Cpu(null);
        Apu = new Apu();
        Joypad = new Joypad(Cpu);
        Timer = new Timer(Cpu);
        Ppu = new Ppu(null, Cpu);
        Mmu = new Mmu(Joypad, Ppu, Timer, Cpu, Apu);

        Cpu.SetMmu(Mmu);
        Ppu.SetMmu(Mmu);

        if (onSync != null)
        {
            JitCpu = new TestableJitCpu(Cpu, Mmu, Ppu, Timer, Apu, onSync);
        }
        else
        {
            JitCpu = new JitCpu(Cpu, Mmu, Ppu, Timer, Apu);
        }
        Mmu.JitBlockCache = JitCpu.Cache;

        // Initialize CPU for DMG mode with PC=0 and SP set up
        Cpu.PC = 0x0000;
        Cpu.SP = 0xFFFE;
    }

    /// <summary>
    /// Creates a minimal 32KB ROM with the given code bytes at the specified address.
    /// </summary>
    public void LoadCode(ushort startAddress, params byte[] code)
    {
        // Create a minimal 32KB ROM (required for RomOnly)
        var rom = new byte[0x8000];
        // Fill with NOPs
        // Set cartridge type header at 0x0147 = 0x00 (ROM ONLY)
        rom[0x0147] = 0x00;
        // Set ROM size at 0x0148 = 0x00 (32KB)
        rom[0x0148] = 0x00;

        // Copy code to the specified address
        for (int i = 0; i < code.Length && startAddress + i < rom.Length; i++)
        {
            rom[startAddress + i] = code[i];
        }

        Mmu.LoadCartridge(rom);

        // Reset CPU state after loading
        Cpu.PC = startAddress;
        Cpu.SP = 0xFFFE;

        // Clear JIT cache since we loaded new code
        JitCpu.Cache.Clear();
    }

    /// <summary>
    /// Runs exactly N instructions via the interpreter, returns a snapshot.
    /// </summary>
    public CpuSnapshot RunInterpreter(int instructionCount)
    {
        int totalCycles = 0;
        for (int i = 0; i < instructionCount; i++)
        {
            Cpu.ProcessScheduledInterruptEnable();
            int cycles = Cpu.Execute();

            // Auto-increment PC like the normal Step() does
            // (Execute already handles AutoIncrementPC internally)

            totalCycles += cycles;
        }
        return TakeSnapshot(totalCycles);
    }

    /// <summary>
    /// Runs via JIT until the PC reaches a certain point or N steps are taken.
    /// Returns a snapshot. Note: JIT executes entire blocks at once.
    /// </summary>
    public CpuSnapshot RunJit(int maxSteps)
    {
        int totalCycles = 0;
        for (int i = 0; i < maxSteps; i++)
        {
            int cycles = JitCpu.Step();
            totalCycles += cycles;
        }
        return TakeSnapshot(totalCycles);
    }

    public CpuSnapshot TakeSnapshot(int totalCycles = 0)
    {
        return new CpuSnapshot(
            Cpu.A, Cpu.F, Cpu.B, Cpu.C, Cpu.D, Cpu.E, Cpu.H, Cpu.L,
            Cpu.SP, Cpu.PC, totalCycles);
    }
}

/// <summary>
/// JitCpu subclass that calls a tracking callback on each sync.
/// </summary>
internal class TestableJitCpu : JitCpu
{
    private readonly Action<int> _onSync;

    public TestableJitCpu(Cpu cpu, Mmu mmu, Ppu ppu, Timer timer, Apu apu, Action<int> onSync)
        : base(cpu, mmu, ppu, timer, apu)
    {
        _onSync = onSync;
    }

    public new void SyncSubsystems(int tCycles, Mmu mmu)
    {
        _onSync(tCycles);
        base.SyncSubsystems(tCycles, mmu);
    }
}
