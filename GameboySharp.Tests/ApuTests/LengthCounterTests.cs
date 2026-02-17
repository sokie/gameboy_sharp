using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class LengthCounterTests
{
    private readonly ApuTestHelper _helper;

    public LengthCounterTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void LengthCounter_DisablesChannelWhenExpired()
    {
        // Set length to 63 (counter = 64 - 63 = 1 tick remaining)
        _helper.Apu.WriteRegister(0xFF11, 0x3F); // length = 63
        _helper.Apu.WriteRegister(0xFF12, 0xF0); // DAC on, vol 15
        _helper.Apu.WriteRegister(0xFF13, 0x00); // freq low
        _helper.Apu.WriteRegister(0xFF14, 0xC0 | 0x06); // trigger + length enable + freq high

        // Step enough to tick the frame sequencer to a length step
        // Frame sequencer runs at 512 Hz, CPU at ~4.194 MHz => ~8192 cycles per FS tick
        // Length clocks on steps 0, 2, 4, 6
        for (int i = 0; i < 20; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Channel should be disabled after length expires
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x01); // Channel 1 not active
    }

    [Fact]
    public void LengthCounter_DoesNotTickWhenDisabled()
    {
        // Set length but don't enable length
        _helper.Apu.WriteRegister(0xFF11, 0x3F); // length = 63 (1 tick)
        _helper.Apu.WriteRegister(0xFF12, 0xF0); // DAC on
        _helper.Apu.WriteRegister(0xFF13, 0x00);
        _helper.Apu.WriteRegister(0xFF14, 0x80 | 0x06); // trigger, NO length enable

        // Step a lot
        for (int i = 0; i < 20; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Channel should still be active
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x01);
    }

    [Fact]
    public void LengthCounter_ReloadsOnTrigger()
    {
        // Set length = 0 (counter = 64 - 0 = 64)
        _helper.Apu.WriteRegister(0xFF11, 0x00);
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        _helper.Apu.WriteRegister(0xFF13, 0x00);
        // Trigger without length enable
        _helper.Apu.WriteRegister(0xFF14, 0x80 | 0x06);

        // Channel should be active
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x01);
    }

    [Fact]
    public void LengthCounter_Channel3Has256MaxLength()
    {
        // Channel 3 length is 8-bit (max 256)
        _helper.Apu.WriteRegister(0xFF1A, 0x80); // DAC on
        _helper.Apu.WriteRegister(0xFF1B, 0xFF); // length = 255 (counter = 256-255 = 1)
        _helper.Apu.WriteRegister(0xFF1C, 0x20); // volume shift = 1
        _helper.Apu.WriteRegister(0xFF1D, 0x00);
        _helper.Apu.WriteRegister(0xFF1E, 0xC0); // trigger + length enable

        // Tick enough for length to expire
        for (int i = 0; i < 20; i++)
        {
            _helper.Apu.Step(8192);
        }

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x04); // Channel 3 not active
    }
}
