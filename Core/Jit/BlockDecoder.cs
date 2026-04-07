namespace GameboySharp.Jit
{
    internal static class BlockDecoder
    {
        public const int MaxBlockSize = 32;

        public static BasicBlock Decode(ushort startAddress, int romBank, Mmu mmu)
        {
            var instructions = new List<DecodedInstruction>();
            ushort pc = startAddress;
            int totalBytes = 0;

            while (instructions.Count < MaxBlockSize)
            {
                byte opcode = mmu.ReadByte(pc);
                bool isCB = opcode == 0xCB;

                byte actualOpcode;
                int instrBytes;
                int cycles;
                int cyclesAlt;
                InstructionKind kind;

                if (isCB)
                {
                    actualOpcode = mmu.ReadByte((ushort)(pc + 1));
                    instrBytes = 2;
                    cycles = GetCBCycles(actualOpcode);
                    cyclesAlt = 0;
                    kind = ClassifyCBOpcode(actualOpcode);
                }
                else
                {
                    actualOpcode = opcode;
                    instrBytes = GetOpcodeBytes(opcode);
                    cycles = GetOpcodeCycles(opcode);
                    cyclesAlt = GetOpcodeCyclesAlt(opcode);
                    kind = ClassifyOpcode(opcode);
                }

                // Read operand bytes (after opcode, or after CB prefix)
                int operandStart = isCB ? 2 : 1;
                int operandCount = instrBytes - operandStart;
                byte[] operands = new byte[operandCount];
                for (int i = 0; i < operandCount; i++)
                {
                    operands[i] = mmu.ReadByte((ushort)(pc + operandStart + i));
                }

                instructions.Add(new DecodedInstruction
                {
                    Address = pc,
                    Opcode = actualOpcode,
                    IsCBPrefixed = isCB,
                    Operands = operands,
                    Bytes = instrBytes,
                    Cycles = cycles,
                    CyclesAlt = cyclesAlt,
                    Kind = kind,
                });

                totalBytes += instrBytes;
                pc = (ushort)(pc + instrBytes);

                // Block terminators: stop after this instruction
                if (IsBlockTerminator(kind))
                    break;
            }

            var block = new BasicBlock
            {
                StartAddress = startAddress,
                EndAddress = (ushort)(startAddress + totalBytes - 1),
                RomBank = romBank,
                Instructions = instructions.ToArray(),
                InstructionCount = instructions.Count,
                Region = BasicBlock.ClassifyRegion(startAddress),
            };

            return block;
        }

        private static bool IsBlockTerminator(InstructionKind kind)
        {
            return kind switch
            {
                InstructionKind.UnconditionalJump => true,
                InstructionKind.ConditionalJump => true,
                InstructionKind.Call => true,
                InstructionKind.Return => true,
                InstructionKind.Rst => true,
                InstructionKind.Halt => true,
                InstructionKind.Stop => true,
                InstructionKind.EnableInterrupts => true,
                _ => false,
            };
        }

        private static int GetOpcodeBytes(byte opcode)
        {
            return opcode switch
            {
                // 3-byte instructions (16-bit immediate or address)
                0x01 or 0x08 or 0x11 or 0x21 or 0x31 => 3, // LD rr,nn / LD (nn),SP
                0xC2 or 0xC3 or 0xCA or 0xD2 or 0xDA => 3, // JP cc,nn / JP nn
                0xC4 or 0xCC or 0xCD or 0xD4 or 0xDC => 3, // CALL cc,nn / CALL nn
                0xEA or 0xFA => 3,                           // LD (nn),A / LD A,(nn)

                // 2-byte instructions (8-bit immediate or relative offset)
                0x06 or 0x0E or 0x16 or 0x1E or 0x26 or 0x2E or 0x36 or 0x3E => 2, // LD r,n / LD (HL),n
                0x10 or 0x18 or 0x20 or 0x28 or 0x30 or 0x38 => 2, // STOP / JR / JR cc
                0xC6 or 0xCE or 0xD6 or 0xDE or 0xE6 or 0xEE or 0xF6 or 0xFE => 2, // ALU A,n
                0xE0 or 0xF0 or 0xE8 or 0xF8 => 2, // LDH / ADD SP,n / LD HL,SP+n
                0xCB => 2, // CB prefix (handled separately but included for completeness)

                // Everything else is 1 byte
                _ => 1,
            };
        }

        private static int GetOpcodeCycles(byte opcode)
        {
            // Returns the base (or taken-branch) cycle count
            return opcode switch
            {
                // 1-byte, 4-cycle instructions (most register ops)
                0x00 => 4,  // NOP
                0x02 => 8,  // LD (BC),A
                0x03 => 8,  // INC BC
                0x04 => 4,  // INC B
                0x05 => 4,  // DEC B
                0x06 => 8,  // LD B,n
                0x07 => 4,  // RLCA
                0x08 => 20, // LD (nn),SP
                0x09 => 8,  // ADD HL,BC
                0x0A => 8,  // LD A,(BC)
                0x0B => 8,  // DEC BC
                0x0C => 4,  // INC C
                0x0D => 4,  // DEC C
                0x0E => 8,  // LD C,n
                0x0F => 4,  // RRCA
                0x10 => 4,  // STOP
                0x11 => 12, // LD DE,nn
                0x12 => 8,  // LD (DE),A
                0x13 => 8,  // INC DE
                0x14 => 4,  // INC D
                0x15 => 4,  // DEC D
                0x16 => 8,  // LD D,n
                0x17 => 4,  // RLA
                0x18 => 12, // JR n
                0x19 => 8,  // ADD HL,DE
                0x1A => 8,  // LD A,(DE)
                0x1B => 8,  // DEC DE
                0x1C => 4,  // INC E
                0x1D => 4,  // DEC E
                0x1E => 8,  // LD E,n
                0x1F => 4,  // RRA
                0x20 => 12, // JR NZ,n (taken)
                0x21 => 12, // LD HL,nn
                0x22 => 8,  // LD (HL+),A
                0x23 => 8,  // INC HL
                0x24 => 4,  // INC H
                0x25 => 4,  // DEC H
                0x26 => 8,  // LD H,n
                0x27 => 4,  // DAA
                0x28 => 12, // JR Z,n (taken)
                0x29 => 8,  // ADD HL,HL
                0x2A => 8,  // LD A,(HL+)
                0x2B => 8,  // DEC HL
                0x2C => 4,  // INC L
                0x2D => 4,  // DEC L
                0x2E => 8,  // LD L,n
                0x2F => 4,  // CPL
                0x30 => 12, // JR NC,n (taken)
                0x31 => 12, // LD SP,nn
                0x32 => 8,  // LD (HL-),A
                0x33 => 8,  // INC SP
                0x34 => 12, // INC (HL)
                0x35 => 12, // DEC (HL)
                0x36 => 12, // LD (HL),n
                0x37 => 4,  // SCF
                0x38 => 12, // JR C,n (taken)
                0x39 => 8,  // ADD HL,SP
                0x3A => 8,  // LD A,(HL-)
                0x3B => 8,  // DEC SP
                0x3C => 4,  // INC A
                0x3D => 4,  // DEC A
                0x3E => 8,  // LD A,n
                0x3F => 4,  // CCF

                // 0x40-0x7F: LD r,r (4 cycles) except LD r,(HL) (8) and HALT (4)
                0x46 or 0x4E or 0x56 or 0x5E or 0x66 or 0x6E or 0x7E => 8, // LD r,(HL)
                0x70 or 0x71 or 0x72 or 0x73 or 0x74 or 0x75 or 0x77 => 8, // LD (HL),r
                0x76 => 4,  // HALT
                >= 0x40 and <= 0x7F => 4, // LD r,r

                // 0x80-0xBF: ALU A,r (4 cycles) except ALU A,(HL) (8)
                0x86 or 0x8E or 0x96 or 0x9E or 0xA6 or 0xAE or 0xB6 or 0xBE => 8,
                >= 0x80 and <= 0xBF => 4,

                // Control flow and special
                0xC0 => 20, // RET NZ (taken)
                0xC1 => 12, // POP BC
                0xC2 => 16, // JP NZ,nn (taken)
                0xC3 => 16, // JP nn
                0xC4 => 24, // CALL NZ,nn (taken)
                0xC5 => 16, // PUSH BC
                0xC6 => 8,  // ADD A,n
                0xC7 => 16, // RST 0x00
                0xC8 => 20, // RET Z (taken)
                0xC9 => 16, // RET
                0xCA => 16, // JP Z,nn (taken)
                0xCB => 0,  // CB prefix (handled separately)
                0xCC => 24, // CALL Z,nn (taken)
                0xCD => 24, // CALL nn
                0xCE => 8,  // ADC A,n
                0xCF => 16, // RST 0x08
                0xD0 => 20, // RET NC (taken)
                0xD1 => 12, // POP DE
                0xD2 => 16, // JP NC,nn (taken)
                // 0xD3 unused
                0xD4 => 24, // CALL NC,nn (taken)
                0xD5 => 16, // PUSH DE
                0xD6 => 8,  // SUB A,n
                0xD7 => 16, // RST 0x10
                0xD8 => 20, // RET C (taken)
                0xD9 => 16, // RETI
                0xDA => 16, // JP C,nn (taken)
                // 0xDB unused
                0xDC => 24, // CALL C,nn (taken)
                // 0xDD unused
                0xDE => 8,  // SBC A,n
                0xDF => 16, // RST 0x18
                0xE0 => 12, // LDH (n),A
                0xE1 => 12, // POP HL
                0xE2 => 8,  // LD (C),A
                // 0xE3-0xE4 unused
                0xE5 => 16, // PUSH HL
                0xE6 => 8,  // AND n
                0xE7 => 16, // RST 0x20
                0xE8 => 16, // ADD SP,n
                0xE9 => 4,  // JP (HL)
                0xEA => 16, // LD (nn),A
                // 0xEB-0xED unused
                0xEE => 8,  // XOR n
                0xEF => 16, // RST 0x28
                0xF0 => 12, // LDH A,(n)
                0xF1 => 12, // POP AF
                0xF2 => 8,  // LD A,(C)
                0xF3 => 4,  // DI
                // 0xF4 unused
                0xF5 => 16, // PUSH AF
                0xF6 => 8,  // OR n
                0xF7 => 16, // RST 0x30
                0xF8 => 12, // LD HL,SP+n
                0xF9 => 8,  // LD SP,HL
                0xFA => 16, // LD A,(nn)
                0xFB => 4,  // EI
                // 0xFC-0xFD unused
                0xFE => 8,  // CP n
                0xFF => 16, // RST 0x38

                _ => 4, // Default for unused opcodes
            };
        }

        private static int GetOpcodeCyclesAlt(byte opcode)
        {
            // Returns not-taken cycle count for conditional instructions, 0 otherwise
            return opcode switch
            {
                // JR cc: taken=12, not-taken=8
                0x20 or 0x28 or 0x30 or 0x38 => 8,
                // JP cc,nn: taken=16, not-taken=12
                0xC2 or 0xCA or 0xD2 or 0xDA => 12,
                // CALL cc,nn: taken=24, not-taken=12
                0xC4 or 0xCC or 0xD4 or 0xDC => 12,
                // RET cc: taken=20, not-taken=8
                0xC0 or 0xC8 or 0xD0 or 0xD8 => 8,
                _ => 0,
            };
        }

        private static int GetCBCycles(byte opcode)
        {
            // BIT b,(HL) is 12 cycles, all other (HL) ops are 16, register ops are 8
            int regIndex = opcode & 0x07;
            if (regIndex == 6) // (HL)
            {
                // BIT instructions: 0x40-0x7F
                if (opcode >= 0x40 && opcode <= 0x7F)
                    return 12;
                return 16; // RLC/RRC/RL/RR/SLA/SRA/SWAP/SRL/RES/SET (HL)
            }
            return 8; // All register operations
        }

        private static InstructionKind ClassifyOpcode(byte opcode)
        {
            return opcode switch
            {
                // Unconditional jumps
                0xC3 or 0xE9 or 0x18 => InstructionKind.UnconditionalJump,

                // Conditional jumps
                0x20 or 0x28 or 0x30 or 0x38 => InstructionKind.ConditionalJump,  // JR cc
                0xC2 or 0xCA or 0xD2 or 0xDA => InstructionKind.ConditionalJump,  // JP cc,nn

                // Calls
                0xCD => InstructionKind.Call,                                        // CALL nn
                0xC4 or 0xCC or 0xD4 or 0xDC => InstructionKind.Call,              // CALL cc,nn

                // Returns
                0xC9 or 0xD9 => InstructionKind.Return,                             // RET / RETI
                0xC0 or 0xC8 or 0xD0 or 0xD8 => InstructionKind.Return,           // RET cc

                // RST
                0xC7 or 0xCF or 0xD7 or 0xDF or 0xE7 or 0xEF or 0xF7 or 0xFF => InstructionKind.Rst,

                // HALT / STOP
                0x76 => InstructionKind.Halt,
                0x10 => InstructionKind.Stop,

                // Interrupt control
                0xFB => InstructionKind.EnableInterrupts,
                0xF3 => InstructionKind.DisableInterrupts,

                // I/O reads
                0xF0 => InstructionKind.IoRead,   // LDH A,(n)
                0xF2 => InstructionKind.IoRead,   // LD A,(C)

                // I/O writes
                0xE0 => InstructionKind.IoWrite,  // LDH (n),A
                0xE2 => InstructionKind.IoWrite,  // LD (C),A

                // Memory reads via register pairs
                0x0A => InstructionKind.MemoryRead,  // LD A,(BC)
                0x1A => InstructionKind.MemoryRead,  // LD A,(DE)
                0x2A => InstructionKind.MemoryRead,  // LD A,(HL+)
                0x3A => InstructionKind.MemoryRead,  // LD A,(HL-)
                0xFA => InstructionKind.MemoryRead,  // LD A,(nn)
                // LD r,(HL)
                0x46 or 0x4E or 0x56 or 0x5E or 0x66 or 0x6E or 0x7E => InstructionKind.MemoryRead,
                0x34 => InstructionKind.MemoryRead,  // INC (HL) - read-modify-write, classify as read for now
                0x35 => InstructionKind.MemoryRead,  // DEC (HL)
                // ALU A,(HL)
                0x86 or 0x8E or 0x96 or 0x9E or 0xA6 or 0xAE or 0xB6 or 0xBE => InstructionKind.MemoryRead,

                // Memory writes via register pairs
                0x02 => InstructionKind.MemoryWrite, // LD (BC),A
                0x12 => InstructionKind.MemoryWrite, // LD (DE),A
                0x22 => InstructionKind.MemoryWrite, // LD (HL+),A
                0x32 => InstructionKind.MemoryWrite, // LD (HL-),A
                0x36 => InstructionKind.MemoryWrite, // LD (HL),n
                0xEA => InstructionKind.MemoryWrite, // LD (nn),A
                0x08 => InstructionKind.MemoryWrite, // LD (nn),SP
                // LD (HL),r
                0x70 or 0x71 or 0x72 or 0x73 or 0x74 or 0x75 or 0x77 => InstructionKind.MemoryWrite,

                // Stack operations
                0xC5 or 0xD5 or 0xE5 or 0xF5 => InstructionKind.StackPush,
                0xC1 or 0xD1 or 0xE1 or 0xF1 => InstructionKind.StackPop,

                // Everything else is a normal register operation
                _ => InstructionKind.Normal,
            };
        }

        private static InstructionKind ClassifyCBOpcode(byte opcode)
        {
            int regIndex = opcode & 0x07;
            if (regIndex == 6)
            {
                // Operations on (HL) — memory read or read-modify-write
                int group = (opcode >> 6) & 0x03;
                return group switch
                {
                    1 => InstructionKind.MemoryRead,  // BIT b,(HL) - read only
                    _ => InstructionKind.MemoryWrite,  // RLC/RRC/RL/RR/SLA/SRA/SWAP/SRL/RES/SET (HL) - read-modify-write
                };
            }
            return InstructionKind.Normal; // All register CB ops
        }
    }
}
