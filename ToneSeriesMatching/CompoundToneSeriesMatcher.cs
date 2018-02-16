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
        private int _viewCenterIndex;

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

            if (this._template.Count <= 1) return;

            if (this.CurrentNoteIndex < this.Score.Length - 1 &&
                PitchDifference(unit.NormalizedPitch, this.Score[this.CurrentNoteIndex + 1].NoteNumber) < 1.0)
            {
                // 予想通り次の音が来たならショートカット
                var i = this.CurrentNoteIndex + 1;
                this.CurrentNoteIndex = i;
                this._viewCenterIndex = i;
            }
            else
            {
                this.Match();
            }
        }

        private void Match()
        {
            var templateWidth = this._template.Count;
            var (startInclusive, endExclusive) = this.CreateView(this._viewCenterIndex, templateWidth);

            const double diffThreshold = 0.5;

            var bestScore = double.NegativeInfinity;
            var bestIndex = 0;
            //var bestMatchPositionScore = double.NegativeInfinity; // 新しいイベントほど重みが大きい
            //var bestLatestEventMatched = false;

            for (var i = startInclusive; i < endExclusive; i++)
            {
                var noteIndex = i + templateWidth - 1;

                var score = 0.0;
                //var matchPositionScore = 0.0;

                for (var j = 0; j < templateWidth; j++)
                {
                    var s = DifferenceScore(this._template[j].NormalizedPitch, this.Score[i + j].NoteNumber);
                    score += s;
                    //matchPositionScore += s * Math.Exp((j - templateWidth + 1) / 2.0);
                }

                //var latestEventMatched = PitchDifference(this._template[templateWidth - 1].NormalizedPitch, this.Score[noteIndex].NoteNumber) < 0.6;

                // スコアで比較
                var scoreDiff = score - bestScore;
                var isBest = scoreDiff > diffThreshold;

                if (!isBest && scoreDiff >= -diffThreshold)
                {
                    isBest = //matchPositionScore > bestMatchPositionScore // 新しいイベントがマッチしてるか
                        //(latestEventMatched && !bestLatestEventMatched)
                        /*||*/ Math.Abs(noteIndex - this.CurrentNoteIndex) <= Math.Abs(bestIndex - this.CurrentNoteIndex); // 現在の認識位置に近いほう
                }

                if (isBest)
                {
                    bestScore = score;
                    bestIndex = noteIndex;
                    //bestMatchPositionScore = matchPositionScore;
                    //bestLatestEventMatched = latestEventMatched;
                }
            }

            if (bestIndex != this.CurrentNoteIndex)
            {
                this.CurrentNoteIndex = bestIndex;
                this._viewCenterIndex = bestIndex;
            }
            else
            {
                // 移動していないので単イベントマッチングへ
                SingleEventMatch();
            }
        }

        private void SingleEventMatch()
        {
            var pitch = this._template[this._template.Count - 1].NormalizedPitch;
            int index;

            for (var i = 1; ; i++)
            {
                index = this.CurrentNoteIndex + i;
                var cond1 = index < this.Score.Length;
                if (cond1)
                {
                    if (PitchDifference(pitch, this.Score[index].NoteNumber) < 1.0)
                        break;
                }

                index = this.CurrentNoteIndex - i;
                if (index >= 0)
                {
                    if (PitchDifference(pitch, this.Score[index].NoteNumber) < 1.0)
                        break;
                }
                else if (!cond1)
                {
                    return; // 全範囲を検索しつくしてしまった
                }
            }

            this.CurrentNoteIndex = index;
            // 単イベントマッチングではビューの中心を更新しない
        }

        private static double PitchDifference(double normalizedPitch, int noteNumber)
        {
            return Math.Min(
                Math.Abs(noteNumber - normalizedPitch),
                noteNumber + 12 - normalizedPitch
            );
        }

        private static double DifferenceScore(double normalizedPitch, int noteNumber)
        {
            // 差が大きいほど値は小さく、差がなければ 1
            return Math.Exp(-PitchDifference(normalizedPitch, noteNumber));
        }

        /// <summary>
        /// 探索範囲を決定する
        /// </summary>
        private (int, int) CreateView(int centerIndex, int templateWidth)
        {
            // 現在演奏中の小節の前後 2 小節 → とりあえず 1920 * 2.5 戻ってみる
            int start1, end1;
            {
                const int searchWidth = 4800;

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

            int start, end;
            if (end1 - start1 >= end2 - start2)
            {
                start = start1;
                end = end1;
            }
            else
            {
                start = start2;
                end = end2;
            }

            // テンプレート幅分を考慮してループ場所を考える
            return (Math.Max(0, start - templateWidth + 1), end - templateWidth);
        }
    }
}
