namespace GameboySharp.Tests;

/// <summary>
/// Helper class that wraps the APU with convenient register shortcuts for testing.
/// </summary>
internal class ApuTestHelper
{
    public Apu Apu { get; }

    private short[]? _lastLeftBuffer;
    private short[]? _lastRightBuffer;

    public ApuTestHelper()
    {
        Apu = new Apu();
        Apu.AudioBufferReady = (left, right) =>
        {
            _lastLeftBuffer = (short[])left.Clone();
            _lastRightBuffer = (short[])right.Clone();
        };
    }

    /// <summary>
    /// Powers on the APU, sets max volume, enables all channel panning.
    /// </summary>
    public void PowerOnWithDefaults()
    {
        // Enable APU (NR52 bit 7)
        Apu.WriteRegister(0xFF26, 0x80);
        // Max master volume both sides (NR50 = 0x77 => volume 7 left, 7 right)
        Apu.WriteRegister(0xFF24, 0x77);
        // All channels panned to both left and right (NR51 = 0xFF)
        Apu.WriteRegister(0xFF25, 0xFF);
    }

    /// <summary>
    /// Sets up Channel 1 (pulse with sweep) with a tone and triggers it.
    /// </summary>
    public void TriggerChannel1(int frequency = 1000, int duty = 2, int volume = 15)
    {
        // NR10 - No sweep
        Apu.WriteRegister(0xFF10, 0x00);
        // NR11 - Duty cycle and length
        Apu.WriteRegister(0xFF11, (byte)(duty << 6));
        // NR12 - Envelope: volume, no change
        Apu.WriteRegister(0xFF12, (byte)(volume << 4));
        // NR13 - Frequency low
        Apu.WriteRegister(0xFF13, (byte)(frequency & 0xFF));
        // NR14 - Frequency high + trigger
        Apu.WriteRegister(0xFF14, (byte)(0x80 | ((frequency >> 8) & 0x07)));
    }

    /// <summary>
    /// Sets up Channel 2 (pulse) with a tone and triggers it.
    /// </summary>
    public void TriggerChannel2(int frequency = 1000, int duty = 2, int volume = 15)
    {
        // NR21 - Duty cycle and length
        Apu.WriteRegister(0xFF16, (byte)(duty << 6));
        // NR22 - Envelope: volume, no change
        Apu.WriteRegister(0xFF17, (byte)(volume << 4));
        // NR23 - Frequency low
        Apu.WriteRegister(0xFF18, (byte)(frequency & 0xFF));
        // NR24 - Frequency high + trigger
        Apu.WriteRegister(0xFF19, (byte)(0x80 | ((frequency >> 8) & 0x07)));
    }

    /// <summary>
    /// Sets up Channel 3 (wave) with a sawtooth wave and triggers it.
    /// </summary>
    public void TriggerChannel3(int frequency = 1000, int volumeShift = 1)
    {
        // NR30 - DAC enable
        Apu.WriteRegister(0xFF1A, 0x80);
        // Load a sawtooth wave pattern into wave RAM
        for (int i = 0; i < 16; i++)
        {
            byte high = (byte)(i * 2);
            byte low = (byte)(i * 2 + 1);
            Apu.WriteRegister((ushort)(0xFF30 + i), (byte)((high << 4) | low));
        }
        // NR31 - Length
        Apu.WriteRegister(0xFF1B, 0x00);
        // NR32 - Volume shift
        Apu.WriteRegister(0xFF1C, (byte)(volumeShift << 5));
        // NR33 - Frequency low
        Apu.WriteRegister(0xFF1D, (byte)(frequency & 0xFF));
        // NR34 - Frequency high + trigger
        Apu.WriteRegister(0xFF1E, (byte)(0x80 | ((frequency >> 8) & 0x07)));
    }

    /// <summary>
    /// Sets up Channel 4 (noise) and triggers it.
    /// </summary>
    public void TriggerChannel4(int volume = 15, int clockShift = 0, int divisor = 1, int widthMode = 0)
    {
        // NR41 - Length
        Apu.WriteRegister(0xFF20, 0x00);
        // NR42 - Envelope: volume, no change
        Apu.WriteRegister(0xFF21, (byte)(volume << 4));
        // NR43 - Polynomial counter
        Apu.WriteRegister(0xFF22, (byte)((clockShift << 4) | (widthMode << 3) | divisor));
        // NR44 - Trigger
        Apu.WriteRegister(0xFF23, 0x80);
    }

    /// <summary>
    /// Steps the APU enough cycles to fill at least one audio buffer.
    /// Returns the captured left and right buffers.
    /// </summary>
    public (short[] left, short[] right) StepUntilBufferReady()
    {
        _lastLeftBuffer = null;
        _lastRightBuffer = null;

        // Step in chunks until we get a buffer callback
        // 512 samples at 44100 Hz ≈ 11.6ms ≈ ~49000 CPU cycles at 4.194304 MHz
        int maxIterations = 200;
        while (_lastLeftBuffer == null && maxIterations-- > 0)
        {
            Apu.Step(256);
        }

        return (_lastLeftBuffer ?? new short[512], _lastRightBuffer ?? new short[512]);
    }

    /// <summary>
    /// Collects float samples by stepping cycle-by-cycle (for precision tests).
    /// Note: this reads the internal buffer via the callback.
    /// </summary>
    public (short[] left, short[] right) CollectSamples(int bufferCount = 1)
    {
        var allLeft = new List<short>();
        var allRight = new List<short>();

        for (int i = 0; i < bufferCount; i++)
        {
            var (left, right) = StepUntilBufferReady();
            allLeft.AddRange(left);
            allRight.AddRange(right);
        }

        return (allLeft.ToArray(), allRight.ToArray());
    }
}
