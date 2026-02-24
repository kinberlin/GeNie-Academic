using Smile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeNie
{
    public class SmileNodeTypeDetector
    {
        public int Decision { get; private set; } = -1;
        public int Utility { get; private set; } = -1;
        public int Chance { get; private set; } = -1;

        /// <summary>
        /// Auto-detects node type integers by scanning all nodes and
        /// using SMILE structural clues. No hardcoding, no prior knowledge
        /// of node names required. Works with any model.
        /// </summary>
        public void AutoDetect(Network net)
        {
            // Group all nodes by their raw type integer
            var typeToNodes = new Dictionary<int, List<int>>();
            for (int h = net.GetFirstNode(); h >= 0; h = net.GetNextNode(h))
            {
                int t = (int)net.GetNodeType(h);
                if (!typeToNodes.ContainsKey(t))
                    typeToNodes[t] = new List<int>();
                typeToNodes[t].Add(h);
            }

            // For each distinct type, probe a sample node to classify it
            foreach (var kv in typeToNodes)
            {
                int typeInt = kv.Key;
                int sampleNode = kv.Value[0];

                NodeCharacter character = Characterize(net, sampleNode);

                switch (character)
                {
                    case NodeCharacter.IsUtility:
                        Utility = typeInt;
                        break;
                    case NodeCharacter.IsDecision:
                        Decision = typeInt;
                        break;
                    case NodeCharacter.IsChance:
                        Chance = typeInt;
                        break;
                }
            }

            Console.WriteLine($"[AutoDetect] Decision={Decision}, Chance={Chance}, Utility={Utility}");

            if (Decision < 0) Console.WriteLine("[AutoDetect] WARNING: No Decision nodes found.");
            if (Utility < 0) Console.WriteLine("[AutoDetect] WARNING: No Utility nodes found.");
            if (Chance < 0) Console.WriteLine("[AutoDetect] WARNING: No Chance nodes found.");
        }

        private enum NodeCharacter { IsChance, IsDecision, IsUtility, Unknown }

        private NodeCharacter Characterize(Network net, int handle)
        {
            int outcomeCount = net.GetOutcomeCount(handle);
            double[] def = null;
            try { def = net.GetNodeDefinition(handle); } catch { }

            // Utility nodes return 0 or negative for outcome count
            if (outcomeCount <= 0)
                return NodeCharacter.IsUtility;

            if (def == null || def.Length == 0)
                return NodeCharacter.IsDecision;

            return NodeCharacter.IsChance;
        }
    }
}
