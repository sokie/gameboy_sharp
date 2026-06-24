using System;
using System.IO;
using Xunit;

namespace GameboySharp.Tests.StateTests;

/// <summary>
/// Exercises <see cref="SaveStateManager"/> end-to-end through the filesystem: it should write a slot
/// file, then load it back and restore the exact machine state. This covers the parts the in-memory
/// round-trip test doesn't — slot file naming, the temp-file-then-move write, and save-folder routing.
/// </summary>
public class SaveStateManagerTests
{
    [SkippableFact]
    public void SaveAndLoadSlot_RestoresStateFromDisk()
    {
        string? rom = EmulatorTestHarness.FindTestRom();
        Skip.If(rom == null, "Set GBSHARP_TEST_ROM to a .gb/.gbc ROM to run this test.");

        var config = new EmulatorConfig();
        string tempDir = Path.Combine(Path.GetTempPath(), "gbsharp_state_test_" + Guid.NewGuid().ToString("N"));
        config.General.SaveDirectory = tempDir;

        try
        {
            using var emulator = EmulatorTestHarness.CreateHeadless();
            emulator.LoadRom(rom!);
            var manager = new SaveStateManager(emulator, config);

            EmulatorTestHarness.RunFrames(emulator, 60);
            ulong atSnapshot = EmulatorTestHarness.Fingerprint(emulator);

            Assert.True(manager.SaveSlot(3));
            Assert.True(File.Exists(manager.SlotPath(3)));
            Assert.True(manager.HasState(3));

            // Run on so the live state diverges from the snapshot, then load the slot back.
            EmulatorTestHarness.RunFrames(emulator, 60);
            Assert.NotEqual(atSnapshot, EmulatorTestHarness.Fingerprint(emulator));

            Assert.True(manager.LoadSlot(3));
            Assert.Equal(atSnapshot, EmulatorTestHarness.Fingerprint(emulator));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
