using Silk.NET.OpenAL;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace GameboySharp
{
    public unsafe class AudioStreamerAL : IDisposable
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

        private bool _streamHasStarted = false;

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

        private void CheckAlError(string operation)
        {
            var error = _al.GetError();
            if (error != AudioError.NoError)
            {
                Debug.WriteLine($"OpenAL Error after {operation}: {error}");
            }
        }
        
        public void ReceiveSamplesFromApu(short[] leftChannel, short[] rightChannel)
        {
            while (_audioQueue.Count > MAX_QUEUE_SIZE) _audioQueue.TryDequeue(out _);

            for (int i = 0; i < leftChannel.Length; i++)
            {
                _audioQueue.Enqueue(leftChannel[i]);
                _audioQueue.Enqueue(rightChannel[i]);
            }
        }

        public void UpdateStream()
        {
            if (!_streamHasStarted)
            {
                // Wait until we have a healthy amount of audio before starting.
                if (_audioQueue.Count < FILL_CHUNK_SIZE_SAMPLES * 2) return;
                
                uint[] initialBuffers = new uint[BUFFER_COUNT];
                int filledCount = 0;
                for(int i = 0; i < BUFFER_COUNT; i++)
                {
                    if (FillBuffer(_buffers[i]))
                    {
                        initialBuffers[filledCount++] = _buffers[i];
                    }
                }

                if (filledCount > 0)
                {
                    // Use a fixed block to get a pointer for the API call.
                    fixed(uint* ptr = initialBuffers)
                    {
                        _al.SourceQueueBuffers(_source, filledCount, ptr);
                    }
                    CheckAlError("Initial Queue");

                    _al.SourcePlay(_source);
                    CheckAlError("Initial Play");
                    _streamHasStarted = true;
                }
                return;
            }

            // For a running stream, unqueue processed buffers.
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processedCount);
            if (processedCount > 0)
            {
                uint[] unqueued = new uint[processedCount];

                fixed(uint* ptr = unqueued)
                {
                    _al.SourceUnqueueBuffers(_source, processedCount, ptr);
                }
                CheckAlError("Unqueue");
                
                // And immediately try to refill and requeue them.
                for (int i = 0; i < processedCount; i++)
                {
                    if (FillBuffer(unqueued[i]))
                    {
                        uint id = unqueued[i];
                        _al.SourceQueueBuffers(_source, 1, &id);
                        CheckAlError("Requeue");
                    }
                }
            }
            
            // Safety net: If source stopped, restart it if we have buffers.
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
            {
                _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queuedCount);
                if (queuedCount > 0) _al.SourcePlay(_source);
            }
        }
        
        private bool FillBuffer(uint buffer)
        {
            int samplesToBuffer = Math.Min(_audioQueue.Count, FILL_CHUNK_SIZE_SAMPLES) & ~1;
            if (samplesToBuffer == 0) return false;

            short[] data = new short[samplesToBuffer];
            for (int i = 0; i < samplesToBuffer; i++)
            {
                if (!_audioQueue.TryDequeue(out data[i]))
                {
                    Array.Clear(data, i, data.Length - i);
                    break;
                }
            }
            
            fixed (short* ptr = data)
            {
                _al.BufferData(buffer, AL_FORMAT, ptr, (int)(data.Length * sizeof(short)), SAMPLE_RATE);
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

        public void Dispose()
        {
            _al.SourceStop(_source);
            _al.DeleteSource(_source);
            _al.DeleteBuffers(_buffers);
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
        }
    }
}