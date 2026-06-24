namespace GameboySharp
{
    /// <summary>
    /// A first-order DC-blocking high-pass filter.
    ///
    /// Removes the constant (DC) offset from an audio signal while passing the audible
    /// content through almost untouched. This is what keeps a channel that idles at a
    /// non-zero level from adding a click to the mix when it switches on or off.
    ///
    /// Transfer function: <c>y[n] = x[n] - x[n-1] + R * y[n-1]</c>.
    /// With <c>R = 0.997</c> the cutoff sits at roughly 20 Hz (at 44.1 kHz), so everything
    /// we can actually hear passes and only the DC drift is removed.
    ///
    /// Each instance carries its own filter state, so one filter per signal (per channel,
    /// per stereo side) keeps them independent without any shared bookkeeping.
    /// </summary>
    internal struct DcBlocker
    {
        // ~20 Hz cutoff at 44.1 kHz; closer to 1.0 means a lower cutoff.
        private const float DecayFactor = 0.997f;

        private float _previousInput;
        private float _previousOutput;

        /// <summary>
        /// Filters one sample and advances the filter state.
        /// </summary>
        public float Process(float input)
        {
            float output = input - _previousInput + DecayFactor * _previousOutput;
            _previousInput = input;
            _previousOutput = output;
            return output;
        }

        /// <summary>
        /// Clears the filter history so the next sample starts from silence.
        /// </summary>
        public void Reset()
        {
            _previousInput = 0f;
            _previousOutput = 0f;
        }

        public void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(_previousInput);
            writer.Write(_previousOutput);
        }

        public void LoadState(System.IO.BinaryReader reader)
        {
            _previousInput = reader.ReadSingle();
            _previousOutput = reader.ReadSingle();
        }
    }
}
