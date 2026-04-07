using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class BlockDecoderTests
{
    private JitTestHelper CreateHelper(params byte[] code)
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, code);
        return helper;
    }

    [Fact]
    public void Decode_LinearSequence_EndsAtUnconditionalJump()
    {
        // LD B, 0x42 / INC B / JP 0x0000
        var helper = CreateHelper(0x06, 0x42, 0x04, 0xC3, 0x00, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.InstructionCount);
        Assert.Equal(0x0000, block.StartAddress);
        Assert.Equal(0x0005, block.EndAddress); // 2 + 1 + 3 = 6 bytes, last byte at 0x0005
    }

    [Fact]
    public void Decode_ConditionalJump_EndsBlock()
    {
        // LD A, 0x01 / CP 0x01 / JR Z, +5
        var helper = CreateHelper(0x3E, 0x01, 0xFE, 0x01, 0x28, 0x05);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.InstructionCount);
        Assert.Equal(InstructionKind.ConditionalJump, block.Instructions[2].Kind);
    }

    [Fact]
    public void Decode_CBPrefix_DecodesCorrectly()
    {
        // LD A, 0x80 / BIT 7, A (CB 7F) / RET
        var helper = CreateHelper(0x3E, 0x80, 0xCB, 0x7F, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.InstructionCount);
        Assert.True(block.Instructions[1].IsCBPrefixed);
        Assert.Equal(0x7F, block.Instructions[1].Opcode);
        Assert.Equal(2, block.Instructions[1].Bytes);
    }

    [Fact]
    public void Decode_EI_EndsBlock()
    {
        // NOP / EI
        var helper = CreateHelper(0x00, 0xFB);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.EnableInterrupts, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_HALT_EndsBlock()
    {
        // NOP / HALT
        var helper = CreateHelper(0x00, 0x76);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.Halt, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_MaxBlockSize_CapsAt32()
    {
        // 40 NOPs (should cap at 32)
        var nops = Enumerable.Repeat((byte)0x00, 40).ToArray();
        var helper = CreateHelper(nops);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(32, block.InstructionCount);
    }

    [Theory]
    [InlineData(0xC7)] // RST 0x00
    [InlineData(0xCF)] // RST 0x08
    [InlineData(0xD7)] // RST 0x10
    [InlineData(0xDF)] // RST 0x18
    [InlineData(0xE7)] // RST 0x20
    [InlineData(0xEF)] // RST 0x28
    [InlineData(0xF7)] // RST 0x30
    [InlineData(0xFF)] // RST 0x38
    public void Decode_RstInstructions_EndBlock(byte rst)
    {
        // NOP + RST
        var helper = CreateHelper(0x00, rst);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.Rst, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_InstructionKind_ClassifiesMemoryRead()
    {
        // LD A,(HL) = 0x7E → MemoryRead, then RET
        var helper = CreateHelper(0x7E, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(InstructionKind.MemoryRead, block.Instructions[0].Kind);
    }

    [Fact]
    public void Decode_InstructionKind_ClassifiesMemoryWrite()
    {
        // LD (HL),B = 0x70 → MemoryWrite, then RET
        var helper = CreateHelper(0x70, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(InstructionKind.MemoryWrite, block.Instructions[0].Kind);
    }

    [Fact]
    public void Decode_InstructionKind_ClassifiesIoWrite()
    {
        // LDH (n),A = 0xE0 0x80 → IoWrite, then RET
        var helper = CreateHelper(0xE0, 0x80, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(InstructionKind.IoWrite, block.Instructions[0].Kind);
    }

    [Fact]
    public void Decode_InstructionKind_ClassifiesIoRead()
    {
        // LDH A,(n) = 0xF0 0x80 → IoRead, then RET
        var helper = CreateHelper(0xF0, 0x80, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(InstructionKind.IoRead, block.Instructions[0].Kind);
    }

    [Fact]
    public void Decode_RET_EndsBlock()
    {
        // NOP / NOP / RET
        var helper = CreateHelper(0x00, 0x00, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.InstructionCount);
        Assert.Equal(InstructionKind.Return, block.Instructions[2].Kind);
    }

    [Fact]
    public void Decode_CALL_EndsBlock()
    {
        // NOP / CALL 0x0050
        var helper = CreateHelper(0x00, 0xCD, 0x50, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.Call, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_JP_HL_EndsBlock()
    {
        // NOP / JP (HL) = 0xE9
        var helper = CreateHelper(0x00, 0xE9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.UnconditionalJump, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_DI_DoesNotEndBlock()
    {
        // DI / NOP / RET
        var helper = CreateHelper(0xF3, 0x00, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.InstructionCount);
        Assert.Equal(InstructionKind.DisableInterrupts, block.Instructions[0].Kind);
    }

    [Fact]
    public void Decode_OperandBytes_CapturedCorrectly()
    {
        // LD BC, 0x1234 (0x01 0x34 0x12) / RET
        var helper = CreateHelper(0x01, 0x34, 0x12, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(3, block.Instructions[0].Bytes);
        Assert.Equal(2, block.Instructions[0].Operands.Length);
        Assert.Equal(0x34, block.Instructions[0].Operands[0]);
        Assert.Equal(0x12, block.Instructions[0].Operands[1]);
    }

    [Fact]
    public void Decode_MemoryRegion_ClassifiesCorrectly()
    {
        var helper = CreateHelper(0x00, 0xC9);

        var block0 = BlockDecoder.Decode(0x0000, 0, helper.Mmu);
        Assert.Equal(MemoryRegion.RomBank0, block0.Region);
    }

    [Fact]
    public void Decode_ConditionalReturn_EndsBlock()
    {
        // NOP / RET NZ (0xC0)
        var helper = CreateHelper(0x00, 0xC0);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.Return, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_STOP_EndsBlock()
    {
        // NOP / STOP
        var helper = CreateHelper(0x00, 0x10, 0x00);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(2, block.InstructionCount);
        Assert.Equal(InstructionKind.Stop, block.Instructions[1].Kind);
    }

    [Fact]
    public void Decode_CBPrefix_CycleCount_RegisterOp()
    {
        // CB 00 = RLC B (8 cycles) / RET
        var helper = CreateHelper(0xCB, 0x00, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(8, block.Instructions[0].Cycles);
    }

    [Fact]
    public void Decode_CBPrefix_CycleCount_HLOp()
    {
        // CB 06 = RLC (HL) (16 cycles) / RET
        var helper = CreateHelper(0xCB, 0x06, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(16, block.Instructions[0].Cycles);
    }

    [Fact]
    public void Decode_CBPrefix_BIT_HL_CycleCount()
    {
        // CB 46 = BIT 0,(HL) (12 cycles) / RET
        var helper = CreateHelper(0xCB, 0x46, 0xC9);
        var block = BlockDecoder.Decode(0x0000, 0, helper.Mmu);

        Assert.Equal(12, block.Instructions[0].Cycles);
    }
}
