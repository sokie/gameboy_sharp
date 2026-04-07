namespace GameboySharp.Jit
{
    internal class JitCpu
    {
        private readonly Cpu _cpu;
        private readonly Mmu _mmu;
        private readonly Ppu _ppu;
        private readonly Timer _timer;
        private readonly Apu _apu;

        public BlockCache Cache { get; }

        public JitCpu(Cpu cpu, Mmu mmu, Ppu ppu, Timer timer, Apu apu)
        {
            _cpu = cpu;
            _mmu = mmu;
            _ppu = ppu;
            _timer = timer;
            _apu = apu;
            Cache = new BlockCache();
        }

        /// <summary>
        /// Executes one step (a full basic block or a single interpreted instruction for edge cases).
        /// Returns machine cycles consumed (adjusted for double-speed mode).
        /// </summary>
        public int Step()
        {
            _cpu.ProcessScheduledInterruptEnable();

            int tCycles = 0;

            // Handle HALT state
            byte pendingInterrupts = (byte)(_cpu.ie_register & _cpu.if_register);
            if (_cpu.IsHalted)
            {
                if (pendingInterrupts != 0)
                {
                    _cpu.IsHalted = false;
                    if (!_cpu.InterruptMasterEnable)
                    {
                        _cpu.IsHaltBugActive = true;
                    }
                }
                else
                {
                    // Stay halted, consume 4 T-cycles, sync subsystems
                    SyncSubsystems(4, _mmu);
                    return _mmu.IsDoubleSpeedMode ? 2 : 4;
                }
            }

            // Fall back to interpreter for edge cases
            if (_cpu.IsHaltBugActive || _cpu.EnableInterruptsScheduled > 0 ||
                _cpu.IsStepping || _cpu.IsPaused)
            {
                tCycles = _cpu.Execute();
                int machineCycles = _mmu.IsDoubleSpeedMode ? tCycles / 2 : tCycles;
                _ppu.Step(machineCycles);
                _timer.Tick(machineCycles);
                _apu.Step(machineCycles);
                tCycles += _cpu.HandleInterrupts();
                return _mmu.IsDoubleSpeedMode ? tCycles / 2 : tCycles;
            }

            // JIT path: look up or compile block
            int romBank = GetCurrentRomBank(_cpu.PC);
            BasicBlock? block = Cache.Lookup(_cpu.PC, romBank);

            if (block == null)
            {
                block = BlockDecoder.Decode(_cpu.PC, romBank, _mmu);
                BlockCompiler.Compile(block, this);
                Cache.Insert(block);
            }

            // Execute the compiled block
            // The block itself calls SyncSubsystems per instruction
            tCycles = block.CompiledExecute!(_cpu, _mmu);
            block.ExecutionCount++;

            // Handle interrupts after the block
            tCycles += _cpu.HandleInterrupts();

            return _mmu.IsDoubleSpeedMode ? tCycles / 2 : tCycles;
        }

        /// <summary>
        /// Called by compiled blocks after each instruction to sync PPU/Timer/APU.
        /// </summary>
        public void SyncSubsystems(int tCycles, Mmu mmu)
        {
            int machineCycles = mmu.IsDoubleSpeedMode ? tCycles / 2 : tCycles;
            _ppu.Step(machineCycles);
            _timer.Tick(machineCycles);
            _apu.Step(machineCycles);
        }

        private int GetCurrentRomBank(ushort pc)
        {
            if (pc < 0x4000) return 0;
            if (pc < 0x8000) return _mmu.GetCurrentRomBank();
            return -1; // Non-ROM region (WRAM, HRAM)
        }
    }
}
