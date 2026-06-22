// Emulator.cs
using System.Text;
using Serilog;
using Silk.NET.Input;

namespace GameboySharp
{
    public static class GameboyConstants
    {
        public const int ScreenWidth = 160;
        public const int ScreenHeight = 144;

        public const double CpuClockSpeed = 4194304; // 4.19 MHz
        public const int CyclesPerFrame = 70224; // 154 scanlines × 456 T-cycles
        public const double FramesPerSecond = CpuClockSpeed / CyclesPerFrame; // ~59.7275
    }

    internal class Emulator : IDisposable
    {
        public readonly Cpu Cpu;
        public readonly Mmu Mmu;
        public readonly Ppu Ppu;
        public readonly Timer Timer;
        public readonly Joypad Joypad;

        public readonly Apu Apu;

        internal IAudioSink? AudioSink;
        
        private bool _disposed = false;
        private Action<char> _serialDataHandler;

        public readonly StringBuilder SerialLog = new();
        public bool IsPaused => Cpu.IsPaused;

        public Emulator()
        {
            Cpu = new Cpu(null); // MMU is set later

            // Build the audio backend first so the APU can generate at the device's native rate
            // and avoid any resampling. If audio fails to initialize, the emulator still runs —
            // just silently.
            int sampleRate = 48000;
            try
            {
                AudioSink = CreateAudioSink();
                sampleRate = AudioSink.SampleRate;
                Log.Information($"Audio system initialized: {AudioSink.GetType().Name} @ {sampleRate} Hz");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize audio system. Sound will be disabled.");
                AudioSink = null;
            }

            Apu = new Apu(sampleRate);
            if (AudioSink != null) Apu.AudioBufferReady += AudioSink.Submit;

            Joypad = new Joypad(Cpu);
            Timer = new Timer(Cpu);
            Ppu = new Ppu(null, Cpu); // MMU is set later
            Mmu = new Mmu(Joypad, Ppu, Timer, Cpu, Apu);

            // Link components
            Cpu.SetMmu(Mmu);
            Ppu.SetMmu(Mmu);

            // Subscribe to events
            _serialDataHandler = c => SerialLog.Append(c);
            Mmu.OnSerialData += _serialDataHandler;
        }

        /// <summary>
        /// Creates the audio backend: SDL3 by default, OpenAL as a fallback. Set the
        /// GBSHARP_AUDIO environment variable to "sdl" or "openal" to force a specific backend
        /// (handy for A/B testing the two implementations).
        /// </summary>
        private static IAudioSink CreateAudioSink()
        {
            string? preference = Environment.GetEnvironmentVariable("GBSHARP_AUDIO")?.ToLowerInvariant();
            if (preference == "openal") return new AudioStreamerAL();
            if (preference == "sdl") return new SdlAudioSink();

            try
            {
                return new SdlAudioSink();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SDL audio backend unavailable; falling back to OpenAL.");
                return new AudioStreamerAL();
            }
        }

        public void LoadRom(string path)
        {
            var romData = File.ReadAllBytes(path);
            Mmu.LoadCartridge(romData);

             // Ensure APU is properly initialized after ROM loading
            Apu.EnsureInitialized();

            // Initialize CPU and hardware registers based on ROM type
            if (Mmu.IsGameBoyColor)
            {
                Cpu.InitializeForGbc();
                InitializeHardwareForGbc();
            }
            else
            {
                Cpu.InitializeForDmg();
            }
           
            Log.Information($"APU Status: {Apu.GetStatus()}");
        }

        public void UpdateInput(IKeyboard keyboard)
        {
            Joypad.Update(keyboard);
        }

        /// <summary>
        /// Gets comprehensive audio system status for debugging.
        /// </summary>
        public string GetAudioStatus()
        {
            var apuStatus = Apu.GetStatus();
            var audioStatus = AudioSink?.GetStatus() ?? "Audio system not available";
            return $"APU: {apuStatus} | Audio: {audioStatus}";
        }

        public void RunFrame()
        {
            if (!Cpu.IsPaused)
            {
                int cyclesThisFrame = 0;
                while (cyclesThisFrame < GameboyConstants.CyclesPerFrame)
                {
                    int cycles = Cpu.Step();

                    // PPU and Timer run in machine cycles, which are half the CPU cycles when
                    // the Game Boy Color is in double-speed mode.
                    int machineCycles = Mmu.IsDoubleSpeedMode ? cycles / 2 : cycles;

                    Ppu.Step(machineCycles);
                    Timer.Tick(machineCycles);
                    Apu.Step(machineCycles);

                    cyclesThisFrame += machineCycles;
                }
            }

            // Always service the audio backend, even while paused, so the device stays fed and
            // alive. The main loop handles frame pacing, so there is no busy-wait here.
            AudioSink?.Update();
        }

        private void InitializeHardwareForGbc()
        {
            // This function writes the initial values to I/O registers
            // as if the GBC boot ROM had just finished running.
            // Using the IORegisters class makes this much more readable.
            Mmu.WriteByte(IORegisters.LCDC, 0x91); // LCD On, BG/Win On, OBJ On
            Mmu.WriteByte(IORegisters.STAT, 0x85); // Initial mode is V-Blank or H-Blank
            Mmu.WriteByte(IORegisters.LY, 0x90);   // The boot ROM finishes with LY at 0x90 (144)
            Mmu.WriteByte(IORegisters.BGP, 0xFC);  // BG Palette Data for DMG mode
            Mmu.WriteByte(IORegisters.OBP0, 0xFF);
            Mmu.WriteByte(IORegisters.OBP1, 0xFF);
            Mmu.WriteByte(IORegisters.VBK, 0xFF);  // VRAM Bank Select - Bank 0
            Mmu.WriteByte(IORegisters.SVBK, 0xFF); // WRAM Bank Select - Bank 1

            // Sound registers
            Mmu.WriteByte(0xFF10, 0x80); // NR10
            Mmu.WriteByte(0xFF11, 0xBF); // NR11
            Mmu.WriteByte(0xFF12, 0xF3); // NR12
            Mmu.WriteByte(0xFF14, 0xBF); // NR14
            Mmu.WriteByte(0xFF26, 0xF1); // NR52

            Mmu.WriteByte(IORegisters.P1_JOYP, 0xCF);
            Mmu.WriteByte(IORegisters.IF, 0xE1);
            Mmu.WriteByte(IORegisters.IE, 0x00);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events
                    if (Mmu != null && _serialDataHandler != null)
                    {
                        Mmu.OnSerialData -= _serialDataHandler;
                    }
                    
                    if (Apu != null && AudioSink != null)
                    {
                        Apu.AudioBufferReady -= AudioSink.Submit;
                    }

                    // Dispose managed resources
                    AudioSink?.Dispose();
                    SerialLog?.Clear();
                }
                _disposed = true;
            }
        }
    }
}