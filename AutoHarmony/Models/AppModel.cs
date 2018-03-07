using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KeyEstimation;
using Kutny.Common;
using NAudio.Wave;

namespace AutoHarmony.Models
{
    public interface IAppActions
    {
        void Initialize();
        void Exit();
        void EnableKeyEstimation(bool enable);
        void EnableUpperHarmony(bool enable);
        void EnableLowerHarmony(bool enable);
    }

    public class AppModel : IAppActions
    {
        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private WasapiLoopbackCapture _waveIn;
        private readonly SampleBuffer _sampleBuffer = new SampleBuffer();
        private readonly double[] _chromaVector = new double[12];

        public void Initialize()
        {
            if (this._waveIn != null)
                throw new InvalidOperationException();

            this._waveIn = new WasapiLoopbackCapture();
            this._waveIn.DataAvailable += this.WaveInDataAvailable;
            this._waveIn.RecordingStopped += this.WaveInRecordingStopped;
            this._waveIn.StartRecording();
        }

        public void Exit()
        {
            // フォアグラウンドスレッドで録音しているので殺す必要がある
            this._waveIn.Dispose();
            this._waveIn = null;
        }

        public void EnableKeyEstimation(bool enable)
        {
            if (this._store.IsKeyEstimationRunning == enable) return;

            if (enable)
            {
                // クロマベクトルをリセット
                Array.Clear(this._chromaVector, 0, this._chromaVector.Length);

                this._store.EstimatedKey = null;
            }

            this._store.IsKeyEstimationRunning = enable;
        }

        public void EnableUpperHarmony(bool enable)
        {
            if (this._store.IsUpperHarmonyEnabled == enable) return;
            this._store.IsUpperHarmonyEnabled = enable;

            // TODO
        }

        public void EnableLowerHarmony(bool enable)
        {
            if (this._store.IsLowerHarmonyEnabled == enable) return;
            this._store.IsLowerHarmonyEnabled = enable;

            // TODO
        }

        private void WaveInDataAvailable(object sender, WaveInEventArgs e)
        {
            var waveFormat = this._waveIn.WaveFormat;
            var expectedSampleCount = e.BytesRecorded / waveFormat.BlockAlign / waveFormat.Channels;

            this._sampleBuffer.Write(expectedSampleCount, segment =>
            {
                var provider = new MemoryWaveProvider(e.Buffer, 0, e.BytesRecorded, waveFormat)
                    .ToSampleProvider().ToMono();

                // ピッチ検出バッファーにコピー
                // TODO: offset のバグを報告する
                var count = provider.Read(segment.Array, segment.Offset * 2, segment.Count);
                if (count != expectedSampleCount) throw new Exception();

                // ピークを計算
                var peak = 0f;
                foreach (var x in segment)
                {
                    var abs = Math.Abs(x);
                    if (abs > peak) peak = abs;
                }
                this._store.SignalPeak = peak;
            });

            ThreadPool.QueueUserWorkItem(_ => this.EstimatePitch());
        }

        private void WaveInRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                ExceptionDispatchInfo.Capture(e.Exception).Throw();
        }

        private void EstimatePitch()
        {
            var gotPitch = false;

            while (true)
            {
                const int pitchWindowLength = 2048;

                double? f0 = null;
                var rms = 0.0;

                var ok = this._sampleBuffer.Read(pitchWindowLength, segment =>
                {
                    foreach (var x in segment)
                        rms += x * x;
                    rms = Math.Sqrt(rms / pitchWindowLength);

                    f0 = McLeodPitchMethod.EstimateFundamentalFrequency(this._waveIn.WaveFormat.SampleRate, segment);
                });

                if (!ok) break; // バッファーをすべて読み終わった

                gotPitch = true;

                if (f0.HasValue && this._store.IsKeyEstimationRunning)
                {
                    // クロマベクトルに反映
                    var noteNum = 69.0 + 12.0 * Math.Log(f0.Value / 440.0, 2.0);
                    var m = (int)Math.Round(noteNum) % 12;
                    if (m < 0) m += 12;

                    this._chromaVector[m] += rms;
                }
            }

            if (gotPitch && this._store.IsKeyEstimationRunning)
            {
                // キー推定
                this._store.EstimatedKey = KeyFinding.FindKey(this._chromaVector);
            }
        }
    }
}
