using Smile;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeNie
{
    // ══════════════════════════════════════════════════════════════════════
    //  DATA STRUCTURES
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One stage in a sequential influence diagram.
    ///   Phase 1 – Observe InformationalChanceNodes (newly revealed at this step)
    ///   Phase 2 – Make decisions in DecisionNodes
    ///   Phase 3 – Collect utility from StageUtilityNodes
    /// </summary>
    public class TimeStep
    {
        public int StepIndex { get; set; }

        /// <summary>
        /// Chance nodes that become observable just before this step's decisions.
        /// Derived from informational arcs (chance-node parents of the decision nodes)
        /// that were not already observed in a prior step.
        /// </summary>
        public List<int> InformationalChanceNodes { get; } = new List<int>();

        /// <summary>Decision node handles at this step, in topological order.</summary>
        public List<int> DecisionNodes { get; } = new List<int>();

        /// <summary>
        /// Utility nodes whose decision-node parents are all resolved by this step.
        /// In a multi-stage model each utility node maps to the step that "closes" it.
        /// </summary>
        public List<int> StageUtilityNodes { get; } = new List<int>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ANALYZER
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps a GENIE Influence Diagram (SMILE) to compute optimal decision paths.
    /// Supports both flat (non-sequential) and multi-timestep (sequential) models.
    /// Compatible with .NET Framework 4.8.
    /// </summary>
    public class InfluenceDiagramAnalyzer
    {
        private readonly Smile.Network _net;
        private readonly SmileNodeTypeDetector _types;

        private readonly HashSet<int> _decisionSet;
        private readonly HashSet<int> _utilitySet;
        private readonly HashSet<int> _chanceSet;

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

            _decisionSet = new HashSet<int>();
            _utilitySet = new HashSet<int>();
            _chanceSet = new HashSet<int>();

            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                int t = (int)_net.GetNodeType(h);
                if (t == _types.Decision) _decisionSet.Add(h);
                else if (t == _types.Utility) _utilitySet.Add(h);
                else if (t == _types.Chance) _chanceSet.Add(h);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  NODE QUERIES
        // ══════════════════════════════════════════════════════════════════

        public List<int> GetDecisionNodes() => _decisionSet.ToList();
        public List<int> GetUtilityNodes() => _utilitySet.ToList();

        public List<(int handle, string id, string[] states)> GetChanceNodeInfo()
        {
            var result = new List<(int, string, string[])>();
            foreach (int h in _chanceSet)
                result.Add((h, _net.GetNodeId(h), GetStateNames(h)));
            return result;
        }

        public string GetNodeId(int handle) => _net.GetNodeId(handle);
        public string[] GetStateNames(int handle) => GetStateNamesInternal(handle);

        /// <summary>Decision nodes in full topological order over the DAG.</summary>
        public List<int> GetDecisionNodesOrdered() => TopologicalSort(_decisionSet.ToList());

        // ══════════════════════════════════════════════════════════════════
        //  TEMPORAL STRUCTURE ANALYSIS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyses the informational arc structure and returns a list of TimeStep objects.
        ///
        /// For each decision node D (in topological order):
        ///   - Its "newly observable" chance nodes = chance parents of D not yet
        ///     claimed by an earlier step.
        ///   - D is merged into the current step if it shares the same information
        ///     horizon; otherwise a new step is opened.
        ///
        /// Utility nodes are assigned to the earliest step at which all their
        /// decision-node parents are resolved.
        ///
        /// A flat model produces exactly one TimeStep containing everything.
        /// </summary>
        public List<TimeStep> BuildTimeSteps()
        {
            List<int> orderedDecisions = GetDecisionNodesOrdered();
            if (orderedDecisions.Count == 0) return new List<TimeStep>();

            var alreadyClaimedChance = new HashSet<int>();
            var steps = new List<TimeStep>();

            foreach (int d in orderedDecisions)
            {
                int[] parents = SafeGetParents(d);

                // Chance parents of this decision that haven't been assigned yet
                var newChance = parents
                    .Where(p => _chanceSet.Contains(p) && !alreadyClaimedChance.Contains(p))
                    .ToList();

                // Try to merge into the last step:
                // merge when this decision introduces NO new chance observations
                // (same information horizon as the previous decision)
                bool merged = false;
                if (steps.Count > 0 && newChance.Count == 0)
                {
                    steps[steps.Count - 1].DecisionNodes.Add(d);
                    merged = true;
                }

                if (!merged)
                {
                    var step = new TimeStep { StepIndex = steps.Count };
                    foreach (int c in newChance) step.InformationalChanceNodes.Add(c);
                    step.DecisionNodes.Add(d);
                    steps.Add(step);
                }

                foreach (int c in newChance) alreadyClaimedChance.Add(c);
            }

            AssignUtilityNodesToSteps(steps);

            // Any utility not yet assigned (e.g. purely terminal) → last step
            var assigned = new HashSet<int>(steps.SelectMany(s => s.StageUtilityNodes));
            foreach (int u in _utilitySet)
                if (!assigned.Contains(u))
                    steps[steps.Count - 1].StageUtilityNodes.Add(u);

            return steps;
        }

        private void AssignUtilityNodesToSteps(List<TimeStep> steps)
        {
            // "Resolved" nodes grow as we walk through steps.
            // Chance nodes are always resolved (they're observable or latent,
            // either way their uncertainty is already in the model).
            var resolved = new HashSet<int>(_chanceSet);
            var assigned = new HashSet<int>();

            foreach (TimeStep step in steps)
            {
                foreach (int d in step.DecisionNodes) resolved.Add(d);

                foreach (int u in _utilitySet)
                {
                    if (assigned.Contains(u)) continue;
                    if (SafeGetParents(u).All(p => resolved.Contains(p)))
                    {
                        step.StageUtilityNodes.Add(u);
                        assigned.Add(u);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  EVIDENCE
        // ══════════════════════════════════════════════════════════════════

        public void SetEvidence(string nodeId, string stateName)
        {
            int handle = _net.GetNode(nodeId);
            if (handle < 0) throw new ArgumentException($"Node '{nodeId}' not found.");
            _net.SetEvidence(handle, FindStateIndex(_net, handle, stateName));
        }

        public void SetEvidenceByHandle(int handle, string stateName)
            => _net.SetEvidence(handle, FindStateIndex(_net, handle, stateName));

        public void ClearAllEvidence()
        {
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                int t = (int)_net.GetNodeType(h);
                if (t == _types.Chance || t == _types.Decision)
                    try { _net.ClearEvidence(h); } catch { }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  INFERENCE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Full inference: returns all decision policies + all utility values.</summary>
        public DecisionResult ComputeOptimalDecisions()
        {
            _net.UpdateBeliefs();
            var result = new DecisionResult();

            foreach (int d in _decisionSet)
            {
                var p = ExtractSingleDecisionPolicy(d);
                if (p != null) result.Policies.Add(p);
            }

            foreach (int u in _utilitySet)
            {
                double[] vals = _net.GetNodeValue(u);
                result.ExpectedUtilities[_net.GetNodeId(u)] =
                    (vals != null && vals.Length > 0) ? vals[0] : double.NaN;
            }

            return result;
        }

        /// <summary>
        /// Runs UpdateBeliefs() and extracts the policy for ONE decision node.
        /// Used in the sequential loop where we re-infer after each observation.
        /// </summary>
        public DecisionPolicy RefreshAndExtractPolicy(int dHandle)
        {
            _net.UpdateBeliefs();
            return ExtractSingleDecisionPolicy(dHandle);
        }

        /// <summary>
        /// Extracts the policy for one decision node WITHOUT calling UpdateBeliefs().
        /// Call after a manual UpdateBeliefs() when you need multiple nodes from the
        /// same inference pass.
        /// </summary>
        public DecisionPolicy ExtractSingleDecisionPolicy(int dHandle)
        {
            string nodeId = _net.GetNodeId(dHandle);
            double[] values = _net.GetNodeValue(dHandle);
            string[] states = GetStateNamesInternal(dHandle);

            if (values == null || values.Length == 0) return null;

            int stateCount = states.Length;
            int combos = Math.Max(1, values.Length / stateCount);

            double[] eu = new double[stateCount];
            for (int c = 0; c < combos; c++)
                for (int s = 0; s < stateCount; s++)
                    eu[s] += values[c * stateCount + s];
            for (int s = 0; s < stateCount; s++) eu[s] /= combos;

            int bestIdx = 0; double bestVal = double.NegativeInfinity;
            for (int i = 0; i < stateCount; i++)
                if (eu[i] > bestVal) { bestVal = eu[i]; bestIdx = i; }

            var policy = new DecisionPolicy
            {
                NodeId = nodeId,
                OptimalState = states[bestIdx],
                OptimalStateIndex = bestIdx,
                ExpectedUtilityPerState = new Dictionary<string, double>()
            };
            for (int i = 0; i < stateCount; i++)
                policy.ExpectedUtilityPerState[states[i]] = eu[i];

            return policy;
        }

        /// <summary>Returns the current scalar utility value for a utility node.</summary>
        public double GetUtilityValue(int uHandle)
        {
            double[] vals = _net.GetNodeValue(uHandle);
            return (vals != null && vals.Length > 0) ? vals[0] : double.NaN;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DEBUG
        // ══════════════════════════════════════════════════════════════════

        public void DebugPrintAllNodes()
        {
            Console.WriteLine("\n=== DEBUG: ALL NODES ===");
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                int typeInt = (int)_net.GetNodeType(h);
                string kind = typeInt == _types.Decision ? "DECISION"
                            : typeInt == _types.Utility ? "UTILITY"
                            : typeInt == _types.Chance ? "CHANCE"
                            : $"UNKNOWN({typeInt})";

                string[] states = new string[0];
                try { states = GetStateNamesInternal(h); } catch { }

                string parentStr = string.Join(", ",
                    SafeGetParents(h).Select(p => _net.GetNodeId(p)));
                if (parentStr == "") parentStr = "—";

                Console.WriteLine($"  {_net.GetNodeId(h),-30} [{kind,-10}]  " +
                                  $"states=[{string.Join(", ", states)}]  " +
                                  $"parents=[{parentStr}]");
            }
            Console.WriteLine($"\n  Type ints: Decision={_types.Decision}  " +
                              $"Chance={_types.Chance}  Utility={_types.Utility}");
        }

        public void DebugPrintTimeSteps()
        {
            Console.WriteLine("\n=== DEBUG: DETECTED TIME STEPS ===");
            foreach (var step in BuildTimeSteps())
            {
                Console.WriteLine($"\n  ── Step {step.StepIndex + 1} " +
                                  new string('─', 40));
                Console.WriteLine("     Observe : [" + string.Join(", ",
                    step.InformationalChanceNodes.Select(h => _net.GetNodeId(h))) + "]");
                Console.WriteLine("     Decide  : [" + string.Join(", ",
                    step.DecisionNodes.Select(h => _net.GetNodeId(h))) + "]");
                Console.WriteLine("     Utility : [" + string.Join(", ",
                    step.StageUtilityNodes.Select(h => _net.GetNodeId(h))) + "]");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════════

        private string[] GetStateNamesInternal(int handle)
        {
            int count = _net.GetOutcomeCount(handle);
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = _net.GetOutcomeId(handle, i);
            return names;
        }

        private int FindStateIndex(Network net, int nodeHandle, string stateName)
        {
            int count = net.GetOutcomeCount(nodeHandle);
            for (int i = 0; i < count; i++)
                if (net.GetOutcomeId(nodeHandle, i) == stateName) return i;
            throw new ArgumentException(
                $"State '{stateName}' not found in '{net.GetNodeId(nodeHandle)}'.");
        }

        private int[] SafeGetParents(int handle)
        {
            try { return _net.GetParents(handle); }
            catch { return new int[0]; }
        }

        /// <summary>Kahn's topological sort over an arbitrary node subset.</summary>
        private List<int> TopologicalSort(List<int> nodes)
        {
            if (nodes.Count <= 1) return new List<int>(nodes);

            var nodeSet = new HashSet<int>(nodes);
            var inDegree = new Dictionary<int, int>();
            var children = new Dictionary<int, List<int>>();

            foreach (int n in nodes) { inDegree[n] = 0; children[n] = new List<int>(); }

            foreach (int n in nodes)
                foreach (int p in SafeGetParents(n))
                    if (nodeSet.Contains(p)) { children[p].Add(n); inDegree[n]++; }

            var queue = new Queue<int>(nodes.Where(n => inDegree[n] == 0));
            var sorted = new List<int>();

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                sorted.Add(cur);
                foreach (int ch in children[cur])
                    if (--inDegree[ch] == 0) queue.Enqueue(ch);
            }

            // Append anything not reached (cycle guard)
            foreach (int n in nodes) if (!sorted.Contains(n)) sorted.Add(n);
            return sorted;
        }

        public void Dispose() => _net?.Dispose();
    }
}