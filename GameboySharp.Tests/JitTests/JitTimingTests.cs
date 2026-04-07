using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class JitTimingTests
{
    [Theory]
    [InlineData(0x00, 4)]   // NOP
    [InlineData(0x04, 4)]   // INC B
    [InlineData(0x05, 4)]   // DEC B
    [InlineData(0x3C, 4)]   // INC A
    [InlineData(0x3D, 4)]   // DEC A
    [InlineData(0x80, 4)]   // ADD A,B
    [InlineData(0x90, 4)]   // SUB B
    [InlineData(0xA0, 4)]   // AND B
    [InlineData(0xB0, 4)]   // OR B
    [InlineData(0xA8, 4)]   // XOR B
    [InlineData(0x07, 4)]   // RLCA
    [InlineData(0x0F, 4)]   // RRCA
    [InlineData(0x17, 4)]   // RLA
    [InlineData(0x1F, 4)]   // RRA
    [InlineData(0x27, 4)]   // DAA
    [InlineData(0x2F, 4)]   // CPL
    [InlineData(0x37, 4)]   // SCF
    [InlineData(0x3F, 4)]   // CCF
    public void CycleCount_SingleInstruction_Correct(byte opcode, int expectedCycles)
    {
        var helper = new JitTestHelper();
        // opcode + JP 0x0000 (to end block)
        helper.LoadCode(0x0000, opcode, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(expectedCycles, block.Instructions[0].Cycles);
    }

    [Theory]
    [InlineData(0x06, 8)]   // LD B,n
    [InlineData(0x0E, 8)]   // LD C,n
    [InlineData(0x16, 8)]   // LD D,n
    [InlineData(0x1E, 8)]   // LD E,n
    [InlineData(0x26, 8)]   // LD H,n
    [InlineData(0x2E, 8)]   // LD L,n
    [InlineData(0x3E, 8)]   // LD A,n
    public void CycleCount_LdImmediate_Correct(byte opcode, int expectedCycles)
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, opcode, 0x00, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(expectedCycles, block.Instructions[0].Cycles);
    }

    [Fact]
    public void CycleCount_ConditionalJR_DifferentCounts()
    {
        var helper = new JitTestHelper();
        // JR NZ,n (0x20): taken=12, not-taken=8
        helper.LoadCode(0x0000, 0x20, 0x02);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(12, block.Instructions[0].Cycles);    // Taken
        Assert.Equal(8, block.Instructions[0].CyclesAlt);  // Not taken
    }

    [Fact]
    public void CycleCount_ConditionalRET_DifferentCounts()
    {
        var helper = new JitTestHelper();
        // RET NZ (0xC0): taken=20, not-taken=8
        helper.LoadCode(0x0000, 0xC0);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(20, block.Instructions[0].Cycles);
        Assert.Equal(8, block.Instructions[0].CyclesAlt);
    }

    [Fact]
    public void CycleCount_ConditionalCALL_DifferentCounts()
    {
        var helper = new JitTestHelper();
        // CALL NZ,nn (0xC4): taken=24, not-taken=12
        helper.LoadCode(0x0000, 0xC4, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(24, block.Instructions[0].Cycles);
        Assert.Equal(12, block.Instructions[0].CyclesAlt);
    }

    [Fact]
    public void CycleCount_ConditionalJP_DifferentCounts()
    {
        var helper = new JitTestHelper();
        // JP NZ,nn (0xC2): taken=16, not-taken=12
        helper.LoadCode(0x0000, 0xC2, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(16, block.Instructions[0].Cycles);
        Assert.Equal(12, block.Instructions[0].CyclesAlt);
    }

    [Fact]
    public void CycleCount_Block_SumsCorrectly()
    {
        var helper = new JitTestHelper();
        // NOP(4) + NOP(4) + NOP(4) + JP nn(16) = 28
        helper.LoadCode(0x0000, 0x00, 0x00, 0x00, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);
        BlockCompiler.Compile(block, helper.JitCpu);

        int cycles = block.CompiledExecute!(helper.Cpu, helper.Mmu);
        Assert.Equal(28, cycles);
    }

    [Fact]
    public void CycleCount_MemoryOps_Correct()
    {
        var helper = new JitTestHelper();
        // LD (HL),B = 8 cycles / JP 0x0000 = 16 cycles
        helper.LoadCode(0x0000, 0x70, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(8, block.Instructions[0].Cycles);
    }

    [Fact]
    public void CycleCount_PUSH_POP_Correct()
    {
        var helper = new JitTestHelper();
        // PUSH BC(16) / POP DE(12) / JP 0x0000(16)
        helper.LoadCode(0x0000, 0xC5, 0xD1, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(16, block.Instructions[0].Cycles); // PUSH
        Assert.Equal(12, block.Instructions[1].Cycles);  // POP
    }
}
