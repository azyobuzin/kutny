using System;
using System.Runtime.ExceptionServices;
using System.Threading;
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
        private readonly bool[] s_majorScale = { true, false, true, false, true, true, false, true, false, true, false, true };
        private readonly bool[] s_minorScale = { true, false, true, true, false, true, false, true, true, false, true, false };

        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private WasapiLoopbackCapture _waveIn;
        private readonly SampleBuffer _sampleBuffer = new SampleBuffer();
        private readonly double[] _chromaVector = new double[12];
        private PitchShiftPlayer _upperHarmonyPlayer;
        private PitchShiftPlayer _lowerHarmonyPlayer;

        public void Initialize()
        {
            if (this._waveIn != null)
                throw new InvalidOperationException();

            this._waveIn = new WasapiLoopbackCapture();
            this._waveIn.DataAvailable += this.WaveInDataAvailable;
            this._waveIn.RecordingStopped += this.WaveInRecordingStopped;
            this._waveIn.StartRecording();

            var waveFormat = this._waveIn.WaveFormat;
            this._upperHarmonyPlayer = new PitchShiftPlayer(waveFormat, 0.5f);
            this._lowerHarmonyPlayer = new PitchShiftPlayer(waveFormat, -0.5f);
        }

        public void Exit()
        {
            if (this._waveIn != null)
            {
                // フォアグラウンドスレッドで録音しているので殺す必要がある
                this._waveIn.Dispose();
                this._waveIn = null;
            }

            if (this._upperHarmonyPlayer != null)
            {
                this._upperHarmonyPlayer.Dispose();
                this._upperHarmonyPlayer = null;
            }

            if (this._lowerHarmonyPlayer != null)
            {
                this._lowerHarmonyPlayer.Dispose();
                this._lowerHarmonyPlayer = null;
            }
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

            if (enable)
            {
                this._upperHarmonyPlayer.Start();
            }
            else
            {
                this._upperHarmonyPlayer.Stop();
            }
        }

        public void EnableLowerHarmony(bool enable)
        {
            if (this._store.IsLowerHarmonyEnabled == enable) return;
            this._store.IsLowerHarmonyEnabled = enable;

            if (enable)
            {
                this._lowerHarmonyPlayer.Start();
            }
            else
            {
                this._lowerHarmonyPlayer.Stop();
            }
        }

        private void WaveInDataAvailable(object sender, WaveInEventArgs e)
        {
            // ハモり再生中ならば、分け与える
            if (this._store.IsUpperHarmonyEnabled)
                this._upperHarmonyPlayer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            if (this._store.IsLowerHarmonyEnabled)
                this._lowerHarmonyPlayer.AddSamples(e.Buffer, 0, e.BytesRecorded);

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

            // バックグラウンドでピッチ検出
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
            double? f0 = null;

            while (true)
            {
                const int pitchWindowLength = 2048;

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
                    var m = CommonUtils.ToChromaIndex(CommonUtils.HzToMidiNote(f0.Value));
                    this._chromaVector[m] += rms;
                }
            }

            if (gotPitch && this._store.IsKeyEstimationRunning)
            {
                // キー推定
                this._store.EstimatedKey = KeyFinding.FindKey(this._chromaVector);
            }

            if (f0.HasValue && (this._store.IsUpperHarmonyEnabled || this._store.IsLowerHarmonyEnabled))
            {
                var key = this._store.EstimatedKey;
                if (key.HasValue)
                {
                    var baseScale = key.Value.Mode == KeyMode.Major ? s_majorScale : s_minorScale;
                    var scale = new bool[12];
                    for (var i = 0; i < scale.Length; i++)
                        scale[i] = baseScale[(key.Value.Tonic + i) % 12];

                    var noteNum = CommonUtils.HzToMidiNote(f0.Value);
                    var m = CommonUtils.ToChromaIndex(noteNum);

                    if (scale[m])
                    {
                        // スケール内の音なら、ハモる音高を求める

                        if (this._store.IsUpperHarmonyEnabled)
                        {
                            // スケール内で3度上の音を探す
                            var harmonyNoteNum = noteNum + 1;
                            for (var i = 0; i < 2; harmonyNoteNum++)
                            {
                                if (scale[CommonUtils.ToChromaIndex(harmonyNoteNum)])
                                    i++;
                            }

                            var freq = CommonUtils.MidiNoteToHz(harmonyNoteNum);
                            this._upperHarmonyPlayer.SetPitchFactor((float)(freq / f0.Value));
                        }

                        if (this._store.IsLowerHarmonyEnabled)
                        {
                            // スケール内で3度下の音を探す
                            var harmonyNoteNum = noteNum - 1;
                            for (var i = 0; i < 2; harmonyNoteNum--)
                            {
                                if (scale[CommonUtils.ToChromaIndex(harmonyNoteNum)])
                                    i++;
                            }

                            var freq = CommonUtils.MidiNoteToHz(harmonyNoteNum);
                            this._upperHarmonyPlayer.SetPitchFactor((float)(freq / f0.Value));
                        }
                    }
                }
            }
        }
    }
}
