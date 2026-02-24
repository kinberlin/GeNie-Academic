using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeNie
{
    public class DecisionPolicy
    {
        public string NodeId { get; set; }
        public string OptimalState { get; set; }
        public int OptimalStateIndex { get; set; }
        public Dictionary<string, double> ExpectedUtilityPerState { get; set; }
    }
}
