using Serilog;

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

        /// <summary>
        /// Clears all button state back to "nothing pressed" for a machine reset. The P1 select bits
        /// return to their power-on value (0xCF = no group selected, all buttons released).
        /// </summary>
        public void Reset()
        {
            _up = _down = _left = _right = false;
            _a = _b = _start = _select = false;
            _p1Register = 0xCF;
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

        /// <summary>
        /// Sets the pressed/released state of all eight buttons at once. The input layer
        /// (<see cref="InputManager"/>) computes these from the user's key/gamepad bindings and calls
        /// this every frame, which keeps the joypad free of any knowledge about input devices.
        /// </summary>
        public void SetButtonStates(bool up, bool down, bool left, bool right,
                                    bool a, bool b, bool select, bool start)
        {
            bool wasUp = _up, wasDown = _down, wasLeft = _left, wasRight = _right;
            bool wasA = _a, wasB = _b, wasStart = _start, wasSelect = _select;

            _up = up; _down = down; _left = left; _right = right;
            _a = a; _b = b; _select = select; _start = start;

            // A fresh press (high→low on the wire) requests a joypad interrupt on real hardware. That
            // interrupt is currently disabled (a known issue, out of scope here), but we keep the edge
            // detection so it's trivial to re-enable later.
            if ((!wasUp && _up) || (!wasDown && _down) || (!wasLeft && _left) || (!wasRight && _right) ||
                (!wasA && _a) || (!wasB && _b) || (!wasStart && _start) || (!wasSelect && _select))
            {
                //_cpu.RequestInterrupt(Cpu.Interrupt.Joypad);
            }
        }
        
        public void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(_p1Register);
            writer.Write(_up); writer.Write(_down); writer.Write(_left); writer.Write(_right);
            writer.Write(_a); writer.Write(_b); writer.Write(_start); writer.Write(_select);
        }

        public void LoadState(System.IO.BinaryReader reader)
        {
            _p1Register = reader.ReadByte();
            _up = reader.ReadBoolean(); _down = reader.ReadBoolean(); _left = reader.ReadBoolean(); _right = reader.ReadBoolean();
            _a = reader.ReadBoolean(); _b = reader.ReadBoolean(); _start = reader.ReadBoolean(); _select = reader.ReadBoolean();
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
