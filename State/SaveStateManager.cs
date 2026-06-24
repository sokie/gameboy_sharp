using System;
using System.IO;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// Manages the numbered save-state slots for the current ROM. Each slot maps to a file named
    /// <c>&lt;rom&gt;.stateN</c> next to the ROM (or in the configured save folder). Provides the
    /// quicksave/quickload pair the hotkeys use, plus thumbnail access for the toolbar's slot menu.
    /// </summary>
    internal class SaveStateManager
    {
        public const int SlotCount = 10;

        private readonly Emulator _emulator;
        private readonly EmulatorConfig _config;

        /// <summary>The slot quicksave/quickload act on, selectable with the number keys.</summary>
        public int CurrentSlot { get; set; }

        public SaveStateManager(Emulator emulator, EmulatorConfig config)
        {
            _emulator = emulator;
            _config = config;
        }

        public bool QuickSave() => SaveSlot(CurrentSlot);
        public bool QuickLoad() => LoadSlot(CurrentSlot);

        /// <summary>Writes the current machine state to <paramref name="slot"/>. Returns success.</summary>
        public bool SaveSlot(int slot)
        {
            if (!_emulator.HasRom)
            {
                Log.Warning("Cannot save state: no ROM is loaded.");
                return false;
            }

            try
            {
                string path = SlotPath(slot);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Write to a temp file first, then move it into place, so a crash mid-write can never
                // leave a half-written (corrupt) save state behind.
                string tempPath = path + ".tmp";
                using (var stream = File.Create(tempPath))
                {
                    SaveState.Save(_emulator, stream);
                }
                File.Move(tempPath, path, overwrite: true);

                Log.Information("Saved state to slot {Slot}: {Path}", slot, path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save state to slot {Slot}.", slot);
                return false;
            }
        }

        /// <summary>Restores the machine state from <paramref name="slot"/>. Returns success.</summary>
        public bool LoadSlot(int slot)
        {
            if (!_emulator.HasRom)
            {
                Log.Warning("Cannot load state: no ROM is loaded.");
                return false;
            }

            string path = SlotPath(slot);
            if (!File.Exists(path))
            {
                Log.Information("No save state in slot {Slot}.", slot);
                return false;
            }

            try
            {
                using var stream = File.OpenRead(path);
                bool ok = SaveState.TryLoad(_emulator, stream);
                if (ok) Log.Information("Loaded state from slot {Slot}.", slot);
                return ok;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load state from slot {Slot}.", slot);
                return false;
            }
        }

        public bool HasState(int slot) => _emulator.HasRom && File.Exists(SlotPath(slot));

        /// <summary>When the slot was last saved, or null if it's empty.</summary>
        public DateTime? Timestamp(int slot)
        {
            string path = _emulator.HasRom ? SlotPath(slot) : "";
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }

        /// <summary>Reads a slot's thumbnail for preview, or null if the slot is empty/unreadable.</summary>
        public SaveStateThumbnail? ReadThumbnail(int slot)
        {
            if (!HasState(slot)) return null;
            try
            {
                using var stream = File.OpenRead(SlotPath(slot));
                return SaveState.TryReadThumbnail(stream);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read thumbnail for slot {Slot}.", slot);
                return null;
            }
        }

        /// <summary>The absolute file path backing a slot for the current ROM.</summary>
        public string SlotPath(int slot)
        {
            string baseName = Path.GetFileNameWithoutExtension(_emulator.CurrentRomPath!);
            return Path.Combine(ResolveSaveDirectory(), $"{baseName}.state{slot}");
        }

        /// <summary>The configured save folder, or the ROM's own folder when none is set.</summary>
        private string ResolveSaveDirectory()
        {
            string configured = _config.General.SaveDirectory;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
            return Path.GetDirectoryName(_emulator.CurrentRomPath!) ?? Directory.GetCurrentDirectory();
        }
    }
}
