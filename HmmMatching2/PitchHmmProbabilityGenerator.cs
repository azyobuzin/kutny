using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HmmMatching
{
    public class PitchHmmProbabilityGenerator
    {
        private readonly ImmutableArray<UtauNote> _notes;

        public PitchHmmProbabilityGenerator(ImmutableArray<UtauNote> notes)
        {
            this._notes = notes;
        }

        // TODO
    }

    public delegate ProbabilityGenerationResult ProbabilityGenerator(ProbabilityGenerationContext context);
}
