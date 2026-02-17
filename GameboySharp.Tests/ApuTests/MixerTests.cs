using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class MixerTests
{
    private readonly ApuTestHelper _helper;

    public MixerTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void Mixer_PanningLeftOnly()
    {
        // Only pan channel 2 to left (bit 5 of NR51)
        _helper.Apu.WriteRegister(0xFF25, 0x20); // only CH2 left
        _helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, right) = _helper.StepUntilBufferReady();
        short maxLeft = left.Max(Math.Abs);
        short maxRight = right.Max(Math.Abs);

        Assert.True(maxLeft > 100, "Left channel should have audio");
        Assert.True(maxRight == 0 || maxRight < maxLeft / 10,
            "Right channel should be silent or near-silent when not panned");
    }

    [Fact]
    public void Mixer_PanningRightOnly()
    {
        // Only pan channel 2 to right (bit 1 of NR51)
        _helper.Apu.WriteRegister(0xFF25, 0x02); // only CH2 right
        _helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, right) = _helper.StepUntilBufferReady();
        short maxLeft = left.Max(Math.Abs);
        short maxRight = right.Max(Math.Abs);

        Assert.True(maxRight > 100, "Right channel should have audio");
        Assert.True(maxLeft == 0 || maxLeft < maxRight / 10,
            "Left channel should be silent when not panned");
    }

    [Fact]
    public void Mixer_FixedDivisor_OneChannelNotAsLoudAsFour()
    {
        // With fixed /4 divisor, 1 channel should be ~1/4 the amplitude of 4 channels
        // Enable only channel 2
        _helper.Apu.WriteRegister(0xFF25, 0x22); // CH2 both sides
        _helper.TriggerChannel2(frequency: 500, volume: 15);

        // Collect samples with 1 channel
        var (left1, _) = _helper.CollectSamples(2);
        short max1Channel = left1.Max(Math.Abs);

        // Now also enable channels 1, 3, 4 with same settings
        _helper.Apu.WriteRegister(0xFF25, 0xFF);
        _helper.TriggerChannel1(frequency: 500, volume: 15);
        _helper.TriggerChannel3(frequency: 500, volumeShift: 1);
        _helper.TriggerChannel4(volume: 15);

        var (left4, _) = _helper.CollectSamples(2);
        short max4Channels = left4.Max(Math.Abs);

        // 1 channel should be noticeably quieter than 4 channels
        Assert.True(max1Channel < max4Channels,
            $"1 channel ({max1Channel}) should be quieter than 4 channels ({max4Channels})");
    }

    [Fact]
    public void Mixer_MasterVolume_Volume0IsNotSilent()
    {
        // NR50 volume 0 should map to 1/8, not 0
        _helper.Apu.WriteRegister(0xFF24, 0x00); // volume 0 both sides
        _helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, _) = _helper.StepUntilBufferReady();
        short maxAbs = left.Max(Math.Abs);

        // Should not be completely silent
        Assert.True(maxAbs > 0, "Master volume 0 should still produce some output (maps to 1/8)");
    }

    [Fact]
    public void Mixer_MasterVolume_Volume7IsLouderThanVolume0()
    {
        // Volume 7
        _helper.Apu.WriteRegister(0xFF24, 0x77);
        _helper.TriggerChannel2(frequency: 1000, volume: 15);
        var (leftLoud, _) = _helper.CollectSamples(2);
        short maxLoud = leftLoud.Max(Math.Abs);

        // Reset and use volume 0
        _helper.PowerOnWithDefaults();
        _helper.Apu.WriteRegister(0xFF24, 0x00);
        _helper.TriggerChannel2(frequency: 1000, volume: 15);
        var (leftQuiet, _) = _helper.CollectSamples(2);
        short maxQuiet = leftQuiet.Max(Math.Abs);

        Assert.True(maxLoud > maxQuiet,
            $"Volume 7 ({maxLoud}) should be louder than volume 0 ({maxQuiet})");
    }

    [Fact]
    public void Mixer_NoPanning_ProducesSilence()
    {
        _helper.Apu.WriteRegister(0xFF25, 0x00); // no channels panned anywhere
        _helper.TriggerChannel2(frequency: 1000, volume: 15);

        var (left, right) = _helper.StepUntilBufferReady();
        Assert.True(left.All(s => s == 0), "No panning should produce silence on left");
        Assert.True(right.All(s => s == 0), "No panning should produce silence on right");
    }
}
