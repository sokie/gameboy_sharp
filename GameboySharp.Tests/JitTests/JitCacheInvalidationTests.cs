using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class JitCacheInvalidationTests
{
    [Fact]
    public void WriteByte_ToWram_InvalidatesCachedBlock()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC9); // dummy ROM

        // Write code to WRAM
        helper.Mmu.WriteByte(0xC000, 0x00); // NOP
        helper.Mmu.WriteByte(0xC001, 0xC9); // RET

        // Manually create and cache a block from WRAM
        var block = BlockDecoder.Decode(0xC000, -1, helper.Mmu);
        helper.JitCpu.Cache.Insert(block);
        Assert.NotNull(helper.JitCpu.Cache.Lookup(0xC000, -1));

        // Write to an address within the block
        helper.Mmu.WriteByte(0xC000, 0x04); // INC B - modifies code

        // Cache should be invalidated
        Assert.Null(helper.JitCpu.Cache.Lookup(0xC000, -1));
    }

    [Fact]
    public void WriteByte_ToHram_InvalidatesCachedBlock()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC9);

        // Write code to HRAM
        helper.Mmu.WriteByte(0xFF80, 0x00); // NOP
        helper.Mmu.WriteByte(0xFF81, 0xC9); // RET

        var block = BlockDecoder.Decode(0xFF80, -1, helper.Mmu);
        helper.JitCpu.Cache.Insert(block);
        Assert.NotNull(helper.JitCpu.Cache.Lookup(0xFF80, -1));

        // Write to HRAM
        helper.Mmu.WriteByte(0xFF80, 0x04);

        Assert.Null(helper.JitCpu.Cache.Lookup(0xFF80, -1));
    }

    [Fact]
    public void WriteByte_OutsideBlock_DoesNotInvalidate()
    {
        var helper = new JitTestHelper();
        helper.LoadCode(0x0000, 0x00, 0xC9);

        // Cache a block in WRAM
        helper.Mmu.WriteByte(0xC000, 0x00);
        helper.Mmu.WriteByte(0xC001, 0xC9);
        var block = BlockDecoder.Decode(0xC000, -1, helper.Mmu);
        helper.JitCpu.Cache.Insert(block);

        // Write to a different WRAM address
        helper.Mmu.WriteByte(0xC010, 0x42);

        // Original block should still be cached
        Assert.NotNull(helper.JitCpu.Cache.Lookup(0xC000, -1));
    }

    [Fact]
    public void SelfModifyingCode_RecompilesCorrectly()
    {
        var helper = new JitTestHelper();
        // Load a dummy ROM first
        helper.LoadCode(0x0000, 0x00, 0xC9);

        // Write code to WRAM: LD A, 0x01 / RET
        helper.Mmu.WriteByte(0xC000, 0x3E);
        helper.Mmu.WriteByte(0xC001, 0x01);
        helper.Mmu.WriteByte(0xC002, 0xC9);

        // Execute from WRAM by setting PC
        helper.Cpu.PC = 0xC000;
        helper.Cpu.SP = 0xFFFE;
        // Push a return address
        helper.Cpu.SP -= 2;
        helper.Mmu.WriteByte(helper.Cpu.SP, 0x00);
        helper.Mmu.WriteByte((ushort)(helper.Cpu.SP + 1), 0x00);

        helper.JitCpu.Step();
        Assert.Equal(0x01, helper.Cpu.A);

        // Modify the immediate operand in WRAM (this should invalidate the cache)
        helper.Mmu.WriteByte(0xC001, 0xFF);

        // Execute again
        helper.Cpu.PC = 0xC000;
        helper.Cpu.SP = 0xFFFE;
        helper.Cpu.SP -= 2;
        helper.Mmu.WriteByte(helper.Cpu.SP, 0x00);
        helper.Mmu.WriteByte((ushort)(helper.Cpu.SP + 1), 0x00);

        helper.JitCpu.Step();
        Assert.Equal(0xFF, helper.Cpu.A); // Must use new value
    }

    [Fact]
    public void RomBankSwitch_UsesCorrectCacheEntry()
    {
        var cache = new BlockCache();
        var block1 = new BasicBlock { StartAddress = 0x4000, EndAddress = 0x4005, RomBank = 1, Instructions = [] };
        var block2 = new BasicBlock { StartAddress = 0x4000, EndAddress = 0x4005, RomBank = 2, Instructions = [] };
        cache.Insert(block1);
        cache.Insert(block2);

        // Same address, different banks return different blocks
        Assert.Same(block1, cache.Lookup(0x4000, 1));
        Assert.Same(block2, cache.Lookup(0x4000, 2));
        Assert.Null(cache.Lookup(0x4000, 3)); // Bank 3 not cached
    }
}
