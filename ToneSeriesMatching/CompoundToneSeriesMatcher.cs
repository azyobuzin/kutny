using System;
using System.Collections.Immutable;

namespace ToneSeriesMatching
{
    /// <summary>
    /// 複合音列法の実装
    /// </summary>
    public class CompoundToneSeriesMatcher
    {
        public ImmutableArray<UtauNote> Score { get; }
        private readonly Buffer<PitchUnit> _template;
        public int CurrentNoteIndex { get; private set; }

        public CompoundToneSeriesMatcher(ImmutableArray<UtauNote> score, int templateWidth)
        {
            if (score.Length < templateWidth)
                throw new ArgumentException(nameof(score) + " が短すぎます。");

            this.Score = score;
            this._template = new Buffer<PitchUnit>(templateWidth);
        }

        public int MaximumTemplateWidth => this._template.Capacity;

        public void InputPitch(PitchUnit unit)
        {
            this._template.Push(unit);

            if (this.CurrentNoteIndex < this.Score.Length - 1 &&
                Math.Abs(unit.NormalizedPitch - this.Score[this.CurrentNoteIndex + 1].NoteNumber) < 1.0)
            {
                // 予想通り次の音が来たならショートカット
                this.CurrentNoteIndex++;
            }
            else
            {
                this.Match();
            }
        }

        private void Match()
        {
            var (startInclusive, endExclusive) = this.CreateView(this.CurrentNoteIndex);
            var templateWidth = this._template.Count;
            endExclusive -= templateWidth; // テンプレートの要素数分引いて、範囲をオーバーしないようにする

            var matchCount = endExclusive - startInclusive;
            var matchResult = new(int, double)[matchCount];
            for (var i = 0; i < matchCount; i++)
            {
                var scoreIndex = startInclusive + i;

                var score = 0.0;
                for (var j = 0; j < templateWidth; j++)
                {
                    var diff = Math.Abs(this._template[j].NormalizedPitch - this.Score[scoreIndex + j].NoteNumber);
                    if (diff < 1.0)
                    {
                        // 差が 1 音以下なら、その差をスコアにする
                        score += 1.0 - diff;
                    }
                }

                matchResult[i] = (scoreIndex + templateWidth, score);
            }

            throw new NotImplementedException();
            // TODO: ソートする
            // 最大だけ分かればいいから、配列にしなくてよかったかも
            // あと、新しい入力がマッチするほどいいので、それ用の指標も
        }

        /// <summary>
        /// 探索範囲を決定する
        /// </summary>
        private (int, int) CreateView(int centerIndex)
        {
            // 現在演奏中の小節の前後 2 小節 → とりあえず 1920 * 3 戻ってみる
            int start1, end1;
            {
                const int searchWidth = 1920 * 3;

                if (centerIndex == 0)
                {
                    start1 = 0;
                }
                else
                {
                    var minPosition = this.Score[centerIndex].Position - searchWidth;
                    start1 = centerIndex;
                    while (start1 > 0 && this.Score[start1 - 1].Position > minPosition)
                        start1--;
                }

                if (centerIndex >= this.Score.Length - 1)
                {
                    end1 = this.Score.Length;
                }
                else
                {
                    var maxPosition = this.Score[centerIndex].Position + searchWidth;
                    end1 = centerIndex + 1;
                    while (end1 < this.Score.Length && this.Score[end1].Position < maxPosition)
                        end1++;
                }
            }

            // 前後テンプレート幅の 2 倍
            var start2 = Math.Max(centerIndex - this.MaximumTemplateWidth * 2, 0);
            var end2 = Math.Min(centerIndex + 1 + this.MaximumTemplateWidth * 2, this.Score.Length);

            return end1 - start1 >= end2 - start2
                ? (start1, end1)
                : (start2, end2);
        }
    }
}
