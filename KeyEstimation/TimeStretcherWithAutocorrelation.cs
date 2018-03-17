using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeyEstimation
{
    public class TimeStretcherWithAutocorrelation
    {
        /// <summary>
        /// 周期として認識する最低サンプル数
        /// </summary>
        private readonly int _minPitch;

        /// <summary>
        /// 周期として認識する最大サンプル数
        /// </summary>
        private readonly int _maxPitch;

        /// <summary>
        /// 周期検出に使うサンプル数（大きくすると重そう）
        /// </summary>
        private readonly int _templateSize;

        public TimeStretcherWithAutocorrelation(int minPitch, int maxPitch, int templateSize)
        {
            this._minPitch = minPitch;
            this._maxPitch = maxPitch;
            this._templateSize = templateSize;
        }

        public float[] Stretch(ReadOnlySpan<float> input, int length)
        {
            if (input.Length == length) return input.ToArray();

            // 自己相関が大きいキリのいい区切りを検出する
            var sections = new List<(int start, int length)>();

            for (var start = 0; start + this._minPitch + this._templateSize < input.Length;)
            {
                var span = input.Slice(start);

                // 自己相関が最大になる点を探す
                var maxCorrelation = float.NegativeInfinity;
                var delay = this._minPitch;
                for (var lag = this._minPitch; lag <= this._maxPitch; lag++)
                {
                    if (lag + this._templateSize > span.Length) break;

                    var r = Autocorrelation(span, lag);
                    if (r > maxCorrelation)
                    {
                        maxCorrelation = r;
                        delay = lag;
                    }
                }

                // 最後の切れ端でキリがいい場所が見つからなかった場合、これ以上探さない
                if (start + this._maxPitch + this._templateSize >= input.Length)
                {
                    var squareSum = this.Autocorrelation(span, 0); // ずらしてないときの自己相関 = 二乗和
                    if (maxCorrelation / squareSum < 0.6f) // 大体合ってるときは 7, 8 割の値が出るはず（勘）
                        break;
                }

                sections.Add((start, delay));
                start += delay;
            }

            var sumOfSectionLength = sections.Sum(x => x.length);
            // 各セクションを順番に並べるだけで良い回数
            var firstFillCount = length * 2 / sumOfSectionLength; 
            var filledTime = sumOfSectionLength * firstFillCount;

            // TODO: ナップザック問題的に残り時間（length * 2 - filledTime）を埋める
            // TODO: 最初と最後をいい感じに埋める
        }

        /// <summary>
        /// <paramref name="span"/> から長さ <see cref="_templateSize"/> の信号を <paramref name="lag"/> だけずらしたときの自己相関
        /// </summary>
        private float Autocorrelation(ReadOnlySpan<float> span, int lag)
        {
            var r = 0f;
            for (var i = 0; i < this._templateSize; i++)
            {
                r += span[i] * span[i + lag];
            }
            return r;
        }

        private float HannWindow(int index, int length)
        {
            return 0.5f - 0.5f * (float)Math.Cos((2.0 * Math.PI * index) / (length - 1));
        }
    }
}
