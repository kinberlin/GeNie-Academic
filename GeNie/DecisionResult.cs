using System;
using Smile;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GeNie.Program;

namespace GeNie
{
    public class DecisionResult
    {
        public List<DecisionPolicy> Policies { get; } = new List<DecisionPolicy>();
        public Dictionary<string, double> ExpectedUtilities { get; } = new Dictionary<string, double>();

        public void Print()
        {
            Console.WriteLine("\n=== OPTIMAL DECISION POLICIES ===");
            foreach (var p in Policies)
            {
                Console.WriteLine($"\nDecision Node: {p.NodeId}");
                Console.WriteLine($"  -> Optimal choice: [{p.OptimalState}]");
                Console.WriteLine("  Expected utility per state:");
                foreach (var kv in p.ExpectedUtilityPerState)
                    Console.WriteLine($"     {kv.Key}: {kv.Value:F4}");
            }

            Console.WriteLine("\n=== EXPECTED UTILITIES ===");
            foreach (var kv in ExpectedUtilities)
                Console.WriteLine($"  {kv.Key}: {kv.Value:F4}");
        }
    }

}
