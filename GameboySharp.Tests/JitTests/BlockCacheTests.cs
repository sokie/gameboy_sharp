using GameboySharp.Jit;
using Xunit;

namespace GameboySharp.Tests.JitTests;

public class BlockCacheTests
{
    [Fact]
    public void Lookup_MissReturnsNull()
    {
        var cache = new BlockCache();
        Assert.Null(cache.Lookup(0x0100, 0));
    }

    [Fact]
    public void Insert_ThenLookup_ReturnsBlock()
    {
        var cache = new BlockCache();
        var block = new BasicBlock
        {
            StartAddress = 0x0100,
            EndAddress = 0x0105,
            RomBank = 0,
            Instructions = [],
            InstructionCount = 0
        };
        cache.Insert(block);
        Assert.Same(block, cache.Lookup(0x0100, 0));
    }

    [Fact]
    public void Lookup_DifferentBank_ReturnsDifferentBlock()
    {
        var cache = new BlockCache();
        var block1 = new BasicBlock { StartAddress = 0x4000, EndAddress = 0x4005, RomBank = 1, Instructions = [] };
        var block2 = new BasicBlock { StartAddress = 0x4000, EndAddress = 0x4005, RomBank = 2, Instructions = [] };
        cache.Insert(block1);
        cache.Insert(block2);

        Assert.Same(block1, cache.Lookup(0x4000, 1));
        Assert.Same(block2, cache.Lookup(0x4000, 2));
    }

    [Fact]
    public void InvalidateAddress_RemovesOverlappingBlock()
    {
        var cache = new BlockCache();
        var block = new BasicBlock { StartAddress = 0xC000, EndAddress = 0xC005, RomBank = -1, Instructions = [] };
        cache.Insert(block);

        cache.InvalidateAddress(0xC003); // Middle of block
        Assert.Null(cache.Lookup(0xC000, -1));
    }

    [Fact]
    public void InvalidateAddress_DoesNotAffectOtherBlocks()
    {
        var cache = new BlockCache();
        var block1 = new BasicBlock { StartAddress = 0xC000, EndAddress = 0xC005, RomBank = -1, Instructions = [] };
        var block2 = new BasicBlock { StartAddress = 0xC010, EndAddress = 0xC015, RomBank = -1, Instructions = [] };
        cache.Insert(block1);
        cache.Insert(block2);

        cache.InvalidateAddress(0xC003);
        Assert.Null(cache.Lookup(0xC000, -1));
        Assert.Same(block2, cache.Lookup(0xC010, -1));
    }

    [Fact]
    public void InvalidateAddress_NoOpForUnmappedAddress()
    {
        var cache = new BlockCache();
        // Should not throw
        cache.InvalidateAddress(0xC000);
    }

    [Fact]
    public void Count_ReflectsInsertions()
    {
        var cache = new BlockCache();
        Assert.Equal(0, cache.Count);

        cache.Insert(new BasicBlock { StartAddress = 0x0100, EndAddress = 0x0105, RomBank = 0, Instructions = [] });
        Assert.Equal(1, cache.Count);

        cache.Insert(new BasicBlock { StartAddress = 0x0200, EndAddress = 0x0205, RomBank = 0, Instructions = [] });
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Clear_RemovesAllBlocks()
    {
        var cache = new BlockCache();
        cache.Insert(new BasicBlock { StartAddress = 0x0100, EndAddress = 0x0105, RomBank = 0, Instructions = [] });
        cache.Insert(new BasicBlock { StartAddress = 0x0200, EndAddress = 0x0205, RomBank = 0, Instructions = [] });

        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Lookup(0x0100, 0));
    }
}
