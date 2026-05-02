using Smile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
            _net = new Smile.Network();
            _net.ReadFile(genieFilePath);

            _types = new SmileNodeTypeDetector();
            _types.AutoDetect(_net);
        }

        // ══════════════════════════════════════════════════════════════════
        //  NODE QUERIES
        // ══════════════════════════════════════════════════════════════════

        /// <summary>All decision node handles.</summary>
        public List<int> GetDecisionNodes()
        {
            var list = new List<int>();
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Decision)
                    list.Add(h);
            return list;
        }

        /// <summary>
        /// Decision nodes in temporal / topological order.
        /// Nodes with no informational predecessors (among decision nodes) come first.
        /// Falls back to raw enumeration order if topology cannot be determined.
        /// </summary>
        public List<int> GetDecisionNodesOrdered()
        {
            List<int> decisions = GetDecisionNodes();
            if (decisions.Count <= 1) return decisions;

            // Build a simple topological sort based on SMILE parent lists.
            // A decision node A precedes B if A is an (indirect) parent of B.
            // We use Kahn's algorithm over the decision-node subgraph.

            // Build adjacency: parents of each decision node that are also decision nodes
            var inEdgeCount = new Dictionary<int, int>();
            var children = new Dictionary<int, List<int>>();
            var handleSet = new HashSet<int>(decisions);

            foreach (int d in decisions)
            {
                inEdgeCount[d] = 0;
                children[d] = new List<int>();
            }

            foreach (int d in decisions)
            {
                // GetParents returns int[] of parent handles
                int[] parents;
                try { parents = _net.GetParents(d); }
                catch { parents = new int[0]; }

                foreach (int p in parents)
                    if (handleSet.Contains(p))
                    {
                        children[p].Add(d);
                        inEdgeCount[d]++;
                    }
            }

            var queue = new Queue<int>(decisions.Where(d => inEdgeCount[d] == 0));
            var sorted = new List<int>();

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                sorted.Add(node);
                foreach (int child in children[node])
                    if (--inEdgeCount[child] == 0)
                        queue.Enqueue(child);
            }

            // If there's a cycle (shouldn't happen in a valid ID) fall back
            return sorted.Count == decisions.Count ? sorted : decisions;
        }

        /// <summary>All utility node handles.</summary>
        public List<int> GetUtilityNodes()
        {
            var list = new List<int>();
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Utility)
                    list.Add(h);
            return list;
        }

        /// <summary>Returns (handle, nodeId, stateNames) for every chance node.</summary>
        public List<(int handle, string id, string[] states)> GetChanceNodeInfo()
        {
            var result = new List<(int, string, string[])>();
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
                if ((int)_net.GetNodeType(h) == _types.Chance)
                    result.Add((h, _net.GetNodeId(h), GetStateNames(h)));
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ACCESSORS (used by Program.cs)
        // ══════════════════════════════════════════════════════════════════

        public string GetNodeId(int handle) => _net.GetNodeId(handle);
        public string[] GetStateNames(int handle) => GetNodeStateNamesInternal(handle);

        // ══════════════════════════════════════════════════════════════════
        //  EVIDENCE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Sets evidence on a chance OR decision node by state name.</summary>
        public void SetEvidence(string nodeId, string stateName)
        {
            int handle = _net.GetNode(nodeId);
            if (handle < 0) throw new ArgumentException($"Node '{nodeId}' not found.");

            int stateIdx = FindStateIndex(_net, handle, stateName);
            _net.SetEvidence(handle, stateIdx);
            Console.WriteLine($"  [Evidence] {nodeId} = {stateName}");
        }

        /// <summary>Sets evidence by handle (used when locking user decisions).</summary>
        public void SetEvidenceByHandle(int handle, string stateName)
        {
            int stateIdx = FindStateIndex(_net, handle, stateName);
            _net.SetEvidence(handle, stateIdx);
        }

        /// <summary>Clears evidence on all chance nodes.</summary>
        public void ClearAllEvidence()
        {
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                int t = (int)_net.GetNodeType(h);
                if (t == _types.Chance || t == _types.Decision)
                {
                    try { _net.ClearEvidence(h); } catch { /* ignore if not applicable */ }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  INFERENCE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Runs SMILE inference and extracts the optimal decision policy and expected utilities.
        /// </summary>
        public DecisionResult ComputeOptimalDecisions()
        {
            _net.UpdateBeliefs();

            var result = new DecisionResult();

            foreach (int dHandle in GetDecisionNodes())
                ExtractDecisionPolicy(dHandle, result);

            foreach (int uHandle in GetUtilityNodes())
            {
                string nodeId = _net.GetNodeId(uHandle);
                double[] vals = _net.GetNodeValue(uHandle);
                result.ExpectedUtilities[nodeId] = (vals != null && vals.Length > 0)
                    ? vals[0] : double.NaN;
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DEBUG
        // ══════════════════════════════════════════════════════════════════

        public void DebugPrintAllNodes()
        {
            Console.WriteLine("\n=== DEBUG: ALL NODES ===");
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                var type = _net.GetNodeType(h);
                string id = _net.GetNodeId(h);
                string[] states = new string[0];
                try { states = GetStateNames(h); } catch { }
                Console.WriteLine(
                    $"  {id,-30} type={type} ({(int)type})  states=[{string.Join(", ", states)}]");
            }
            Console.WriteLine($"\n  Detected: Decision={_types.Decision}  Chance={_types.Chance}  Utility={_types.Utility}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════════

        private void ExtractDecisionPolicy(int dHandle, DecisionResult result)
        {
            string nodeId = _net.GetNodeId(dHandle);
            double[] values = _net.GetNodeValue(dHandle);
            string[] states = GetStateNames(dHandle);

            if (values == null || values.Length == 0)
            {
                Console.WriteLine($"  [WARNING] No values for '{nodeId}', skipping.");
                return;
            }

            int stateCount = states.Length;

            // values layout: [state0_parentCombo0, state1_parentCombo0, state0_parentCombo1, ...]
            double[] euPerState = new double[stateCount];
            int numCombinations = values.Length / stateCount;

            for (int combo = 0; combo < numCombinations; combo++)
                for (int s = 0; s < stateCount; s++)
                    euPerState[s] += values[combo * stateCount + s];

            for (int s = 0; s < stateCount; s++)
                euPerState[s] /= numCombinations;

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

        private string[] GetNodeStateNamesInternal(int handle)
        {
            int count = _net.GetOutcomeCount(handle);
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = _net.GetOutcomeId(handle, i);
            return names;
        }

        private int FindStateIndex(Network net, int nodeHandle, string stateName)
        {
            int count = net.GetOutcomeCount(nodeHandle);
            for (int i = 0; i < count; i++)
                if (net.GetOutcomeId(nodeHandle, i) == stateName)
                    return i;
            throw new ArgumentException(
                $"State '{stateName}' not found in node '{net.GetNodeId(nodeHandle)}'.");
        }

        public void Dispose()
        {
            _net?.Dispose();
        }
    }
}