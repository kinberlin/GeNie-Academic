using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeNie
{
    internal class Program
    {
        // ══════════════════════════════════════════════════════════════════
        //  ENTRY POINT
        // ══════════════════════════════════════════════════════════════════
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintBanner();

            string modelPath = ResolveModelPath(args);
            if (modelPath == null) return;

            Console.WriteLine($"\n  Model : {Path.GetFileName(modelPath)}");
            Console.WriteLine(new string('─', 60));

            InfluenceDiagramAnalyzer analyzer;
            try { analyzer = new InfluenceDiagramAnalyzer(modelPath); }
            catch (Exception ex) { Error($"Failed to load model: {ex.Message}"); return; }

            // ── Ask once: sequential or flat? ─────────────────────────────
            bool isSequential = AskModelMode(analyzer);

            // ── Main loop ─────────────────────────────────────────────────
            bool running = true;
            while (running)
            {
                Console.WriteLine();
                PrintMenu(isSequential);
                string cmd = Prompt("Command").Trim().ToLowerInvariant();

                switch (cmd)
                {
                    case "1":
                        if (isSequential) RunSequentialInteractive(analyzer);
                        else RunFlatInteractive(analyzer);
                        break;
                    case "2":
                        RunAutoSession(analyzer);
                        break;
                    case "3":
                        isSequential = AskModelMode(analyzer);
                        break;
                    case "4":
                        analyzer.DebugPrintAllNodes();
                        analyzer.DebugPrintTimeSteps();
                        break;
                    case "q":
                    case "quit":
                    case "exit":
                        running = false;
                        break;
                    default:
                        Warn("Unknown command.");
                        break;
                }
            }

            Console.WriteLine("\n  Goodbye.\n");
        }

        // ══════════════════════════════════════════════════════════════════
        //  MODE SELECTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asks the user whether the model is sequential (multi-timestep) or flat.
        /// Also shows the auto-detected step structure so the user can confirm.
        /// </summary>
        static bool AskModelMode(InfluenceDiagramAnalyzer analyzer)
        {
            Console.WriteLine("\n" + Header("MODEL TYPE SELECTION"));

            // Show what we detected
            List<TimeStep> steps = analyzer.BuildTimeSteps();
            Console.WriteLine($"\n  Auto-detected structure: {steps.Count} time step(s).");

            foreach (var step in steps)
            {
                string obs = step.InformationalChanceNodes.Count == 0
                    ? "(none)"
                    : string.Join(", ", step.InformationalChanceNodes
                        .Select(h => analyzer.GetNodeId(h)));
                string dec = string.Join(", ",
                    step.DecisionNodes.Select(h => analyzer.GetNodeId(h)));
                string util = step.StageUtilityNodes.Count == 0
                    ? "(none)"
                    : string.Join(", ", step.StageUtilityNodes
                        .Select(h => analyzer.GetNodeId(h)));

                Console.WriteLine($"\n  Step {step.StepIndex + 1}:");
                Console.WriteLine($"    Observe  : {obs}");
                Console.WriteLine($"    Decide   : {dec}");
                Console.WriteLine($"    Utility  : {util}");
            }

            Console.WriteLine();
            Console.WriteLine("  [1] Flat / non-sequential  – all evidence upfront, then all decisions");
            Console.WriteLine("  [2] Sequential             – interleave observation and decision per step");

            // If only one step detected, default to flat
            string suggestion = steps.Count <= 1 ? "1" : "2";

            while (true)
            {
                string raw = Prompt($"  Select mode [1/2, Enter={suggestion}]").Trim();
                if (string.IsNullOrEmpty(raw)) raw = suggestion;
                if (raw == "1") { Console.WriteLine("  → Flat mode selected."); return false; }
                if (raw == "2") { Console.WriteLine("  → Sequential mode selected."); return true; }
                Warn("  Please enter 1 or 2.");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FLAT INTERACTIVE SESSION
        //  All evidence upfront → single inference → user picks all decisions
        //  → final EU. Suitable for single-timestep influence diagrams.
        // ══════════════════════════════════════════════════════════════════
        static void RunFlatInteractive(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("FLAT INTERACTIVE SESSION"));

            // ── Optional upfront evidence ──────────────────────────────────
            if (AskYesNo("\n  Enter any observed evidence now?"))
                CollectEvidence(analyzer, analyzer.GetChanceNodeInfo());

            // ── Baseline inference ─────────────────────────────────────────
            DecisionResult baseline = analyzer.ComputeOptimalDecisions();

            List<int> decisions = analyzer.GetDecisionNodesOrdered();
            if (decisions.Count == 0) { Warn("No decision nodes found."); return; }

            var userChoices = new List<(string nodeId, string chosen, bool wasOptimal)>();

            // ── Walk every decision ────────────────────────────────────────
            foreach (int dHandle in decisions)
            {
                string nodeId = analyzer.GetNodeId(dHandle);
                DecisionPolicy suggestion = baseline.Policies
                    .FirstOrDefault(p => p.NodeId == nodeId);

                PrintDecisionPrompt(nodeId, suggestion, analyzer.GetStateNames(dHandle));

                string chosen = AskChoice(
                    analyzer.GetStateNames(dHandle),
                    suggestion?.OptimalState,
                    $"Your choice for '{nodeId}'");

                bool wasOptimal = suggestion != null && chosen == suggestion.OptimalState;
                userChoices.Add((nodeId, chosen, wasOptimal));

                // Lock this decision so subsequent inferences reflect it
                try { analyzer.SetEvidence(nodeId, chosen); } catch { }
            }

            // ── Final inference with all decisions locked ──────────────────
            DecisionResult final = analyzer.ComputeOptimalDecisions();

            PrintSessionSummary(userChoices, final);
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  SEQUENTIAL INTERACTIVE SESSION
        //  For each time step:
        //    1. Show which chance nodes are observable at this step
        //    2. Ask user for observations
        //    3. Re-infer → show updated EU
        //    4. Ask user to make decisions
        //    5. Lock decisions as evidence
        //    6. Show stage utility
        //  At the end: total utility.
        // ══════════════════════════════════════════════════════════════════
        static void RunSequentialInteractive(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("SEQUENTIAL INTERACTIVE SESSION"));
            Console.WriteLine("  The model will guide you step by step.");
            Console.WriteLine("  At each stage you first observe, then decide.\n");

            List<TimeStep> steps = analyzer.BuildTimeSteps();
            if (steps.Count == 0) { Warn("No steps detected."); return; }

            var allChoices = new List<(int step, string nodeId, string chosen, bool wasOptimal)>();

            for (int si = 0; si < steps.Count; si++)
            {
                TimeStep step = steps[si];
                Console.WriteLine();
                Console.WriteLine(StepHeader(si + 1, steps.Count));

                // ── Phase 1: Observation ───────────────────────────────────
                if (step.InformationalChanceNodes.Count > 0)
                {
                    Console.WriteLine("\n  ► OBSERVE");
                    Console.WriteLine("    The following chance nodes become observable at this step:");

                    var chanceInfo = step.InformationalChanceNodes
                        .Select(h => (handle: h,
                                      id: analyzer.GetNodeId(h),
                                      states: analyzer.GetStateNames(h)))
                        .ToList();

                    foreach (var (_, id, states) in chanceInfo)
                        Console.WriteLine($"    • {id,-28} states: {string.Join(", ", states)}");

                    if (AskYesNo("\n    Do you want to set observations for these nodes?"))
                        CollectEvidence(analyzer, chanceInfo
                            .Select(c => (c.handle, c.id, c.states)).ToList());
                }
                else
                {
                    Console.WriteLine("\n  (No new observations at this step.)");
                }

                // ── Phase 2: Inference → Decisions ────────────────────────
                Console.WriteLine("\n  ► DECIDE");

                // Re-run inference with current evidence
                _net_UpdateBeliefs_wrapper(analyzer);

                foreach (int dHandle in step.DecisionNodes)
                {
                    string nodeId = analyzer.GetNodeId(dHandle);
                    string[] states = analyzer.GetStateNames(dHandle);

                    // Extract updated policy (beliefs already updated above)
                    DecisionPolicy policy = analyzer.ExtractSingleDecisionPolicy(dHandle);

                    PrintDecisionPrompt(nodeId, policy, states);

                    string chosen = AskChoice(states, policy?.OptimalState,
                        $"Your choice for '{nodeId}'");

                    bool wasOptimal = policy != null && chosen == policy.OptimalState;
                    allChoices.Add((si + 1, nodeId, chosen, wasOptimal));

                    // Lock this decision immediately so later decisions in the
                    // same step (and future steps) see it
                    try { analyzer.SetEvidence(nodeId, chosen); } catch { }

                    // Re-infer after each locked decision within the same step
                    // so the next decision at this step gets updated beliefs
                    if (step.DecisionNodes.IndexOf(dHandle) < step.DecisionNodes.Count - 1)
                        _net_UpdateBeliefs_wrapper(analyzer);
                }

                // ── Phase 3: Stage utility ────────────────────────────────
                if (step.StageUtilityNodes.Count > 0)
                {
                    _net_UpdateBeliefs_wrapper(analyzer);
                    Console.WriteLine("\n  ► STAGE UTILITY");
                    foreach (int uHandle in step.StageUtilityNodes)
                    {
                        string uid = analyzer.GetNodeId(uHandle);
                        double uv = analyzer.GetUtilityValue(uHandle);
                        Console.WriteLine($"    {uid,-30} EU = {uv:F4}");
                    }
                }
            }

            // ── Final inference for total utility ──────────────────────────
            DecisionResult final = analyzer.ComputeOptimalDecisions();

            // Print session summary
            Console.WriteLine("\n" + Header("SESSION SUMMARY"));
            Console.WriteLine("\n  Your decisions by step:");

            int lastStep = 0;
            foreach (var (stepNum, nodeId, chosen, wasOptimal) in allChoices)
            {
                if (stepNum != lastStep)
                {
                    Console.WriteLine($"\n  Step {stepNum}:");
                    lastStep = stepNum;
                }
                string tag = wasOptimal ? " ✓ optimal" : " (custom)";
                Console.WriteLine($"    {nodeId,-28} → {chosen}{tag}");
            }

            PrintTotalUtilities(final);
            analyzer.ClearAllEvidence();
        }

        /// <summary>
        /// Thin wrapper so the sequential loop can call UpdateBeliefs via the
        /// ComputeOptimalDecisions path without duplicating evidence logic.
        /// We call the public method and discard the result — we only want the
        /// side effect of refreshing internal SMILE beliefs.
        /// </summary>
        static void _net_UpdateBeliefs_wrapper(InfluenceDiagramAnalyzer analyzer)
        {
            try { analyzer.ComputeOptimalDecisions(); }
            catch { /* ignore transient SMILE errors during partial evidence */ }
        }

        // ══════════════════════════════════════════════════════════════════
        //  AUTO SESSION  – SMILE picks everything, one inference call
        // ══════════════════════════════════════════════════════════════════
        static void RunAutoSession(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("AUTOMATIC (SMILE-OPTIMAL) SESSION"));

            if (AskYesNo("\n  Enter any observed evidence first?"))
                CollectEvidence(analyzer, analyzer.GetChanceNodeInfo());

            analyzer.ComputeOptimalDecisions().Print();
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  SHARED UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        static void PrintDecisionPrompt(string nodeId, DecisionPolicy policy, string[] states)
        {
            Console.WriteLine();
            Console.WriteLine(SubHeader($"Decision: {nodeId}"));

            if (policy != null && policy.ExpectedUtilityPerState.Count > 0)
            {
                Console.WriteLine("    Expected utility per option:");
                foreach (var kv in policy.ExpectedUtilityPerState)
                {
                    bool best = kv.Key == policy.OptimalState;
                    string mark = best ? " ★" : "  ";
                    Console.WriteLine($"   {mark} {kv.Key,-24}  EU = {kv.Value:F4}");
                }
                Console.WriteLine($"\n    SMILE suggests → [{policy.OptimalState}]");
            }
            else
            {
                Console.WriteLine("    (No EU data available – choosing blind)");
            }
        }

        /// <summary>
        /// Collects evidence interactively for a specific set of chance nodes.
        /// Passing the full chance list shows all nodes; passing a subset
        /// restricts the prompt to nodes observable at the current step.
        /// </summary>
        static void CollectEvidence(
            InfluenceDiagramAnalyzer analyzer,
            List<(int handle, string id, string[] states)> candidates)
        {
            if (candidates.Count == 0) { Warn("  No observable nodes at this point."); return; }

            Console.WriteLine("\n  Observable nodes:");
            foreach (var (_, id, states) in candidates)
                Console.WriteLine($"    {id,-28} states: {string.Join(", ", states)}");

            Console.WriteLine("\n  Enter as  NodeId=StateName  (blank line to finish):");

            // Build a quick lookup so we only accept listed nodes
            var allowed = new HashSet<string>(
                candidates.Select(c => c.id), StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                string line = Prompt("  Evidence").Trim();
                if (string.IsNullOrEmpty(line)) break;

                int eq = line.IndexOf('=');
                if (eq < 1) { Warn("  Format: NodeId=StateName"); continue; }

                string nid = line.Substring(0, eq).Trim();
                string state = line.Substring(eq + 1).Trim();

                if (!allowed.Contains(nid))
                {
                    Warn($"  '{nid}' is not in the observable set at this step.");
                    continue;
                }

                try
                {
                    analyzer.SetEvidence(nid, state);
                    Ok($"  Evidence set: {nid} = {state}");
                }
                catch (Exception ex) { Warn($"  ✗ {ex.Message}"); }
            }
        }

        static void PrintSessionSummary(
            List<(string nodeId, string chosen, bool wasOptimal)> choices,
            DecisionResult final)
        {
            Console.WriteLine("\n" + Header("SESSION SUMMARY"));
            Console.WriteLine("\n  Your decisions:");
            foreach (var (nid, chosen, wasOptimal) in choices)
            {
                string tag = wasOptimal ? " ✓ optimal" : " (custom)";
                Console.WriteLine($"    {nid,-28} → {chosen}{tag}");
            }
            PrintTotalUtilities(final);
        }

        static void PrintTotalUtilities(DecisionResult result)
        {
            Console.WriteLine("\n  Total expected utility:");
            if (result.ExpectedUtilities.Count == 0)
                Console.WriteLine("    (none returned by SMILE)");
            else
                foreach (var kv in result.ExpectedUtilities)
                    Console.WriteLine($"    {kv.Key,-28}  EU = {kv.Value:F4}");
        }

        static string AskChoice(string[] states, string defaultState, string prompt)
        {
            for (int i = 0; i < states.Length; i++)
            {
                bool isDefault = states[i] == defaultState;
                Console.WriteLine($"   [{i + 1}] {states[i]}{(isDefault ? "  ← suggested" : "")}");
            }

            while (true)
            {
                string suffix = defaultState != null
                    ? $"[1-{states.Length}, Enter=suggested]"
                    : $"[1-{states.Length}]";
                string raw = Prompt($"  {prompt} {suffix}").Trim();

                if (string.IsNullOrEmpty(raw) && defaultState != null)
                    return defaultState;

                if (int.TryParse(raw, out int idx) && idx >= 1 && idx <= states.Length)
                    return states[idx - 1];

                string byName = states.FirstOrDefault(
                    s => s.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;

                Warn($"  Enter a number 1–{states.Length}, or the state name.");
            }
        }

        static bool AskYesNo(string question)
        {
            while (true)
            {
                string ans = Prompt($"{question} [y/n]").Trim().ToLowerInvariant();
                if (ans == "y" || ans == "yes") return true;
                if (ans == "n" || ans == "no") return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  UTILITY / FORMATTING
        // ══════════════════════════════════════════════════════════════════

        static string ResolveModelPath(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0])) return args[0];

            string def = Path.Combine(AppContext.BaseDirectory, "Models", "Network2.xdsl");
            if (File.Exists(def)) return def;

            Console.WriteLine("\n  Default model not found.");
            string path = Prompt("  Path to .xdsl file").Trim().Trim('"');
            if (!File.Exists(path)) { Error($"File not found: {path}"); return null; }
            return path;
        }

        static void PrintMenu(bool sequential)
        {
            string mode = sequential ? "Sequential" : "Flat";
            Console.WriteLine($"  ┌──────────────────────────────────────────┐");
            Console.WriteLine($"  │  Mode: {mode,-34}│");
            Console.WriteLine($"  ├──────────────────────────────────────────┤");
            Console.WriteLine($"  │  1  Interactive (you decide)             │");
            Console.WriteLine($"  │  2  Automatic  (SMILE decides)           │");
            Console.WriteLine($"  │  3  Change model mode                    │");
            Console.WriteLine($"  │  4  Debug (nodes + time steps)           │");
            Console.WriteLine($"  │  q  Quit                                 │");
            Console.WriteLine($"  └──────────────────────────────────────────┘");
        }

        static void PrintBanner()
        {
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║          GeNIe Influence Diagram Analyzer            ║");
            Console.WriteLine("  ║     Interactive · Sequential · Decision Console      ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════╝");
        }

        static string Header(string t)
        {
            int pad = Math.Max(0, 52 - t.Length);
            return $"╔══ {t} {new string('═', pad)}╗";
        }

        static string SubHeader(string t) => $"  ┌─ {t}";

        static string StepHeader(int step, int total)
        {
            string label = $" STEP {step} / {total} ";
            int width = 50;
            int left = (width - label.Length) / 2;
            int right = width - label.Length - left;
            return "  ╠" + new string('═', left) + label + new string('═', right) + "╣";
        }

        static string Prompt(string label)
        {
            Console.Write($"\n  {label}: ");
            return Console.ReadLine() ?? "";
        }

        static void Warn(string msg) => WriteColored(msg, ConsoleColor.Yellow);
        static void Error(string msg) => WriteColored($"ERROR: {msg}", ConsoleColor.Red);
        static void Ok(string msg) => WriteColored(msg, ConsoleColor.Green);

        static void WriteColored(string msg, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = old;
        }
    }
}