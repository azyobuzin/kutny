using System;

namespace AutoHarmony.Models
{
    public class SampleBuffer
    {
        private float[] _buffer = Array.Empty<float>();
        private int _count;
        private readonly object _lockObj = new object();

        public bool Read(int count, Action<ArraySegment<float>> action)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            lock (this._lockObj)
            {
                if (count > this._count) return false;

                action(new ArraySegment<float>(this._buffer, 0, count));

                // 読みだした分は捨てる
                Array.Copy(this._buffer, count, this._buffer, 0, this._count - count);
                this._count -= count;
            }

            return true;
        }

        // 本当は Span にしたかったが、 SampleProvider から直接書き込めないことに気づいた
        public void Write(int count, Action<ArraySegment<float>> action)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            lock (this._lockObj)
            {
                if (this._buffer.Length - this._count < count)
                {
                    Array.Resize(ref this._buffer, Math.Max(this._buffer.Length * 2, this._count + count));
                }

                action(new ArraySegment<float>(this._buffer, this._count, count));

                this._count += count;
            }
        }

        public void Clear()
        {
            lock (this._lockObj)
            {
                this._count = 0;
            }
        }
    }
}
