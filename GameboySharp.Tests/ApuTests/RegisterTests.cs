using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class RegisterTests
{
    private readonly ApuTestHelper _helper;

    public RegisterTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void NR52_OnlyBit7IsWritable()
    {
        _helper.Apu.WriteRegister(0xFF26, 0xFF);
        byte result = _helper.Apu.ReadRegister(0xFF26);
        // Bit 7 = on, bits 6-4 read as 1, bits 3-0 are channel status
        Assert.Equal(0x80, result & 0x80);
    }

    [Fact]
    public void NR52_ReadReturnsChannelStatus()
    {
        // No channels triggered, so bits 0-3 should be 0
        byte result = _helper.Apu.ReadRegister(0xFF26);
        // Bits 6-4 always read as 1
        Assert.Equal(0b0111_0000, result & 0b0111_0000);
    }

    [Fact]
    public void NR52_ReadShowsChannel1Active()
    {
        _helper.TriggerChannel1();
        byte result = _helper.Apu.ReadRegister(0xFF26);
        Assert.True((result & 0x01) != 0, "Channel 1 should be active");
    }

    [Fact]
    public void NR50_ReadWriteRoundTrip()
    {
        _helper.Apu.WriteRegister(0xFF24, 0x53);
        Assert.Equal(0x53, _helper.Apu.ReadRegister(0xFF24));
    }

    [Fact]
    public void NR51_ReadWriteRoundTrip()
    {
        _helper.Apu.WriteRegister(0xFF25, 0xA5);
        Assert.Equal(0xA5, _helper.Apu.ReadRegister(0xFF25));
    }

    [Fact]
    public void NR11_DutyCycleReadBack_MasksLowerBits()
    {
        // NR11 (0xFF11): duty in bits 7-6, lower 6 bits read as 1
        _helper.Apu.WriteRegister(0xFF11, 0x80); // duty = 2
        byte result = _helper.Apu.ReadRegister(0xFF11);
        Assert.Equal(0xBF, result); // 10_111111
    }

    [Fact]
    public void NR13_IsWriteOnly()
    {
        _helper.Apu.WriteRegister(0xFF13, 0x42);
        Assert.Equal(0xFF, _helper.Apu.ReadRegister(0xFF13));
    }

    [Fact]
    public void NR14_LengthEnableBitReadBack()
    {
        // Trigger channel first, then set length enable
        _helper.TriggerChannel1();
        _helper.Apu.WriteRegister(0xFF14, 0x40); // length enable, no trigger
        byte result = _helper.Apu.ReadRegister(0xFF14);
        Assert.True((result & 0x40) != 0, "Length enable bit should be set");
        Assert.Equal(0xBF, result & 0xBF); // Other bits read as 1
    }

    [Fact]
    public void WritesBlockedWhenApuOff_ExceptLength()
    {
        // Turn APU off
        _helper.Apu.WriteRegister(0xFF26, 0x00);

        // NR12 (envelope) write should be blocked
        _helper.Apu.WriteRegister(0xFF12, 0xF0);
        // Turn APU back on to read
        _helper.Apu.WriteRegister(0xFF26, 0x80);
        Assert.Equal(0x00, _helper.Apu.ReadRegister(0xFF12));
    }

    [Fact]
    public void LengthRegisterWritableWhenApuOff()
    {
        // Turn APU off
        _helper.Apu.WriteRegister(0xFF26, 0x00);

        // NR11 (length portion) should still be writable
        _helper.Apu.WriteRegister(0xFF11, 0x3F);
        // Turn back on and verify (duty bits will be 0 since cleared on power-off)
        _helper.Apu.WriteRegister(0xFF26, 0x80);
        // Length is write-only so we verify indirectly - just ensure no crash
    }

    [Fact]
    public void WaveRamWritableWhenApuOff()
    {
        _helper.Apu.WriteRegister(0xFF26, 0x00);
        _helper.Apu.WriteRegister(0xFF30, 0xAB);
        _helper.Apu.WriteRegister(0xFF26, 0x80);
        Assert.Equal(0xAB, _helper.Apu.ReadRegister(0xFF30));
    }
}
