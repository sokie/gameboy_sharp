using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class JitEdgeCaseTests
{
    [Fact]
    public void HaltBug_FallsBackToInterpreter()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC3, 0x00, 0x00); // NOP / JP 0x0000
        helper.Cpu.IsHaltBugActive = true;

        helper.JitCpu.Step();

        // Halt bug: PC should NOT be incremented after the instruction
        Assert.Equal(0x0000, helper.Cpu.PC);
        // Halt bug should be consumed
        Assert.False(helper.Cpu.IsHaltBugActive);
    }

    [Fact]
    public void DebugMode_FallsBackToInterpreter()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0x00, 0xC3, 0x00, 0x00); // NOP NOP JP

        helper.Cpu.IsStepping = true;

        helper.JitCpu.Step();

        // Should execute single instruction and pause
        Assert.True(helper.Cpu.IsPaused);
    }

    [Fact]
    public void EmptyRom_AllNops_DecodesMaxBlock()
    {
        var helper = new JitTestHelper();
        // All zeros = all NOPs
        helper.LoadCode(0x0000, new byte[40]);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(32, block.InstructionCount); // Hits max block size
    }

    [Fact]
    public void SingleInstructionBlock_Works()
    {
        var helper = new JitTestHelper();
        // Just a JP - single instruction block
        helper.LoadCode(0x0000, 0xC3, 0x00, 0x00);

        helper.JitCpu.Step();
        Assert.Equal(0x0000, helper.Cpu.PC);
    }

    [Fact]
    public void BlockCache_HitOnSecondExecution()
    {
        var helper = new JitTestHelper();
        // NOP / JP 0x0000 (loops back)
        helper.LoadCode(0x0000, 0x00, 0xC3, 0x00, 0x00);

        // First execution compiles
        helper.JitCpu.Step();
        var block = helper.JitCpu.Cache.Lookup(0x0000, 0);
        Assert.NotNull(block);
        Assert.Equal(1, block!.ExecutionCount);

        // Second execution hits cache
        helper.JitCpu.Step();
        Assert.Equal(2, block.ExecutionCount);
    }

    [Fact]
    public void Halted_ConsumesCorrectCycles()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x76); // HALT
        helper.Cpu.IsHalted = true;
        helper.Cpu.InterruptMasterEnable = false;

        // No interrupts pending, should stay halted and return 4 T-cycles
        int machineCycles = helper.JitCpu.Step();
        Assert.Equal(4, machineCycles); // 4 T-cycles = 4 machine cycles (not double-speed)
        Assert.True(helper.Cpu.IsHalted);
    }

    [Fact]
    public void BlockRegion_ClassifiedCorrectly()
    {
        Assert.Equal(MemoryRegion.RomBank0, BasicBlock.ClassifyRegion(0x0000));
        Assert.Equal(MemoryRegion.RomBank0, BasicBlock.ClassifyRegion(0x3FFF));
        Assert.Equal(MemoryRegion.RomBankN, BasicBlock.ClassifyRegion(0x4000));
        Assert.Equal(MemoryRegion.RomBankN, BasicBlock.ClassifyRegion(0x7FFF));
        Assert.Equal(MemoryRegion.Wram, BasicBlock.ClassifyRegion(0xC000));
        Assert.Equal(MemoryRegion.Wram, BasicBlock.ClassifyRegion(0xDFFF));
        Assert.Equal(MemoryRegion.Hram, BasicBlock.ClassifyRegion(0xFF80));
        Assert.Equal(MemoryRegion.Hram, BasicBlock.ClassifyRegion(0xFFFE));
    }

    [Fact]
    public void DI_InsideBlock_Works()
    {
        var helper = new JitTestHelper();
        // DI / NOP / JP 0x0000
        helper.LoadCode(0x0000, 0xF3, 0x00, 0xC3, 0x00, 0x00);
        helper.Cpu.InterruptMasterEnable = true;

        helper.JitCpu.Step();

        Assert.False(helper.Cpu.InterruptMasterEnable);
    }

    [Fact]
    public void MultipleBlocks_IndependentlyCompiled()
    {
        var helper = new JitTestHelper();
        // Block 1 at 0x0000: LD A,0x42 / JP 0x0010
        // Block 2 at 0x0010: LD B,0x13 / JP 0x0000
        helper.LoadCode(0x0000,
            0x3E, 0x42,                   // LD A, 0x42
            0xC3, 0x10, 0x00,             // JP 0x0010
            0x00, 0x00, 0x00, 0x00, 0x00, // padding
            0x00, 0x00, 0x00, 0x00, 0x00, // padding
            0x00,                         // padding at 0x000F
            0x06, 0x13,                   // LD B, 0x13 (at 0x0010)
            0xC3, 0x00, 0x00);            // JP 0x0000

        // Execute block 1
        helper.JitCpu.Step();
        Assert.Equal(0x42, helper.Cpu.A);
        Assert.Equal(0x0010, helper.Cpu.PC);

        // Execute block 2
        helper.JitCpu.Step();
        Assert.Equal(0x13, helper.Cpu.B);
        Assert.Equal(0x0000, helper.Cpu.PC);

        // Both blocks should be cached
        Assert.NotNull(helper.JitCpu.Cache.Lookup(0x0000, 0));
        Assert.NotNull(helper.JitCpu.Cache.Lookup(0x0010, 0));
    }
}
