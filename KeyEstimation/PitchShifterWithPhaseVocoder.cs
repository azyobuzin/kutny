using System;
using System.Numerics;
using Accord.Audio.Windows;
using Accord.Math;
using Accord.Math.Transforms;

namespace KeyEstimation
{
    public class PitchShifterWithPhaseVocoder
    {
        private readonly int _windowLength;
        private bool _isFirstInput = true;
        private bool _isFirstVocoder = true;

        private readonly RaisedCosineWindow _window;
        private readonly Complex[] _fftBuffer;
        private readonly double[] _magnitudeSpectrum;
        private readonly double[] _phaseSpectrum;
        private readonly double[] _modifiedMagnitudeSpectrum;
        private readonly double[] _omega;
        private readonly double[] _lastPhase;
        private readonly double[] _targetPhase;

        private readonly float[] _inputBuffer;
        private readonly float[] _vocoderInputBuffer;
        private readonly float[] _vocoderOutputBuffer;
        private readonly float[] _nextOutputBuffer;

        public PitchShifterWithPhaseVocoder(int windowLength)
        {
            if (windowLength % 2 != 0) throw new ArgumentException();

            this._windowLength = windowLength;

            this._window = RaisedCosineWindow.Hann(windowLength);
            this._fftBuffer = new Complex[windowLength];
            this._magnitudeSpectrum = new double[windowLength];
            this._phaseSpectrum = new double[windowLength];
            this._modifiedMagnitudeSpectrum = new double[windowLength];

            var omega = new double[windowLength];
            for (var i = 0; i < omega.Length; i++)
                omega[i] = 2.0 * Math.PI * (windowLength / 2.0) * i / windowLength;
            this._omega = omega;

            this._lastPhase = new double[windowLength];
            this._targetPhase = new double[windowLength];

            this._inputBuffer = new float[windowLength / 2];
            this._vocoderInputBuffer = new float[windowLength];
            this._vocoderOutputBuffer = new float[windowLength];
            this._nextOutputBuffer = new float[windowLength];
        }

        public float[] InputFrame(ReadOnlySpan<float> input, double pitchRate)
        {
            if (input.Length != this._windowLength) throw new ArgumentException();

            var hop = this._windowLength / 2;

            if (this._isFirstInput)
            {
                input.CopyTo(this._vocoderInputBuffer);
                input.Slice(hop, hop).CopyTo(this._inputBuffer);

                this.VocoderInput(pitchRate);
                Array.Copy(this._vocoderOutputBuffer, this._nextOutputBuffer, this._windowLength);

                // 初回は出力しない
                this._isFirstInput = false;
                return null;
            }

            // 残りのバッファーと input の前半を使って中間の波形を作る
            Array.Copy(this._inputBuffer, this._vocoderInputBuffer, hop);
            input.Slice(0, hop).CopyTo(new Span<float>(this._vocoderInputBuffer, hop, hop));
            this.VocoderInput(pitchRate);

            // 戻り値を作成
            var result = new float[this._windowLength];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = this._nextOutputBuffer[i];

                // _vocoderOutputBuffer の前半だけ使う
                if (i >= hop)
                    result[i] += this._vocoderOutputBuffer[i - hop];
            }

            // 余りを _inputBuffer に退避
            Array.Copy(this._vocoderOutputBuffer, hop, this._inputBuffer, 0, hop);

            // input 全体で生成
            input.CopyTo(this._vocoderInputBuffer);
            this.VocoderInput(pitchRate);

            // _nextOutputBuffer に input 全体の結果を保存しておく
            for (var i = 0; i < this._nextOutputBuffer.Length; i++)
            {
                this._nextOutputBuffer[i] = this._vocoderOutputBuffer[i];

                // 余りを足し合わせる
                if (i < hop)
                    this._nextOutputBuffer[i] += this._inputBuffer[i];
            }

            // _inputBuffer に後半を詰めておく
            input.Slice(hop, hop).CopyTo(this._inputBuffer);

            return result;
        }

        private void VocoderInput(double pitchRate)
        {
            var hop = this._windowLength / 2;

            for (var i = 0; i < this._vocoderInputBuffer.Length; i++)
            {
                var destIndex = (hop + i) % this._windowLength;
                this._fftBuffer[destIndex] = this._vocoderInputBuffer[i] * this._window[i];
            }

            FourierTransform2.FFT(this._fftBuffer, FourierTransform.Direction.Forward);

            for (var i = 0; i < this._fftBuffer.Length; i++)
            {
                this._magnitudeSpectrum[i] = this._fftBuffer[i].Magnitude;
                this._phaseSpectrum[i] = this._fftBuffer[i].Phase;
            }

            // ピッチ変更
            //for (var i = 0; i < hop; i++)
            //{
            //    var pos = i / pitchRate;
            //    var a = (int)pos;
            //    double newMag;

            //    if (a >= this._windowLength)
            //    {
            //        // 範囲外なので諦める
            //        newMag = 0;
            //    }
            //    else if (a == this._windowLength - 1 || a == pos)
            //    {
            //        newMag = this._magnitudeSpectrum[a];
            //    }
            //    else
            //    {
            //        // 補間
            //        newMag = this._magnitudeSpectrum[a]
            //            + (this._magnitudeSpectrum[a + 1] - this._magnitudeSpectrum[a])
            //            * (pos - a);
            //    }

            //    this._modifiedMagnitudeSpectrum[i] = newMag;
            //    this._modifiedMagnitudeSpectrum[this._windowLength - i - 1] = newMag;
            //}
            Array.Copy(this._magnitudeSpectrum, this._modifiedMagnitudeSpectrum, this._windowLength);

            if (this._isFirstVocoder)
            {
                this._isFirstVocoder = false;

                // 初回
                for (var i = 0; i < this._phaseSpectrum.Length; i++)
                {
                    this._lastPhase[i] = this._phaseSpectrum[i];
                    this._targetPhase[i] = this._phaseSpectrum[i] + this._omega[i];
                }
            }
            else
            {
                for (var i = 0; i < this._windowLength; i++)
                {
                    var principleArg = this._phaseSpectrum[i] - this._targetPhase[i];
                    var unwrappedPhaseDiff = (principleArg + Math.PI) % (-2.0 * Math.PI) + Math.PI;
                    var instantaneousFreq = (this._omega[i] + unwrappedPhaseDiff) / hop;
                    this._lastPhase[i] += instantaneousFreq;
                    this._targetPhase[i] = this._phaseSpectrum[i] + this._omega[i];
                }
            }

            for (var i = 0; i < this._fftBuffer.Length; i++)
                this._fftBuffer[i] = Complex.FromPolarCoordinates(this._modifiedMagnitudeSpectrum[i], this._lastPhase[i]);

            FourierTransform2.FFT(this._fftBuffer, FourierTransform.Direction.Backward);

            for (var i = 0; i < this._vocoderOutputBuffer.Length; i++)
                this._vocoderOutputBuffer[i] = (float)this._fftBuffer[(hop + i) % this._windowLength].Real * this._window[i];
        }
    }
}
