using System;
using System.Diagnostics;
using System.Threading;
using GameboySharp;
using SDL;
using static SDL.SDL3;

// A hands-on harness for validating the SDL3 audio backend on a real machine. The unit tests
// (GameboySharp.Tests/AudioTests) cover the device-free pipeline; this covers what needs an
// actual audio device or your ears.
//
// Usage:
//   dotnet run --project AudioHarness -- info
//   dotnet run --project AudioHarness -- tone [seconds]      (audible 440 Hz sine)
//   dotnet run --project AudioHarness -- recover             (underrun -> auto-recovery, PASS/FAIL)
//   dotnet run --project AudioHarness -- resample            (offline 44100->device resample, PASS/FAIL)
//   dotnet run --project AudioHarness -- play <rom> [seconds] (real emulator audio, audible)
internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "info";
        switch (mode)
        {
            case "info": return Info();
            case "tone": return Tone(args.Length > 1 ? double.Parse(args[1]) : 2.0);
            case "recover": return Recover();
            case "resample": return Resample();
            case "play": return Play(args.Length > 1 ? args[1] : null, args.Length > 2 ? double.Parse(args[2]) : 5.0);
            default:
                Console.WriteLine($"unknown mode '{mode}'. Modes: info | tone | recover | resample | play");
                return 2;
        }
    }

    /// <summary>Print which driver/rate SDL negotiated for the default device.</summary>
    private static int Info()
    {
        using var sink = new SdlAudioSink();
        Console.WriteLine($"SDL audio driver : {SDL_GetCurrentAudioDriver()}");
        Console.WriteLine($"Negotiated rate  : {sink.SampleRate} Hz");
        Console.WriteLine($"Status           : {sink.GetStatus()}");
        return 0;
    }

    /// <summary>Play an audible 440 Hz sine through the real sink for a few seconds.</summary>
    private static int Tone(double seconds)
    {
        using var sink = new SdlAudioSink();
        int rate = sink.SampleRate;
        Console.WriteLine($"Playing 440 Hz for {seconds:F1}s at {rate} Hz — you should hear a steady tone.");

        int chunkFrames = rate / 100; // 10 ms blocks, like the emulator's cadence
        var left = new short[chunkFrames];
        var right = new short[chunkFrames];
        double phase = 0, increment = 2 * Math.PI * 440 / rate;

        long chunks = (long)(seconds * 100);
        var clock = Stopwatch.StartNew();
        for (long c = 0; c < chunks; c++)
        {
            for (int i = 0; i < chunkFrames; i++)
            {
                short sample = (short)(8000 * Math.Sin(phase));
                phase += increment;
                left[i] = sample;
                right[i] = sample;
            }
            sink.Submit(left, right);
            sink.Update();
            SleepUntil(clock, (c + 1) * 10.0);
        }
        Console.WriteLine("tone done");
        return 0;
    }

    /// <summary>
    /// Drive the real sink, force a full underrun by not feeding, then feed again and confirm
    /// audio resumes (queue grows then drains) — i.e. no permanent stall.
    /// </summary>
    private static int Recover()
    {
        using var sink = new SdlAudioSink();
        int rate = sink.SampleRate;
        var (left, right) = Tone(rate / 5, 440, rate); // 0.2s

        sink.Submit(left, right);
        sink.Update();
        Console.WriteLine($"after feed     : {sink.GetStatus()}");

        // Stop feeding and let it drain to a full underrun.
        for (int i = 0; i < 12; i++) { Thread.Sleep(40); sink.Update(); }
        int drained = QueuedBytes(sink.GetStatus());
        Console.WriteLine($"after starve   : {sink.GetStatus()}");

        // Feed again; it must accept the audio and start consuming it (not stuck).
        sink.Submit(left, right);
        int afterRefeed = QueuedBytes(sink.GetStatus());
        Thread.Sleep(80);
        int consuming = QueuedBytes(sink.GetStatus());
        Console.WriteLine($"after refeed   : {sink.GetStatus()}");

        bool recovered = drained == 0 && afterRefeed > 0 && consuming < afterRefeed;
        Console.WriteLine($"RECOVER: {(recovered ? "PASS" : "FAIL")} (drained={drained}, afterRefeed={afterRefeed}, later={consuming})");
        return recovered ? 0 : 1;
    }

    /// <summary>
    /// Offline (no device) correctness of SDL's resampler: feed a 1 s 440 Hz sine at 44100 and
    /// convert to 48000, then check the output length ratio and RMS are preserved.
    /// </summary>
    private static int Resample()
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            Console.WriteLine("SDL_Init failed: " + SDL_GetError());
            return 1;
        }

        var src = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_S16LE, channels = 2, freq = 44100 };
        var dst = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_S16LE, channels = 2, freq = 48000 };
        SDL_AudioStream* conv = SDL_CreateAudioStream(&src, &dst);
        if (conv == null) { Console.WriteLine("CreateAudioStream failed: " + SDL_GetError()); return 1; }

        // Build 1 second = 44100 interleaved stereo frames (88200 shorts) of a 440 Hz sine.
        const int inFrames = 44100;
        var (l, r) = Tone(inFrames, 440, 44100);
        var input = new short[inFrames * 2];
        for (int i = 0; i < inFrames; i++) { input[i * 2] = l[i]; input[i * 2 + 1] = r[i]; }

        double inRms = Rms(input);
        fixed (short* ip = input) SDL_PutAudioStreamData(conv, (IntPtr)ip, input.Length * sizeof(short));
        SDL_FlushAudioStream(conv);

        int available = SDL_GetAudioStreamAvailable(conv);
        byte[] outBytes = new byte[available];
        int got;
        fixed (byte* op = outBytes) got = SDL_GetAudioStreamData(conv, (IntPtr)op, available);
        var outShorts = new short[got / sizeof(short)];
        Buffer.BlockCopy(outBytes, 0, outShorts, 0, got);

        int outFrames = got / (sizeof(short) * 2);
        double ratio = (double)outFrames / inFrames;
        double rmsRatio = Rms(outShorts) / inRms;
        bool ok = Math.Abs(ratio - 48000.0 / 44100.0) < 0.01 && Math.Abs(rmsRatio - 1.0) < 0.10;
        Console.WriteLine($"resample 44100->48000: outFrames={outFrames} ratio={ratio:F4} (expect 1.0884), rmsRatio={rmsRatio:F3} (expect ~1.0)");
        Console.WriteLine($"RESAMPLE: {(ok ? "PASS" : "FAIL")}");

        SDL_DestroyAudioStream(conv);
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
        return ok ? 0 : 1;
    }

    /// <summary>Run the real emulator headless and play its audio through the real sink.</summary>
    private static int Play(string romPath, double seconds)
    {
        if (romPath == null) { Console.WriteLine("usage: play <rom> [seconds]"); return 2; }

        using var emulator = new Emulator();
        emulator.LoadRom(romPath);
        Console.WriteLine($"Audio: {emulator.GetAudioStatus()}");
        Console.WriteLine($"Playing '{romPath}' for {seconds:F1}s — you should hear the game.");

        double frameMs = 1000.0 / GameboyConstants.FramesPerSecond;
        long frames = (long)(seconds * GameboyConstants.FramesPerSecond);
        var frameTimer = Stopwatch.StartNew();
        for (long f = 0; f < frames; f++)
        {
            frameTimer.Restart();
            emulator.RunFrame();
            double elapsed = frameTimer.Elapsed.TotalMilliseconds;
            if (frameMs - elapsed > 0) Thread.Sleep((int)(frameMs - elapsed));
        }
        Console.WriteLine($"done. Final audio: {emulator.GetAudioStatus()}");
        return 0;
    }

    // --- helpers ---

    private static (short[] left, short[] right) Tone(int frames, double freq, int rate)
    {
        var left = new short[frames];
        var right = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            short s = (short)(8000 * Math.Sin(2 * Math.PI * freq * i / rate));
            left[i] = s;
            right[i] = s;
        }
        return (left, right);
    }

    private static double Rms(short[] samples)
    {
        double sum = 0;
        foreach (short s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    // Pull the "Queued: N bytes" number back out of the sink's status string.
    private static int QueuedBytes(string status)
    {
        int start = status.IndexOf("Queued: ", StringComparison.Ordinal);
        if (start < 0) return -1;
        start += "Queued: ".Length;
        int end = status.IndexOf(" bytes", start, StringComparison.Ordinal);
        return end < 0 ? -1 : int.Parse(status.Substring(start, end - start));
    }

    private static void SleepUntil(Stopwatch clock, double targetMs)
    {
        double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
        if (remaining > 0) Thread.Sleep((int)remaining);
    }
}
