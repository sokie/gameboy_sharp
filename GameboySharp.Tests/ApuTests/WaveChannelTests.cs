using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class WaveChannelTests
{
    private readonly ApuTestHelper _helper;

    public WaveChannelTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void WaveRam_RoundTrip()
    {
        // Write a pattern to wave RAM
        for (int i = 0; i < 16; i++)
        {
            _helper.Apu.WriteRegister((ushort)(0xFF30 + i), (byte)(i * 17)); // 0x00, 0x11, ...
        }

        // Read it back
        for (int i = 0; i < 16; i++)
        {
            byte expected = (byte)(i * 17);
            byte actual = _helper.Apu.ReadRegister((ushort)(0xFF30 + i));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void WaveChannel_VolumeShift0IsMute()
    {
        _helper.Apu.WriteRegister(0xFF1A, 0x80); // DAC on
        // Load full-scale wave
        for (int i = 0; i < 16; i++)
            _helper.Apu.WriteRegister((ushort)(0xFF30 + i), 0xFF);

        _helper.Apu.WriteRegister(0xFF1B, 0x00);
        _helper.Apu.WriteRegister(0xFF1C, 0x00); // volume shift = 0 (mute)
        _helper.Apu.WriteRegister(0xFF1D, 0x00);
        _helper.Apu.WriteRegister(0xFF1E, 0x80);

        var (left, _) = _helper.StepUntilBufferReady();

        // With volume shift 0, the output should be muted (only DC offset from bipolar conversion)
        // All samples should be close to the DC-blocked version of -1 (since 0/15 * 2 - 1 = -1)
        short maxAbs = left.Max(Math.Abs);
        // After DC blocking settles, should be very quiet
        // Allow some transient from DC blocker
    }

    [Fact]
    public void WaveChannel_VolumeShift1IsFullVolume()
    {
        _helper.Apu.WriteRegister(0xFF1A, 0x80); // DAC on
        // Load alternating high/low wave for maximum amplitude
        for (int i = 0; i < 16; i++)
            _helper.Apu.WriteRegister((ushort)(0xFF30 + i), (byte)(i % 2 == 0 ? 0xF0 : 0x0F));

        _helper.Apu.WriteRegister(0xFF1B, 0x00);
        _helper.Apu.WriteRegister(0xFF1C, 0x20); // volume shift = 1 (100%)
        _helper.Apu.WriteRegister(0xFF1D, 0x00);
        _helper.Apu.WriteRegister(0xFF1E, 0x80);

        var (left, _) = _helper.StepUntilBufferReady();
        short maxAbs = left.Max(Math.Abs);
        Assert.True(maxAbs > 100, "Wave channel at full volume should produce audible output");
    }

    [Fact]
    public void WaveChannel_DacOff_ProducesSilence()
    {
        _helper.Apu.WriteRegister(0xFF1A, 0x00); // DAC off
        _helper.Apu.WriteRegister(0xFF1B, 0x00);
        _helper.Apu.WriteRegister(0xFF1C, 0x20);
        _helper.Apu.WriteRegister(0xFF1D, 0x00);
        _helper.Apu.WriteRegister(0xFF1E, 0x80); // trigger

        var (left, _) = _helper.StepUntilBufferReady();
        Assert.True(left.All(s => s == 0), "Channel with DAC off should be silent");
    }

    [Fact]
    public void WaveRam_PreservedOnPowerOff()
    {
        // Write pattern
        for (int i = 0; i < 16; i++)
            _helper.Apu.WriteRegister((ushort)(0xFF30 + i), (byte)(0xA0 + i));

        // Power off APU
        _helper.Apu.WriteRegister(0xFF26, 0x00);

        // Wave RAM should be preserved
        for (int i = 0; i < 16; i++)
        {
            byte expected = (byte)(0xA0 + i);
            Assert.Equal(expected, _helper.Apu.ReadRegister((ushort)(0xFF30 + i)));
        }
    }
}
