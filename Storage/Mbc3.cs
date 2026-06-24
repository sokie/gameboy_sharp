using System.IO;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// Memory Bank Controller 3 (MBC3) implementation
    /// Supports ROM sizes up to 2MB, RAM up to 32KB, and Real-Time Clock (RTC)
    /// </summary>
    public class Mbc3 : IMbc
    {
        private readonly byte[] _romData;
        private readonly byte[] _ramData;
        private readonly int _romSize;
        private readonly int _ramSize;
        private readonly int _romBankCount;
        private readonly int _ramBankCount;
        private readonly bool _hasBattery;
        private readonly bool _hasRtc;

        // MBC3 registers
        private bool _ramEnabled = false;
        private int _romBankNumber = 1; // Bank 0 is always mapped to 0x0000-0x3FFF
        private int _ramBankNumber = 0;
        private bool _rtcLatched = false;

        // RTC registers
        private byte _rtcSeconds = 0;
        private byte _rtcMinutes = 0;
        private byte _rtcHours = 0;
        private byte _rtcDaysLow = 0;
        private byte _rtcDaysHigh = 0;
        private DateTime _rtcBaseTime;

        public bool IsRamEnabled => _ramEnabled;
        public int CurrentRomBank => _romBankNumber;
        public int CurrentRamBank => _ramBankNumber;
        public bool HasBattery => _hasBattery;

        /// <summary>True for MBC3 cartridges that include the real-time clock (cartridge types 0x0F/0x10).</summary>
        public bool HasRtc => _hasRtc;
        
        /// <summary>
        /// Gets detailed information about the MBC3 state including RTC
        /// </summary>
        public string GetDetailedInfo()
        {
            var info = $"{GetType().Name}: ROM Bank {_romBankNumber}, RAM Bank {_ramBankNumber}, RAM {( _ramEnabled ? "Enabled" : "Disabled")}";
            
            if (_ramBankNumber >= 0x08 && _ramBankNumber <= 0x0C)
            {
                info += $", RTC Register 0x{_ramBankNumber:X2}";
            }
            
            if (_rtcLatched)
            {
                info += ", RTC Latched";
            }
            
            return info;
        }

        public Mbc3(byte[] romData, int ramSize, bool hasBattery = false, bool hasRtc = false)
        {
            _romData = romData ?? throw new ArgumentNullException(nameof(romData));
            _romSize = romData.Length;
            _ramSize = ramSize;
            _hasBattery = hasBattery;
            _hasRtc = hasRtc;

            // Calculate bank counts
            _romBankCount = _romSize / 0x4000; // 16KB per bank
            _ramBankCount = _ramSize > 0 ? _ramSize / 0x2000 : 0; // 8KB per bank

            // Initialize RAM if present
            if (_ramSize > 0)
            {
                _ramData = new byte[_ramSize];
                // Initialize RAM with 0xFF (uninitialized value)
                for (int i = 0; i < _ramSize; i++)
                {
                    _ramData[i] = 0xFF;
                }
            }
            else
            {
                _ramData = new byte[0];
            }

            // Initialize RTC
            _rtcBaseTime = DateTime.Now;

            Log.Information($"MBC3 initialized: ROM={_romSize / 1024}KB ({_romBankCount} banks), RAM={_ramSize / 1024}KB ({_ramBankCount} banks)");
            Log.Information($"MBC3: Initial ROM bank 0, RAM bank 0, RAM disabled, RTC enabled");
        }

        public byte ReadRom(ushort address)
        {
            if (address < 0x4000)
            {
                // Bank 0 area (0x0000-0x3FFF) - always bank 0
                if (address < _romSize)
                {
                    return _romData[address];
                }
                else
                {
                    Log.Warning($"MBC3: ROM read out of bounds at address 0x{address:X4}");
                    return 0xFF;
                }
            }
            else if (address < 0x8000)
            {
                // Bank 1 area (0x4000-0x7FFF) - uses ROM bank number
                int romAddress = (_romBankNumber * 0x4000) + (address - 0x4000);
                
                if (romAddress < _romSize)
                {
                    return _romData[romAddress];
                }
                else
                {
                    Log.Warning($"MBC3: ROM read out of bounds at address 0x{address:X4} (ROM address 0x{romAddress:X6})");
                    return 0xFF;
                }
            }
            else
            {
                Log.Warning($"MBC3: Invalid ROM read address 0x{address:X4}");
                return 0xFF;
            }
        }

        public void WriteRom(ushort address, byte value)
        {
            if (address < 0x2000)
            {
                // RAM Enable Register (0x0000-0x1FFF)
                _ramEnabled = (value & 0x0F) == 0x0A;
                Log.Debug($"MBC3: RAM {( _ramEnabled ? "enabled" : "disabled")} (value: 0x{value:X2})");
            }
            else if (address < 0x4000)
            {
                // ROM Bank Number Register (0x2000-0x3FFF)
                int bankNumber = value & 0x7F; // 7 bits for ROM bank
                
                // If bank number is 0, set to 1 (bank 0 is not accessible in this area)
                if (bankNumber == 0)
                {
                    bankNumber = 1;
                }

                _romBankNumber = bankNumber;

                // Ensure bank number doesn't exceed available banks
                if (_romBankNumber >= _romBankCount)
                {
                    _romBankNumber = _romBankNumber % _romBankCount;
                }

                Log.Debug($"MBC3: ROM bank set to {_romBankNumber} (value: 0x{value:X2})");
            }
            else if (address < 0x6000)
            {
                // RAM Bank Number / RTC Register Select (0x4000-0x5FFF)
                int bankNumber = value & 0x0F;
                
                if (bankNumber <= 0x03)
                {
                    // RAM bank selection (0x00-0x03)
                    _ramBankNumber = bankNumber;
                    if (_ramBankNumber >= _ramBankCount)
                    {
                        _ramBankNumber = _ramBankNumber % _ramBankCount;
                    }
                    Log.Debug($"MBC3: RAM bank set to {_ramBankNumber} (value: 0x{value:X2})");
                }
                else if (bankNumber >= 0x08 && bankNumber <= 0x0C)
                {
                    // RTC register selection (0x08-0x0C)
                    _ramBankNumber = bankNumber; // Store RTC register selection
                    Log.Debug($"MBC3: RTC register selected: 0x{bankNumber:X2}");
                }
            }
            else if (address < 0x8000)
            {
                // RTC Latch Register (0x6000-0x7FFF)
                if (value == 0x00)
                {
                    _rtcLatched = false;
                }
                else if (value == 0x01)
                {
                    _rtcLatched = true;
                    UpdateRtcRegisters();
                    Log.Debug("MBC3: RTC latched");
                }
            }
            else
            {
                Log.Warning($"MBC3: Invalid ROM write address 0x{address:X4}");
            }
        }

        public byte ReadRam(ushort address)
        {
            if (!_ramEnabled)
            {
                return 0xFF; // RAM disabled
            }

            // Check if RTC register is selected
            if (_ramBankNumber >= 0x08 && _ramBankNumber <= 0x0C)
            {
                return ReadRtcRegister(_ramBankNumber);
            }

            // Convert address to RAM address
            ushort ramAddress = (ushort)(address - 0xA000);
            
            // Add bank offset
            int bankOffset = _ramBankNumber * 0x2000; // 8KB per bank
            int fullRamAddress = bankOffset + ramAddress;

            if (fullRamAddress < _ramSize)
            {
                return _ramData[fullRamAddress];
            }
            else
            {
                Log.Warning($"MBC3: RAM read out of bounds at address 0x{address:X4} (RAM address 0x{fullRamAddress:X6})");
                return 0xFF;
            }
        }

        public void WriteRam(ushort address, byte value)
        {
            if (!_ramEnabled)
            {
                return; // RAM disabled, ignore write
            }

            // Check if RTC register is selected
            if (_ramBankNumber >= 0x08 && _ramBankNumber <= 0x0C)
            {
                WriteRtcRegister(_ramBankNumber, value);
                return;
            }

            // Convert address to RAM address
            ushort ramAddress = (ushort)(address - 0xA000);
            
            // Add bank offset
            int bankOffset = _ramBankNumber * 0x2000; // 8KB per bank
            int fullRamAddress = bankOffset + ramAddress;

            if (fullRamAddress < _ramSize)
            {
                _ramData[fullRamAddress] = value;
            }
            else
            {
                Log.Warning($"MBC3: RAM write out of bounds at address 0x{address:X4} (RAM address 0x{fullRamAddress:X6})");
            }
        }

        public byte[] GetRam() => _ramData;

        public void SetRam(byte[] data)
        {
            if (data == null || _ramData.Length == 0) return;
            Array.Copy(data, _ramData, Math.Min(data.Length, _ramData.Length));
        }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_ramEnabled);
            writer.Write(_romBankNumber);
            writer.Write(_ramBankNumber);
            writer.Write(_rtcLatched);

            // The RTC's latched register values plus its base time. The base time is the easy-to-miss
            // part: it is what the live seconds/minutes/hours are derived from, so without it the
            // clock would jump on reload.
            writer.Write(_rtcSeconds);
            writer.Write(_rtcMinutes);
            writer.Write(_rtcHours);
            writer.Write(_rtcDaysLow);
            writer.Write(_rtcDaysHigh);
            writer.Write(_rtcBaseTime.Ticks);

            writer.Write(_ramData.Length);
            writer.Write(_ramData);
        }

        public void LoadState(BinaryReader reader)
        {
            _ramEnabled = reader.ReadBoolean();
            _romBankNumber = reader.ReadInt32();
            _ramBankNumber = reader.ReadInt32();
            _rtcLatched = reader.ReadBoolean();

            _rtcSeconds = reader.ReadByte();
            _rtcMinutes = reader.ReadByte();
            _rtcHours = reader.ReadByte();
            _rtcDaysLow = reader.ReadByte();
            _rtcDaysHigh = reader.ReadByte();
            _rtcBaseTime = new DateTime(reader.ReadInt64());

            int ramLength = reader.ReadInt32();
            byte[] ram = reader.ReadBytes(ramLength);
            SetRam(ram);
        }

        /// <summary>
        /// Writes the RTC state in the BGB/VBA <c>.sav</c> footer layout: ten little-endian 32-bit
        /// registers (the live clock followed by the latched copy) and a 64-bit Unix timestamp of the
        /// moment of saving. The timestamp lets a loader advance the clock by however long the cartridge
        /// was "powered off".
        /// </summary>
        public void WriteRtcSave(System.IO.BinaryWriter writer)
        {
            // Live registers, derived from the base time (independent of the latch).
            long total = (long)(DateTime.Now - _rtcBaseTime).TotalSeconds;
            if (total < 0) total = 0;
            writer.Write((uint)(total % 60));
            writer.Write((uint)((total / 60) % 60));
            writer.Write((uint)((total / 3600) % 24));
            long days = total / 86400;
            writer.Write((uint)(days & 0xFF));
            writer.Write((uint)((days >> 8) & 0x01));

            // Latched registers (the snapshot the game last read).
            writer.Write((uint)_rtcSeconds);
            writer.Write((uint)_rtcMinutes);
            writer.Write((uint)_rtcHours);
            writer.Write((uint)_rtcDaysLow);
            writer.Write((uint)_rtcDaysHigh);

            writer.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        /// <summary>Restores RTC state previously written by <see cref="WriteRtcSave"/>.</summary>
        public void ReadRtcSave(System.IO.BinaryReader reader)
        {
            uint realSeconds = reader.ReadUInt32();
            uint realMinutes = reader.ReadUInt32();
            uint realHours = reader.ReadUInt32();
            uint realDaysLow = reader.ReadUInt32();
            uint realDaysHigh = reader.ReadUInt32();

            _rtcSeconds = (byte)(reader.ReadUInt32() & 0x3F);
            _rtcMinutes = (byte)(reader.ReadUInt32() & 0x3F);
            _rtcHours = (byte)(reader.ReadUInt32() & 0x1F);
            _rtcDaysLow = (byte)(reader.ReadUInt32() & 0xFF);
            _rtcDaysHigh = (byte)(reader.ReadUInt32() & 0x01);

            long savedTimestamp = reader.ReadInt64();

            // Reconstruct the base time so the live clock reads the saved live value plus however much
            // real time has elapsed since the save (a real RTC keeps ticking while powered off).
            long savedRealTotal = realSeconds + realMinutes * 60L + realHours * 3600L
                                  + ((realDaysLow | ((long)realDaysHigh << 8)) * 86400L);
            long elapsedWhileOff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - savedTimestamp;
            if (elapsedWhileOff < 0) elapsedWhileOff = 0;

            _rtcBaseTime = DateTime.Now.AddSeconds(-(savedRealTotal + elapsedWhileOff));
        }

        private void UpdateRtcRegisters()
        {
            if (_rtcLatched)
            {
                // Don't update if latched
                return;
            }

            var now = DateTime.Now;
            var elapsed = now - _rtcBaseTime;
            var totalSeconds = (int)elapsed.TotalSeconds;

            _rtcSeconds = (byte)(totalSeconds % 60);
            _rtcMinutes = (byte)((totalSeconds / 60) % 60);
            _rtcHours = (byte)((totalSeconds / 3600) % 24);
            
            var totalDays = totalSeconds / 86400;
            _rtcDaysLow = (byte)(totalDays & 0xFF);
            _rtcDaysHigh = (byte)((totalDays >> 8) & 0x01);
        }

        private byte ReadRtcRegister(int register)
        {
            UpdateRtcRegisters();

            return register switch
            {
                0x08 => _rtcSeconds,
                0x09 => _rtcMinutes,
                0x0A => _rtcHours,
                0x0B => _rtcDaysLow,
                0x0C => _rtcDaysHigh,
                _ => 0xFF
            };
        }

        private void WriteRtcRegister(int register, byte value)
        {
            switch (register)
            {
                case 0x08: // Seconds
                    _rtcSeconds = (byte)(value & 0x3F); // 6 bits
                    break;
                case 0x09: // Minutes
                    _rtcMinutes = (byte)(value & 0x3F); // 6 bits
                    break;
                case 0x0A: // Hours
                    _rtcHours = (byte)(value & 0x1F); // 5 bits
                    break;
                case 0x0B: // Days (low 8 bits)
                    _rtcDaysLow = value;
                    break;
                case 0x0C: // Days (high bit)
                    _rtcDaysHigh = (byte)(value & 0x01); // 1 bit
                    break;
            }

            // Recalculate base time when RTC is written
            var totalSeconds = _rtcSeconds + (_rtcMinutes * 60) + (_rtcHours * 3600) + 
                              ((_rtcDaysLow | (_rtcDaysHigh << 8)) * 86400);
            _rtcBaseTime = DateTime.Now.AddSeconds(-totalSeconds);
        }
    }
}