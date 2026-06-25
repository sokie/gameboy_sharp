# GameboySharp

A Game Boy and Game Boy Color emulator written in C# with a focus on code clarity and readability.

![Main View](docs/main_view.png)
![Debug Window](docs/debug_window.png)

## Philosophy

This emulator prioritizes clean, understandable code over raw performance optimizations. The goal is to provide a well-documented, educational implementation that accurately emulates the Game Boy hardware while remaining accessible to developers interested in learning about emulation.

## Features

Cross-platform support Windows, macOS, or Linux (via Silk.NET)

### CPU
- Complete implementation of all 256 base opcodes
- Complete implementation of all 256 CB-prefixed (extended) opcodes
- Accurate interrupt handling (V-Blank, LCD STAT, Timer, Serial, Joypad)
- HALT and HALT bug emulation
- Game Boy Color double-speed mode support

### PPU (Pixel Processing Unit)
- Full scanline-based rendering
- Background, Window, and Sprite layers
- Game Boy Color features:
  - VRAM banking
  - Color palettes (Background and Sprite)
  - Sprite priority attributes

### APU (Audio Processing Unit)
- All four sound channels:
  - Channel 1: Pulse with sweep
  - Channel 2: Pulse
  - Channel 3: Wave
  - Channel 4: Noise
- Real-time audio output (SDL3 by default, OpenAL fallback)
- Master volume, mute, and per-channel mute

### Memory Bank Controllers
- ROM Only
- MBC1
- MBC2
- MBC3 (with RTC support)
- MBC5

### Other
- Joypad input handling (keyboard **and** gamepad, both fully rebindable)
- Timer emulation
- Serial output logging
- Debug window with CPU state visualization

### Front-End & Quality of Life
- **In-app toolbar** on the game window: Open ROM, Play/Pause, Reset, frame-advance, speed, save-state slots, debug toggle, and settings — change games and tweak options without restarting
- **Settings dialog** with Controls / Audio / Video / General tabs (click-to-capture key & gamepad rebinding, volume/mute, DMG palettes, integer-scale/aspect-lock, scanline shader), persisted to disk
- **Save states** — 10 slots per game with thumbnails, quicksave/quickload hotkeys, and a versioned format that refuses to load into the wrong ROM
- **Battery saves** (`.sav`) — cartridge RAM (and MBC3 RTC) saved automatically and on exit, using the common raw-SRAM format other emulators read
- **Fast-forward / speed control** — 0.5x / 1x / 2x, hold-to-turbo, and single-frame advance while paused
- **Recent ROMs**, drag-and-drop ROM loading, an in-app file browser, and optional pause-on-focus-loss
- **Persistent configuration** — key bindings, audio/video preferences, recent ROMs, and window size are remembered between sessions

## Downloads

Pre-built, **self-contained** bundles for Windows, Linux, and macOS (Intel & Apple Silicon) are
attached to each [GitHub Release](../../releases) — no .NET install required. Download the archive
for your platform, extract it, and run the `GameboySharp` executable (or `GameboySharp.app` on macOS).
These are produced automatically by the release workflow (see [CI/CD](#cicd)).

### macOS: one-time Gatekeeper step

The macOS app is **not notarized** (notarization requires a paid Apple Developer account), so Gatekeeper
blocks it on first launch. Clear the quarantine flag once and it opens normally afterwards:

```bash
xattr -cr /path/to/GameboySharp.app
```

Alternatively, right-click the app → **Open** → **Open** the first time. Either way it's a one-time step.

> **Windows** shows a similar SmartScreen prompt for unsigned apps — click **More info → Run anyway**.
> **Linux** has no such gate; just `chmod +x GameboySharp` if needed and run it.

## Requirements

### Runtime Requirements
- **.NET 9.0 Runtime** or SDK
- **OpenGL 3.3** compatible graphics card
- **OpenAL** compatible audio system
- **Operating System**: Windows, macOS, or Linux (via Silk.NET)

### Development Requirements
- **.NET 9.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)
- **C# 13** compatible IDE (optional but recommended):
  - Visual Studio 2022 (v17.12+)
  - Visual Studio Code with C# Dev Kit
  - JetBrains Rider 2024.3+
- **Git** - For cloning the repository

## Building

Clone the repository and build using the .NET CLI:

```bash
git clone https://github.com/yourusername/gameboy_sharp.git
cd gameboy_sharp
dotnet build
```

For a release build:

```bash
dotnet build -c Release
```

## Running

Pass the path to your ROM as the first argument:

```bash
dotnet run -- path/to/your/rom.gb
```

Or run the compiled executable directly:

```bash
./bin/Debug/net9.0/GameboySharp path/to/your/rom.gb
```

The ROM argument is now **optional** — you can also launch with no ROM and open one from the
toolbar's **Open** button, the **Recent** menu, or by dragging a `.gb`/`.gbc` file onto the window.

### Audio backend
Audio plays through **SDL3** by default — it opens the device at its native sample rate (so no
resampling is needed) and recovers cleanly from buffer underruns. If SDL is unavailable it falls
back to OpenAL. Force a specific backend with the `GBSHARP_AUDIO` environment variable
(`sdl` or `openal`).

### First Run
On first run you'll see the **Game Window** with a toolbar across the top. The **Debug Window**
(CPU state, memory, VRAM, sprites) starts hidden — show it any time with the toolbar's **Debug**
button or the `F11` hotkey.

The emulator starts running immediately. Use the toolbar's **Play/Pause** and **Frame Adv** buttons
(or the debug window's **Step**/**Continue** controls) to inspect execution.

## Controls

### In-Game Controls (defaults)
These are the out-of-the-box bindings. Every button can be remapped to a different key **or a
gamepad button** in **Settings → Controls** (click a binding, then press the key/button you want).

| Game Boy Button | Keyboard Key | Gamepad     |
|----------------|--------------|-------------|
| D-Pad Up       | Arrow Up     | D-Pad Up / Left Stick |
| D-Pad Down     | Arrow Down   | D-Pad Down / Left Stick |
| D-Pad Left     | Arrow Left   | D-Pad Left / Left Stick |
| D-Pad Right    | Arrow Right  | D-Pad Right / Left Stick |
| A Button       | Z            | A           |
| B Button       | X            | B           |
| Start          | Enter        | Start       |
| Select         | Right Shift  | Back        |

### Toolbar
The toolbar across the top of the game window provides: **Open** / **Recent** (load a ROM),
**Play/Pause**, **Frame Adv** (step one frame while paused), **Reset** (with confirmation),
**Speed** (cycles 0.5x → 1x → 2x), **Save / Load / States** (save-state slots with thumbnails),
**Show/Hide Debug**, and **Settings**. The status area shows the ROM name, FPS, pause state, the
active save slot, and a fast-forward indicator.

### Hotkeys (defaults, configurable in Settings)
| Action                       | Key       |
|------------------------------|-----------|
| Save state (current slot)    | F5        |
| Load state (current slot)    | F8        |
| Select save slot             | 0 – 9     |
| Toggle debug window          | F11       |
| Pause / resume               | Space     |
| Reset                        | R         |
| Fast-forward (hold)          | Tab       |
| Frame-advance (while paused) | N         |

### Debug Controls
- **Continue** - Resume execution
- **Pause** - Pause execution
- **Step** - Execute one CPU instruction
- **Memory Viewer** - Inspect memory addresses
- **VRAM Viewer** - View tiles and background data
- **Sprite Viewer** - Inspect sprite attributes
- **Serial Output** - View debug messages from ROM

## Configuration & Save Data

### Settings (`config.json`)
All settings — key/gamepad bindings, audio volume & mutes, video options, recent ROMs, and the
last window size — are stored in a single JSON file that's loaded at startup and saved on change
and on exit. It lives in your platform's application-data folder:

| Platform | Location |
|----------|----------|
| Windows  | `%AppData%\GameboySharp\config.json` |
| macOS    | `~/Library/Application Support/GameboySharp/config.json` |
| Linux    | `~/.config/GameboySharp/config.json` |

The file is human-readable (enums are written as names), so you can hand-edit it if you like. If
it's missing or corrupt, the emulator quietly falls back to sensible defaults.

### Save states
Save states capture the **entire** machine — CPU, memory, PPU, timer, APU, and cartridge state — so
you can resume exactly where you left off. There are **10 slots per game**, each shown with a
thumbnail in the toolbar's **States** menu.

- Quicksave/quickload the active slot with `F5` / `F8`; pick a slot with `0`–`9`.
- Files are written next to the ROM as `<rom>.state0` … `<rom>.state9` (or in the configured save
  folder).
- Each state stores a guard (ROM title + header checksum), so loading a state into the wrong game
  is rejected rather than corrupting it.

### Battery saves (`.sav`)
Games with battery-backed cartridges (e.g. RPG save files) persist automatically. Cartridge RAM —
and the MBC3 real-time clock, in the standard BGB/VBA layout — is written to `<rom>.sav` next to the
ROM. It's loaded when the game starts and saved on exit, when switching games, and periodically
while playing (only when the RAM has actually changed). The raw-SRAM format is compatible with other
emulators.

> By default save states and `.sav` files sit next to the ROM. Set a dedicated **Save folder** in
> **Settings → General** to keep them elsewhere.

## Testing

### Unit tests

```bash
dotnet test
```

Covers the APU and audio pipeline (`IAudioSink`), config save/load round-trips, and battery `.sav`
round-trips. The save-state and reset **determinism** tests (run → save/reset → run again and assert
the subsequent output is byte-identical — framebuffer, CPU registers, work RAM, and high RAM — which
catches any unserialized latch whose omission would change execution) are opt-in: they run only when
you point `GBSHARP_TEST_ROM` at a Game Boy ROM, and skip cleanly otherwise (no ROM is bundled).

```bash
GBSHARP_TEST_ROM=/path/to/rom.gb dotnet test
```

The CPU vector test described below is likewise skipped unless its data is present, so the suite
stays fast and self-contained by default.

### CPU accuracy — SM83 single-step vectors

The CPU is validated against the community-standard
[SingleStepTests/sm83](https://github.com/SingleStepTests/sm83) ("TomHarte") vectors: one JSON
file per opcode, 1000 single-instruction cases each (**500,000** total). Each case sets an initial
CPU + memory state, executes exactly one instruction, and checks every register, flag, and memory
cell against the expected final state.

The vectors (~160 MB) are **not** committed to this repo. Pull them first:

```bash
# Clone just the latest snapshot; the vectors are in sm83/v1
git clone --depth 1 https://github.com/SingleStepTests/sm83.git
```

Then point `SM83_TEST_DIR` at the vector folder, which enables the otherwise-skipped CPU test:

```bash
SM83_TEST_DIR=sm83/v1 dotnet test
```

All 500 opcodes pass 100% of register/RAM checks (~500,000 cases in a couple of seconds). STOP and
HALT differ only in reported cycle count — a known modeling nuance for those two instructions that
doesn't affect computed results.

### Audio & PPU ROM tests — screenshot comparison

Audio and graphics are validated against the community-standard
[c-sp/game-boy-test-roms](https://github.com/c-sp/game-boy-test-roms) suite by rendering a fixed
number of frames and comparing the framebuffer to a reference screenshot, **pixel for pixel**. Four
ROMs are covered, spanning monochrome (DMG) and colour (CGB) hardware:

| Test ROM    | Checks            | Status |
|-------------|-------------------|--------|
| `dmg-acid2` | DMG PPU rendering | Passes — pixel-exact |
| `cgb-acid2` | CGB PPU rendering | Passes — pixel-exact |
| `dmg_sound` | APU (Blargg)      | Partial — frequency sweep passes; wave-RAM-access and the sweep "exit negate mode" quirk still fail |
| `cgb_sound` | APU (Blargg)      | Partial — as `dmg_sound`, plus some CGB-only length/power edge cases |

The ROMs and reference images (~3.6 MB) are **not** committed. Download the
`game-boy-test-roms-v7.0.zip` asset from the
[v7.0 release](https://github.com/c-sp/game-boy-test-roms/releases/tag/v7.0) and extract it once (the
archive unzips flat, co-locating each ROM with its reference PNG):

```bash
gh release download v7.0 --repo c-sp/game-boy-test-roms --pattern '*.zip'
unzip game-boy-test-roms-v7.0.zip -d game-boy-test-roms
```

Then point `GBSHARP_TEST_ROMS_DIR` at the extracted folder to enable the otherwise-skipped tests:

```bash
GBSHARP_TEST_ROMS_DIR=game-boy-test-roms dotnet test
```

Both acid2 tests must match their reference exactly — a regression fails the build. The two Blargg
sound tests are **documented accuracy gaps**: each *skips* (rather than fails) while it still
mismatches, and will automatically stop skipping the moment the emulator renders it correctly — a
built-in nudge to lock the win in. The remaining APU sub-tests — wave-RAM access timing, the sweep
"exit negate mode" quirk, and some CGB length/power edge cases — are the current accuracy backlog.

### Audio validation harness

`AudioHarness` exercises the SDL3 backend against a real audio device:

```bash
dotnet run --project AudioHarness -- info        # show the negotiated audio driver/rate
dotnet run --project AudioHarness -- tone        # play an audible 440 Hz tone
dotnet run --project AudioHarness -- resample    # offline resample correctness (PASS/FAIL)
dotnet run --project AudioHarness -- recover     # buffer-underrun auto-recovery (PASS/FAIL)
dotnet run --project AudioHarness -- play <rom>  # play a real game's audio, headless
```

## CI/CD

Two GitHub Actions workflows live in `.github/workflows/`:

- **`ci.yml`** — builds and runs the test suite on every push / PR to `main` (fully headless on Linux).
- **`release.yml`** — on pushing a version tag (`vX.Y.Z`), cross-builds **self-contained** bundles for
  Windows (`win-x64`), Linux (`linux-x64`), and macOS (`osx-x64` + `osx-arm64`), packages each
  (`.zip` on Windows, `.tar.gz` elsewhere, with a double-clickable `.app` on macOS), and attaches them
  to an auto-generated GitHub Release.

Cut a release by pushing a tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Project Structure

```
GameboySharp/
├── Core/
│   ├── Cpu.cs          # CPU emulation and opcode implementation
│   ├── Ppu.cs          # Graphics processing unit
│   ├── Apu.cs          # Audio processing unit
│   ├── Mmu.cs          # Memory management unit
│   ├── Timer.cs        # Timer and divider registers
│   ├── Joypad.cs       # Input handling
│   ├── RomHeader.cs    # ROM header parsing
│   └── IORegisters.cs  # I/O register addresses
├── Storage/
│   ├── IMbc.cs         # Memory bank controller interface
│   ├── RomOnly.cs      # No MBC (32KB ROMs)
│   ├── Mbc1.cs         # MBC1 implementation
│   ├── Mbc2.cs         # MBC2 implementation
│   ├── Mbc3.cs         # MBC3 implementation (with RTC)
│   ├── Mbc5.cs         # MBC5 implementation
│   └── BatterySave.cs  # .sav battery RAM (+ MBC3 RTC) file format
├── Config/
│   ├── EmulatorConfig.cs  # All user settings (POCO)
│   ├── ConfigStore.cs     # Load/save config.json
│   ├── DmgPalettes.cs     # DMG palette presets
│   └── RuntimeConfig.cs   # Applies config to the live emulator
├── Input/
│   ├── GbButton.cs        # The eight logical Game Boy buttons
│   └── InputManager.cs    # Keyboard + gamepad → joypad, hotkeys
├── State/
│   ├── SaveState.cs        # Versioned save-state container + thumbnail
│   └── SaveStateManager.cs # Slots, quicksave/quickload
├── Sound/
│   ├── ChannelBase.cs       # Base class for audio channels
│   ├── PulseChannel.cs      # Pulse wave channel
│   ├── PulseWithSweepChannel.cs  # Pulse with frequency sweep
│   ├── WaveChannel.cs       # Wavetable channel
│   ├── NoiseChannel.cs      # Noise channel
│   └── AudioStreamerAL.cs   # OpenAL audio output
├── UI/
│   ├── GameWindow.cs      # Main emulator window (hosts the toolbar + dialogs)
│   ├── DebugWindow.cs     # Debug/inspection window
│   ├── Toolbar.cs         # Top toolbar
│   ├── FileBrowser.cs     # In-app "Open ROM" file picker
│   ├── SettingsDialog.cs  # Controls / Audio / Video / General settings
│   ├── ThumbnailCache.cs  # GL textures for save-state thumbnails
│   └── ScreenRenderer.cs  # OpenGL rendering
├── Emulator.cs         # Main emulator orchestration
└── Program.cs          # Entry point
```

## Project Status

**Current State: Functional**

The emulator successfully runs many commercial Game Boy and Game Boy Color games. Core functionality is complete:

- All CPU instructions implemented and tested
- PPU renders graphics correctly for most games
- APU produces accurate sound output
- Major MBC types supported

### Known Limitations

- No boot ROM emulation (games start directly)
- Some edge-case timing behaviors may differ from real hardware
- The joypad interrupt is currently disabled (most games poll the joypad register instead, so this
  rarely matters)

## Architecture Overview

### Component Design
The emulator follows a **component-based architecture** where each hardware component is isolated:

```
Emulator (Coordinator)
├── CPU (Sharp LR35902)
│   └── Opcodes: 256 base + 256 CB extended
├── MMU (Memory Management)
│   ├── ROM (via MBC)
│   ├── VRAM (8KB x 2 banks)
│   ├── External RAM (via MBC)
│   ├── WRAM (4KB + 4KB x 7 banks)
│   ├── OAM (160 bytes)
│   └── I/O Registers
├── PPU (Graphics)
│   ├── Background rendering
│   ├── Window rendering
│   ├── Sprite rendering
│   └── Frame buffer (160x144 RGBA)
├── APU (Audio)
│   ├── Channel 1: Pulse + Sweep
│   ├── Channel 2: Pulse
│   ├── Channel 3: Wave
│   └── Channel 4: Noise
├── Timer (Timer & Divider)
├── Joypad (Input)
└── MBC (Cartridge Banking)
```

## Development

### Debugging
The emulator includes extensive debugging capabilities:

1. **CPU Debugging**:
   - View all registers in real-time
   - Step through instructions one by one
   - See opcode mnemonics and descriptions
   - Monitor interrupt status

2. **Memory Debugging**:
   - Memory viewer with hex/ASCII display
   - Watch specific memory addresses
   - View MBC state (current ROM/RAM banks)

3. **Graphics Debugging**:
   - View all 384 tiles from VRAM
   - Inspect sprite attributes (position, tile, flags)
   - View GBC palettes in real-time
   - Toggle rendering layers (BG, Window, Sprites)

4. **Logging**:
   - Serilog structured logging
   - Console output (Info level)
   - File output: `logs/log-YYYYMMDD.txt`
   - Serial data output in debug window

## Dependencies

- [Silk.NET](https://github.com/dotnet/Silk.NET) - Windowing, input, gamepad, OpenGL, and OpenAL bindings
- [SDL3-CS](https://github.com/ppy/SDL3-CS) - SDL3 bindings (default audio backend)
- [ImGui.NET](https://github.com/mellinoe/ImGui.NET) - Toolbar, settings/file dialogs, and debug UI
- [Serilog](https://serilog.net/) - Logging

Settings are serialized with `System.Text.Json`, which ships with .NET — no extra dependency.

## License

This project is provided for educational purposes.

## Acknowledgments

- [Pan Docs](https://gbdev.io/pandocs/) - Comprehensive Game Boy technical reference
- [gbdev community](https://gbdev.io/) - Resources and test ROMs
- [Blargg's test ROMs](https://github.com/retrio/gb-test-roms) - CPU instruction tests
