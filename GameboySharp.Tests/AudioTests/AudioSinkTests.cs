using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GameboySharp.Tests.AudioTests;

/// <summary>
/// Validates the APU -> <see cref="IAudioSink"/> pipeline end to end, using a capturing sink
/// instead of a real audio device so the checks are deterministic and run anywhere (no speakers
/// required). These confirm the things we can't hear in CI: that audio actually reaches the sink,
/// that it is DC-centered (the de-pop fix), and that "no output" really means digital silence.
/// </summary>
public class AudioSinkTests
{
    /// <summary>A test double that just records everything the APU submits.</summary>
    private sealed class CapturingAudioSink : IAudioSink
    {
        public int SampleRate { get; }
        public List<short> Left { get; } = new();
        public List<short> Right { get; } = new();

        public CapturingAudioSink(int sampleRate) => SampleRate = sampleRate;

        public void Submit(short[] left, short[] right)
        {
            Left.AddRange(left);
            Right.AddRange(right);
        }

        public void Update() { }
        public string GetStatus() => $"captured {Left.Count} frames";
        public void Dispose() { }
    }

    /// <summary>Builds an APU at the given rate, wired to a fresh capturing sink, powered on.</summary>
    private static (Apu apu, CapturingAudioSink sink) NewApu(int sampleRate)
    {
        var apu = new Apu(sampleRate);
        var sink = new CapturingAudioSink(sampleRate);
        apu.AudioBufferReady = sink.Submit;

        // Power on, max master volume, all channels panned to both sides.
        apu.WriteRegister(0xFF26, 0x80);
        apu.WriteRegister(0xFF24, 0x77);
        apu.WriteRegister(0xFF25, 0xFF);
        return (apu, sink);
    }

    private static void TriggerPulseChannel2(Apu apu, int frequencyReg = 0x06FF, int volume = 15)
    {
        apu.WriteRegister(0xFF16, 2 << 6);                                  // 50% duty
        apu.WriteRegister(0xFF17, (byte)(volume << 4));                     // envelope volume, DAC on
        apu.WriteRegister(0xFF18, (byte)(frequencyReg & 0xFF));             // frequency low
        apu.WriteRegister(0xFF19, (byte)(0x80 | ((frequencyReg >> 8) & 7))); // trigger + frequency high
    }

    private static void StepUntil(Apu apu, CapturingAudioSink sink, int frames)
    {
        int guard = 0;
        while (sink.Left.Count < frames && guard++ < 5_000_000) apu.Step(256);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void Apu_PlayingTone_ReachesSinkAndIsDcCentered(int sampleRate)
    {
        var (apu, sink) = NewApu(sampleRate);
        TriggerPulseChannel2(apu);

        // Generate ~0.1s of audio, then analyze a window past the filter settling time.
        StepUntil(apu, sink, sampleRate / 10);
        var window = sink.Left.Skip(1024).ToArray();

        int peak = window.Select(s => Math.Abs((int)s)).Max();
        double mean = window.Average(s => (double)s);

        Assert.True(peak > 1000, $"a playing channel should reach the sink with real amplitude (peak={peak})");
        Assert.True(Math.Abs(mean) < peak * 0.1,
            $"output should be DC-centered after the de-pop filter (mean={mean:F1}, peak={peak})");
    }

    [Fact]
    public void Apu_DeviceRate_ProducesCorrectNumberOfSamplesPerSecond()
    {
        // The whole point of the rate change: the APU emits at exactly the host rate, so one
        // second of emulated time yields ~one second of audio frames (within decimation rounding).
        const int rate = 48000;
        var (apu, sink) = NewApu(rate);
        TriggerPulseChannel2(apu);

        // Step exactly one second of CPU cycles.
        int cyclesPerSecond = (int)GameboyConstants.CpuClockSpeed;
        for (int stepped = 0; stepped < cyclesPerSecond; stepped += 256) apu.Step(256);

        Assert.InRange(sink.Left.Count, rate - 600, rate + 600);
    }

    [Fact]
    public void Apu_NoPanning_ProducesDigitalSilenceThroughSink()
    {
        var (apu, sink) = NewApu(48000);
        apu.WriteRegister(0xFF25, 0x00); // nothing panned anywhere
        TriggerPulseChannel2(apu);

        StepUntil(apu, sink, 4096);

        Assert.All(sink.Left, s => Assert.Equal(0, s));
        Assert.All(sink.Right, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Apu_DisablingChannel_SettlesToSilenceWithNoStuckDc()
    {
        // Turning the only playing channel off must leave the output at clean digital silence
        // shortly after — no sustained DC offset left behind (the per-channel and output DC
        // blockers settle to 0). This is the steady-state half of the de-pop behavior.
        var (apu, sink) = NewApu(48000);
        TriggerPulseChannel2(apu);
        StepUntil(apu, sink, 4096);

        int peakWhilePlaying = sink.Left.Skip(1024).Select(s => Math.Abs((int)s)).Max();
        Assert.True(peakWhilePlaying > 1000, "sanity: the channel should be audibly playing first");

        apu.WriteRegister(0xFF17, 0x00); // NR22 upper bits 0 -> CH2 DAC off -> channel disabled
        int afterDisable = sink.Left.Count;
        StepUntil(apu, sink, afterDisable + 48000 / 5); // ~200 ms of decay time

        var tail = sink.Left.Skip(sink.Left.Count - 2048).ToArray(); // last ~43 ms
        int tailPeak = tail.Select(s => Math.Abs((int)s)).Max();
        Assert.True(tailPeak < 50,
            $"after disabling the channel the output should settle to silence (tailPeak={tailPeak})");
    }
}
