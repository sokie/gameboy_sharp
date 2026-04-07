namespace GameboySharp.Jit
{
    internal class BlockCache
    {
        private readonly Dictionary<ulong, BasicBlock> _cache = new();
        private readonly Dictionary<ushort, List<BasicBlock>> _addressToBlocks = new();

        public static ulong MakeKey(ushort address, int romBank)
        {
            return ((ulong)(uint)(romBank + 1) << 16) | address;
        }

        public BasicBlock? Lookup(ushort address, int romBank)
        {
            _cache.TryGetValue(MakeKey(address, romBank), out var block);
            return block;
        }

        public void Insert(BasicBlock block)
        {
            ulong key = MakeKey(block.StartAddress, block.RomBank);
            _cache[key] = block;

            // Register all addresses covered by this block for invalidation
            for (int addr = block.StartAddress; addr <= block.EndAddress; addr++)
            {
                ushort a = (ushort)addr;
                if (!_addressToBlocks.TryGetValue(a, out var list))
                {
                    list = new List<BasicBlock>(1);
                    _addressToBlocks[a] = list;
                }
                list.Add(block);
            }
        }

        public void InvalidateAddress(ushort address)
        {
            if (!_addressToBlocks.TryGetValue(address, out var blocks))
                return;

            // Copy list to avoid modification during iteration
            var blocksToRemove = new List<BasicBlock>(blocks);
            foreach (var block in blocksToRemove)
            {
                ulong key = MakeKey(block.StartAddress, block.RomBank);
                _cache.Remove(key);

                // Remove all address mappings for this block
                for (int a = block.StartAddress; a <= block.EndAddress; a++)
                {
                    if (_addressToBlocks.TryGetValue((ushort)a, out var addrList))
                    {
                        addrList.Remove(block);
                        if (addrList.Count == 0)
                            _addressToBlocks.Remove((ushort)a);
                    }
                }
            }
        }

        public int Count => _cache.Count;

        public void Clear()
        {
            _cache.Clear();
            _addressToBlocks.Clear();
        }
    }
}
