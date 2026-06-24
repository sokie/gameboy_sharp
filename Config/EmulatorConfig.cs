using System.Collections.Generic;
using Silk.NET.Input;

namespace GameboySharp
{
    /// <summary>
    /// The set of DMG (original Game Boy) colour palettes the user can pick from. Each entry maps
    /// to four concrete ARGB colours in <see cref="Ppu"/>; this enum is just the persisted choice.
    /// </summary>
    public enum DmgPalettePreset
    {
        Green,      // The classic pea-green LCD look (the emulator's original default)
        Grey,       // Neutral greyscale
        Pocket,     // Game Boy Pocket's higher-contrast greys
        Yellow      // A warm amber tint
    }

    /// <summary>Which audio backend the emulator should try to use.</summary>
    public enum AudioBackend
    {
        Auto,       // SDL, falling back to OpenAL
        Sdl,
        OpenAl
    }

    /// <summary>
    /// All user-tweakable settings, serialised to <c>config.json</c> by <see cref="ConfigStore"/>.
    ///
    /// This is a plain data object (a POCO): no behaviour, just the values. It is split into small
    /// nested sections so each settings tab maps cleanly to one object. Every property has a sane
    /// default, so a brand-new install (or a config file that predates a new setting) still works.
    /// </summary>
    public class EmulatorConfig
    {
        public ControlsConfig Controls { get; set; } = new();
        public AudioConfig Audio { get; set; } = new();
        public VideoConfig Video { get; set; } = new();
        public GeneralConfig General { get; set; } = new();
        public HotkeysConfig Hotkeys { get; set; } = new();

        /// <summary>Most-recently-opened ROM paths, newest first (see <see cref="AddRecentRom"/>).</summary>
        public List<string> RecentRoms { get; set; } = new();

        /// <summary>Last game-window size in pixels, restored on the next launch.</summary>
        public int WindowWidth { get; set; } = GameboyConstants.ScreenWidth * 4;
        public int WindowHeight { get; set; } = GameboyConstants.ScreenHeight * 4;

        private const int MaxRecentRoms = 10;

        /// <summary>
        /// Records <paramref name="path"/> as the most recently used ROM, moving it to the front and
        /// trimming the list to a fixed length. De-duplicates case-insensitively so re-opening the
        /// same game doesn't create multiple entries.
        /// </summary>
        public void AddRecentRom(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            RecentRoms.RemoveAll(p => string.Equals(p, path, System.StringComparison.OrdinalIgnoreCase));
            RecentRoms.Insert(0, path);

            if (RecentRoms.Count > MaxRecentRoms)
            {
                RecentRoms.RemoveRange(MaxRecentRoms, RecentRoms.Count - MaxRecentRoms);
            }
        }
    }

    /// <summary>Keyboard and gamepad button bindings for the eight Game Boy buttons.</summary>
    public class ControlsConfig
    {
        /// <summary>Keyboard key bound to each Game Boy button.</summary>
        public Dictionary<GbButton, Key> Keyboard { get; set; } = DefaultKeyboard();

        /// <summary>Gamepad button bound to each Game Boy button (used when a gamepad is connected).</summary>
        public Dictionary<GbButton, ButtonName> Gamepad { get; set; } = DefaultGamepad();

        /// <summary>Analog-stick deflection (0..1) past which the stick counts as a d-pad press.</summary>
        public float GamepadDeadzone { get; set; } = 0.5f;

        /// <summary>The original hardcoded bindings, kept as the out-of-the-box defaults.</summary>
        public static Dictionary<GbButton, Key> DefaultKeyboard() => new()
        {
            { GbButton.Up, Key.Up },
            { GbButton.Down, Key.Down },
            { GbButton.Left, Key.Left },
            { GbButton.Right, Key.Right },
            { GbButton.A, Key.Z },
            { GbButton.B, Key.X },
            { GbButton.Start, Key.Enter },
            { GbButton.Select, Key.ShiftRight },
        };

        /// <summary>A conventional gamepad layout (face buttons for A/B, Start/Back for Start/Select).</summary>
        public static Dictionary<GbButton, ButtonName> DefaultGamepad() => new()
        {
            { GbButton.Up, ButtonName.DPadUp },
            { GbButton.Down, ButtonName.DPadDown },
            { GbButton.Left, ButtonName.DPadLeft },
            { GbButton.Right, ButtonName.DPadRight },
            { GbButton.A, ButtonName.A },
            { GbButton.B, ButtonName.B },
            { GbButton.Start, ButtonName.Start },
            { GbButton.Select, ButtonName.Back },
        };
    }

    /// <summary>Master volume, mute, and per-channel mute switches.</summary>
    public class AudioConfig
    {
        /// <summary>Master output gain, 0.0 (silent) to 1.0 (full).</summary>
        public float MasterVolume { get; set; } = 1.0f;

        /// <summary>Global mute. Independent of <see cref="MasterVolume"/> so un-muting restores it.</summary>
        public bool Muted { get; set; } = false;

        /// <summary>Per-channel mute switches: pulse A, pulse B, wave, noise.</summary>
        public bool MuteChannel1 { get; set; } = false;
        public bool MuteChannel2 { get; set; } = false;
        public bool MuteChannel3 { get; set; } = false;
        public bool MuteChannel4 { get; set; } = false;

        public AudioBackend Backend { get; set; } = AudioBackend.Auto;
    }

    /// <summary>Display options: palette, scaling, and the LCD/scanline shader toggle.</summary>
    public class VideoConfig
    {
        public DmgPalettePreset Palette { get; set; } = DmgPalettePreset.Green;

        /// <summary>Snap the game image to whole-number scale factors (crisp pixels, no blur).</summary>
        public bool IntegerScale { get; set; } = true;

        /// <summary>Keep the 10:9 Game Boy aspect ratio (letterbox/pillarbox instead of stretching).</summary>
        public bool LockAspectRatio { get; set; } = true;

        /// <summary>Apply the scanline/LCD-grid shader effect.</summary>
        public bool ScanlineShader { get; set; } = false;
    }

    /// <summary>Miscellaneous behaviour preferences.</summary>
    public class GeneralConfig
    {
        /// <summary>Automatically pause emulation when the game window loses focus.</summary>
        public bool PauseOnFocusLoss { get; set; } = false;

        /// <summary>
        /// Where battery (.sav) and save-state files are written. Empty means "next to the ROM",
        /// which is what most emulators do by default.
        /// </summary>
        public string SaveDirectory { get; set; } = "";

        /// <summary>Emulation speed multiplier for normal (non-turbo) running: 0.5, 1.0, or 2.0.</summary>
        public double SpeedMultiplier { get; set; } = 1.0;

        /// <summary>Silence audio while the turbo/fast-forward key is held (avoids chipmunk audio).</summary>
        public bool MuteDuringTurbo { get; set; } = true;
    }

    /// <summary>
    /// Global hotkeys. These are deliberately kept off the eight game-button defaults so they never
    /// clash with gameplay. Save-slot selection uses the number-row keys 0-9 and is not rebindable.
    /// </summary>
    public class HotkeysConfig
    {
        public Key SaveState { get; set; } = Key.F5;
        public Key LoadState { get; set; } = Key.F8;
        public Key ToggleDebug { get; set; } = Key.F11;
        public Key PausePlay { get; set; } = Key.Space;
        public Key Turbo { get; set; } = Key.Tab;
        public Key FrameAdvance { get; set; } = Key.N;
        public Key Reset { get; set; } = Key.R;
    }
}
