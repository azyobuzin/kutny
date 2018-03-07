using System;
using System.Runtime.ExceptionServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AutoHarmony.Models
{
    public class PitchShiftPlayer : IDisposable
    {
        private readonly BufferedWaveProvider _buffer;
        private readonly ISampleProvider _monoSampleProvider;
        private readonly float _pan;
        private SmbPitchShiftingSampleProvider _pitchShiftProvider;
        private readonly WaveOutEvent _waveOut;

        public PitchShiftPlayer(WaveFormat waveFormat, float pan)
        {
            this._pan = pan;
            this._buffer = new BufferedWaveProvider(waveFormat) { DiscardOnBufferOverflow = true };
            this._monoSampleProvider = this._buffer.ToSampleProvider().ToMono();
            this._waveOut = new WaveOutEvent();
            this._waveOut.PlaybackStopped += this.WaveOutPlaybackStopped;
        }

        public void Start()
        {
            if (this._waveOut.PlaybackState != PlaybackState.Stopped)
                this._waveOut.Stop();

            // ISampleProvider をつくる
            // SmbPitchShiftingSampleProvider は内部でバッファーを持っているので、使いまわせない
            this._pitchShiftProvider = new SmbPitchShiftingSampleProvider(this._monoSampleProvider);
            var provider = new PanningSampleProvider(this._pitchShiftProvider) { Pan = this._pan };

            this._waveOut.Init(provider);
            this._waveOut.Play();
        }

        public void Stop()
        {
            this._waveOut.Stop();
            this._buffer.ClearBuffer();
            this._pitchShiftProvider = null;
        }

        public void AddSamples(byte[] buffer, int offset, int count)
        {
            this._buffer.AddSamples(buffer, offset, count);
        }

        public void SetPitchFactor(float pitchFactor)
        {
            if (this._pitchShiftProvider == null)
                throw new InvalidOperationException();

            this._pitchShiftProvider.PitchFactor = pitchFactor;
        }

        public void Dispose()
        {
            this._waveOut.Dispose();
        }

        private void WaveOutPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                ExceptionDispatchInfo.Capture(e.Exception).Throw();
        }
    }
}
