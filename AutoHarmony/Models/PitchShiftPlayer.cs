using System;
using System.Runtime.ExceptionServices;
using NAudio.CoreAudioApi;
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
        //private StftPitchShiftingSampleProvider _pitchShiftProvider;
        private WasapiOut _waveOut;

        public PitchShiftPlayer(WaveFormat waveFormat, float pan)
        {
            this._pan = pan;
            this._buffer = new BufferedWaveProvider(waveFormat) { DiscardOnBufferOverflow = true };
            this._monoSampleProvider = this._buffer.ToSampleProvider().ToMono();
        }

        public void Start()
        {
            this._waveOut?.Dispose();

            // Stop → Init だと WASAPI がエラー出すので作り直す
            this._waveOut = new WasapiOut(AudioClientShareMode.Shared, true, 50);
            this._waveOut.PlaybackStopped += this.WaveOutPlaybackStopped;

            // ISampleProvider をつくる
            // SmbPitchShiftingSampleProvider は内部でバッファーを持っているので、使いまわせない
            this._pitchShiftProvider = new SmbPitchShiftingSampleProvider(this._monoSampleProvider, 1024, 2, 1f);
            //this._pitchShiftProvider = new StftPitchShiftingSampleProvider(this._monoSampleProvider);
            var provider = new PanningSampleProvider(this._pitchShiftProvider) { Pan = this._pan };

            this._waveOut.Init(provider);
            this._waveOut.Play();
        }

        public void Stop()
        {
            this._waveOut?.Stop();
            this._buffer.ClearBuffer();
            this._pitchShiftProvider = null;
        }

        public void AddSamples(byte[] buffer, int offset, int count)
        {
            this._buffer.AddSamples(buffer, offset, count);
        }

        public void SetPitchFactor(float pitchFactor)
        {
            var psp = this._pitchShiftProvider;
            if (psp != null)
                psp.PitchFactor = pitchFactor;
        }

        public void Dispose()
        {
            this._waveOut?.Dispose();
        }

        private void WaveOutPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                ExceptionDispatchInfo.Capture(e.Exception).Throw();
        }
    }
}
