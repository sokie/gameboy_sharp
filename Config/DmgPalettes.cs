namespace GameboySharp
{
    /// <summary>
    /// Maps each <see cref="DmgPalettePreset"/> to the four concrete shades the PPU renders DMG games
    /// with (index 0 = lightest … 3 = darkest). Colours are packed in the PPU's AABBGGRR layout via
    /// <see cref="Rgb"/>, which takes plain red/green/blue so the tables below stay readable.
    /// </summary>
    public static class DmgPalettes
    {
        /// <summary>Packs an opaque colour into the PPU's AABBGGRR uint layout.</summary>
        private static uint Rgb(byte r, byte g, byte b) =>
            0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;

        public static uint[] Colors(DmgPalettePreset preset) => preset switch
        {
            // The original pea-green LCD look (kept as the shared default in Ppu).
            DmgPalettePreset.Green => (uint[])Ppu._dmgColors.Clone(),

            // Neutral greyscale.
            DmgPalettePreset.Grey => new[]
            {
                Rgb(0xFF, 0xFF, 0xFF), Rgb(0xAA, 0xAA, 0xAA), Rgb(0x55, 0x55, 0x55), Rgb(0x00, 0x00, 0x00)
            },

            // Game Boy Pocket's cooler, slightly green-tinted greys.
            DmgPalettePreset.Pocket => new[]
            {
                Rgb(0xE3, 0xE6, 0xD0), Rgb(0xA8, 0xB0, 0x95), Rgb(0x53, 0x5A, 0x4A), Rgb(0x10, 0x14, 0x0C)
            },

            // A warm amber tint.
            DmgPalettePreset.Yellow => new[]
            {
                Rgb(0xFF, 0xF0, 0xB9), Rgb(0xE6, 0xC8, 0x64), Rgb(0xA0, 0x78, 0x28), Rgb(0x50, 0x32, 0x0A)
            },

            _ => (uint[])Ppu._dmgColors.Clone(),
        };
    }
}
