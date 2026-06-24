using System;

namespace GameboySharp
{
    /// <summary>
    /// Base class for all Game Boy sound channels.
    /// Contains common functionality like length counters and envelope generators.
    /// </summary>
    internal abstract class ChannelBase
    {
        // --- Length Counter ---
        protected bool _lengthEnabled;
        protected int _lengthCounter;
        protected int _lengthMax;

        // --- Envelope Generator ---
        protected bool _envelopeEnabled;
        protected int _envelopePeriod;
        protected int _envelopeCounter;
        protected int _envelopeVolume;
        protected int _envelopeDirection; // 0 = decrease, 1 = increase
        protected int _envelopeInitialVolume;

        // --- Channel State ---
        protected bool _enabled;
        protected bool _dacEnabled;
        public bool IsEnabled => _enabled && _dacEnabled;
        public bool IsActiveForStatus => _enabled; 

        protected ChannelBase()
        {
            _enabled = false;
            _dacEnabled = false;
            _lengthEnabled = false;
            _lengthCounter = 0;
            _envelopeEnabled = false;
            _envelopePeriod = 0;
            _envelopeCounter = 0;
            _envelopeVolume = 0;
            _envelopeDirection = 0;
            _envelopeInitialVolume = 0;
        }

        /// <summary>
        /// Called by the frame sequencer to tick the length counter.
        /// </summary>
        public virtual void TickLength()
        {
            if (!_lengthEnabled || _lengthCounter == 0)
            {
                return;
            }

            _lengthCounter--;
            if (_lengthCounter == 0)
            {
                _enabled = false;
            }
        }

        /// <summary>
        /// Called by the frame sequencer to tick the envelope generator.
        /// </summary>
        public virtual void TickEnvelope()
        {
            if (!_envelopeEnabled) return;

            if (_envelopePeriod > 0)
            {
                _envelopeCounter--;
                if (_envelopeCounter <= 0)
                {
                    _envelopeCounter = _envelopePeriod;

                    if (_envelopeDirection == 1 && _envelopeVolume < 15)
                    {
                        _envelopeVolume++;
                    }
                    else if (_envelopeDirection == 0 && _envelopeVolume > 0)
                    {
                        _envelopeVolume--;
                    }
                }
            }
        }

        /// <summary>
        /// Called by the frame sequencer to tick the sweep generator.
        /// Only Channel1 uses this.
        /// </summary>
        public virtual void TickSweep()
        {
            // Default implementation does nothing
        }

        /// <summary>
        /// Triggers the channel (starts playback).
        /// </summary>
        public virtual void Trigger(bool isNextStepNotLength)
        {
            _enabled = true;
            _envelopeVolume = _envelopeInitialVolume;
            _envelopeCounter = (_envelopePeriod == 0) ? 8 : _envelopePeriod;
            _envelopeEnabled = true;

            // Length rules
            if (_lengthCounter == 0)
                _lengthCounter = _lengthMax; // 64 (CH1/2/4) or 256 (CH3)
            // 63/255 quirk when next FS step does not clock length
            if (_lengthEnabled && isNextStepNotLength && _lengthCounter == _lengthMax)
                _lengthCounter--;

            // If DAC is off at trigger, channel disables immediately
            if (!_dacEnabled) _enabled = false;
        }

        /// <summary>
        /// Gets the current sample value from this channel.
        /// </summary>
        public abstract float GetSample();

        protected void OnLengthEnableWritten(bool enable, bool isLengthClockStep)
        {
            // If enabling at a length-tick step, apply the quirk
            if (!_lengthEnabled && enable && isLengthClockStep && _lengthCounter > 0)
            {
                _lengthCounter--;
                if (_lengthCounter == 0)
                    _enabled = false;
            }
            _lengthEnabled = enable;
        }

        /// <summary>
        /// Reads a register value from this channel.
        /// </summary>
        public abstract byte ReadRegister(ushort address);

        /// <summary>
        /// Writes a register value to this channel.
        /// </summary>
        public abstract void WriteRegister(ushort address, byte value, bool isApuEnabled, bool IsLengthClockStep);

        public virtual void PowerOff()
        {
            _lengthEnabled = false;
            //_lengthCounter = 0;
            _envelopeEnabled = false;
            _envelopePeriod = 0;
            _envelopeCounter = 0;
            _envelopeVolume = 0;
            _envelopeDirection = 0;
            _envelopeInitialVolume = 0;
            _enabled = false;
            _dacEnabled = false;
        }

        /// <summary>
        /// Resets the channel to its initial state.
        /// </summary>
        public virtual void Reset()
        {
            _enabled = false;
            _lengthCounter = 0;
            _envelopeVolume = 0;
            _envelopeCounter = 0;
        }

        /// <summary>
        /// Writes the length-counter, envelope, and enable state shared by every channel. Derived
        /// channels override this to chain their own state on after calling the base.
        /// </summary>
        public virtual void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(_lengthEnabled);
            writer.Write(_lengthCounter);
            writer.Write(_lengthMax);
            writer.Write(_envelopeEnabled);
            writer.Write(_envelopePeriod);
            writer.Write(_envelopeCounter);
            writer.Write(_envelopeVolume);
            writer.Write(_envelopeDirection);
            writer.Write(_envelopeInitialVolume);
            writer.Write(_enabled);
            writer.Write(_dacEnabled);
        }

        public virtual void LoadState(System.IO.BinaryReader reader)
        {
            _lengthEnabled = reader.ReadBoolean();
            _lengthCounter = reader.ReadInt32();
            _lengthMax = reader.ReadInt32();
            _envelopeEnabled = reader.ReadBoolean();
            _envelopePeriod = reader.ReadInt32();
            _envelopeCounter = reader.ReadInt32();
            _envelopeVolume = reader.ReadInt32();
            _envelopeDirection = reader.ReadInt32();
            _envelopeInitialVolume = reader.ReadInt32();
            _enabled = reader.ReadBoolean();
            _dacEnabled = reader.ReadBoolean();
        }
    }
}
