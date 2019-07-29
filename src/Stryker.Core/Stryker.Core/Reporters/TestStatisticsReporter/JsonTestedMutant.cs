using System.Collections.Generic;
using Stryker.Core.Mutants;
using Stryker.Core.Reporters.Json;

namespace Stryker.Core.Reporters
{
    public class JsonTestedMutant
    {
        public int Id { get; set; }
        public string Replacement { get; set; }
        public JsonMutantLocation Location { get; set; }
        public MutantStatus Status { get; set; }
        public IDictionary<string, bool> Tests { get; set; }
    }
}
