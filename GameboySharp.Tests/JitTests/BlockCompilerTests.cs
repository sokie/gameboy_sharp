using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class BlockCompilerTests
{
    private (JitTestHelper helper, BasicBlock block) CompileBlock(params byte[] code)
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, code);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);
        BlockCompiler.Compile(block, helper.JitCpu);
        return (helper, block);
    }

    [Fact]
    public void Compile_NOP_ReturnsCorrectCycles()
    {
        // NOP (4 cycles) / RET (16 cycles)
        var (helper, block) = CompileBlock(0x00, 0xC9);
        helper.Cpu.SP = 0xFFFE;
        // Push a return address for RET
        helper.Cpu.SP -= 2;
        helper.Mmu.WriteByte(helper.Cpu.SP, 0x10);       // low byte of return addr
        helper.Mmu.WriteByte((ushort)(helper.Cpu.SP + 1), 0x00); // high byte

        int cycles = block.CompiledExecute!(helper.Cpu, helper.Mmu);
        Assert.Equal(4 + 16, cycles);
    }

    [Fact]
    public void Compile_LdBImmediate_SetsRegister()
    {
        // LD B, 0x42 (0x06 0x42) / JR 0x00 (end block, jump to self)
        var (helper, block) = CompileBlock(0x06, 0x42, 0x18, 0xFE);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x42, helper.Cpu.B);
    }

    [Fact]
    public void Compile_LdAImmediate_SetsRegister()
    {
        // LD A, 0xFF / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0xFF, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0xFF, helper.Cpu.A);
    }

    [Fact]
    public void Compile_RegisterToRegister_LD()
    {
        // LD B, 0x42 / LD C, B / JP 0x0000
        var (helper, block) = CompileBlock(0x06, 0x42, 0x48, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x42, helper.Cpu.B);
        Assert.Equal(0x42, helper.Cpu.C);
    }

    [Fact]
    public void Compile_IncB_SetsFlags()
    {
        // LD B, 0xFF / INC B / JP 0x0000
        var (helper, block) = CompileBlock(0x06, 0xFF, 0x04, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x00, helper.Cpu.B);
        Assert.True(helper.Cpu.FlagZ);
        Assert.True(helper.Cpu.FlagH);
        Assert.False(helper.Cpu.FlagN);
    }

    [Fact]
    public void Compile_AddAB_SetsFlags()
    {
        // LD A, 0xFF / LD B, 0x01 / ADD A,B / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0xFF, 0x06, 0x01, 0x80, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x00, helper.Cpu.A);
        Assert.True(helper.Cpu.FlagZ);
        Assert.False(helper.Cpu.FlagN);
        Assert.True(helper.Cpu.FlagH);
        Assert.True(helper.Cpu.FlagC);
    }

    [Fact]
    public void Compile_SubAB_SetsFlags()
    {
        // LD A, 0x10 / LD B, 0x10 / SUB A,B / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0x10, 0x06, 0x10, 0x90, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x00, helper.Cpu.A);
        Assert.True(helper.Cpu.FlagZ);
        Assert.True(helper.Cpu.FlagN);
    }

    [Fact]
    public void Compile_XorAA_ClearsA()
    {
        // LD A, 0xFF / XOR A / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0xFF, 0xAF, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x00, helper.Cpu.A);
        Assert.True(helper.Cpu.FlagZ);
    }

    [Fact]
    public void Compile_LD16_LoadsRegisterPair()
    {
        // LD BC, 0x1234 / JP 0x0000
        var (helper, block) = CompileBlock(0x01, 0x34, 0x12, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x1234, helper.Cpu.BC);
    }

    [Fact]
    public void Compile_CP_SetsFlags_Equal()
    {
        // LD A, 0x42 / CP 0x42 / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0x42, 0xFE, 0x42, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.True(helper.Cpu.FlagZ);
        Assert.True(helper.Cpu.FlagN);
        Assert.False(helper.Cpu.FlagC);
        // A should be unchanged
        Assert.Equal(0x42, helper.Cpu.A);
    }

    [Fact]
    public void Compile_CP_SetsFlags_Less()
    {
        // LD A, 0x10 / CP 0x20 / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0x10, 0xFE, 0x20, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.False(helper.Cpu.FlagZ);
        Assert.True(helper.Cpu.FlagC); // A < n sets carry
    }

    [Fact]
    public void Compile_MultipleLdImmediates_AllSet()
    {
        // LD B,0x11 / LD C,0x22 / LD D,0x33 / LD E,0x44 / JP 0x0000
        var (helper, block) = CompileBlock(
            0x06, 0x11, 0x0E, 0x22, 0x16, 0x33, 0x1E, 0x44,
            0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x11, helper.Cpu.B);
        Assert.Equal(0x22, helper.Cpu.C);
        Assert.Equal(0x33, helper.Cpu.D);
        Assert.Equal(0x44, helper.Cpu.E);
    }

    [Fact]
    public void Compile_BlockCycleCount_IsSum()
    {
        // LD B,0x42 (8) + LD C,0x13 (8) + LD C,B (4) + JP nn (16) = 36
        var (helper, block) = CompileBlock(0x06, 0x42, 0x0E, 0x13, 0x48, 0xC3, 0x00, 0x00);
        int cycles = block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(8 + 8 + 4 + 16, cycles);
    }

    [Fact]
    public void Compile_RLCA_Works()
    {
        // LD A, 0x85 / RLCA / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0x85, 0x07, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        // 0x85 = 1000_0101, RLCA -> 0000_1011 = 0x0B, carry = 1
        Assert.Equal(0x0B, helper.Cpu.A);
        Assert.True(helper.Cpu.FlagC);
    }

    [Fact]
    public void Compile_DAA_Works()
    {
        // LD A, 0x15 / LD B, 0x27 / ADD A,B / DAA / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0x15, 0x06, 0x27, 0x80, 0x27, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        // 0x15 + 0x27 = 0x3C, DAA should give BCD 0x42 (15 + 27 = 42 decimal)
        Assert.Equal(0x42, helper.Cpu.A);
    }

    [Fact]
    public void Compile_SCF_CCF_Work()
    {
        // SCF / CCF / JP 0x0000
        var (helper, block) = CompileBlock(0x37, 0x3F, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        // SCF sets carry, CCF complements it -> carry should be false
        Assert.False(helper.Cpu.FlagC);
    }

    [Fact]
    public void Compile_CPL_ComplementsA()
    {
        // LD A, 0xAA / CPL / JP 0x0000
        var (helper, block) = CompileBlock(0x3E, 0xAA, 0x2F, 0xC3, 0x00, 0x00);
        block.CompiledExecute!(helper.Cpu, helper.Mmu);

        Assert.Equal(0x55, helper.Cpu.A);
        Assert.True(helper.Cpu.FlagN);
        Assert.True(helper.Cpu.FlagH);
    }
}
