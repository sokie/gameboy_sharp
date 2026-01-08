using Serilog;
using Silk.NET.Input;

namespace GameboySharp
{
    internal class Joypad
    {
        // These will hold the current state of each button. True = pressed.
        private bool _up, _down, _left, _right;
        private bool _a, _b, _start, _select;

        // Stores the value written by the game to 0xFF00
        private byte _p1Register = 0xCF;

        private Cpu _cpu;

        public Joypad(Cpu cpu)
        {
            _cpu = cpu;
        }

        // This method will be called by the MMU when the game writes to 0xFF00
        public void WriteP1(byte value)
        {
            Log.Debug($"Writing joypad value:{value:X2}");
            // We only care about bits 4 and 5, the rest are read-only
            _p1Register = (byte)((_p1Register & 0xCF) | (value & 0x30));
        }

        // This method will be called by the MMU when the game reads from 0xFF00
        public byte ReadP1()
        {
            byte result = (byte)(_p1Register | 0x0F); // Start with all buttons appearing "unpressed"

            // Check if Direction buttons are selected (bit 4 is 0)
            // This must be its own 'if', not part of an 'else if' chain.
            if ((result & 0x10) == 0)
            {
                if (_right) result &= 0b11111110;
                if (_left) result &= 0b11111101;
                if (_up) result &= 0b11111011;
                if (_down) result &= 0b11110111;
            }

            // Check if Action buttons are selected (bit 5 is 0)
            // Changed from 'else if' to 'if'
            if ((result & 0x20) == 0)
            {
                if (_a) result &= 0b11111110;
                if (_b) result &= 0b11111101;
                if (_select) result &= 0b11111011;
                if (_start) result &= 0b11110111;
            }
            DebugRender();
            return result;
        }

        // This method will be called from our main loop to poll the keyboard
        public void Update(IKeyboard keyboard)
        {
            // Standard key mapping
            bool wasUp = _up, wasDown = _down, wasLeft = _left, wasRight = _right;
            bool wasA = _a, wasB = _b, wasStart = _start, wasSelect = _select;

            _up = keyboard.IsKeyPressed(Key.Up);
            _down = keyboard.IsKeyPressed(Key.Down);
            _left = keyboard.IsKeyPressed(Key.Left);
            _right = keyboard.IsKeyPressed(Key.Right);

            _a = keyboard.IsKeyPressed(Key.Z);
            _b = keyboard.IsKeyPressed(Key.X);
            _start = keyboard.IsKeyPressed(Key.Enter);
            _select = keyboard.IsKeyPressed(Key.ShiftRight);

            // Check if any button was just pressed (state changed from false to true)
            if ((!wasUp && _up) || (!wasDown && _down) || (!wasLeft && _left) || (!wasRight && _right) ||
                (!wasA && _a) || (!wasB && _b) || (!wasStart && _start) || (!wasSelect && _select))
            {
                // Request a joypad interrupt. This is essential for many games!
                //TODO: broken atm
                //_cpu.RequestInterrupt(Cpu.Interrupt.Joypad);
            }
        }
        
        public void DebugRender()
        {
            Log.Debug("--- Joypad State ---");
            Log.Debug($"      [Up: {_up}]");
            Log.Debug($"[Left: {_left}] [Right: {_right}]");
            Log.Debug($"     [Down: {_down}]");
            Log.Debug("");
            Log.Debug($" [A: {_a}] [B: {_b}]");
            Log.Debug($"[Select: {_select}] [Start: {_start}]");
            Log.Debug("--------------------");
            Log.Debug($"P1 Register: {_p1Register:X2}"); // Show current P1 value
        }
    }
}
