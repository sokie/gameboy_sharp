using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class FrameSequencerTests
{
    private readonly ApuTestHelper _helper;

    public FrameSequencerTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void FrameSequencer_LengthTicksOnEvenSteps()
    {
        // Set up channel with short length and length enabled
        _helper.Apu.WriteRegister(0xFF16, 0x80); // duty 50%
        _helper.Apu.WriteRegister(0xFF17, 0xF0); // vol=15, no envelope change
        _helper.Apu.WriteRegister(0xFF18, 0x00);

        // Set length to 62 (counter = 64 - 62 = 2)
        _helper.Apu.WriteRegister(0xFF16, (byte)(0x80 | 62));
        _helper.Apu.WriteRegister(0xFF19, 0xC0 | 0x06); // trigger + length enable

        // Channel should be active initially
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x02);

        // Step through enough frame sequencer ticks to expire the length
        // Length clocks on even steps (0, 2, 4, 6), so 2 length clocks expire counter of 2
        // Need at least 4 FS ticks to get 2 even-step clocks (steps 0 and 2)
        for (int i = 0; i < 30; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Channel should be disabled
        status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x02);
    }

    [Fact]
    public void FrameSequencer_EnvelopeTicksOnStep7()
    {
        // Set up with envelope decrease, period=1
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF17, 0xF1); // vol=15, decrease, period=1
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        // Get initial output
        var (left1, _) = _helper.StepUntilBufferReady();

        // Step through many FS cycles to allow envelope to decrease
        for (int i = 0; i < 200; i++)
        {
            _helper.Apu.Step(8192);
        }

        var (left2, _) = _helper.StepUntilBufferReady();

        // Envelope should have decreased the volume
        short max1 = left1.Max(Math.Abs);
        short max2 = left2.Max(Math.Abs);
        Assert.True(max2 < max1, "Envelope should decrease volume over time");
    }

    [Fact]
    public void FrameSequencer_SweepTicksOnSteps2And6()
    {
        // Set up CH1 with sweep increase, high freq to cause overflow
        _helper.Apu.WriteRegister(0xFF10, 0x11); // period=1, increase, shift=1
        _helper.Apu.WriteRegister(0xFF11, 0x80);
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        _helper.Apu.WriteRegister(0xFF13, 0xFF);
        _helper.Apu.WriteRegister(0xFF14, 0x87); // freq = 0x7FF, trigger

        // Step through enough frames for sweep to overflow
        for (int i = 0; i < 30; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Channel should be disabled by sweep overflow
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x01);
    }
}
