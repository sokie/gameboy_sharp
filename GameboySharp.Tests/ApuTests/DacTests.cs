using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class DacTests
{
    private readonly ApuTestHelper _helper;

    public DacTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void DacOff_DisablesChannel_PulseChannel()
    {
        // Trigger channel 2
        _helper.TriggerChannel2(frequency: 1000, volume: 15);
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x02); // CH2 active

        // Turn DAC off (NR22 upper 5 bits = 0)
        _helper.Apu.WriteRegister(0xFF17, 0x00);
        status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x02); // CH2 disabled
    }

    [Fact]
    public void DacOff_DisablesChannel_WaveChannel()
    {
        _helper.TriggerChannel3();
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x04); // CH3 active

        // Turn DAC off
        _helper.Apu.WriteRegister(0xFF1A, 0x00);
        status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x04); // CH3 disabled
    }

    [Fact]
    public void DacOff_DisablesChannel_NoiseChannel()
    {
        _helper.TriggerChannel4(volume: 15);
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x08);

        // Turn DAC off
        _helper.Apu.WriteRegister(0xFF21, 0x00);
        status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x08);
    }

    [Fact]
    public void DacOff_TriggerDoesNotEnableChannel()
    {
        // Set envelope to 0 (DAC off)
        _helper.Apu.WriteRegister(0xFF17, 0x00);
        // Try to trigger
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x02); // CH2 should not activate when DAC is off
    }

    [Fact]
    public void DacOn_ChannelCanBeTriggered()
    {
        // DAC on (upper 5 bits non-zero)
        _helper.Apu.WriteRegister(0xFF17, 0x08); // vol=0, increase, period=0 -> DAC on
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x02); // CH2 should be active
    }

    [Fact]
    public void WaveChannel_DacControlledByNR30Bit7()
    {
        // NR30 bit 7 controls wave DAC
        _helper.Apu.WriteRegister(0xFF1A, 0x80); // DAC on
        byte nr30 = _helper.Apu.ReadRegister(0xFF1A);
        Assert.Equal(0xFF, nr30); // bit 7 set, others read as 1

        _helper.Apu.WriteRegister(0xFF1A, 0x00); // DAC off
        nr30 = _helper.Apu.ReadRegister(0xFF1A);
        Assert.Equal(0x7F, nr30); // bit 7 clear, others read as 1
    }
}
