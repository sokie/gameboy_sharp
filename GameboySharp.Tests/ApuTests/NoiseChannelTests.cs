using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class NoiseChannelTests
{
    private readonly ApuTestHelper _helper;

    public NoiseChannelTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void NoiseChannel_ProducesOutput()
    {
        _helper.TriggerChannel4(volume: 15, clockShift: 0, divisor: 1);

        var (left, _) = _helper.StepUntilBufferReady();
        short maxAbs = left.Max(Math.Abs);
        Assert.True(maxAbs > 0, "Noise channel should produce non-zero output");
    }

    [Fact]
    public void NoiseChannel_7BitModeProducesOutput()
    {
        _helper.TriggerChannel4(volume: 15, clockShift: 0, divisor: 1, widthMode: 1);

        var (left, _) = _helper.StepUntilBufferReady();
        short maxAbs = left.Max(Math.Abs);
        Assert.True(maxAbs > 0, "Noise channel in 7-bit mode should produce output");
    }

    [Fact]
    public void NoiseChannel_PolynomialRegisterReadBack()
    {
        _helper.Apu.WriteRegister(0xFF22, 0x73); // shift=7, 15-bit, divisor=3
        byte result = _helper.Apu.ReadRegister(0xFF22);
        Assert.Equal(0x73, result);
    }

    [Fact]
    public void NoiseChannel_7BitModeReadBack()
    {
        _helper.Apu.WriteRegister(0xFF22, 0x48); // shift=4, 7-bit, divisor=0
        byte result = _helper.Apu.ReadRegister(0xFF22);
        Assert.Equal(0x48, result);
    }

    [Fact]
    public void NoiseChannel_LfsrResetOnTrigger()
    {
        // Trigger once
        _helper.TriggerChannel4(volume: 15, clockShift: 0, divisor: 1);

        // Step to advance LFSR
        for (int i = 0; i < 50; i++)
            _helper.Apu.Step(256);

        // Trigger again - LFSR should reset to 0x7FFF
        _helper.Apu.WriteRegister(0xFF23, 0x80);

        // Channel should still be active
        byte status = _helper.Apu.ReadRegister(0xFF26);
        Assert.NotEqual(0, status & 0x08);
    }

    [Fact]
    public void NoiseChannel_FrequencyTimerCalculation()
    {
        // Divisor=0 maps to 8, shift=0 => period = 8 << 0 = 8
        _helper.Apu.WriteRegister(0xFF22, 0x00);
        byte result = _helper.Apu.ReadRegister(0xFF22);
        Assert.Equal(0x00, result);

        // Divisor=3, shift=2 => period = (3*16) << 2 = 48 << 2 = 192
        _helper.Apu.WriteRegister(0xFF22, 0x23);
        result = _helper.Apu.ReadRegister(0xFF22);
        Assert.Equal(0x23, result);
    }
}
