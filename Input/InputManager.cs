using System.Collections.Generic;
using Silk.NET.Input;

namespace GameboySharp
{
    /// <summary>
    /// Translates physical input — keyboard keys and a gamepad — into Game Boy button presses, using
    /// the bindings in <see cref="EmulatorConfig.Controls"/>. It also provides rising-edge detection
    /// for the configurable hotkeys (save/load state, turbo, frame-advance, …) so callers can act on a
    /// key the moment it is pressed rather than for every frame it is held.
    ///
    /// Keeping all of this here means the <see cref="Joypad"/> stays a pure hardware model and the
    /// window code doesn't need to know anything about key bindings.
    /// </summary>
    internal class InputManager
    {
        private readonly EmulatorConfig _config;
        private IKeyboard? _keyboard;

        // Remembers each tracked key's pressed state from the previous frame, for edge detection.
        private readonly Dictionary<Key, bool> _previousKeyState = new();

        public InputManager(EmulatorConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Polls the input devices and pushes the resulting button states into the joypad. Call once
        /// per frame, before querying any hotkeys.
        /// </summary>
        public void Update(IInputContext input, Joypad joypad)
        {
            _keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
            IGamepad? gamepad = input.Gamepads.Count > 0 ? input.Gamepads[0] : null;

            joypad.SetButtonStates(
                up: IsPressed(GbButton.Up, gamepad),
                down: IsPressed(GbButton.Down, gamepad),
                left: IsPressed(GbButton.Left, gamepad),
                right: IsPressed(GbButton.Right, gamepad),
                a: IsPressed(GbButton.A, gamepad),
                b: IsPressed(GbButton.B, gamepad),
                select: IsPressed(GbButton.Select, gamepad),
                start: IsPressed(GbButton.Start, gamepad));
        }

        /// <summary>True if the given Game Boy button is currently pressed on the keyboard or gamepad.</summary>
        private bool IsPressed(GbButton button, IGamepad? gamepad)
        {
            if (_keyboard != null &&
                _config.Controls.Keyboard.TryGetValue(button, out Key key) &&
                _keyboard.IsKeyPressed(key))
            {
                return true;
            }

            return gamepad != null && IsGamepadPressed(button, gamepad);
        }

        private bool IsGamepadPressed(GbButton button, IGamepad gamepad)
        {
            // Mapped digital button.
            if (_config.Controls.Gamepad.TryGetValue(button, out ButtonName name))
            {
                foreach (Button b in gamepad.Buttons)
                {
                    if (b.Name == name && b.Pressed) return true;
                }
            }

            // The left analog stick doubles as the d-pad once it deflects past the deadzone.
            float deadzone = _config.Controls.GamepadDeadzone;
            if (gamepad.Thumbsticks.Count > 0)
            {
                Thumbstick stick = gamepad.Thumbsticks[0];
                switch (button)
                {
                    case GbButton.Left when stick.X < -deadzone: return true;
                    case GbButton.Right when stick.X > deadzone: return true;
                    case GbButton.Up when stick.Y < -deadzone: return true;
                    case GbButton.Down when stick.Y > deadzone: return true;
                }
            }

            return false;
        }

        /// <summary>Whether a specific key is held right now (for hold-to-turbo and similar).</summary>
        public bool IsKeyDown(Key key) => _keyboard?.IsKeyPressed(key) ?? false;

        /// <summary>
        /// True only on the frame <paramref name="key"/> transitions from released to pressed. Must be
        /// called at most once per frame per key (the edge state is updated as a side effect).
        /// </summary>
        public bool WasKeyJustPressed(Key key)
        {
            bool down = _keyboard?.IsKeyPressed(key) ?? false;
            bool previous = _previousKeyState.TryGetValue(key, out bool wasDown) && wasDown;
            _previousKeyState[key] = down;
            return down && !previous;
        }
    }
}
