namespace GameboySharp
{
    /// <summary>
    /// Pushes the user's saved settings into the live emulator objects. This is the one place that
    /// knows how a config value maps to a runtime knob, so startup and the Settings dialog can both
    /// apply changes the same way (the dialog edits the config, then calls back here).
    /// </summary>
    internal static class RuntimeConfig
    {
        /// <summary>Applies the audio settings (master volume, mutes) to the APU.</summary>
        public static void ApplyAudio(EmulatorConfig config, Apu apu)
        {
            apu.MasterVolume = config.Audio.MasterVolume;
            apu.Muted = config.Audio.Muted;
            apu.MuteChannel1 = config.Audio.MuteChannel1;
            apu.MuteChannel2 = config.Audio.MuteChannel2;
            apu.MuteChannel3 = config.Audio.MuteChannel3;
            apu.MuteChannel4 = config.Audio.MuteChannel4;
        }

        /// <summary>Applies the video settings (DMG palette, scanline effect) to the PPU and renderer.</summary>
        public static void ApplyVideo(EmulatorConfig config, Ppu ppu, ScreenRenderer renderer)
        {
            ppu.SetDmgPalette(DmgPalettes.Colors(config.Video.Palette));
            renderer.Scanlines = config.Video.ScanlineShader;
        }
    }
}
