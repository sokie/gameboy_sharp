using System;
using SDL;
using static SDL.SDL3;

namespace GameboySharp
{
    /// <summary>
    /// The default <see cref="IAudioSink"/>, backed by SDL3.
    ///
    /// SDL plays from an "audio device stream": we push interleaved stereo samples with
    /// <c>SDL_PutAudioStreamData</c> and SDL pulls them on its own audio thread. We open the
    /// device at its native rate and generate the APU output at that same rate (queried up
    /// front), so SDL never has to resample. An underrun simply plays silence and resumes the
    /// moment we feed again — there is no buffer-queue state machine that can get stuck (which
    /// is exactly the failure the OpenAL backend was prone to), and on macOS this avoids Apple's
    /// deprecated OpenAL implementation entirely.
    /// </summary>
    internal unsafe class SdlAudioSink : IAudioSink
    {
        private const int Channels = 2;
        private const int FallbackSampleRate = 48000;

        private readonly SDL_AudioStream* _stream;
        private readonly int _sampleRate;

        // Drop incoming audio once more than this is already buffered, so latency can't grow
        // without bound if the emulator briefly runs ahead of the device.
        private readonly int _maxQueuedBytes;

        // Reused interleave buffer so Submit doesn't allocate every frame.
        private short[] _interleaved = new short[1024];
        private bool _disposed;

        public int SampleRate => _sampleRate;

        public SdlAudioSink()
        {
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO))
                throw new Exception($"SDL_Init(AUDIO) failed: {SDL_GetError()}");

            // Ask the default playback device what rate it natively runs at, and generate at that
            // rate so SDL performs zero resampling.
            int rate = FallbackSampleRate;
            SDL_AudioSpec deviceSpec;
            if (SDL_GetAudioDeviceFormat(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &deviceSpec, null) && deviceSpec.freq > 0)
                rate = deviceSpec.freq;
            _sampleRate = rate;
            _maxQueuedBytes = _sampleRate * Channels * sizeof(short) / 10; // ~100 ms of audio

            var spec = new SDL_AudioSpec
            {
                format = SDL_AudioFormat.SDL_AUDIO_S16LE,
                channels = Channels,
                freq = _sampleRate,
            };
            _stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
            if (_stream == null)
            {
                string error = SDL_GetError();
                SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
                throw new Exception($"SDL_OpenAudioDeviceStream failed: {error}");
            }

            // Streams start paused; resume so SDL begins pulling our audio.
            SDL_ResumeAudioStreamDevice(_stream);
        }

        public void Submit(short[] left, short[] right)
        {
            // If SDL still has plenty buffered, skip this block rather than pile on latency.
            if (SDL_GetAudioStreamQueued(_stream) > _maxQueuedBytes) return;

            int frames = left.Length;
            int needed = frames * Channels;
            if (_interleaved.Length < needed) _interleaved = new short[needed];

            for (int i = 0; i < frames; i++)
            {
                _interleaved[i * 2] = left[i];
                _interleaved[i * 2 + 1] = right[i];
            }

            fixed (short* ptr = _interleaved)
            {
                SDL_PutAudioStreamData(_stream, (IntPtr)ptr, needed * sizeof(short));
            }
        }

        // SDL pulls audio on its own thread, so there is nothing to pump each frame.
        public void Update() { }

        public string GetStatus()
        {
            int queuedBytes = SDL_GetAudioStreamQueued(_stream);
            int queuedMs = queuedBytes * 1000 / (_sampleRate * Channels * sizeof(short));
            return $"SDL @ {_sampleRate} Hz, Queued: {queuedBytes} bytes (~{queuedMs} ms)";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_stream != null) SDL_DestroyAudioStream(_stream);
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
        }
    }
}
