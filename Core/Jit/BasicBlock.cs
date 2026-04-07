namespace GameboySharp.Jit
{
    internal enum MemoryRegion
    {
        RomBank0,    // 0x0000-0x3FFF -- effectively immutable
        RomBankN,    // 0x4000-0x7FFF -- changes with bank switch
        Wram,        // 0xC000-0xDFFF -- writable, self-modifying code possible
        Hram,        // 0xFF80-0xFFFE -- writable, some games execute from here
    }

    internal class BasicBlock
    {
        public ushort StartAddress;
        public ushort EndAddress;        // Last byte of the last instruction
        public int RomBank;              // -1 for non-ROM
        public DecodedInstruction[] Instructions = [];
        public int InstructionCount;
        public Func<Cpu, Mmu, int>? CompiledExecute;
        public int ExecutionCount;
        public MemoryRegion Region;

        public static MemoryRegion ClassifyRegion(ushort address)
        {
            if (address < 0x4000) return MemoryRegion.RomBank0;
            if (address < 0x8000) return MemoryRegion.RomBankN;
            if (address >= 0xC000 && address <= 0xDFFF) return MemoryRegion.Wram;
            if (address >= 0xFF80 && address <= 0xFFFE) return MemoryRegion.Hram;
            return MemoryRegion.RomBank0; // Fallback
        }
    }
}
