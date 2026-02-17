using Xunit;

namespace GameboySharp.Tests.ApuTests;

public class DcBlockFilterTests
{
    [Fact]
    public void DcBlock_PassesAcSignal()
    {
        float prevX = 0, prevY = 0;

        // Feed a square wave (AC signal) through the filter
        float maxOutput = 0;
        for (int i = 0; i < 1000; i++)
        {
            float input = (i % 2 == 0) ? 1.0f : -1.0f;
            float output = Apu.DcBlock(input, ref prevX, ref prevY);
            if (i > 100) // Let filter settle
            {
                maxOutput = Math.Max(maxOutput, Math.Abs(output));
            }
        }

        // AC signal should pass through with minimal attenuation
        Assert.True(maxOutput > 0.9f,
            $"AC signal should pass through DC blocker with >90% amplitude, got {maxOutput}");
    }

    [Fact]
    public void DcBlock_RemovesDcOffset()
    {
        float prevX = 0, prevY = 0;

        // Feed a constant DC signal
        float lastOutput = 0;
        for (int i = 0; i < 10000; i++)
        {
            lastOutput = Apu.DcBlock(0.5f, ref prevX, ref prevY);
        }

        // DC should be removed (output near 0)
        Assert.True(Math.Abs(lastOutput) < 0.01f,
            $"DC offset should be removed, got {lastOutput}");
    }

    [Fact]
    public void DcBlock_PreservesAmplitudeOfAcOnDc()
    {
        float prevX = 0, prevY = 0;

        // Feed AC signal with DC offset
        float minOutput = float.MaxValue;
        float maxOutput = float.MinValue;

        for (int i = 0; i < 5000; i++)
        {
            // 0.5 DC offset + 0.3 amplitude square wave
            float input = 0.5f + (i % 2 == 0 ? 0.3f : -0.3f);
            float output = Apu.DcBlock(input, ref prevX, ref prevY);

            if (i > 2000) // Let filter settle
            {
                minOutput = Math.Min(minOutput, output);
                maxOutput = Math.Max(maxOutput, output);
            }
        }

        float amplitude = (maxOutput - minOutput) / 2.0f;
        // The AC component (amplitude 0.3) should be preserved
        Assert.True(amplitude > 0.25f,
            $"AC amplitude should be preserved, got {amplitude}");
        // The DC offset should be removed (center near 0)
        float center = (maxOutput + minOutput) / 2.0f;
        Assert.True(Math.Abs(center) < 0.05f,
            $"DC offset should be removed, center at {center}");
    }

    [Fact]
    public void DcBlock_ZeroInputProducesZeroOutput()
    {
        float prevX = 0, prevY = 0;

        for (int i = 0; i < 100; i++)
        {
            float output = Apu.DcBlock(0.0f, ref prevX, ref prevY);
            Assert.Equal(0.0f, output);
        }
    }

    [Fact]
    public void DcBlock_IntegrationWithApu_AudioNotDestroyed()
    {
        // This tests the full path: channel -> mixer -> DC block -> output
        var helper = new ApuTestHelper();
        helper.PowerOnWithDefaults();
        helper.TriggerChannel2(frequency: 1000, volume: 15);

        // Collect several buffers to let DC blocker settle
        var (left, _) = helper.CollectSamples(3);

        // Take samples from the last buffer (after settling)
        var lastBuffer = left.Skip(left.Length - 512).ToArray();
        short maxAbs = lastBuffer.Max(Math.Abs);

        // Audio should NOT be destroyed (the old bug reduced to ~0.3%)
        Assert.True(maxAbs > 500,
            $"Audio should not be destroyed by DC blocker, max amplitude = {maxAbs}");
    }
}
