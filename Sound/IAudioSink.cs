using System;

namespace GameboySharp
{
    /// <summary>
    /// A destination for the stereo audio the APU produces.
    ///
    /// The emulator depends only on this interface, so the actual audio backend (SDL3,
    /// OpenAL, ...) can be swapped without touching any emulation code. The APU pushes
    /// samples in via <see cref="Submit"/>, and the main loop calls <see cref="Update"/>
    /// once per frame to let the backend service its device.
    /// </summary>
    internal interface IAudioSink : IDisposable
    {
        /// <summary>
        /// The sample rate (Hz) this backend plays at — the device's native rate, so the APU
        /// can generate at exactly this rate and avoid any resampling. The emulator reads this
        /// to configure the APU.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Queues one block of audio. <paramref name="left"/>[i] and <paramref name="right"/>[i]
        /// are the paired 16-bit samples for frame i. Wired to <see cref="Apu.AudioBufferReady"/>.
        /// </summary>
        void Submit(short[] left, short[] right);

        /// <summary>
        /// Pumps the backend so it keeps feeding the audio device. Safe to call every frame,
        /// including while the emulator is paused.
        /// </summary>
        void Update();

        /// <summary>
        /// A short human-readable description of the backend state, for the debug overlay/logs.
        /// </summary>
        string GetStatus();
    }
}
