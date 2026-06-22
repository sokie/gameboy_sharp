using Silk.NET.OpenAL;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace GameboySharp
{
    /// <summary>
    /// An <see cref="IAudioSink"/> backed by OpenAL.
    ///
    /// Audio is streamed with a small pool of buffers: the APU pushes samples into a queue,
    /// and each frame we top up any buffers the device has finished playing and re-queue them.
    /// This is the legacy/fallback backend; the default backend is <see cref="SdlAudioSink"/>,
    /// which avoids OpenAL's manual buffer bookkeeping (and, on macOS, Apple's deprecated
    /// OpenAL implementation).
    /// </summary>
    public unsafe class AudioStreamerAL : IAudioSink
    {
        private const int BUFFER_COUNT = 4;
        private const int SAMPLE_RATE = 44100;
        private const BufferFormat AL_FORMAT = BufferFormat.Stereo16;
        private const int MAX_QUEUE_SIZE = 16384;
        private const int FILL_CHUNK_SIZE_SAMPLES = 2048;

        private readonly AL _al;
        private readonly ALContext _alc;
        private readonly Device* _device;
        private readonly Context* _context;
        private readonly uint _source;
        private readonly uint[] _buffers;
        private readonly ConcurrentQueue<short> _audioQueue = new ConcurrentQueue<short>();
        private readonly short[] _fillData = new short[FILL_CHUNK_SIZE_SAMPLES];

        // True once the source is primed and playing. Reset to false on a full underrun so the
        // cold-start path re-primes from scratch (see RestartIfUnderran).
        private bool _isPlaying = false;
        private bool _disposed = false;

        // OpenAL is told to play at this rate; the OS resamples to the device if they differ.
        public int SampleRate => SAMPLE_RATE;

        public AudioStreamerAL()
        {
            _al = AL.GetApi();
            _alc = ALContext.GetApi();
            _device = _alc.OpenDevice("");
            if (_device == null) throw new Exception("Could not open audio device.");
            _context = _alc.CreateContext(_device, null);
            if (_context == null) throw new Exception("Could not create OpenAL context.");

            _alc.MakeContextCurrent(_context);
            CheckAlError("Context");

            _source = _al.GenSource();
            CheckAlError("GenSource");
            _buffers = _al.GenBuffers(BUFFER_COUNT);
            CheckAlError("GenBuffers");
        }

        /// <summary>Queues a block of stereo samples produced by the APU.</summary>
        public void Submit(short[] left, short[] right)
        {
            // Drop the oldest audio if the queue is backing up, so latency can't grow unbounded.
            while (_audioQueue.Count > MAX_QUEUE_SIZE) _audioQueue.TryDequeue(out _);

            for (int i = 0; i < left.Length; i++)
            {
                _audioQueue.Enqueue(left[i]);
                _audioQueue.Enqueue(right[i]);
            }
        }

        /// <summary>Services the source: starts it, refills played buffers, and recovers underruns.</summary>
        public void Update()
        {
            if (!_isPlaying)
            {
                TryStartPlayback();
                return;
            }

            RecycleProcessedBuffers();
            RestartIfUnderran();
        }

        /// <summary>
        /// Cold start: once enough audio is buffered, fill all buffers, queue them, and play.
        /// </summary>
        private void TryStartPlayback()
        {
            // Wait for a healthy amount of audio so playback doesn't immediately underrun.
            if (_audioQueue.Count < FILL_CHUNK_SIZE_SAMPLES * 2) return;

            uint[] toQueue = new uint[BUFFER_COUNT];
            int filled = 0;
            for (int i = 0; i < BUFFER_COUNT; i++)
            {
                if (FillBuffer(_buffers[i])) toQueue[filled++] = _buffers[i];
            }
            if (filled == 0) return;

            fixed (uint* ptr = toQueue)
            {
                _al.SourceQueueBuffers(_source, filled, ptr);
            }
            CheckAlError("Initial Queue");

            _al.SourcePlay(_source);
            CheckAlError("Initial Play");
            _isPlaying = true;
        }

        /// <summary>
        /// Unqueues each buffer the device has finished with and refills it from the queue,
        /// keeping the source fed.
        /// </summary>
        private void RecycleProcessedBuffers()
        {
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processedCount);
            for (int i = 0; i < processedCount; i++)
            {
                uint buffer;
                _al.SourceUnqueueBuffers(_source, 1, &buffer);
                CheckAlError("Unqueue");

                // Only re-queue if we could refill it; otherwise let the source drain.
                if (FillBuffer(buffer))
                {
                    _al.SourceQueueBuffers(_source, 1, &buffer);
                    CheckAlError("Requeue");
                }
            }
        }

        /// <summary>
        /// Restarts the source after a stall, and — critically — re-primes from scratch after a
        /// FULL underrun. Without the re-prime, an empty source can never recover: RecycleProcessed
        /// Buffers only refills buffers the device reports as processed, and a drained source has
        /// none queued, so a single underrun would otherwise silence audio for the whole session.
        /// </summary>
        private void RestartIfUnderran()
        {
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state == SourceState.Playing) return;

            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queuedCount);
            if (queuedCount > 0)
            {
                // Buffers are still queued — the source just stalled briefly. Kick it again.
                _al.SourcePlay(_source);
            }
            else
            {
                // Fully drained: fall back to the cold-start path to re-prime once we have audio.
                _isPlaying = false;
            }
        }

        /// <summary>
        /// Fills one OpenAL buffer with a whole chunk from the queue. Returns false (without
        /// touching the buffer) when a full chunk isn't ready yet, so we never submit a
        /// half-silent buffer — that would be an audible click.
        /// </summary>
        private bool FillBuffer(uint buffer)
        {
            if (_audioQueue.Count < FILL_CHUNK_SIZE_SAMPLES) return false;

            for (int i = 0; i < FILL_CHUNK_SIZE_SAMPLES; i++)
            {
                _audioQueue.TryDequeue(out _fillData[i]);
            }

            fixed (short* ptr = _fillData)
            {
                _al.BufferData(buffer, AL_FORMAT, ptr, FILL_CHUNK_SIZE_SAMPLES * sizeof(short), SAMPLE_RATE);
            }
            CheckAlError($"BufferData on {buffer}");
            return true;
        }

        public string GetStatus()
        {
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queuedBuffers);
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processedBuffers);
            return $"State: {(SourceState)state}, Queued: {queuedBuffers}, Processed: {processedBuffers}, Queue Size: {_audioQueue.Count}";
        }

        private void CheckAlError(string operation)
        {
            var error = _al.GetError();
            if (error != AudioError.NoError)
            {
                Debug.WriteLine($"OpenAL Error after {operation}: {error}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _al.SourceStop(_source);
            _al.DeleteSource(_source);
            _al.DeleteBuffers(_buffers);
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
        }
    }
}
