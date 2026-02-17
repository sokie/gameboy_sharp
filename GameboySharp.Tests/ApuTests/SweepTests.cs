using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class SweepTests
{
    private readonly ApuTestHelper _helper;

    public SweepTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void Sweep_OverflowDisablesChannel()
    {
        // Set up Channel 1 with high frequency and sweep increase
        _helper.Apu.WriteRegister(0xFF10, 0x11); // period=1, increase, shift=1
        _helper.Apu.WriteRegister(0xFF11, 0x80); // duty 50%
        _helper.Apu.WriteRegister(0xFF12, 0xF0); // vol=15, no envelope
        // Set frequency to near max (2047)
        _helper.Apu.WriteRegister(0xFF13, 0xFF); // freq low = 0xFF
        _helper.Apu.WriteRegister(0xFF14, 0x87); // freq high = 7, trigger

        // Step until sweep overflow occurs
        for (int i = 0; i < 50; i++)
        {
            _helper.Apu.Step(8192);
        }

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x01); // Channel 1 disabled by sweep overflow
    }

    [Fact]
    public void Sweep_DecreaseDoesNotOverflow()
    {
        // Sweep decrease from low frequency should not overflow
        _helper.Apu.WriteRegister(0xFF10, 0x19); // period=1, decrease, shift=1
        _helper.Apu.WriteRegister(0xFF11, 0x80);
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        _helper.Apu.WriteRegister(0xFF13, 0x00); // low freq
        _helper.Apu.WriteRegister(0xFF14, 0x81); // trigger

        for (int i = 0; i < 20; i++)
        {
            _helper.Apu.Step(8192);
        }

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x01); // Channel should still be active
    }

    [Fact]
    public void Sweep_Period0TreatedAs8()
    {
        // Period=0 is treated as 8 internally
        _helper.Apu.WriteRegister(0xFF10, 0x01); // period=0, increase, shift=1
        _helper.Apu.WriteRegister(0xFF11, 0x80);
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        _helper.Apu.WriteRegister(0xFF13, 0x00);
        _helper.Apu.WriteRegister(0xFF14, 0x82); // trigger

        // Channel should remain active for a while with period=0 (treated as 8)
        for (int i = 0; i < 10; i++)
        {
            _helper.Apu.Step(8192);
        }

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x01);
    }

    [Fact]
    public void Sweep_NoShiftDoesNotWriteBack()
    {
        // With shift=0, sweep calculates offset = freq >> 0 = freq, so newFreq = 2*freq.
        // Use freq <= 1023 so 2*freq <= 2046 (no overflow).
        // With shift=0, the write-back is skipped so frequency stays unchanged.
        _helper.Apu.WriteRegister(0xFF10, 0x10); // period=1, increase, shift=0
        _helper.Apu.WriteRegister(0xFF11, 0x80);
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        _helper.Apu.WriteRegister(0xFF13, 0x00);
        _helper.Apu.WriteRegister(0xFF14, 0x82); // freq = 0x200 (512), trigger

        for (int i = 0; i < 30; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Channel should still be active since shift=0 means no write-back
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x01);
    }
}
