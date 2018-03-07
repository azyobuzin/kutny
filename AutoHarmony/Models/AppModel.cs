using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using KeyEstimation;
using Kutny.Common;
using NAudio.CoreAudioApi;
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
        void SelectRecoderProvider(int index);
    }

    public class AppModel : IAppActions
    {
        private readonly bool[] s_majorScale = { true, false, true, false, true, true, false, true, false, true, false, true };
        private readonly bool[] s_minorScale = { true, false, true, true, false, true, false, true, true, false, true, false };

        private readonly AppStore _store = new AppStore();
        public IAppStore Store => this._store;

        private IWaveIn _waveIn;
        private readonly SampleBuffer _sampleBuffer = new SampleBuffer();
        private readonly double[] _chromaVector = new double[12];
        private PitchShiftPlayer _upperHarmonyPlayer;
        private PitchShiftPlayer _lowerHarmonyPlayer;

        public void Initialize()
        {
            this.UpdateRecoderProviders();
            this.SelectRecoderProvider(0);
        }

        public void Exit()
        {
            // フォアグラウンドスレッドで録音しているので殺す必要がある
            this.DisposeAudioResources();
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

            if (enable)
            {
                this._upperHarmonyPlayer.Start();
                this._store.IsUpperHarmonyEnabled = true;
            }
            else
            {
                this._store.IsUpperHarmonyEnabled = false;
                this._upperHarmonyPlayer.Stop();
            }
        }

        public void EnableLowerHarmony(bool enable)
        {
            if (this._store.IsLowerHarmonyEnabled == enable) return;

            if (enable)
            {
                this._lowerHarmonyPlayer.Start();
                this._store.IsLowerHarmonyEnabled = true;
            }
            else
            {
                this._store.IsLowerHarmonyEnabled = false;
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
                var count = provider.Read(segment.Array, waveFormat.Channels == 2 ? segment.Offset * 2 : segment.Offset, segment.Count);
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

            if (gotPitch)
            {
                this._store.EstimatedPitch = f0;
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

        public void SelectRecoderProvider(int index)
        {
            this._store.SelectedRecoderProviderIndex = index;

            this.DisposeAudioResources();

            this._waveIn = this._store.RecoderProviders[index]
                .CreateWaveIn();
            this._waveIn.DataAvailable += this.WaveInDataAvailable;
            this._waveIn.RecordingStopped += this.WaveInRecordingStopped;
            this._waveIn.StartRecording();

            var waveFormat = this._waveIn.WaveFormat;
            this._upperHarmonyPlayer = new PitchShiftPlayer(waveFormat, 0.5f);
            this._lowerHarmonyPlayer = new PitchShiftPlayer(waveFormat, -0.5f);

            if (this._store.IsUpperHarmonyEnabled)
                this._upperHarmonyPlayer.Start();

            if (this._store.IsLowerHarmonyEnabled)
                this._lowerHarmonyPlayer.Start();
        }

        private void DisposeAudioResources()
        {
            if (this._waveIn != null)
            {
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

        private void UpdateRecoderProviders()
        {
            var devices = new MMDeviceEnumerator();
            var builder = ImmutableArray.CreateBuilder<RecoderProvider>();

            builder.Add(DefaultCaptureDeviceRecoderProvider.Default);

            builder.AddRange(
                devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .Select(x => new CaptureDeviceRecoderProvider(x))
            );

            builder.Add(DefaultRenderDeviceRecoderProvider.Default);

            builder.AddRange(
                devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Select(x => new RenderDeviceRecoderProvider(x))
            );

            this._store.RecoderProviders = builder.ToImmutable();
        }
    }
}
