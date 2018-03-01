using System;
using System.Collections.Generic;

namespace HmmMatching
{
    public static class ProbabilityGenerators
    {
        /// <summary>
        /// 自己ループの確率（音程変化の判定が過剰に反応してしまった場合）
        /// </summary>
        public static IEnumerable<ProbabilityGenerationResult> SelfLoop(ProbabilityGenerationContext context)
        {
            if (context.TargetNoteNode == null)
            {
                // スタート状態
                yield return ProbabilityGenerationResult.CreateToStartState(0.8, false);
            }
            else
            {
                yield return new ProbabilityGenerationResult(context.TargetNoteNode.Value.Index, 0.33, false);
            }
        }

        /// <summary>
        /// 歌唱をやめてスタートに戻る確率
        /// </summary>
        public static IEnumerable<ProbabilityGenerationResult> StopSinging(ProbabilityGenerationContext context)
        {
            if (context.TargetNoteNode?.Next == null) yield break; // 最後のノートからスタートへの移動は後で

            yield return ProbabilityGenerationResult.CreateToStartState(0.05, false);
        }

        /// <summary>
        /// 小節の最初に戻る確率
        /// </summary>
        public static IEnumerable<ProbabilityGenerationResult> MoveToFirstNoteInMeasure(ProbabilityGenerationContext context)
        {
            if (context.TargetNoteNode?.Next == null) yield break; // スタート状態または最後のノートならいらない

            var note = context.TargetNoteNode.Value;
            var measureStartPosition = note.Position - note.Position % 1920;

            var n = context.TargetNoteNode;
            var firstStateInMeasure = n;
            while ((n = n.Previous)?.Value.Position >= measureStartPosition)
            {
                if (!n.Value.IsRestNote) firstStateInMeasure = n;
            }

            if (firstStateInMeasure.Value.Position < note.Position)
                yield return new ProbabilityGenerationResult(firstStateInMeasure.Value.Index, 0.05, true);
        }

        /// <summary>
        /// 1 小節前の最初に戻る確率
        /// </summary>
        public static IEnumerable<ProbabilityGenerationResult> MoveToFirstNoteInPreviousMeasure(ProbabilityGenerationContext context)
        {
            if (context.TargetNoteNode?.Next == null) yield break; // スタート状態または最後のノートならいらない

            var note = context.TargetNoteNode.Value;
            var measureStartPosition = note.Position - note.Position % 1920;
            var previousMeasureStartPosition = measureStartPosition - 1920;

            var n = context.TargetNoteNode;
            var firstStateInPreviousMeasure = n;
            while ((n = n.Previous)?.Value.Position >= measureStartPosition)
            {
                if (!n.Value.IsRestNote) firstStateInPreviousMeasure = n;
            }

            if (firstStateInPreviousMeasure.Value.Position < measureStartPosition)
                yield return new ProbabilityGenerationResult(firstStateInPreviousMeasure.Value.Index, 0.05, true);
        }

        /// <summary>
        /// 先のノートに移動する確率
        /// </summary>
        public static IEnumerable<ProbabilityGenerationResult> MoveToForwardNotes(ProbabilityGenerationContext context)
        {
            var next = context.TargetNoteNode == null ? context.Notes.First : context.TargetNoteNode.Next;
            if (next == null) yield break;

            // 2分音符の長さまでの範囲のノートに移動する
            const int maxSkipLength = 960;
            var targetNote = context.TargetNoteNode?.Value;
            var maxSkipPosition = targetNote != null
                ? targetNote.Position + targetNote.Length + maxSkipLength
                : next.Value.Position + maxSkipLength; // スタート状態からの場合は最初のノートの開始位置から

            var sentinel = next.Next;
            while (sentinel != null && sentinel.Value.Position < maxSkipPosition)
                sentinel = sentinel.Next;

            var totalLength = 0.0;
            var node = next;
            while (node != sentinel)
            {
                if (!node.Value.IsRestNote)
                    totalLength += VirtualLength(node);
                node = node.Next;
            }

            node = next;
            while (node != sentinel)
            {
                var n = node.Value;

                if (!n.IsRestNote)
                {
                    var p = context.RemainingProbability * VirtualLength(node) / totalLength;

                    if (context.TargetNoteNode == null)
                    {
                        // スタート状態からの遷移なら、無音状態を経由することはない
                        yield return new ProbabilityGenerationResult(n.Index, p, false);
                    }
                    else
                    {
                        var viaNoSoundProbability = node.Previous?.Value is UtauNote prevNote && prevNote.IsRestNote
                            ? ProbabilityOfNoSoundWhenRestNote(prevNote)
                            : ProbabilityOfNoSoundAfter(n);

                        yield return new ProbabilityGenerationResult(n.Index, (1.0 - viaNoSoundProbability) * p, false);
                        yield return new ProbabilityGenerationResult(n.Index, viaNoSoundProbability * p, true);
                    }
                }

                node = node.Next;
            }

            double VirtualLength(LinkedListNode<UtauNote> noteNode)
            {
                double l = Math.Min(noteNode.Value.Position + noteNode.Value.Length, maxSkipPosition) - noteNode.Value.Position;

                // 前のノートと同じ高さなら、認識しにくいので、本来よりも短いものとして認識する
                if (noteNode.Previous?.Value.NoteNumber == noteNode.Value.NoteNumber)
                    l *= 0.8;

                // next から離れているほど確率を下げる
                var n = next;
                while (n != noteNode)
                {
                    l *= 0.5;
                    n = n.Next;
                }

                return l;
            }
        }

        /// <summary>
        /// <paramref name="prevNote"/> のあとに無音状態になる確率
        /// </summary>
        private static double ProbabilityOfNoSoundAfter(UtauNote prevNote)
        {
            // ノートが長いほど、そのあとは休みがち
            const int minLength = 480;
            const double minProbability = 0.05;
            const double maxProbability = 0.2;

            // 短いときはほとんど休まない
            if (prevNote.Length <= minLength) return minProbability;

            // 長いほど確率が上がり、全音符なら 0.2
            return Math.Min(
                minProbability + (prevNote.Length - minLength) * ((maxProbability - minProbability) / (1920 - minLength)),
                maxProbability
            );
        }

        /// <summary>
        /// <paramref name="restNote"/> を通過するときに無音状態になる確率
        /// </summary>
        private static double ProbabilityOfNoSoundWhenRestNote(UtauNote restNote)
        {
            if (!restNote.IsRestNote) throw new ArgumentException();

            // 長さ 720 程度で 0.65 に到達するくらいの確率
            const int maxLength = 720;
            const double minProbability = 0.3;
            const double maxProbability = 0.65;
            return Math.Min(
                minProbability + ((maxProbability - minProbability) / maxLength) * restNote.Length,
                maxProbability
            );
        }

        public static IEnumerable<ProbabilityGenerationResult> MoveToStartFromLastNote(ProbabilityGenerationContext context)
        {
            if (context.TargetNoteNode == null || context.TargetNoteNode.Next != null) yield break;
            yield return ProbabilityGenerationResult.CreateToStartState(context.RemainingProbability, false);
        }
    }
}
