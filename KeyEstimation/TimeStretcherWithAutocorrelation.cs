#define LOG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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

#if LOG
                using (var writer = CreateLogWriter($"section{sections.Count}.txt"))
                {
                    for (var i = 0; i < delay; i++)
                        writer.WriteLine("{0} {1}", i, span[i]);
                }
#endif

                sections.Add(new Section(start, delay));
                start += delay;
            }

            if (sections.Count == 0)
            {
                // 逃げ
                throw new Exception("区切りが検出できませんでした。 input が小さすぎます。");
            }

            // 重ね合わせる関係上、各セクションの長さを半分として考えたいが、
            // 半分として考えると小数になるので、逆に 2 倍必要ってことにしちゃう
            // 最後のセクションの終わりから、 input の終わりまでの間をねじ込みたいので、その分は neededLength から削る
            var lastSection = sections[sections.Count - 1];
            var neededLength = (length /*- (input.Length - (lastSection.Start + lastSection.Length))*/) * 2;

            var sumOfSectionLength = sections.Sum(x => x.Length);
            // 各セクションを順番に並べるだけで良い回数
            var firstFillCount = neededLength / sumOfSectionLength;

            var filledTime = sumOfSectionLength * firstFillCount;

            // 最後のセクションは長さを 2 倍として考える必要がある
            // （他は重ね合わせるから半分として考えているが、最後のセクションはまるまる 1 個分使うので）
            // すべてのセクションを使う場合は、最後のセクションが確定しているのでこの時点で filledTime に加えておく
            var useAllSections = firstFillCount > 0;
            if (useAllSections)
            {
                filledTime += sections[sections.Count - 1].Length;

                if (filledTime > neededLength)
                {
                    // 足したらオーバーしてしまった場合 1 回分減らす
                    if (firstFillCount == 1)
                    {
                        firstFillCount = 0;
                        filledTime = 0;
                    }
                    else
                    {
                        firstFillCount--;
                        filledTime -= sumOfSectionLength;
                    }
                }
            }

            var fillCount = new int[sections.Count];
            for (var i = 0; i < fillCount.Length; i++)
                fillCount[i] = firstFillCount;

            // 残りの時間の埋め方を探す
            foreach (var i in FillRemainingTime(sections, neededLength - filledTime))
                fillCount[i]++;

            // 更新された fillCount を使って、実際に埋められた時間を計算
            filledTime = 0;
            for (var i = 0; i < fillCount.Length; i++)
                filledTime += sections[i].Length * fillCount[i];

            if (useAllSections)
            {
                // 最後のセクションはわかりきってる
                filledTime += sections[sections.Count - 1].Length;
            }
            else
            {
                // すべてのセクションを使わない場合は、ここで最後のセクションを 2 倍にすることを考慮する必要がある
                var lastUsedSection = Array.FindLastIndex(fillCount, IsSectionUsed);

                if (lastUsedSection < 0)
                {
                    // 1個も使われてない → ないとは思うが一応
                    filledTime = 0;
                }
                else
                {
                    // useAllSections が false なので、すべてのセクションの使用回数は高々 1 回
                    Debug.Assert(fillCount[lastUsedSection] == 1);

                    // 実際に使われる最後のセクションを考慮して埋めた時間を求めてみる
                    var newFilledTime = filledTime + sections[lastUsedSection].Length;

                    if (newFilledTime > neededLength)
                    {
                        // 時間をオーバーしているので、オーバー分をなくすのにちょうど良い 1 個を削る

                        var prevSection = lastUsedSection == 0 ? -1
                            : Array.FindLastIndex(fillCount, lastUsedSection - 1, IsSectionUsed);
                        if (prevSection < 0)
                        {
                            // セクションが 1 個しかない → 全部消すしかないじゃん
                            fillCount[lastUsedSection] = 0;
                            filledTime = 0;
                        }
                        else
                        {
                            var lastUsedSectionLength = sections[lastUsedSection].Length;

                            // 最初の候補: 最後のセクションを削除してみる
                            var sectionToRemove = lastUsedSection;
                            newFilledTime = filledTime - lastUsedSectionLength + sections[prevSection].Length;

                            // 他の候補で newFilledTime を更新していく
                            for (var sectionIndex = prevSection; sectionIndex >= 0; sectionIndex--)
                            {
                                if (fillCount[sectionIndex] > 0)
                                {
                                    Debug.Assert(fillCount[sectionIndex] == 1);

                                    // sectionIndex のセクションを削除してみる
                                    var t = filledTime - sections[sectionIndex].Length + lastUsedSectionLength;

                                    // 最後のセクションを削除しても neededLength を超えているパターンはありえなくはない…？
                                    if (newFilledTime > neededLength || (t > newFilledTime && t <= neededLength))
                                    {
                                        // より良い候補
                                        newFilledTime = t;
                                        sectionToRemove = sectionIndex;
                                    }
                                }
                            }

                            fillCount[sectionToRemove] = 0;
                        }
                    }

                    filledTime = newFilledTime;
                }
            }

            Debug.WriteLine("filledTime: {0} / {1} ({2})", filledTime, neededLength, filledTime.CompareTo(neededLength));

            // 波形を作っていく
            var output = new float[length];
            var startIndex = 0;
            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var fc = fillCount[sectionIndex];
                if (fc > 0)
                {
                    var section = sections[sectionIndex];
                    var span = input.Slice(section.Start, section.Length);

                    for (var i = 0; i < fc; i++)
                    {
#if LOG
                        using (var writer = CreateLogWriter($"{sectionIndex}_{i}.txt"))
#endif
                        {
                            for (var j = 0; j < span.Length; j++)
                            {
                                var outputIndex = startIndex + j;
                                // 最後出ないセクションがクソデカい場合、収まらないサンプルもあるかも
                                if (outputIndex >= output.Length) break;
                                output[outputIndex] += span[j] * HannWindow(j, span.Length);

#if LOG
                                writer.WriteLine("{0} {1}", outputIndex, span[j] * HannWindow(j, span.Length));
#endif
                            }
                        }

                        startIndex += section.Length / 2;
                    }
                }
            }

            // 最初～最初のセクションの半分 を input の最初を使って埋める
            var firstUsedSection = Array.FindIndex(fillCount, IsSectionUsed);
            var leftFillLength = firstUsedSection >= 0
                ? sections[firstUsedSection].Length / 2
                : length; // 1個も合成できないときは、全体を使う
#if LOG
            using (var writer = CreateLogWriter("start.txt"))
#endif
            {
                for (var i = 0; i < leftFillLength; i++)
                {
                    // 窓関数の右半分を使う
                    var w = HannWindow(leftFillLength + i, leftFillLength * 2);
                    output[i] += input[i] * w;

#if LOG
                    writer.WriteLine("{0} {1}", i, input[i] * w);
#endif
                }
            }

            // 最後のセクションの半分(startIndex)～最後を input の最後を使って埋める
            var rightFillLength = length - startIndex;
            var inputStart = input.Length - rightFillLength;
#if LOG
            using (var writer = CreateLogWriter("end.txt"))
#endif
            {
                for (var i = 0; i < rightFillLength; i++)
                {
                    // 窓関数の左半分を使う
                    var w = HannWindow(i, rightFillLength * 2);
                    output[startIndex + i] += input[inputStart + i] * w;

#if LOG
                    writer.WriteLine("{0} {1}", startIndex + i, input[inputStart + i] * w);
#endif
                }
            }

            return output;

            bool IsSectionUsed(int x) => x > 0;
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

        /// <summary>
        /// 時間 <paramref name="length"/> をセクションで埋める
        /// </summary>
        /// <returns>使用するセクションのインデックス</returns>
        private static IEnumerable<int> FillRemainingTime(IReadOnlyList<Section> sections, int length)
        {
            // インデックス = 時間
            // この時間分だけ埋めるときの埋め方を記録
            var times = new SingleLinkedList<int>[length + 1];

            var sectionCount = sections.Count;
            for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
            {
                var sectionLength = sections[sectionIndex].Length;

                // time に到達する埋め方からつなげるパターン
                for (var time = 1; ; time++)
                {
                    var to = time + sectionLength;
                    if (to > length) break; // length をオーバーするなら終わり

                    var list = times[time];

                    if (list != null // time に到達するルートは存在する
                        && list.Value != sectionIndex // このセクションを含んでいない
                        && times[to] == null) // to に到達するルートはまだ見つかっていない
                    {
                        times[to] = new SingleLinkedList<int>(sectionIndex, list);
                    }
                }

                // このセクションだけを使った場合
                if (sectionLength <= length && times[sectionLength] == null)
                    times[sectionLength] = new SingleLinkedList<int>(sectionIndex, null);
            }

            // 最大時間を埋められる組み合わせを出力
            for (var i = length; i >= 0; i--)
            {
                var list = times[i];
                if (list != null)
                {
                    do
                    {
                        yield return list.Value;
                        list = list.Next;
                    } while (list != null);
                    yield break;
                }
            }
        }

        private static StreamWriter CreateLogWriter(string name)
        {
            Directory.CreateDirectory("log");
            return new StreamWriter(Path.Combine("log", name), false, new UTF8Encoding(false));
        }

        /// <summary>
        /// 自己相関で区切ったやつ（セクションとか読んでる（良い名前～～））
        /// </summary>
        private struct Section
        {
            public int Start { get; }
            public int Length { get; }

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
