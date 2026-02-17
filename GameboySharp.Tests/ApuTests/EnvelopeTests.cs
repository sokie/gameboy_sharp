using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class EnvelopeTests
{
    private readonly ApuTestHelper _helper;

    public EnvelopeTests()
    {
        _helper = new ApuTestHelper();
        _helper.PowerOnWithDefaults();
    }

    [Fact]
    public void Envelope_DecreaseReducesVolume()
    {
        // Channel 2: envelope decrease, period=1, initial vol=15
        _helper.Apu.WriteRegister(0xFF16, 0x80); // duty 50%
        _helper.Apu.WriteRegister(0xFF17, 0xF1); // vol=15, decrease, period=1
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        // Get initial buffer
        var (left1, _) = _helper.StepUntilBufferReady();
        short maxInitial = left1.Max(Math.Abs);

        // Step enough for envelope to tick several times
        // Envelope clocks on frame sequencer step 7 (every 8 FS ticks)
        for (int i = 0; i < 100; i++)
        {
            _helper.Apu.Step(8192);
        }

        var (left2, _) = _helper.StepUntilBufferReady();
        short maxLater = left2.Max(Math.Abs);

        // Volume should have decreased
        Assert.True(maxLater < maxInitial, "Envelope decrease should reduce volume over time");
    }

    [Fact]
    public void Envelope_IncreaseRaisesVolume()
    {
        // Channel 2: envelope increase, period=1, initial vol=0
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF17, 0x09); // vol=0, increase, period=1
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        // Wait for envelope to tick several times
        for (int i = 0; i < 100; i++)
        {
            _helper.Apu.Step(8192);
        }

        var (left, _) = _helper.StepUntilBufferReady();
        short maxAfter = left.Max(Math.Abs);

        // Volume should have increased from 0
        Assert.True(maxAfter > 0, "Envelope increase from vol=0 should produce audible output");
    }

    [Fact]
    public void Envelope_Period0DoesNotChange()
    {
        // Period=0 means envelope is disabled
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF17, 0x80); // vol=8, decrease, period=0
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        // Let DC blocker settle first
        _helper.CollectSamples(3);

        var (left1, _) = _helper.StepUntilBufferReady();

        // Step a lot more
        for (int i = 0; i < 100; i++)
        {
            _helper.Apu.Step(8192);
        }

        var (left2, _) = _helper.StepUntilBufferReady();

        // Volume should remain roughly the same
        short max1 = left1.Max(Math.Abs);
        short max2 = left2.Max(Math.Abs);
        Assert.True(max1 > 0, "Channel should produce output");
        Assert.True(max2 > 0, "Channel should still produce output after stepping");
        // Allow tolerance for DC filter and timing variance
        Assert.True(Math.Abs(max1 - max2) < Math.Max(max1, max2) * 0.5 + 200,
            $"Envelope with period=0 should not change volume significantly (max1={max1}, max2={max2})");
    }

    [Fact]
    public void Envelope_TriggerReloadsVolume()
    {
        // Set up with vol=15, decrease, period=1
        _helper.Apu.WriteRegister(0xFF16, 0x80);
        _helper.Apu.WriteRegister(0xFF17, 0xF1);
        _helper.Apu.WriteRegister(0xFF18, 0x00);
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);

        // Let envelope decrease
        for (int i = 0; i < 200; i++)
        {
            _helper.Apu.Step(8192);
        }

        // Re-trigger - should reload volume to 15
        _helper.Apu.WriteRegister(0xFF19, 0x80 | 0x06);
        var (left, _) = _helper.StepUntilBufferReady();
        short maxAfterRetrigger = left.Max(Math.Abs);

        Assert.True(maxAfterRetrigger > 100, "Retriggering should reload envelope volume");
    }
}
