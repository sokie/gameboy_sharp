using System;
using System.IO;
using Silk.NET.Input;
using Xunit;

namespace GameboySharp.Tests.ConfigTests;

/// <summary>
/// Verifies that <see cref="EmulatorConfig"/> survives a JSON save/load round-trip — in particular
/// the enum-keyed control maps, which are the part most likely to break under a serializer change.
/// Uses temp files so the developer's real config is never touched.
/// </summary>
public class ConfigStoreTests
{
    [Fact]
    public void Config_RoundTripsThroughDisk()
    {
        var config = new EmulatorConfig();
        config.Audio.MasterVolume = 0.42f;
        config.Audio.Muted = true;
        config.Audio.MuteChannel3 = true;
        config.Audio.Backend = AudioBackend.OpenAl;
        config.Video.Palette = DmgPalettePreset.Yellow;
        config.Video.ScanlineShader = true;
        config.Video.IntegerScale = false;
        config.General.PauseOnFocusLoss = true;
        config.General.SpeedMultiplier = 2.0;
        config.General.SaveDirectory = "/tmp/saves";
        config.Controls.Keyboard[GbButton.A] = Key.K;            // changed keyboard binding
        config.Controls.Gamepad[GbButton.Start] = ButtonName.Home; // changed gamepad binding
        config.Hotkeys.SaveState = Key.F2;
        config.AddRecentRom("/games/zelda.gb");
        config.WindowWidth = 800;
        config.WindowHeight = 720;

        string path = TempConfigPath();
        try
        {
            ConfigStore.Save(config, path);
            Assert.True(File.Exists(path));

            EmulatorConfig loaded = ConfigStore.Load(path);

            Assert.Equal(0.42f, loaded.Audio.MasterVolume);
            Assert.True(loaded.Audio.Muted);
            Assert.True(loaded.Audio.MuteChannel3);
            Assert.Equal(AudioBackend.OpenAl, loaded.Audio.Backend);
            Assert.Equal(DmgPalettePreset.Yellow, loaded.Video.Palette);
            Assert.True(loaded.Video.ScanlineShader);
            Assert.False(loaded.Video.IntegerScale);
            Assert.True(loaded.General.PauseOnFocusLoss);
            Assert.Equal(2.0, loaded.General.SpeedMultiplier);
            Assert.Equal("/tmp/saves", loaded.General.SaveDirectory);
            Assert.Equal(Key.K, loaded.Controls.Keyboard[GbButton.A]);
            Assert.Equal(ButtonName.Home, loaded.Controls.Gamepad[GbButton.Start]);
            Assert.Equal(Key.F2, loaded.Hotkeys.SaveState);
            Assert.Contains("/games/zelda.gb", loaded.RecentRoms);
            Assert.Equal(800, loaded.WindowWidth);
            Assert.Equal(720, loaded.WindowHeight);
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        EmulatorConfig loaded = ConfigStore.Load(TempConfigPath());

        Assert.NotNull(loaded);
        // Default keyboard bindings should be present.
        Assert.Equal(Key.Z, loaded.Controls.Keyboard[GbButton.A]);
        Assert.Equal(Key.Up, loaded.Controls.Keyboard[GbButton.Up]);
    }

    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), "gbsharp_config_" + Guid.NewGuid().ToString("N") + ".json");

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
