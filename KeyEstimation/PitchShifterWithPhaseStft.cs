using System;
using System.Numerics;
using Accord.Audio.Windows;
using Accord.Math;
using Accord.Math.Transforms;

namespace KeyEstimation
{
    public class PitchShifterWithStft
    {
        private readonly int _windowLength;
        private bool _isFirstInput = true;

        private readonly RaisedCosineWindow _window;
        private readonly Complex[] _fftBuffer;
        private readonly Complex[] _modifiedSpectrum;

        private readonly float[] _inputBuffer;
        private readonly float[] _fftInputBuffer;
        private readonly float[] _fftOutputBuffer;
        private readonly float[] _nextOutputBuffer;

        public PitchShifterWithStft(int windowLength)
        {
            if (windowLength % 2 != 0) throw new ArgumentException();

            this._windowLength = windowLength;

            this._window = RaisedCosineWindow.Hann(windowLength);
            this._fftBuffer = new Complex[windowLength];
            this._modifiedSpectrum = new Complex[windowLength];

            this._inputBuffer = new float[windowLength / 2];
            this._fftInputBuffer = new float[windowLength];
            this._fftOutputBuffer = new float[windowLength];
            this._nextOutputBuffer = new float[windowLength];
        }

        public float[] InputFrame(ReadOnlySpan<float> input, double pitchRate)
        {
            if (input.Length != this._windowLength) throw new ArgumentException();

            var hop = this._windowLength / 2;

            if (this._isFirstInput)
            {
                input.CopyTo(this._fftInputBuffer);
                input.Slice(hop, hop).CopyTo(this._inputBuffer);

                this.InputCore(pitchRate);
                Array.Copy(this._fftOutputBuffer, this._nextOutputBuffer, this._windowLength);

                // 初回は出力しない
                this._isFirstInput = false;
                return null;
            }

            // 残りのバッファーと input の前半を使って中間の波形を作る
            Array.Copy(this._inputBuffer, this._fftInputBuffer, hop);
            input.Slice(0, hop).CopyTo(new Span<float>(this._fftInputBuffer, hop, hop));
            this.InputCore(pitchRate);

            // 戻り値を作成
            var result = new float[this._windowLength];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = this._nextOutputBuffer[i];

                // _vocoderOutputBuffer の前半だけ使う
                if (i >= hop)
                    result[i] += this._fftOutputBuffer[i - hop];
            }

            // 余りを _inputBuffer に退避
            Array.Copy(this._fftOutputBuffer, hop, this._inputBuffer, 0, hop);

            // input 全体で生成
            input.CopyTo(this._fftInputBuffer);
            this.InputCore(pitchRate);

            // _nextOutputBuffer に input 全体の結果を保存しておく
            for (var i = 0; i < this._nextOutputBuffer.Length; i++)
            {
                this._nextOutputBuffer[i] = this._fftOutputBuffer[i];

                // 余りを足し合わせる
                if (i < hop)
                    this._nextOutputBuffer[i] += this._inputBuffer[i];
            }

            // _inputBuffer に後半を詰めておく
            input.Slice(hop, hop).CopyTo(this._inputBuffer);

            // 音量調整
            //var volChange = (float)(Rms(input) / Rms(result));
            //if (volChange != 1)
            //{
            //    for (var i = 0; i < result.Length; i++)
            //        result[i] *= volChange;
            //}

            return result;
        }

        private void InputCore(double pitchRate)
        {
            var hop = this._windowLength / 2;

            for (var i = 0; i < this._fftInputBuffer.Length; i++)
                this._fftBuffer[i] = this._fftInputBuffer[i] * this._window[i];

            FourierTransform2.FFT(this._fftBuffer, FourierTransform.Direction.Forward);

            // ピッチ変更
            for (var i = 0; i < hop; i++)
            {
                var pos = i / pitchRate;
                var a = (int)pos;
                Complex newSpec;

                if (a >= this._windowLength)
                {
                    // 範囲外なので諦める
                    newSpec = this._fftBuffer[i];
                }
                else if (a == this._windowLength - 1 || a == pos)
                {
                    newSpec = this._fftBuffer[a];
                }
                else
                {
                    // 補間
                    newSpec = this._fftBuffer[a]
                        + (this._fftBuffer[a + 1] - this._fftBuffer[a])
                        * (pos - a);
                }

                this._modifiedSpectrum[i] = newSpec;
                this._modifiedSpectrum[this._windowLength - i - 1] = newSpec;
            }

            FourierTransform2.FFT(this._modifiedSpectrum, FourierTransform.Direction.Backward);

            for (var i = 0; i < this._fftOutputBuffer.Length; i++)
                this._fftOutputBuffer[i] = (float)this._modifiedSpectrum[i].Real * this._window[i];
        }

        private static double Rms(ReadOnlySpan<float> samples)
        {
            var squared = 0.0;
            foreach (var x in samples)
                squared += x * x;
            return Math.Sqrt(squared / samples.Length);
        }
    }
}
