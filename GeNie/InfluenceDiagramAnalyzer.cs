using Smile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GeNie
{
    // ══════════════════════════════════════════════════════════════════════
    //  TIME STEP DATA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One stage of a temporal plate model as seen by the interactive loop.
    /// All data here is sliced from the full flat arrays returned by SMILE.
    /// </summary>
    public class TimeStep
    {
        public int StepIndex { get; set; }   // 0-based
        public string StepLabel { get; set; }   // "1".."5" or "Init" etc.

        /// <summary>
        /// Chance nodes whose beliefs are observable at this step.
        /// For a temporal plate these are the plate chance nodes (Sensor_Position etc.)
        /// whose value array is sliced per step.
        /// </summary>
        public List<int> InformationalChanceNodes { get; } = new List<int>();

        /// <summary>Decision node handles whose policy table has an entry for this step.</summary>
        public List<int> DecisionNodes { get; } = new List<int>();

        /// <summary>Utility nodes assigned to this step.</summary>
        public List<int> StageUtilityNodes { get; } = new List<int>();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TEMPORAL PLATE METADATA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Describes how a temporal plate model is encoded in SMILE's flat arrays.
    ///
    /// SMILE stores all T steps for a compressed plate node concatenated in one array:
    ///   GetNodeValue(node).Length = T × stateCount   (for chance/decision nodes)
    ///
    /// This class captures T (number of steps) and which nodes participate in the plate,
    /// so the interactive loop can slice the correct sub-array per step.
    /// </summary>
    public class TemporalPlateInfo
    {
        /// <summary>Number of time steps detected (e.g. 5).</summary>
        public int StepCount { get; set; }

        /// <summary>
        /// Plate node handles: nodes whose GetNodeValue length = StepCount × stateCount.
        /// These are the nodes that repeat across all time steps.
        /// </summary>
        public HashSet<int> PlateNodes { get; } = new HashSet<int>();

        /// <summary>True when StepCount > 1 and at least one plate node was found.</summary>
        public bool IsTemporalPlate => StepCount > 1 && PlateNodes.Count > 0;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ANALYZER
    // ══════════════════════════════════════════════════════════════════════

    public class InfluenceDiagramAnalyzer
    {
        private readonly Smile.Network _net;
        private readonly SmileNodeTypeDetector _types;

        private readonly HashSet<int> _decisionSet;
        private readonly HashSet<int> _utilitySet;
        private readonly HashSet<int> _chanceSet;
        private readonly Dictionary<int, string> _idOf = new Dictionary<int, string>();

        // Cached after first call to DetectTemporalPlate()
        private TemporalPlateInfo _plateInfo;

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
                _idOf[h] = _net.GetNodeId(h);
                int t = (int)_net.GetNodeType(h);
                if (t == _types.Decision) _decisionSet.Add(h);
                else if (t == _types.Utility) _utilitySet.Add(h);
                else if (t == _types.Chance) _chanceSet.Add(h);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PUBLIC NODE QUERIES
        // ══════════════════════════════════════════════════════════════════

        public List<int> GetDecisionNodes() => _decisionSet.ToList();
        public List<int> GetUtilityNodes() => _utilitySet.ToList();

        public List<(int handle, string id, string[] states)> GetChanceNodeInfo()
        {
            var r = new List<(int, string, string[])>();
            foreach (int h in _chanceSet)
                r.Add((h, _idOf[h], GetStateNames(h)));
            return r;
        }

        public string GetNodeId(int h) => _idOf.TryGetValue(h, out var s) ? s : "?";
        public string[] GetStateNames(int h) => GetStateNamesInternal(h);
        public List<int> GetDecisionNodesOrdered() => TopologicalSort(_decisionSet.ToList());

        // ══════════════════════════════════════════════════════════════════
        //  TEMPORAL PLATE DETECTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Detects whether SMILE has internally unrolled a temporal plate.
        ///
        /// Evidence: after UpdateBeliefs(), a compressed plate node returns an array
        /// of length = T × stateCount instead of stateCount.
        ///
        /// To find T we look at DECISION nodes first (cleanest signal):
        ///   Action has 3 states and length 15 → T = 15/3 = 5.
        ///
        /// We then verify that at least one CHANCE node also confirms T
        /// (its length = T × its state count), and collect all such "plate nodes".
        ///
        /// A non-plate node has length == stateCount (T=1).
        /// </summary>
        public TemporalPlateInfo DetectTemporalPlate()
        {
            if (_plateInfo != null) return _plateInfo;

            _plateInfo = new TemporalPlateInfo { StepCount = 1 };

            // Need beliefs to inspect value arrays
            try { _net.UpdateBeliefs(); }
            catch { return _plateInfo; }

            // ── Step 1: find T from decision nodes ────────────────────────
            int detectedT = 1;
            foreach (int h in _decisionSet)
            {
                int sc = _net.GetOutcomeCount(h);
                if (sc <= 0) continue;
                double[] vals = SafeGetNodeValue(h);
                if (vals == null || vals.Length % sc != 0) continue;

                int t = vals.Length / sc;
                if (t > 1) { detectedT = t; _plateInfo.PlateNodes.Add(h); break; }
            }

            if (detectedT == 1)
            {
                // No temporal plate found — flat model
                Console.WriteLine($"  [Plate] Not detected — flat model.");
                return _plateInfo;
            }

            _plateInfo.StepCount = detectedT;

            // ── Step 2: collect all plate nodes (chance + decision) ────────
            // A node is a plate node if its value length = T × stateCount
            foreach (int h in _chanceSet.Concat(_decisionSet))
            {
                int sc = _net.GetOutcomeCount(h);
                if (sc <= 0) continue;
                double[] vals = SafeGetNodeValue(h);
                if (vals != null && vals.Length == detectedT * sc)
                    _plateInfo.PlateNodes.Add(h);
            }

            Console.WriteLine($"  [Plate] Detected {detectedT} time steps. " +
                              $"Plate nodes: [{string.Join(", ", _plateInfo.PlateNodes.Select(h => _idOf[h]))}]");

            return _plateInfo;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TIME STEP BUILDING
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the list of TimeSteps for this model.
        ///
        /// For a temporal plate model: one TimeStep per plate step (T steps total).
        ///   Each step contains:
        ///     InformationalChanceNodes = plate chance nodes (observable each step)
        ///     DecisionNodes            = plate decision nodes
        ///     StageUtilityNodes        = utility nodes assigned to this step
        ///       (for a compressed plate the utility is typically only at the last step
        ///        or distributed — we assign all to the last step unless there is an
        ///        explicit per-step utility node)
        ///
        /// For a flat model: single TimeStep with all nodes.
        /// </summary>
        public List<TimeStep> BuildTimeSteps()
        {
            TemporalPlateInfo plate = DetectTemporalPlate();

            if (plate.IsTemporalPlate)
                return BuildPlateTimeSteps(plate);

            return BuildFlatTimeStep();
        }

        private List<TimeStep> BuildPlateTimeSteps(TemporalPlateInfo plate)
        {
            int T = plate.StepCount;

            // Plate decision nodes
            var plateDecisions = _decisionSet
                .Where(h => plate.PlateNodes.Contains(h))
                .ToList();

            // Plate chance nodes (these are the observable ones per step)
            var plateChance = _chanceSet
                .Where(h => plate.PlateNodes.Contains(h))
                .ToList();

            // Non-plate chance nodes go into step 0 as "always observable" context
            // (they are init-condition nodes with flat beliefs)
            var initChance = _chanceSet
                .Where(h => !plate.PlateNodes.Contains(h))
                .ToList();

            var steps = new List<TimeStep>();

            for (int t = 0; t < T; t++)
            {
                var step = new TimeStep
                {
                    StepIndex = t,
                    StepLabel = (t + 1).ToString()
                };

                // All plate chance nodes are observable at every step
                step.InformationalChanceNodes.AddRange(plateChance);

                // Add init-condition chance nodes only at step 0
                if (t == 0) step.InformationalChanceNodes.AddRange(initChance);

                step.DecisionNodes.AddRange(plateDecisions);
                steps.Add(step);
            }

            // Assign utility nodes: put each utility at the last step unless
            // there is exactly one utility per step (detected by value-array length)
            AssignUtilityToPlateSteps(steps, plate);

            return steps;
        }

        private void AssignUtilityToPlateSteps(List<TimeStep> steps, TemporalPlateInfo plate)
        {
            int T = plate.StepCount;

            foreach (int u in _utilitySet)
            {
                double[] vals = SafeGetNodeValue(u);
                // If the utility array length is divisible by T, assign slice per step
                if (vals != null && vals.Length > 1 && vals.Length % T == 0)
                {
                    // Per-step utility — assign to each step
                    foreach (var step in steps)
                        step.StageUtilityNodes.Add(u);
                }
                else
                {
                    // Terminal utility — assign only to last step
                    steps[steps.Count - 1].StageUtilityNodes.Add(u);
                }
            }
        }

        private List<TimeStep> BuildFlatTimeStep()
        {
            var step = new TimeStep { StepIndex = 0, StepLabel = "1" };

            step.InformationalChanceNodes.AddRange(_chanceSet);
            step.DecisionNodes.AddRange(GetDecisionNodesOrdered());

            AssignUtilityNodesToSteps(new List<TimeStep> { step });

            // Any unassigned → this step
            foreach (int u in _utilitySet)
                if (!step.StageUtilityNodes.Contains(u))
                    step.StageUtilityNodes.Add(u);

            return new List<TimeStep> { step };
        }

        private void AssignUtilityNodesToSteps(List<TimeStep> steps)
        {
            var resolved = new HashSet<int>(_chanceSet);
            var assigned = new HashSet<int>();
            foreach (var step in steps)
            {
                foreach (int d in step.DecisionNodes) resolved.Add(d);
                foreach (int u in _utilitySet)
                {
                    if (assigned.Contains(u)) continue;
                    if (SafeGetParents(u).All(p => resolved.Contains(p)))
                    { step.StageUtilityNodes.Add(u); assigned.Add(u); }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  POLICY EXTRACTION — STEP-AWARE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Extracts the decision policy for one decision node AT a specific time step.
        ///
        /// For a compressed plate node the value array layout is:
        ///   [step0_state0, step0_state1, ..., step0_stateN,
        ///    step1_state0, step1_state1, ..., step1_stateN,
        ///    ...]
        ///
        /// So the slice for step t is: values[t*stateCount .. (t+1)*stateCount-1]
        ///
        /// For a flat (non-plate) node, stepIndex is ignored and the full array is used.
        /// </summary>
        public DecisionPolicy ExtractPolicyAtStep(int dHandle, int stepIndex)
        {
            string nodeId = _idOf[dHandle];
            string[] states = GetStateNamesInternal(dHandle);
            int sc = states.Length;

            double[] allVals = SafeGetNodeValue(dHandle);
            if (allVals == null || allVals.Length == 0) return null;

            // Determine which slice to use
            double[] eu;
            if (allVals.Length == sc)
            {
                // Flat node — single step
                eu = allVals;
            }
            else if (allVals.Length % sc == 0)
            {
                int T = allVals.Length / sc;
                int t = Math.Min(stepIndex, T - 1);
                eu = new double[sc];
                Array.Copy(allVals, t * sc, eu, 0, sc);
            }
            else
            {
                // Fallback: average across all entries
                eu = AverageAcrossCombos(allVals, sc);
            }

            int bestIdx = 0; double bestVal = double.NegativeInfinity;
            for (int i = 0; i < sc; i++)
                if (eu[i] > bestVal) { bestVal = eu[i]; bestIdx = i; }

            var policy = new DecisionPolicy
            {
                NodeId = nodeId,
                OptimalState = states[bestIdx],
                OptimalStateIndex = bestIdx,
                ExpectedUtilityPerState = new Dictionary<string, double>()
            };
            for (int i = 0; i < sc; i++)
                policy.ExpectedUtilityPerState[states[i]] = eu[i];

            return policy;
        }

        /// <summary>
        /// Returns the expected utility for a utility node at a specific step.
        /// For compressed plate utilities the array has T entries.
        /// For a terminal utility node there is 1 entry (or a large CPT table).
        /// </summary>
        public double GetUtilityAtStep(int uHandle, int stepIndex, int totalSteps)
        {
            double[] vals = SafeGetNodeValue(uHandle);
            if (vals == null || vals.Length == 0) return double.NaN;

            if (vals.Length == 1) return vals[0];

            // If length == totalSteps, one value per step
            if (vals.Length == totalSteps)
                return vals[Math.Min(stepIndex, totalSteps - 1)];

            // Otherwise it's a large CPT — return the mean as an approximation
            return vals.Average();
        }

        // ══════════════════════════════════════════════════════════════════
        //  FULL INFERENCE (used by Auto session and flat interactive)
        // ══════════════════════════════════════════════════════════════════

        public DecisionResult ComputeOptimalDecisions()
        {
            _net.UpdateBeliefs();
            var result = new DecisionResult();

            TemporalPlateInfo plate = DetectTemporalPlate();
            int T = plate.StepCount;

            foreach (int d in _decisionSet)
            {
                // For a plate, report the step-0 policy as the "first recommendation"
                var p = ExtractPolicyAtStep(d, 0);
                if (p != null) result.Policies.Add(p);
            }

            foreach (int u in _utilitySet)
            {
                double[] vals = SafeGetNodeValue(u);
                // For terminal utility return vals[0]; for per-step return average
                double eu = double.NaN;
                if (vals != null && vals.Length > 0)
                    eu = vals.Length == 1 ? vals[0] : vals.Average();
                result.ExpectedUtilities[_idOf[u]] = eu;
            }

            return result;
        }

        /// <summary>
        /// Runs UpdateBeliefs() and returns the full per-step policy table for all
        /// decision nodes. Used by the sequential session to show the complete
        /// pre-computed policy before asking the user to step through it.
        /// Returns dict[nodeId][stepIndex] → DecisionPolicy
        /// </summary>
        public Dictionary<string, List<DecisionPolicy>> ComputeFullPolicyTable()
        {
            _net.UpdateBeliefs();
            TemporalPlateInfo plate = DetectTemporalPlate();
            int T = plate.StepCount;

            var table = new Dictionary<string, List<DecisionPolicy>>();
            foreach (int d in _decisionSet)
            {
                string nodeId = _idOf[d];
                var policies = new List<DecisionPolicy>();
                for (int t = 0; t < T; t++)
                    policies.Add(ExtractPolicyAtStep(d, t));
                table[nodeId] = policies;
            }
            return table;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CHANCE NODE BELIEFS — STEP-AWARE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the belief distribution for a chance node at a specific step.
        /// For plate chance nodes the value array is T × stateCount.
        /// Returns null if the node has no beliefs yet.
        /// </summary>
        public Dictionary<string, double> GetBeliefAtStep(int cHandle, int stepIndex, int totalSteps)
        {
            string[] states = GetStateNamesInternal(cHandle);
            int sc = states.Length;
            double[] vals = SafeGetNodeValue(cHandle);
            if (vals == null || vals.Length == 0) return null;

            double[] slice;
            if (vals.Length == sc)
                slice = vals;
            else if (vals.Length % sc == 0)
            {
                int T = vals.Length / sc;
                int t = Math.Min(stepIndex, T - 1);
                slice = new double[sc];
                Array.Copy(vals, t * sc, slice, 0, sc);
            }
            else
                return null;

            var result = new Dictionary<string, double>();
            for (int i = 0; i < sc; i++) result[states[i]] = slice[i];
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  EVIDENCE
        // ══════════════════════════════════════════════════════════════════

        public void SetEvidence(string nodeId, string stateName)
        {
            int h = _net.GetNode(nodeId);
            if (h < 0) throw new ArgumentException($"Node '{nodeId}' not found.");
            _net.SetEvidence(h, FindStateIndex(h, stateName));
        }

        public void SetEvidenceByHandle(int handle, string stateName)
            => _net.SetEvidence(handle, FindStateIndex(handle, stateName));

        public void ClearAllEvidence()
        {
            for (int h = _net.GetFirstNode(); h >= 0; h = _net.GetNextNode(h))
            {
                int t = (int)_net.GetNodeType(h);
                if (t == _types.Chance || t == _types.Decision)
                    try { _net.ClearEvidence(h); } catch { }
            }
            // Reset cached plate info so next UpdateBeliefs() re-reads fresh values
            _plateInfo = null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DEBUG
        // ══════════════════════════════════════════════════════════════════

        public void DebugPrintAllNodes()
        {
            Console.WriteLine("\n=== DEBUG: ALL NODES ===");
            foreach (var kv in _idOf)
            {
                int h = kv.Key;
                int ti = (int)_net.GetNodeType(h);
                string kind = ti == _types.Decision ? "DECISION"
                            : ti == _types.Utility ? "UTILITY"
                            : ti == _types.Chance ? "CHANCE"
                            : $"UNK({ti})";
                string[] states = new string[0];
                try { states = GetStateNamesInternal(h); } catch { }
                string pStr = string.Join(", ", SafeGetParents(h).Select(p => _idOf[p]));
                Console.WriteLine($"  {kv.Value,-34} [{kind,-10}]  " +
                    $"states=[{string.Join(", ", states)}]  " +
                    $"parents=[{(pStr == "" ? "—" : pStr)}]");
            }
            Console.WriteLine($"\n  Type ints: Decision={_types.Decision}  " +
                              $"Chance={_types.Chance}  Utility={_types.Utility}");
        }

        public void DebugPrintTimeSteps()
        {
            Console.WriteLine("\n=== DEBUG: TIME STEPS ===");
            foreach (var step in BuildTimeSteps())
            {
                Console.WriteLine($"\n  ── Step {step.StepLabel} ──────────────────");
                Console.WriteLine("     Observe : [" + string.Join(", ",
                    step.InformationalChanceNodes.Select(h => _idOf[h])) + "]");
                Console.WriteLine("     Decide  : [" + string.Join(", ",
                    step.DecisionNodes.Select(h => _idOf[h])) + "]");
                Console.WriteLine("     Utility : [" + string.Join(", ",
                    step.StageUtilityNodes.Select(h => _idOf[h])) + "]");
            }
        }

        public void DebugPrintRawValues()
        {
            string outPath = Path.Combine(AppContext.BaseDirectory, "debug_values.txt");
            Console.WriteLine($"\n  Writing raw value dump to:\n  {outPath}");

            try { _net.UpdateBeliefs(); }
            catch (Exception ex) { Console.WriteLine($"  UpdateBeliefs() failed: {ex.Message}"); return; }

            using (var w = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("=== RAW NODE VALUES (after UpdateBeliefs) ===");
                w.WriteLine($"Generated: {DateTime.Now}");

                w.WriteLine("\n--- DECISION NODES ---");
                Console.WriteLine("\n  DECISION NODES:");
                foreach (int h in _decisionSet)
                {
                    string[] states = new string[0];
                    try { states = GetStateNamesInternal(h); } catch { }
                    double[] vals = SafeGetNodeValue(h);
                    int len = vals?.Length ?? -1;

                    w.WriteLine($"\n  Node   : {_idOf[h]}");
                    w.WriteLine($"  States : [{string.Join(", ", states)}]  (count={states.Length})");
                    w.WriteLine($"  Length : {len}");

                    Console.WriteLine($"    {_idOf[h],-28} states={states.Length}  length={len}");

                    if (vals != null)
                    {
                        for (int i = 0; i < vals.Length; i++)
                            w.WriteLine($"    [{i,6}] = {vals[i]:F8}");

                        if (states.Length > 0 && vals.Length % states.Length == 0)
                        {
                            int combos = vals.Length / states.Length;
                            w.WriteLine($"\n  Interpreted as {combos} combo(s) × {states.Length} states:");
                            for (int c = 0; c < combos; c++)
                            {
                                w.Write($"    combo {c,6}: ");
                                for (int s = 0; s < states.Length; s++)
                                    w.Write($"{states[s]}={vals[c * states.Length + s]:F4}  ");
                                w.WriteLine();
                            }
                            int show = Math.Min(10, combos);
                            Console.WriteLine($"      → {combos} combo(s) × {states.Length} states (first {show}):");
                            for (int c = 0; c < show; c++)
                            {
                                Console.Write($"        combo {c,3}: ");
                                for (int s = 0; s < states.Length; s++)
                                    Console.Write($"{states[s]}={vals[c * states.Length + s]:F4}  ");
                                Console.WriteLine();
                            }
                        }
                    }
                }

                w.WriteLine("\n--- UTILITY NODES ---");
                Console.WriteLine("\n  UTILITY NODES:");
                foreach (int h in _utilitySet)
                {
                    double[] vals = SafeGetNodeValue(h);
                    int len = vals?.Length ?? -1;
                    string pStr = string.Join(", ", SafeGetParents(h).Select(p => _idOf[p]));
                    w.WriteLine($"\n  Node    : {_idOf[h]}");
                    w.WriteLine($"  Parents : [{pStr}]");
                    w.WriteLine($"  Length  : {len}");
                    if (vals != null)
                        for (int i = 0; i < vals.Length; i++)
                            w.WriteLine($"    [{i,6}] = {vals[i]:F8}");
                    Console.WriteLine($"    {_idOf[h],-28} length={len}");
                    if (vals != null && len <= 20)
                        for (int i = 0; i < len; i++)
                            Console.WriteLine($"      [{i}] = {vals[i]:F6}");
                }

                w.WriteLine("\n--- CHANCE NODES (beliefs) ---");
                Console.WriteLine("\n  CHANCE NODES:");
                foreach (int h in _chanceSet)
                {
                    string[] states = new string[0];
                    try { states = GetStateNamesInternal(h); } catch { }
                    double[] vals = SafeGetNodeValue(h);
                    if (vals == null) continue;
                    w.Write($"  {_idOf[h],-34} length={vals.Length,-8} ");
                    for (int i = 0; i < Math.Min(vals.Length, states.Length); i++)
                        w.Write($"{states[i]}={vals[i]:F4}  ");
                    w.WriteLine();
                    Console.Write($"    {_idOf[h],-34} length={vals.Length,-8} ");
                    for (int i = 0; i < Math.Min(vals.Length, states.Length); i++)
                        Console.Write($"{states[i]}={vals[i]:F4}  ");
                    Console.WriteLine();
                }
            }
            Console.WriteLine($"\n  Done. Full detail in: {outPath}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════════

        private double[] AverageAcrossCombos(double[] vals, int stateCount)
        {
            double[] eu = new double[stateCount];
            int combos = vals.Length / stateCount;
            for (int c = 0; c < combos; c++)
                for (int s = 0; s < stateCount; s++)
                    eu[s] += vals[c * stateCount + s];
            for (int s = 0; s < stateCount; s++) eu[s] /= combos;
            return eu;
        }

        private string[] GetStateNamesInternal(int h)
        {
            int n = _net.GetOutcomeCount(h);
            var r = new string[n];
            for (int i = 0; i < n; i++) r[i] = _net.GetOutcomeId(h, i);
            return r;
        }

        private int FindStateIndex(int h, string stateName)
        {
            int n = _net.GetOutcomeCount(h);
            for (int i = 0; i < n; i++)
                if (_net.GetOutcomeId(h, i) == stateName) return i;
            throw new ArgumentException($"State '{stateName}' not found in '{_idOf[h]}'.");
        }

        private double[] SafeGetNodeValue(int h)
        {
            try { return _net.GetNodeValue(h); } catch { return null; }
        }

        private int[] SafeGetParents(int h)
        {
            try { return _net.GetParents(h); } catch { return new int[0]; }
        }

        private List<int> TopologicalSort(List<int> nodes)
        {
            if (nodes.Count <= 1) return new List<int>(nodes);
            var ns = new HashSet<int>(nodes);
            var ind = nodes.ToDictionary(n => n, _ => 0);
            var ch = nodes.ToDictionary(n => n, _ => new List<int>());
            foreach (int n in nodes)
                foreach (int p in SafeGetParents(n))
                    if (ns.Contains(p)) { ch[p].Add(n); ind[n]++; }
            var q = new Queue<int>(nodes.Where(n => ind[n] == 0));
            var r = new List<int>();
            while (q.Count > 0)
            {
                int cur = q.Dequeue(); r.Add(cur);
                foreach (int c in ch[cur]) if (--ind[c] == 0) q.Enqueue(c);
            }
            foreach (int n in nodes) if (!r.Contains(n)) r.Add(n);
            return r;
        }

        public void Dispose() => _net?.Dispose();
    }
}