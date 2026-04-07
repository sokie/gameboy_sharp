namespace GameboySharp.Jit
{
    internal enum InstructionKind
    {
        Normal,              // Register-only ALU, loads between registers
        MemoryRead,          // Reads from (HL), (BC), (DE), (nn)
        MemoryWrite,         // Writes to (HL), (BC), (DE), (nn)
        IoRead,              // LDH A,(n), LD A,(C) -- reads 0xFF00+offset
        IoWrite,             // LDH (n),A, LD (C),A -- writes 0xFF00+offset
        UnconditionalJump,   // JP nn, JP (HL), JR n
        ConditionalJump,     // JP cc, JR cc
        Call,                // CALL nn, CALL cc,nn
        Return,              // RET, RET cc, RETI
        Rst,                 // RST xx
        Halt,                // HALT
        Stop,                // STOP
        EnableInterrupts,    // EI
        DisableInterrupts,   // DI
        StackPush,           // PUSH rr
        StackPop,            // POP rr
    }

    internal struct DecodedInstruction
    {
        public ushort Address;
        public byte Opcode;
        public bool IsCBPrefixed;
        public byte[] Operands;      // 0-2 immediate bytes, baked in at decode time
        public int Bytes;            // Total instruction length (1, 2, or 3)
        public int Cycles;           // Base cycle count
        public int CyclesAlt;        // Alternate cycle count (for conditional branches not-taken), 0 if N/A
        public InstructionKind Kind;
    }
}
