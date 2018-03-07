using System;
using NAudio.Wave;

namespace AutoHarmony.Models
{
    public class MemoryWaveProvider : IWaveProvider
    {
        private readonly byte[] _buffer;
        private readonly int _endIndex;
        private int _offset;

        public MemoryWaveProvider(byte[] buffer, int offset, int count, WaveFormat waveFormat)
        {
            this._buffer = buffer;
            this._endIndex = offset + count;
            this._offset = offset;
            this.WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            count = Math.Min(this._endIndex - this._offset, count);
            Buffer.BlockCopy(this._buffer, this._offset, buffer, offset, count);
            return count;
        }
    }
}
