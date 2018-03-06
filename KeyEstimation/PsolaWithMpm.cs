using System;

namespace KeyEstimation
{
    public class PsolaWithMpm
    {
        // 次のタイムストレッチに使うデータ
        private float[] _nextBuffer;

        /// <param name="input">入力信号</param>
        /// <param name="fundamentalDelay">基本周期</param>
        /// <param name="rate">長さを何倍にするか</param>
        /// <returns>長さ <c>FrameSize * rate</c> の配列</returns>
        public float[] Stretch(ReadOnlySpan<float> input, double? fundamentalDelay, double rate)
        {
            var expectedLength = input.Length * rate;
            var output = new float[(int)Math.Round(expectedLength)];

            // 前回からの残留分をコピー
            if (this._nextBuffer != null)
            {
                Array.Copy(this._nextBuffer, output, Math.Min(this._nextBuffer.Length, output.Length));
                this._nextBuffer = null;
            }

            if (!fundamentalDelay.HasValue)
            {
                // （ほとんどないとは思うけど）
                // 基本周期が見つけられなかったので、諦める
                // 残留分によって滑らかに無音になっているので、そのまま返す
                return output;
            }

            var iFundamentalDelay = (int)Math.Round(fundamentalDelay.Value);

            // ピッチマークから合成用の窓をつくる
            var pitchMarker = FindPitchMarker(input, iFundamentalDelay);
            var window = new float[iFundamentalDelay];
            for (var i = 0; i < iFundamentalDelay; i++)
            {
                var v = input[pitchMarker - iFundamentalDelay / 2 + i];
                // ハン窓
                var w = 0.5f - 0.5f * (float)Math.Cos((2.0 * Math.PI * i) / (iFundamentalDelay - 1));
                window[i] = v * w;
            }

            // output を窓で埋め尽くす
            this._nextBuffer = FillWithWindows(output, expectedLength, window);

            return output;
        }

        private static int FindPitchMarker(ReadOnlySpan<float> input, int fundamentalDelay)
        {
            // 中心付近の最大値をピッチマークとする
            var start = (input.Length - fundamentalDelay) / 2;
            if (start - fundamentalDelay / 2 < 0)
                start = fundamentalDelay / 2;

            var end = (input.Length + fundamentalDelay) / 2;
            if (end + fundamentalDelay / 2 > input.Length)
                end = input.Length - fundamentalDelay / 2;

            var maxIndex = input.Length / 2; // 1回もループしなかった場合は知るか
            var maxValue = float.NegativeInfinity;
            for (var i = start; i < end; i++)
            {
                var v = input[i];
                if (v > maxValue)
                {
                    maxIndex = i;
                    maxValue = v;
                }
            }

            return maxIndex;
        }

        /// <returns>次回で合成するデータ</returns>
        private static float[] FillWithWindows(float[] output, double expectedLength, float[] window)
        {
            var div = expectedLength / window.Length;

            // separationCount * 2 個の窓を使って埋める
            // 切り捨てをしているので、余りがあるときは密度が小さくなる
            var separationCount = (int)div;

            // 窓が入りきらないので諦める
            if (separationCount <= 0) return null;

            var separationWidth = expectedLength / separationCount;

            // 余りによって、本来の位置からずれる分
            var move = (separationWidth - window.Length) / 2.0;

            // 次への残留バッファを用意
            var nextBuffer = new float[window.Length];

            for (var i = 0; i < separationCount; i++)
            {
                // 窓 1つめ
                //CopyWindow((int)(separationWidth * i + move));

                // この窓の半分の位置からスタートする窓
                CopyWindow((int)(separationWidth * (i + 0.5) + move));
            }

            return nextBuffer;

            void CopyWindow(int left)
            {
                for (var i = 0; i < window.Length; i++)
                {
                    var outputIndex = left + i;

                    if (outputIndex < output.Length)
                    {
                        output[outputIndex] += window[i];
                    }
                    else
                    {
                        // output に入りきらなかったので次回へ
                        nextBuffer[outputIndex - output.Length] += window[i];
                    }
                }
            }
        }
    }
}
