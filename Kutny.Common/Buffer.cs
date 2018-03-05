using System;
using System.Collections;
using System.Collections.Generic;

namespace Kutny.Common
{
    /// <summary>
    /// 件数上限付きリングバッファ
    /// </summary>
    public class Buffer<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;
        private int _front;
        private int _rear;

        public int Count { get; private set; }
        public int Capacity => this._items.Length;

        public Buffer(int capacity)
        {
            this._items = new T[capacity];
        }

        public T this[int index]
        {
            get
            {
                if (index >= this.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return this._items[(this._front + index) % this.Capacity];
            }
        }

        public void Push(T value)
        {
            this._items[this._rear] = value;

            if (this.Count < this.Capacity)
            {
                this.Count++;
            }
            else
            {
                if (++this._front >= this.Capacity)
                    this._front = 0;
            }

            if (++this._rear >= this.Capacity)
                this._rear = 0;
        }

        public void Clear()
        {
            this.Count = 0;
            Array.Clear(this._items, 0, this._items.Length);
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < this.Count; i++)
                yield return this._items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
