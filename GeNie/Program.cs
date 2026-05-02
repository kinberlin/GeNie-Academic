using Smile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeNie
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintBanner();

            // ── Resolve model path ─────────────────────────────────────────
            string modelPath = ResolveModelPath(args);
            if (modelPath == null) return;

            Console.WriteLine($"\n  Model : {Path.GetFileName(modelPath)}");
            Console.WriteLine(new string('─', 60));

            // ── Build analyzer ─────────────────────────────────────────────
            InfluenceDiagramAnalyzer analyzer;
            try
            {
                analyzer = new InfluenceDiagramAnalyzer(modelPath);
            }
            catch (Exception ex)
            {
                Error($"Failed to load model: {ex.Message}");
                return;
            }

            // ── Main interaction loop ──────────────────────────────────────
            bool running = true;
            while (running)
            {
                Console.WriteLine();
                PrintMenu();
                string cmd = Prompt("Command").Trim().ToLowerInvariant();

                switch (cmd)
                {
                    case "1": RunInteractiveSession(analyzer); break;
                    case "2": RunAutoSession(analyzer); break;
                    case "3": analyzer.DebugPrintAllNodes(); break;
                    case "q":
                    case "quit":
                    case "exit":
                        running = false;
                        break;
                    default:
                        Warn("Unknown command. Try 1, 2, 3 or q.");
                        break;
                }
            }

            Console.WriteLine("\n  Goodbye.\n");
        }

        // ══════════════════════════════════════════════════════════════════
        //  INTERACTIVE SESSION  – user picks each decision themselves
        // ══════════════════════════════════════════════════════════════════
        static void RunInteractiveSession(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();

            Console.WriteLine("\n" + Header("INTERACTIVE SESSION"));
            Console.WriteLine("  For each decision node you can accept the suggested");
            Console.WriteLine("  optimal choice or pick any alternative.");

            // ── Optional evidence ──────────────────────────────────────────
            if (AskYesNo("\n  Do you want to enter observed evidence first?"))
                CollectEvidence(analyzer);

            // ── Compute baseline suggestions ──────────────────────────────
            DecisionResult baseline = analyzer.ComputeOptimalDecisions();

            List<int> decisionHandles = analyzer.GetDecisionNodesOrdered();

            if (decisionHandles.Count == 0)
            {
                Warn("No decision nodes found in this model.");
                return;
            }

            // ── Walk through each decision ─────────────────────────────────
            var userChoices = new List<(string nodeId, string chosen)>();

            foreach (int dHandle in decisionHandles)
            {
                string nodeId = analyzer.GetNodeId(dHandle);
                string[] states = analyzer.GetStateNames(dHandle);

                // Find what SMILE suggests for this node
                DecisionPolicy suggestion = baseline.Policies
                    .FirstOrDefault(p => p.NodeId == nodeId);

                Console.WriteLine();
                Console.WriteLine(SubHeader($"Decision: {nodeId}"));

                // Show EU per state if available
                if (suggestion != null)
                {
                    Console.WriteLine("  Expected utility per option:");
                    foreach (var kv in suggestion.ExpectedUtilityPerState)
                    {
                        bool isBest = kv.Key == suggestion.OptimalState;
                        string marker = isBest ? " ★" : "  ";
                        Console.WriteLine($"  {marker} {kv.Key,-20}  EU = {kv.Value:F4}");
                    }
                    Console.WriteLine($"\n  SMILE suggests → [{suggestion.OptimalState}]");
                }
                else
                {
                    Console.WriteLine("  (No EU data – picking blind)");
                    for (int i = 0; i < states.Length; i++)
                        Console.WriteLine($"   [{i + 1}] {states[i]}");
                }

                // Let user choose
                string chosen = AskChoice(states,
                    suggestion?.OptimalState,
                    $"  Your choice for '{nodeId}'");

                userChoices.Add((nodeId, chosen));

                // Feed this decision as evidence so subsequent nodes update
                try { analyzer.SetEvidence(nodeId, chosen); }
                catch { /* decision nodes may not accept SetEvidence in all SMILE builds */ }
            }

            // ── Re-compute with user decisions locked in ──────────────────
            DecisionResult finalResult = analyzer.ComputeOptimalDecisions();

            // ── Summary ────────────────────────────────────────────────────
            Console.WriteLine("\n" + Header("SESSION SUMMARY"));
            Console.WriteLine("\n  Your decisions:");
            foreach (var (nid, chosen) in userChoices)
            {
                DecisionPolicy p = baseline.Policies.FirstOrDefault(pp => pp.NodeId == nid);
                string tag = (p != null && chosen == p.OptimalState) ? " ✓ optimal" : " (custom)";
                Console.WriteLine($"    {nid,-28} → {chosen}{tag}");
            }

            PrintUtilities(finalResult);
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  AUTO SESSION  – SMILE picks everything
        // ══════════════════════════════════════════════════════════════════
        static void RunAutoSession(InfluenceDiagramAnalyzer analyzer)
        {
            analyzer.ClearAllEvidence();

            Console.WriteLine("\n" + Header("AUTOMATIC (SMILE-OPTIMAL) SESSION"));

            if (AskYesNo("\n  Do you want to enter observed evidence first?"))
                CollectEvidence(analyzer);

            DecisionResult result = analyzer.ComputeOptimalDecisions();
            result.Print();
            analyzer.ClearAllEvidence();
        }

        // ══════════════════════════════════════════════════════════════════
        //  EVIDENCE COLLECTION
        // ══════════════════════════════════════════════════════════════════
        static void CollectEvidence(InfluenceDiagramAnalyzer analyzer)
        {
            Console.WriteLine("\n  Available chance nodes:");
            List<(int handle, string id, string[] states)> chances = analyzer.GetChanceNodeInfo();

            if (chances.Count == 0) { Warn("  No chance nodes found."); return; }

            foreach (var (_, id, states) in chances)
                Console.WriteLine($"    {id,-28} states: {string.Join(", ", states)}");

            Console.WriteLine("\n  Enter evidence as  NodeId=StateName  (blank line to stop):");
            while (true)
            {
                string line = Prompt("  Evidence").Trim();
                if (string.IsNullOrEmpty(line)) break;

                int eq = line.IndexOf('=');
                if (eq < 1) { Warn("  Format: NodeId=StateName"); continue; }

                string nid = line.Substring(0, eq).Trim();
                string state = line.Substring(eq + 1).Trim();
                try
                {
                    analyzer.SetEvidence(nid, state);
                    Console.WriteLine($"  ✓ Evidence set: {nid} = {state}");
                }
                catch (Exception ex) { Warn($"  ✗ {ex.Message}"); }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        static string ResolveModelPath(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0])) return args[0];

            string defaultPath = Path.Combine(AppContext.BaseDirectory, "Models", "Network2.xdsl");
            if (File.Exists(defaultPath)) return defaultPath;

            // Interactive picker
            Console.WriteLine("\n  No model found at default path.");
            string path = Prompt("  Enter full path to .xdsl file").Trim().Trim('"');
            if (!File.Exists(path)) { Error($"File not found: {path}"); return null; }
            return path;
        }

        /// <summary>Asks the user to pick from a list of states. Pressing Enter accepts the default.</summary>
        static string AskChoice(string[] states, string defaultState, string prompt)
        {
            for (int i = 0; i < states.Length; i++)
            {
                bool isDefault = states[i] == defaultState;
                Console.WriteLine($"   [{i + 1}] {states[i]}{(isDefault ? "  ← suggested" : "")}");
            }

            while (true)
            {
                string raw = Prompt($"{prompt} [1-{states.Length}, Enter=suggested]").Trim();

                if (string.IsNullOrEmpty(raw) && defaultState != null)
                    return defaultState;

                if (int.TryParse(raw, out int idx) && idx >= 1 && idx <= states.Length)
                    return states[idx - 1];

                // Allow typing the state name directly
                string byName = states.FirstOrDefault(
                    s => s.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;

                Warn($"  Please enter a number between 1 and {states.Length}.");
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

        static void PrintUtilities(DecisionResult result)
        {
            Console.WriteLine("\n  Expected utilities:");
            if (result.ExpectedUtilities.Count == 0)
            {
                Console.WriteLine("    (none returned by SMILE)");
                return;
            }
            foreach (var kv in result.ExpectedUtilities)
                Console.WriteLine($"    {kv.Key,-28}  EU = {kv.Value:F4}");
        }

        // ── Formatting ──────────────────────────────────────────────────
        static string Header(string t) => $"╔══ {t} {new string('═', Math.Max(0, 52 - t.Length))}╗";
        static string SubHeader(string t) => $"  ┌─ {t}";
        static void PrintMenu()
        {
            Console.WriteLine("  ┌─────────────────────────────────┐");
            Console.WriteLine("  │  1  Interactive (you decide)    │");
            Console.WriteLine("  │  2  Automatic  (SMILE decides)  │");
            Console.WriteLine("  │  3  Debug node list             │");
            Console.WriteLine("  │  q  Quit                        │");
            Console.WriteLine("  └─────────────────────────────────┘");
        }
        static void PrintBanner()
        {
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║          GeNIe Influence Diagram Analyzer            ║");
            Console.WriteLine("  ║           Interactive Decision Console               ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════╝");
        }
        static string Prompt(string label)
        {
            Console.Write($"\n  {label}: ");
            return Console.ReadLine() ?? "";
        }
        static void Warn(string msg) => WriteColored(msg, ConsoleColor.Yellow);
        static void Error(string msg) => WriteColored($"ERROR: {msg}", ConsoleColor.Red);
        static void WriteColored(string msg, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = old;
        }
    }
}