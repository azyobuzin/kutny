using System;
using KeyEstimation;
using NAudio.Wave;

namespace AutoHarmony.Models
{
    public class StftPitchShiftingSampleProvider : ISampleProvider
    {
        private const int WindowLength = 2048; // 最低 2048 ないと低音が消える

        private readonly ISampleProvider _baseProvider;
        private readonly PitchShifterWithStft _pitchShifter = new PitchShifterWithStft(WindowLength);
        private readonly float[] _pitchShifterInputbuffer = new float[WindowLength];
        private int _pitchShifterInputBufferWritten;
        private readonly float[] _outputBuffer = new float[WindowLength];
        private int _outputBufferWritten;

        public StftPitchShiftingSampleProvider(ISampleProvider provider)
        {
            this._baseProvider = provider;
        }

        public WaveFormat WaveFormat => this._baseProvider.WaveFormat;

        public float PitchFactor { get; set; } = 1f;

        public int Read(float[] buffer, int offset, int count)
        {
            var resultCount = 0;

            if (this._outputBufferWritten > 0)
            {
                resultCount = Math.Min(this._outputBufferWritten, count);
                Array.Copy(this._outputBuffer, 0, buffer, offset, resultCount);

                if (resultCount < this._outputBufferWritten)
                {
                    Array.Copy(this._outputBuffer, resultCount, this._outputBuffer, 0, this._outputBufferWritten - resultCount);
                    this._outputBufferWritten -= resultCount;
                    return resultCount;
                }
                else
                {
                    this._outputBufferWritten = 0;
                }
            }

            while (true)
            {
                while (this._pitchShifterInputBufferWritten < this._pitchShifterInputbuffer.Length)
                {
                    var readSamples = this._baseProvider.Read(
                        this._pitchShifterInputbuffer,
                        this._pitchShifterInputBufferWritten,
                        this._pitchShifterInputbuffer.Length - this._pitchShifterInputBufferWritten
                    );
                    if (readSamples == 0) return resultCount;
                    this._pitchShifterInputBufferWritten += readSamples;
                }

                this._pitchShifterInputBufferWritten = 0;

                var result = this._pitchShifter.InputFrame(this._pitchShifterInputbuffer, this.PitchFactor);
                if (result != null)
                {
                    var copyCount = Math.Min(result.Length, count - resultCount);
                    Array.Copy(result, 0, buffer, resultCount + offset, copyCount);

                    resultCount += copyCount;

                    if (copyCount < result.Length)
                    {
                        this._outputBufferWritten = result.Length - copyCount;
                        Array.Copy(result, copyCount, this._outputBuffer, 0, this._outputBufferWritten);
                    }

                    return resultCount;
                }
            }
        }
    }
}
