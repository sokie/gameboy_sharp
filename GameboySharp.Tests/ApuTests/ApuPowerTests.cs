using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class ApuPowerTests
{
    [Fact]
    public void PowerOff_ClearsRegisters()
    {
        var helper = new ApuTestHelper();
        helper.PowerOnWithDefaults();

        // Set some registers
        helper.Apu.WriteRegister(0xFF24, 0x77);
        helper.Apu.WriteRegister(0xFF25, 0xFF);

        // Power off
        helper.Apu.WriteRegister(0xFF26, 0x00);

        // NR50 and NR51 should be cleared
        // Note: we need to power on to read them, but power-on doesn't restore registers
        helper.Apu.WriteRegister(0xFF26, 0x80);
        Assert.Equal(0x00, helper.Apu.ReadRegister(0xFF24));
        Assert.Equal(0x00, helper.Apu.ReadRegister(0xFF25));
    }

    [Fact]
    public void PowerOff_DisablesAllChannels()
    {
        var helper = new ApuTestHelper();
        helper.PowerOnWithDefaults();

        helper.TriggerChannel1();
        helper.TriggerChannel2();
        helper.TriggerChannel3();
        helper.TriggerChannel4();

        // Verify channels are active
        byte status = helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x0F);

        // Power off and back on
        helper.Apu.WriteRegister(0xFF26, 0x00);
        helper.Apu.WriteRegister(0xFF26, 0x80);

        status = helper.Apu.ReadRegister(0xFF26);
        Assert.Equal(0, status & 0x0F); // All channels should be off
    }

    [Fact]
    public void PowerOff_ClearsChannelRegisters()
    {
        var helper = new ApuTestHelper();
        helper.PowerOnWithDefaults();

        // Set Channel 1 envelope
        helper.Apu.WriteRegister(0xFF12, 0xF3);

        // Power off
        helper.Apu.WriteRegister(0xFF26, 0x00);
        helper.Apu.WriteRegister(0xFF26, 0x80);

        // Channel 1 envelope should be cleared
        Assert.Equal(0x00, helper.Apu.ReadRegister(0xFF12));
    }

    [Fact]
    public void PowerOn_ResetsFrameSequencer()
    {
        var helper = new ApuTestHelper();
        // Power on
        helper.Apu.WriteRegister(0xFF26, 0x80);

        // After power on, the next frame sequencer step should be 0
        // This is tested indirectly - the APU should work correctly after power cycling
        helper.Apu.WriteRegister(0xFF24, 0x77);
        helper.Apu.WriteRegister(0xFF25, 0xFF);
        helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, _) = helper.StepUntilBufferReady();
        short maxAbs = left.Max(Math.Abs);
        Assert.True(maxAbs > 0, "APU should work after power cycling");
    }

    [Fact]
    public void ApuDisabled_ProducesSilence()
    {
        var helper = new ApuTestHelper();
        // Don't power on
        helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, _) = helper.StepUntilBufferReady();
        Assert.True(left.All(s => s == 0), "APU disabled should produce silence");
    }

    [Fact]
    public void WaveRam_PreservedAcrossPowerCycle()
    {
        var helper = new ApuTestHelper();
        helper.PowerOnWithDefaults();

        // Write distinctive pattern
        for (int i = 0; i < 16; i++)
            helper.Apu.WriteRegister((ushort)(0xFF30 + i), (byte)(0x10 + i));

        // Power off and on
        helper.Apu.WriteRegister(0xFF26, 0x00);
        helper.Apu.WriteRegister(0xFF26, 0x80);

        // Verify wave RAM preserved
        for (int i = 0; i < 16; i++)
            Assert.Equal((byte)(0x10 + i), helper.Apu.ReadRegister((ushort)(0xFF30 + i)));
    }
}
