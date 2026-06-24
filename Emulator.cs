// Emulator.cs
using System.Text;
using Serilog;

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

        private readonly EmulatorConfig? _config;

        // Battery-save autosave bookkeeping: persist cartridge RAM periodically, but only when it has
        // actually changed since the last write (tracked by a cheap checksum).
        private int _batteryAutosaveCounter;
        private long _lastBatteryChecksum = -1;
        private const int BatteryAutosaveFrames = 600; // ~10 seconds at ~60 fps

        private bool _disposed = false;
        private Action<char> _serialDataHandler;

        public readonly StringBuilder SerialLog = new();
        public bool IsPaused => Cpu.IsPaused;

        /// <summary>Absolute path of the currently loaded ROM, or null if none has been loaded yet.</summary>
        public string? CurrentRomPath { get; private set; }

        /// <summary>A short, display-friendly name for the loaded ROM (the file name without extension).</summary>
        public string CurrentRomName =>
            CurrentRomPath is null ? "(no ROM)" : Path.GetFileNameWithoutExtension(CurrentRomPath);

        /// <summary>The loaded cartridge's title, used as part of the save-state ROM guard.</summary>
        public string RomTitle { get; private set; } = "";

        /// <summary>The cartridge header checksum (0x014D), used as part of the save-state ROM guard.</summary>
        public byte RomHeaderChecksum { get; private set; }

        /// <summary>True if a cartridge is currently loaded (save states and battery saves require one).</summary>
        public bool HasRom => CurrentRomPath != null;

        public Emulator(EmulatorConfig? config = null)
        {
            _config = config;
            Cpu = new Cpu(null); // MMU is set later

            // Build the audio backend first so the APU can generate at the device's native rate
            // and avoid any resampling. If audio fails to initialize, the emulator still runs —
            // just silently.
            int sampleRate = 48000;
            string? audioEnv = Environment.GetEnvironmentVariable("GBSHARP_AUDIO")?.ToLowerInvariant();
            if (audioEnv == "none")
            {
                // Explicitly headless: no audio device at all. Handy for automated tests and for
                // users on machines without working audio. The APU still runs and stays deterministic.
                Log.Information("Audio disabled via GBSHARP_AUDIO=none.");
                AudioSink = null;
            }
            else
            {
                // The env var (if set) wins, otherwise use the configured backend (Auto by default).
                AudioBackend backend = audioEnv switch
                {
                    "sdl" => AudioBackend.Sdl,
                    "openal" => AudioBackend.OpenAl,
                    _ => config?.Audio.Backend ?? AudioBackend.Auto
                };

                try
                {
                    AudioSink = CreateAudioSink(backend);
                    sampleRate = AudioSink.SampleRate;
                    Log.Information($"Audio system initialized: {AudioSink.GetType().Name} @ {sampleRate} Hz");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to initialize audio system. Sound will be disabled.");
                    AudioSink = null;
                }
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
        /// Creates the requested audio backend. <see cref="AudioBackend.Auto"/> prefers SDL3 and falls
        /// back to OpenAL if SDL is unavailable. The <c>GBSHARP_AUDIO</c> environment variable can force
        /// a specific backend (or "none") and takes precedence over the configured choice.
        /// </summary>
        private static IAudioSink CreateAudioSink(AudioBackend backend)
        {
            if (backend == AudioBackend.OpenAl) return new AudioStreamerAL();
            if (backend == AudioBackend.Sdl) return new SdlAudioSink();

            // Auto: SDL3 first, OpenAL as a fallback.
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

        /// <summary>
        /// Loads a ROM from disk and starts it from a clean power-on state. This works both for the
        /// very first ROM and for swapping games at runtime: it power-cycles the machine, inserts the
        /// new cartridge, and re-applies the post-boot-ROM register state.
        /// </summary>
        public void LoadRom(string path)
        {
            // Persist the outgoing game's battery RAM before we swap cartridges, so switching games at
            // runtime doesn't lose progress. (No-op on the very first load.)
            SaveBattery();

            var romData = File.ReadAllBytes(path);

            // A runtime load is a full power cycle: wipe the previous game's RAM and hardware state,
            // swap in the new cartridge, then re-apply boot state. On the very first load the reset
            // simply runs over already-zeroed arrays, so it is harmless.
            ResetMachineState();
            Mmu.LoadCartridge(romData);
            ApplyBootState();

            // Capture the ROM identity for the save-state guard (so a state can only be loaded back
            // into the same game).
            var header = RomHeader.Parse(romData);
            RomTitle = header.Title;
            RomHeaderChecksum = header.HeaderChecksum;

            CurrentRomPath = path;

            // Restore the incoming game's battery RAM (and RTC) from its .sav, if one exists.
            LoadBattery();
            _batteryAutosaveCounter = 0;

            Log.Information($"APU Status: {Apu.GetStatus()}");
        }

        /// <summary>
        /// Power-cycles the machine while keeping the same cartridge inserted (like hitting the reset
        /// button on real hardware). Battery-backed cartridge RAM lives inside the MBC and is left
        /// intact, exactly as a real reset would leave it.
        /// </summary>
        public void Reset()
        {
            ResetMachineState();
            ApplyBootState();
        }

        /// <summary>
        /// Returns every emulated component to its power-on state. Does not touch the inserted
        /// cartridge (the MBC and its RAM) or the live audio device.
        /// </summary>
        private void ResetMachineState()
        {
            Cpu.Reset();
            Mmu.Reset();
            Ppu.Reset();
            Timer.Reset();
            Apu.Reset();
            Joypad.Reset();
        }

        /// <summary>
        /// Re-applies the state a boot ROM would have left behind: an enabled APU plus the CPU and I/O
        /// registers for the detected hardware (DMG or GBC). This is the single place that re-runs the
        /// "boot" so <see cref="LoadRom"/> and <see cref="Reset"/> stay consistent.
        /// </summary>
        private void ApplyBootState()
        {
            // We have no boot ROM, so make sure the APU is on and generating sound.
            Apu.EnsureInitialized();

            if (Mmu.IsGameBoyColor)
            {
                Cpu.InitializeForGbc();
                InitializeHardwareForGbc();
            }
            else
            {
                Cpu.InitializeForDmg();
            }
        }

        /// <summary>The <c>.sav</c> file path for the current ROM, or null if no ROM is loaded.</summary>
        private string? BatterySavePath()
        {
            if (CurrentRomPath == null) return null;

            string? configuredDir = _config?.General.SaveDirectory;
            string directory = !string.IsNullOrWhiteSpace(configuredDir)
                ? configuredDir!
                : Path.GetDirectoryName(CurrentRomPath) ?? ".";

            return Path.Combine(directory, Path.GetFileNameWithoutExtension(CurrentRomPath) + ".sav");
        }

        /// <summary>Loads the current cartridge's battery RAM (and RTC) from its <c>.sav</c>, if present.</summary>
        private void LoadBattery()
        {
            if (!Mmu.CartridgeHasBattery) return;
            string? path = BatterySavePath();
            if (path == null || !File.Exists(path)) return;

            try
            {
                BatterySave.Load(path, Mmu.Cartridge!);
                _lastBatteryChecksum = BatteryChecksum(Mmu.GetCartridgeRam());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load battery save {Path}", path);
            }
        }

        /// <summary>Writes the current cartridge's battery RAM (and RTC) to its <c>.sav</c> file.</summary>
        public void SaveBattery()
        {
            if (!Mmu.CartridgeHasBattery) return;
            string? path = BatterySavePath();
            if (path == null) return;

            try
            {
                BatterySave.Save(path, Mmu.Cartridge!);
                _lastBatteryChecksum = BatteryChecksum(Mmu.GetCartridgeRam());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write battery save {Path}", path);
            }
        }

        /// <summary>Periodically flushes battery RAM to disk, but only when it has actually changed.</summary>
        private void MaybeAutosaveBattery()
        {
            if (!Mmu.CartridgeHasBattery) return;
            if (++_batteryAutosaveCounter < BatteryAutosaveFrames) return;

            _batteryAutosaveCounter = 0;
            long checksum = BatteryChecksum(Mmu.GetCartridgeRam());
            if (checksum == _lastBatteryChecksum) return; // unchanged since the last write
            SaveBattery();
        }

        /// <summary>A cheap FNV-1a checksum used only to detect whether battery RAM changed.</summary>
        private static long BatteryChecksum(byte[] data)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (byte b in data)
                {
                    hash ^= b;
                    hash *= 1099511628211L;
                }
                return hash;
            }
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
                RunOneFrame();
            }

            // Always service the audio backend, even while paused, so the device stays fed and
            // alive. The main loop handles frame pacing, so there is no busy-wait here.
            AudioSink?.Update();

            // Flush battery RAM to disk now and then so progress survives a crash, not just a clean exit.
            MaybeAutosaveBattery();
        }

        /// <summary>
        /// Runs exactly one frame even while the CPU is paused. This backs the frame-advance feature:
        /// step the game forward one frame at a time while otherwise frozen.
        /// </summary>
        public void StepFrame()
        {
            RunOneFrame();
            AudioSink?.Update();
        }

        /// <summary>Executes one frame's worth of CPU/PPU/timer/APU cycles.</summary>
        private void RunOneFrame()
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
                    // Final flush of battery RAM (and RTC) before we tear everything down.
                    SaveBattery();

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