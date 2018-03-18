using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace KeyEstimation
{
    public class TimeStretcherWithAutocorrelation2
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

        public TimeStretcherWithAutocorrelation2(int minPitch, int maxPitch, int templateSize)
        {
            this._minPitch = minPitch;
            this._maxPitch = maxPitch;
            this._templateSize = templateSize;
        }

        public float[] Stretch(ReadOnlySpan<float> input, int length)
        {
            if (input.Length == length) return input.ToArray();

            // 自己相関が大きいキリのいい区切りを検出する
            var sections = new List<Section>();

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

                var section = new Section(start, delay);

                // オーバーラップに支障が出るのでなし
                if (section.End + section.Length / 2 > input.Length)
                    break;

                sections.Add(section);
                start += delay;
            }

            if (sections.Count == 0)
            {
                // 逃げ
                throw new Exception("区切りが検出できませんでした。 input が小さすぎます。");
            }

            // 最後のセクションの終わりから、 input の終わりまでの間をねじ込みたいので、その分は neededLength から削る
            var lastSection = sections[sections.Count - 1];
            var neededLength = length - (input.Length - lastSection.End);

            var sumOfSectionLength = sections.Sum(x => x.Length);
            // 各セクションを順番に並べるだけで良い回数
            var firstFillCount = neededLength / sumOfSectionLength;

            var fillCount = new int[sections.Count];
            for (var i = 0; i < fillCount.Length; i++)
                fillCount[i] = firstFillCount;

            // 残りの時間の埋め方を探す
            var filledTime = sumOfSectionLength * firstFillCount;
            foreach (var i in FillRemainingTime(sections, neededLength - filledTime, length - filledTime))
                fillCount[i]++;

            // 波形を作っていく
            var output = new float[length];
            var lastCopiedSection = -1;
            filledTime = 0;
            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var fc = fillCount[sectionIndex];
                if (fc > 0)
                {
                    var section = sections[sectionIndex];
                    var span = input.Slice(section.Start, section.Length);

                    for (var i = 0; i < fc; i++)
                    {
                        if (lastCopiedSection == sectionIndex - 1)
                        {
                            // 前回コピーしたセクションが 1 個前なら、オーバーラップする必要なく、そのまま書き込むだけ
                            span.CopyTo(new Span<float>(output, filledTime, section.Length));
                        }
                        else
                        {
                            // 前回コピーしたセクションの続きとオーバーラップする
                            var prevSection = sections[lastCopiedSection];
                            var prevSectionEnd = prevSection.Start + prevSection.Length;

                            // input が足りない場合があるので、考慮する
                            var overlapLength = Math.Min(section.Length, input.Length - prevSectionEnd);

                            if (overlapLength < section.Length)
                                Debug.WriteLine("overlapLength: {0} / {1}", overlapLength, section.Length);

                            for (var j = 0; j < overlapLength; j++)
                            {
                                var rate = (float)j / (section.Length - 1);
                                output[filledTime + j] = span[j] * rate + input[prevSectionEnd + j] * (1f - rate);
                            }

                            // オーバーラップする必要がない部分はそのままコピー
                            if (overlapLength < section.Length)
                                span.Slice(overlapLength).CopyTo(new Span<float>(output, filledTime + overlapLength, section.Length - overlapLength));
                        }

                        lastCopiedSection = sectionIndex;
                        filledTime += section.Length;
                    }
                }
            }

            Debug.WriteLine("filledTime: {0} / {1} ({2})", filledTime, neededLength, filledTime.CompareTo(neededLength));

            // 残りの部分を input の最後を使って埋める
            var remainingSamples = length - filledTime;
            var inputRemainingStart = input.Length - remainingSamples;
            input.Slice(inputRemainingStart, remainingSamples)
                .CopyTo(new Span<float>(output, filledTime, remainingSamples));

            // 残っている時間が、セクション検出の残りの時間と一致していて、かつ最後のセクションが使用されているならこれで終わり
            // そうでないならオーバーラップしてあげる必要がある
            if (remainingSamples == input.Length - lastSection.End
                && fillCount[fillCount.Length - 1] > 0)
            {
                Debug.WriteLine("完全一致！");
            }
            else
            {
                var lastUsedSection = Array.FindLastIndex(fillCount, x => x > 0);
                var lastUsedSectionLength = sections[lastUsedSection].Length;

                var inputStart = inputRemainingStart - lastUsedSectionLength;
                var overlapLength = lastUsedSectionLength;
                if (inputStart < 0)
                {
                    // input が足りなかったら 0 からスタート
                    overlapLength += inputStart;
                    inputStart = 0;
                }

                var startTime = filledTime - overlapLength;
                for (var i = 0; i < overlapLength; i++)
                {
                    var rate = (float)i / (overlapLength - 1);
                    output[startTime + i] = input[inputStart + i] * rate + output[startTime + i] * (1f - rate);
                }
            }

            return output;
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

        /// <summary>
        /// できるだけ <paramref name="targetLength"/> に近くなるように、使うセクションを決める
        /// </summary>
        /// <returns>使用するセクションのインデックス</returns>
        private static IEnumerable<int> FillRemainingTime(IReadOnlyList<Section> sections, int targetLength, int maxLength)
        {
            // インデックス = 時間
            // この時間分だけ埋めるときの埋め方を記録
            var times = new SingleLinkedList<int>[maxLength + 1];

            var sectionCount = sections.Count;
            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                var sectionLength = sections[sectionIndex].Length;

                // time に到達する埋め方からつなげるパターン
                for (var time = 1; ; time++)
                {
                    var to = time + sectionLength;
                    if (to > maxLength) break; // length をオーバーするなら終わり

                    var list = times[time];

                    if (list != null // time に到達するルートは存在する
                        && list.Value != sectionIndex // このセクションを含んでいない
                        && times[to] == null) // to に到達するルートはまだ見つかっていない
                    {
                        times[to] = new SingleLinkedList<int>(sectionIndex, list);
                    }
                }

                // このセクションだけを使った場合
                if (sectionLength <= maxLength && times[sectionLength] == null)
                    times[sectionLength] = new SingleLinkedList<int>(sectionIndex, null);
            }

            // targetLength に一番近い組み合わせを出力
            for (var i = 0; ; i++)
            {
                var leftTime = targetLength - i;
                var rightTime = targetLength + i;

                var canUseLeft = leftTime >= 0;
                var canUseRight = rightTime <= maxLength;

                if (!canUseLeft && !canUseRight) break;

                SingleLinkedList<int> list = null;
                if (canUseLeft) list = times[leftTime];
                if (list == null && canUseRight) list = times[rightTime];

                if (list != null)
                {
                    do
                    {
                        yield return list.Value;
                        list = list.Next;
                    } while (list != null);
                    break;
                }
            }
        }

        /// <summary>
        /// 自己相関で区切ったやつ（セクションとか読んでる（良い名前～～））
        /// </summary>
        private struct Section
        {
            public int Start { get; }
            public int Length { get; }
            public int End => this.Start + this.Length;

            public Section(int start, int length)
            {
                this.Start = start;
                this.Length = length;
            }
        }

        private class SingleLinkedList<T>
        {
            public T Value { get; }
            public SingleLinkedList<T> Next { get; }

            public SingleLinkedList(T value, SingleLinkedList<T> next)
            {
                this.Value = value;
                this.Next = next;
            }
        }
    }
}
