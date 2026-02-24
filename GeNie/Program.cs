using Smile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeNie
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //Network net = new Network();
            string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "Esercizio1.xdsl"); 

            var analyzer = new InfluenceDiagramAnalyzer(modelPath);
            Console.WriteLine(">>> Scenario: No evidence");
            analyzer.ComputeOptimalDecisions().Print();

            Console.WriteLine("\n>>> Scenario: With evidence");
            analyzer.SetEvidence("Research_result", "High");
            analyzer.ComputeOptimalDecisions().Print();

            analyzer.ClearAllEvidence();
        }

    }
}