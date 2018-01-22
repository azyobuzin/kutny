using System;
using System.Linq;
using Accord.Math;
using Accord.Neuro;
using Accord.Neuro.Learning;

namespace PitchDetector
{
    public class NeuralNetworkVowelClassifier : VowelClassifier
    {
        private const int OutputCount = (int)VowelType.Other + 1;
        private readonly ActivationNetwork _network = new ActivationNetwork(new BipolarSigmoidFunction(), 12, 20, 10, OutputCount);

        public override void Learn()
        {
            var inputs = new double[this.TrainingData.Count][];
            var outputs = new double[this.TrainingData.Count][];

            for (var i = 0; i < this.TrainingData.Count; i++)
            {
                var (x, y) = this.TrainingData[i];
                inputs[i] = x;

                var o = new double[OutputCount];
                for (var j = 0; j < o.Length; j++)
                    o[j] = j == (int)y ? 1 : -1;
                outputs[i] = o;
            }

            new NguyenWidrow(this._network).Randomize();
            var teacher = new ParallelResilientBackpropagationLearning(this._network);

            double error;
            var count = 0;
            do
            {
                error = teacher.RunEpoch(inputs, outputs);
                Console.WriteLine("{0}回目: {1}", ++count, error);
            } while (error > 1e-5);
        }

        public override VowelType Decide(double[] input)
        {
            this._network.Compute(input).Max(out var i);
            return (VowelType)i;
        }
    }
}
