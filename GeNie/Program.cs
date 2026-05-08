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

            // Detect plate structure once at startup (also warms up SMILE)
            TemporalPlateInfo plate = analyzer.DetectTemporalPlate();
            bool isSequential = AskModelMode(analyzer, plate);

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
                        plate = analyzer.DetectTemporalPlate();
                        isSequential = AskModelMode(analyzer, plate);
                        break;
                    case "4":
                        analyzer.DebugPrintAllNodes();
                        analyzer.DebugPrintTimeSteps();
                        break;
                    case "5":
                        analyzer.DebugPrintRawValues();
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
        static bool AskModelMode(InfluenceDiagramAnalyzer analyzer, TemporalPlateInfo plate)
        {
            Console.WriteLine("\n" + Header("MODEL TYPE SELECTION"));

            if (plate.IsTemporalPlate)
            {
                Console.WriteLine($"\n  Temporal plate detected: {plate.StepCount} time steps.");
                string plateNodeNames = string.Join(", ", plate.PlateNodes.Select(h => analyzer.GetNodeId(h)));
                Console.WriteLine($"  Plate nodes: [{plateNodeNames}]");
            }
            else
            {
                Console.WriteLine("\n  No temporal plate detected — flat model.");
                List<TimeStep> steps = analyzer.BuildTimeSteps();
                foreach (var step in steps)
                {
                    string obsNames = string.Join(", ", step.InformationalChanceNodes.Select(h => analyzer.GetNodeId(h)));
                    string decNames = string.Join(", ", step.DecisionNodes.Select(h => analyzer.GetNodeId(h)));
                    string utilNames = string.Join(", ", step.StageUtilityNodes.Select(h => analyzer.GetNodeId(h)));
                    Console.WriteLine($"\n  Step {step.StepLabel}:");
                    Console.WriteLine($"    Observe : [{obsNames}]");
                    Console.WriteLine($"    Decide  : [{decNames}]");
                    Console.WriteLine($"    Utility : [{utilNames}]");
                }
            }

            Console.WriteLine();
            Console.WriteLine("  [1] Flat / non-sequential  – all evidence up-front, one inference pass");
            Console.WriteLine("  [2] Sequential             – step-by-step for all time steps");

            string suggestion = plate.IsTemporalPlate ? "2" : "1";

            while (true)
            {
                string raw = Prompt($"  Select mode [1/2, Enter={suggestion}]").Trim();
                if (string.IsNullOrEmpty(raw)) raw = suggestion;
                if (raw == "1") { Console.WriteLine("  → Flat mode."); return false; }
                if (raw == "2") { Console.WriteLine("  → Sequential mode."); return true; }
                Warn("  Please enter 1 or 2.");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  SEQUENTIAL INTERACTIVE
        //
        //  How it works for a compressed temporal plate (e.g. 5 steps):
        //
        //  1. Call ComputeFullPolicyTable() ONCE — SMILE solves the full
        //     5-step policy in one UpdateBeliefs() pass.
        //     This gives us: for each decision node, a list of 5 DecisionPolicies,
        //     one per time step (sliced from the flat value array).
        //
        //  2. Loop t = 0..4:
        //     a. Show current PRIOR beliefs for observable chance nodes at step t
        //        (sliced from their flat value arrays).
        //     b. Ask user: what do you actually observe? → SetEvidence()
        //     c. Re-run UpdateBeliefs() → re-slice policy at step t
        //        (the recommendation updates to reflect the observation).
        //     d. Show recommended action, ask user to accept or override.
        //     e. Lock decision as evidence.
        //     f. Show stage utility at step t.
        //
        //  This correctly mirrors the sequential structure: at each step you
        //  see new information, get an updated recommendation, and decide.
        // ══════════════════════════════════════════════════════════════════
        static void RunSequentialInteractive(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("SEQUENTIAL INTERACTIVE SESSION"));

            List<TimeStep> steps = analyzer.BuildTimeSteps();
            int T = steps.Count;

            if (T == 0) { Warn("No steps detected."); return; }

            Console.WriteLine($"\n  {T} time step(s) to play through.\n");

            var allChoices = new List<(string stepLabel, string nid, string chosen, bool wasOptimal)>();

            for (int si = 0; si < T; si++)
            {
                TimeStep step = steps[si];

                Console.WriteLine();
                Console.WriteLine(StepBanner(step, T));

                // ── Phase 1: Show prior beliefs then ask for observations ──
                if (step.InformationalChanceNodes.Count > 0)
                {
                    Console.WriteLine("\n  ► OBSERVE");

                    // Show what SMILE currently believes for each observable node
                    Console.WriteLine("    Prior beliefs at this step:");
                    foreach (int cHandle in step.InformationalChanceNodes)
                    {
                        string cid = analyzer.GetNodeId(cHandle);
                        string[] cStates = analyzer.GetStateNames(cHandle);
                        var beliefs = analyzer.GetBeliefAtStep(cHandle, si, T);

                        Console.Write($"    • {cid,-30} ");
                        if (beliefs != null)
                            foreach (var kv in beliefs)
                                Console.Write($"{kv.Key}={kv.Value:F3}  ");
                        else
                            Console.Write("(no beliefs yet)");
                        Console.WriteLine();
                    }

                    // Only prompt for observations on plate chance nodes
                    // (init-condition nodes at step 0 may or may not be observed)
                    if (AskYesNo("\n    Do you want to enter observations?"))
                    {
                        var candidates = step.InformationalChanceNodes
                            .Select(h => (h, analyzer.GetNodeId(h), analyzer.GetStateNames(h)))
                            .ToList();
                        CollectEvidence(analyzer, candidates);

                        // Re-run inference so the policy updates to reflect observations
                        TryUpdateBeliefs(analyzer);
                    }
                }
                else
                {
                    Console.WriteLine("\n  (No observable nodes at this step.)");
                }

                // ── Phase 2: Decide ────────────────────────────────────────
                if (step.DecisionNodes.Count > 0)
                {
                    Console.WriteLine("\n  ► DECIDE");

                    for (int di = 0; di < step.DecisionNodes.Count; di++)
                    {
                        int dHandle = step.DecisionNodes[di];
                        string nid = analyzer.GetNodeId(dHandle);
                        string[] states = analyzer.GetStateNames(dHandle);

                        // Extract policy slice for THIS step index
                        DecisionPolicy policy = analyzer.ExtractPolicyAtStep(dHandle, si);

                        PrintDecisionBlock(nid, policy, states);

                        string chosen = AskChoice(states, policy?.OptimalState,
                            $"Your choice for '{nid}'");
                        bool wasOptimal = policy != null && chosen == policy.OptimalState;

                        allChoices.Add((step.StepLabel, nid, chosen, wasOptimal));

                        // Lock decision as evidence
                        try { analyzer.SetEvidence(nid, chosen); } catch { }

                        // Re-infer if more decisions follow in this step
                        if (di < step.DecisionNodes.Count - 1)
                            TryUpdateBeliefs(analyzer);
                    }
                }

                // ── Phase 3: Stage utility ────────────────────────────────
                if (step.StageUtilityNodes.Count > 0)
                {
                    TryUpdateBeliefs(analyzer);
                    Console.WriteLine("\n  ► STAGE UTILITY");
                    foreach (int uHandle in step.StageUtilityNodes)
                    {
                        string uid = analyzer.GetNodeId(uHandle);
                        double uv = analyzer.GetUtilityAtStep(uHandle, si, T);
                        string uvStr = double.IsNaN(uv) ? "N/A" : uv.ToString("F4");
                        Console.WriteLine($"    {uid,-32}  EU = {uvStr}");
                    }
                }
            }

            // ── Final total utility ────────────────────────────────────────
            DecisionResult final = new DecisionResult();
            try { final = analyzer.ComputeOptimalDecisions(); } catch { }

            // ── Summary ────────────────────────────────────────────────────
            Console.WriteLine("\n" + Header("SESSION SUMMARY"));
            Console.WriteLine("\n  Your decisions:");
            string lastLabel = null;
            foreach (var (stepLabel, nid, chosen, wasOptimal) in allChoices)
            {
                if (stepLabel != lastLabel)
                {
                    Console.WriteLine($"\n  Step {stepLabel}:");
                    lastLabel = stepLabel;
                }
                string tag = wasOptimal ? " ✓ optimal" : " (custom)";
                Console.WriteLine($"    {nid,-32} → {chosen}{tag}");
            }

            PrintTotalUtilities(final);
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  FLAT INTERACTIVE
        // ══════════════════════════════════════════════════════════════════
        static void RunFlatInteractive(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("FLAT INTERACTIVE SESSION"));

            if (AskYesNo("\n  Enter any observed evidence now?"))
                CollectEvidence(analyzer, analyzer.GetChanceNodeInfo());

            DecisionResult baseline = analyzer.ComputeOptimalDecisions();
            List<int> decisions = analyzer.GetDecisionNodesOrdered();
            if (decisions.Count == 0) { Warn("No decision nodes found."); return; }

            var choices = new List<(string nid, string chosen, bool wasOptimal)>();

            foreach (int dHandle in decisions)
            {
                string nid = analyzer.GetNodeId(dHandle);
                string[] states = analyzer.GetStateNames(dHandle);
                DecisionPolicy p = baseline.Policies.FirstOrDefault(x => x.NodeId == nid);

                PrintDecisionBlock(nid, p, states);
                string chosen = AskChoice(states, p?.OptimalState, $"Your choice for '{nid}'");
                bool wasOptimal = p != null && chosen == p.OptimalState;
                choices.Add((nid, chosen, wasOptimal));

                try { analyzer.SetEvidence(nid, chosen); } catch { }
            }

            DecisionResult final = analyzer.ComputeOptimalDecisions();
            PrintSummary(choices, final);
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  AUTO SESSION
        // ══════════════════════════════════════════════════════════════════
        static void RunAutoSession(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();
            Console.WriteLine("\n" + Header("AUTOMATIC (SMILE-OPTIMAL) SESSION"));

            if (AskYesNo("\n  Enter any observed evidence first?"))
                CollectEvidence(analyzer, analyzer.GetChanceNodeInfo());

            Console.WriteLine();

            // For a temporal plate: show the full policy table across all steps
            TemporalPlateInfo plate = analyzer.DetectTemporalPlate();
            if (plate.IsTemporalPlate)
            {
                var table = analyzer.ComputeFullPolicyTable();
                int T = plate.StepCount;

                Console.WriteLine("  SMILE optimal policy across all steps:\n");
                foreach (var kv in table)
                {
                    Console.WriteLine($"  Decision node: {kv.Key}");
                    for (int t = 0; t < kv.Value.Count; t++)
                    {
                        DecisionPolicy p = kv.Value[t];
                        if (p == null) { Console.WriteLine($"    Step {t + 1}: (no data)"); continue; }
                        Console.Write($"    Step {t + 1}: → [{p.OptimalState}]   EU per option: ");
                        foreach (var eu in p.ExpectedUtilityPerState)
                            Console.Write($"{eu.Key}={eu.Value:F4}  ");
                        Console.WriteLine();
                    }
                    Console.WriteLine();
                }

                // Show total utility
                DecisionResult r = analyzer.ComputeOptimalDecisions();
                PrintTotalUtilities(r);
            }
            else
            {
                analyzer.ComputeOptimalDecisions().Print();
            }

            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  SHARED UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        static void PrintDecisionBlock(string nodeId, DecisionPolicy policy, string[] states)
        {
            Console.WriteLine();
            Console.WriteLine(SubHeader($"Decision: {nodeId}"));

            if (policy?.ExpectedUtilityPerState != null &&
                policy.ExpectedUtilityPerState.Count > 0)
            {
                Console.WriteLine("    Expected utility per option:");
                foreach (var kv in policy.ExpectedUtilityPerState)
                {
                    bool best = kv.Key == policy.OptimalState;
                    string mark = best ? " ★" : "  ";
                    Console.WriteLine($"   {mark} {kv.Key,-26}  EU = {kv.Value:F4}");
                }
                Console.WriteLine($"\n    SMILE suggests → [{policy.OptimalState}]");
            }
            else
            {
                Console.WriteLine("    (No EU data — choosing without suggestion)");
            }
        }

        static void CollectEvidence(
            InfluenceDiagramAnalyzer analyzer,
            List<(int handle, string id, string[] states)> candidates)
        {
            if (candidates.Count == 0) { Warn("  No observable nodes."); return; }

            Console.WriteLine("\n  Observable:");
            foreach (var (_, id, states) in candidates)
                Console.WriteLine($"    {id,-32} states: {string.Join(", ", states)}");

            var allowed = new HashSet<string>(
                candidates.Select(c => c.id), StringComparer.OrdinalIgnoreCase);

            Console.WriteLine("\n  Enter  NodeId=StateName  (blank to finish):");
            while (true)
            {
                string line = Prompt("  Evidence").Trim();
                if (string.IsNullOrEmpty(line)) break;

                int eq = line.IndexOf('=');
                if (eq < 1) { Warn("  Format: NodeId=StateName"); continue; }

                string nid = line.Substring(0, eq).Trim();
                string state = line.Substring(eq + 1).Trim();

                if (!allowed.Contains(nid))
                { Warn($"  '{nid}' is not observable at this step."); continue; }

                try { analyzer.SetEvidence(nid, state); Ok($"  ✓ {nid} = {state}"); }
                catch (Exception ex) { Warn($"  ✗ {ex.Message}"); }
            }
        }

        static void PrintSummary(
            List<(string nid, string chosen, bool wasOptimal)> choices,
            DecisionResult final)
        {
            Console.WriteLine("\n" + Header("SESSION SUMMARY"));
            Console.WriteLine("\n  Your decisions:");
            foreach (var (nid, chosen, wasOptimal) in choices)
            {
                string tag = wasOptimal ? " ✓ optimal" : " (custom)";
                Console.WriteLine($"    {nid,-32} → {chosen}{tag}");
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
                    Console.WriteLine($"    {kv.Key,-32}  EU = {kv.Value:F4}");
        }

        static string AskChoice(string[] states, string defaultState, string prompt)
        {
            for (int i = 0; i < states.Length; i++)
                Console.WriteLine(
                    $"   [{i + 1}] {states[i]}{(states[i] == defaultState ? "  ← suggested" : "")}");

            while (true)
            {
                string sfx = defaultState != null
                    ? $"[1-{states.Length}, Enter=suggested]" : $"[1-{states.Length}]";
                string raw = Prompt($"  {prompt} {sfx}").Trim();

                if (string.IsNullOrEmpty(raw) && defaultState != null)
                    return defaultState;
                if (int.TryParse(raw, out int idx) && idx >= 1 && idx <= states.Length)
                    return states[idx - 1];
                string byName = states.FirstOrDefault(
                    s => s.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;

                Warn($"  Enter 1–{states.Length} or a state name.");
            }
        }

        static bool AskYesNo(string question)
        {
            while (true)
            {
                string a = Prompt($"{question} [y/n]").Trim().ToLowerInvariant();
                if (a == "y" || a == "yes") return true;
                if (a == "n" || a == "no") return false;
            }
        }

        static void TryUpdateBeliefs(InfluenceDiagramAnalyzer analyzer)
        {
            try { analyzer.ComputeOptimalDecisions(); } catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        //  FORMATTING
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
            Console.WriteLine($"  ┌──────────────────────────────────────────────┐");
            Console.WriteLine($"  │  Mode: {mode,-38}│");
            Console.WriteLine($"  ├──────────────────────────────────────────────┤");
            Console.WriteLine($"  │  1  Interactive (you decide)                 │");
            Console.WriteLine($"  │  2  Automatic  (SMILE decides)               │");
            Console.WriteLine($"  │  3  Change model mode                        │");
            Console.WriteLine($"  │  4  Debug  (nodes + time steps)              │");
            Console.WriteLine($"  │  5  Debug  (raw node values → file)          │");
            Console.WriteLine($"  │  q  Quit                                     │");
            Console.WriteLine($"  └──────────────────────────────────────────────┘");
        }

        static void PrintBanner()
        {
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║        GeNIe Influence Diagram Analyzer              ║");
            Console.WriteLine("  ║   Interactive · Sequential · Temporal-Plate          ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════╝");
        }

        static string Header(string t)
        {
            int pad = Math.Max(0, 54 - t.Length);
            return $"╔══ {t} {new string('═', pad)}╗";
        }
        static string SubHeader(string t) => $"  ┌─ {t}";

        static string StepBanner(TimeStep step, int total)
        {
            string label = $"  STEP {step.StepLabel} / {total}  ";
            int w = 50; int left = (w - label.Length) / 2;
            int righ = w - label.Length - left;
            return "  ╠" + new string('═', left) + label + new string('═', righ) + "╣";
        }

        static string Prompt(string label)
        {
            Console.Write($"\n  {label}: ");
            return Console.ReadLine() ?? "";
        }

        static void Warn(string m) => Colored(m, ConsoleColor.Yellow);
        static void Error(string m) => Colored("ERROR: " + m, ConsoleColor.Red);
        static void Ok(string m) => Colored(m, ConsoleColor.Green);

        static void Colored(string m, ConsoleColor c)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = c;
            Console.WriteLine(m);
            Console.ForegroundColor = old;
        }
    }
}