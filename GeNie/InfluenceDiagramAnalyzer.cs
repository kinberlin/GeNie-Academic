using Smile;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static GeNie.Program;

namespace GeNie
{
    /// <summary>
    /// Wraps a GENIE Influence Diagram (SMILE) to compute optimal decision paths.
    /// Compatible with .NET Framework 4.8
    /// </summary>
    public class InfluenceDiagramAnalyzer
    {
        private readonly Smile.Network _net;
        private readonly SmileNodeTypeDetector _types;

        public InfluenceDiagramAnalyzer(string genieFilePath)
        {
            new Smile.License(
    "SMILE LICENSE e40743ca b7147f77 f31a5926 " +
    "THIS IS AN ACADEMIC LICENSE AND CAN BE USED " +
    "SOLELY FOR ACADEMIC RESEARCH AND TEACHING, " +
    "AS DEFINED IN THE BAYESFUSION ACADEMIC " +
    "SOFTWARE LICENSING AGREEMENT. " +
    "Serial #: 7avpy1jgkei4311nlgpq22rmr " +
    "Issued for: Anderson Tchamba (andersontchamba@gmail.com) " +
    "Academic institution: Universita del Piemonte Orientale " +
    "Valid until: 2026-08-27 " +
    "Issued by BayesFusion activation server",
    new byte[] {
    0x5c,0x3f,0xd5,0xa5,0x87,0x73,0x11,0x70,0x63,0xec,0x8d,0xc5,0xdb,0x85,0x5b,0x71,
    0x6b,0x9e,0x23,0xa5,0x86,0xac,0xa6,0x00,0xd3,0x78,0x18,0x98,0x3a,0xf6,0xbe,0xc2,
    0x63,0x96,0x67,0x9e,0x36,0x13,0x76,0x23,0x40,0x33,0xed,0x49,0x8f,0x12,0xb6,0x98,
    0xff,0x83,0x51,0x70,0x93,0xbb,0xc2,0xe0,0x7a,0xa1,0x7e,0x7d,0xdd,0x54,0x6d,0x7b
    }
);
            // Load your GeNIe model
            _net = new Smile.Network();
            _net.ReadFile(genieFilePath);

            // Auto-detect node types — no hardcoding, works with any model
            _types = new SmileNodeTypeDetector();
            _types.AutoDetect(_net);
        }

        /// <summary>
        /// Finds all decision nodes in the network.
        /// </summary>
        public List<int> GetDecisionNodes()
        {
            var list = new List<int>();
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Decision)
                    list.Add(h);
            return list;
        }

        /// <summary>
        /// Finds all utility nodes in the network.
        /// </summary>
        public List<int> GetUtilityNodes()
        {
            var list = new List<int>();
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Utility)
                    list.Add(h);
            return list;
        }

        /// <summary>
        /// Sets evidence on a chance node by state name.
        /// Call this BEFORE ComputeOptimalDecisions().
        /// </summary>
        public void SetEvidence(string nodeId, string stateName)
        {
            int handle = _net.GetNode(nodeId);
            if (handle < 0) throw new ArgumentException($"Node '{nodeId}' not found.");

            int stateIdx = GetStateIndexByName(_net, handle, stateName);
            if (stateIdx < 0) throw new ArgumentException($"State '{stateName}' not found in node '{nodeId}'.");

            _net.SetEvidence(handle, stateIdx);
            Console.WriteLine($"[Evidence] {nodeId} = {stateName}");
        }

        /// <summary>
        /// Clears all evidence from the network.
        /// </summary>
        public void ClearAllEvidence()
        {
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Chance)
                    _net.ClearEvidence(h);
        }
        private void ExtractDecisionPolicy(int dHandle, DecisionResult result)
        {
            string nodeId = _net.GetNodeId(dHandle);
            double[] values = _net.GetNodeValue(dHandle);
            string[] states = GetNodeStateNames(dHandle);

            if (values == null || values.Length == 0)
            {
                Console.WriteLine($"  [WARNING] No values for '{nodeId}', skipping.");
                return;
            }

            int stateCount = states.Length;

            // values may be stateCount * N where N = number of parent combinations
            // Layout is: [state0_parentCombo0, state1_parentCombo0, state0_parentCombo1, ...]
            // Aggregate by taking the average EU per state across all parent combinations
            double[] euPerState = new double[stateCount];
            int numCombinations = values.Length / stateCount;

            for (int combo = 0; combo < numCombinations; combo++)
                for (int s = 0; s < stateCount; s++)
                    euPerState[s] += values[combo * stateCount + s];

            for (int s = 0; s < stateCount; s++)
                euPerState[s] /= numCombinations;

            // Find best state
            int bestIdx = 0;
            double bestVal = double.NegativeInfinity;
            for (int i = 0; i < stateCount; i++)
                if (euPerState[i] > bestVal) { bestVal = euPerState[i]; bestIdx = i; }

            var policy = new DecisionPolicy
            {
                NodeId = nodeId,
                OptimalState = states[bestIdx],
                OptimalStateIndex = bestIdx,
                ExpectedUtilityPerState = new Dictionary<string, double>()
            };

            for (int i = 0; i < stateCount; i++)
                policy.ExpectedUtilityPerState[states[i]] = euPerState[i];

            result.Policies.Add(policy);
        }
        /// <summary>
        /// Runs SMILE inference and extracts the optimal decision policy and expected utilities.
        /// SMILE handles traversal order internally — you do NOT need to start from the utility node.
        /// </summary>
        public DecisionResult ComputeOptimalDecisions()
        {
            // This single call triggers full ID/LIMID inference
            _net.UpdateBeliefs();

            var result = new DecisionResult();

            foreach (int dHandle in GetDecisionNodes())
                ExtractDecisionPolicy(dHandle, result);

            // --- Extract total expected utility from utility nodes ---
            foreach (int uHandle in GetUtilityNodes())
            {
                string nodeId = _net.GetNodeId(uHandle);
                double[] vals = _net.GetNodeValue(uHandle);
                // Utility nodes typically return a single aggregated EU value
                result.ExpectedUtilities[nodeId] = vals.Length > 0 ? vals[0] : double.NaN;
            }

            return result;
        }

        private string[] GetNodeStateNames(int handle)
        {
            int count = _net.GetOutcomeCount(handle);
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = _net.GetOutcomeId(handle, i);
            return names;
        }
        private int GetStateIndexByName(Network net, int nodeHandle, string stateName)
        {
            int count = net.GetOutcomeCount(nodeHandle);
            for (int i = 0; i < count; i++)
            {
                if (net.GetOutcomeId(nodeHandle, i) == stateName)
                    return i;
            }
            throw new ArgumentException($"State '{stateName}' not found in node '{net.GetNodeId(nodeHandle)}'.");
        }
        public void Dispose()
        {
            _net?.Dispose();
        }

        // Add this debug method to InfluenceDiagramAnalyzer
        public void DebugPrintAllNodes()
        {
            Console.WriteLine("\n=== DEBUG: ALL NODES ===");
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                var type = _net.GetNodeType(h);
                string id = _net.GetNodeId(h);
                Console.WriteLine($"  Node: {id} | TypeRawValue: {(int)type} | TypeName: {type}");
            }
        }
    }

}
