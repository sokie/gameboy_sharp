namespace GameboySharp
{
    /// <summary>
    /// The eight physical Game Boy buttons.
    ///
    /// This enum is the stable "logical button" vocabulary shared by the input system and the
    /// on-disk config. Keyboard keys and gamepad buttons are mapped *to* these values, so the
    /// rest of the emulator never has to care which physical key the user happens to have bound.
    ///
    /// The order matches the joypad register bit layout (see <see cref="Joypad"/>): the direction
    /// buttons occupy the low nibble and the action buttons the high nibble.
    /// </summary>
    public enum GbButton
    {
        Right,
        Left,
        Up,
        Down,
        A,
        B,
        Select,
        Start
    }
}
