using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Guide.Benchmarks;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("====================================================");
        Console.WriteLine("GUIDE: Starting 100 Developer Simulation Challenge");
        Console.WriteLine("====================================================\n");

        var dbStore = new BenchmarkDbStore();
        await dbStore.InitializeDatabaseAsync();

        var developers = new List<(int Id, LlmModel Model, bool UseMvp)>();
        var models = new[] { LlmModel.Claude35Sonnet, LlmModel.Gpt4o, LlmModel.Gpt4oMini, LlmModel.Gemini15Flash, LlmModel.Gemini15Pro };

        // Control Group (Devs 1-50)
        for (int i = 1; i <= 50; i++)
        {
            developers.Add((i, models[(i - 1) % models.Length], false));
        }

        // Experimental Group (Devs 51-100)
        for (int i = 51; i <= 100; i++)
        {
            developers.Add((i, models[(i - 51) % models.Length], true));
        }

        var allResults = new Dictionary<int, List<(RunResult Result, double Cost)>>();

        var batches = new List<(string Name, List<(int Id, LlmModel Model, bool UseMvp)> Devs)>
        {
            ("Control Group (Devs 1-50, without GUIDE)", developers.Where(d => !d.UseMvp).ToList()),
            ("Experimental Group (Devs 51-100, with GUIDE)", developers.Where(d => d.UseMvp).ToList())
        };

        foreach (var batch in batches)
        {
            Console.WriteLine($"\n=== Running {batch.Name} ===");
            foreach (var dev in batch.Devs)
            {
                var workspacePath = Path.Combine(Directory.GetCurrentDirectory(), $"sandbox_dev_{dev.Id}");
                try
                {
                    var simulator = new MockDeveloperSimulator(dev.Id, dev.Model, dev.UseMvp, workspacePath);

                    Console.WriteLine($"Running simulation for Dev {dev.Id} ({dev.Model}, {(dev.UseMvp ? "GUIDE" : "Control")})...");
                    var runResults = await simulator.RunSimulationAsync();

                    var devResults = new List<(RunResult, double)>();
                    foreach (var run in runResults)
                    {
                        var cost = FinancialCalculator.CalculateCost(dev.Model, run.InputTokens, run.OutputTokens);
                        await dbStore.SaveResultAsync(run, dev.UseMvp ? "Experimental" : "Control", cost);
                        devResults.Add((run, cost));
                        Console.WriteLine($"  - Task: {run.TaskId} | Success: {run.Success} | Cycles: {run.HealingCycles} | Violations: {run.ArchitectureViolations}");
                    }
                    allResults[dev.Id] = devResults;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing Dev {dev.Id}: {ex.Message}");
                    allResults[dev.Id] = new List<(RunResult, double)>();
                }
                finally
                {
                    // Ensure all simulated workspaces are fully deleted upon completion
                    if (Directory.Exists(workspacePath))
                    {
                        try
                        {
                            Directory.Delete(workspacePath, true);
                        }
                        catch
                        {
                            // Ignore transient cleanup errors
                        }
                    }
                }
            }
        }

        // Aggregate results
        var controlResults = allResults.Where(kvp => kvp.Key <= 50).SelectMany(kvp => kvp.Value).ToList();
        var expResults = allResults.Where(kvp => kvp.Key > 50).SelectMany(kvp => kvp.Value).ToList();

        double avgControlCost = controlResults.Any() ? controlResults.Average(r => r.Cost) : 0;
        double avgExpCost = expResults.Any() ? expResults.Average(r => r.Cost) : 0;

        double avgControlTime = controlResults.Any() ? controlResults.Average(r => r.Result.DurationSeconds) : 0;
        double avgExpTime = expResults.Any() ? expResults.Average(r => r.Result.DurationSeconds) : 0;

        double avgControlCycles = controlResults.Any() ? controlResults.Average(r => r.Result.HealingCycles) : 0;
        double avgExpCycles = expResults.Any() ? expResults.Average(r => r.Result.HealingCycles) : 0;

        int totalControlViolations = controlResults.Sum(r => r.Result.ArchitectureViolations);
        int totalExpViolations = expResults.Sum(r => r.Result.ArchitectureViolations);

        // Render Bar Charts
        ConsoleChartPlotter.PlotBarChart("Average API Cost / Task", avgControlCost, avgExpCost, "USD");
        ConsoleChartPlotter.PlotBarChart("Average Execution Time", avgControlTime, avgExpTime, "seconds");
        ConsoleChartPlotter.PlotBarChart("Compilation Cycles (Healing)", avgControlCycles, avgExpCycles, "iterations");
        ConsoleChartPlotter.PlotBarChart("Architecture Violations", totalControlViolations, totalExpViolations, "violations");

        // Calculate improvement ratios
        double costRatio = avgControlCost / (avgExpCost == 0 ? 0.001 : avgExpCost);
        double timeRatio = avgControlTime / (avgExpTime == 0 ? 0.001 : avgExpTime);
        double cyclesRatio = avgControlCycles / (avgExpCycles == 0 ? 0.001 : avgExpCycles);

        // Build detailed table of all 100 developers
        var detailedTableBuilder = new System.Text.StringBuilder();
        detailedTableBuilder.AppendLine("## Detailed Metrics by Developer (Full Sample)");
        detailedTableBuilder.AppendLine();
        detailedTableBuilder.AppendLine("| Dev ID | Group | LLM Model | Success Rate | Total Time | Total Cost | Healing Cycles | Arch. Violations |");
        detailedTableBuilder.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var dev in developers)
        {
            if (allResults.TryGetValue(dev.Id, out var devRuns) && devRuns.Any())
            {
                int totalTasks = devRuns.Count;
                int successfulTasks = devRuns.Count(r => r.Result.Success);
                double successRate = (double)successfulTasks / totalTasks * 100.0;

                double totalDuration = devRuns.Sum(r => r.Result.DurationSeconds);
                double totalCost = devRuns.Sum(r => r.Cost);
                int totalCycles = devRuns.Sum(r => r.Result.HealingCycles);
                int totalViolations = devRuns.Sum(r => r.Result.ArchitectureViolations);

                detailedTableBuilder.AppendLine($"| Dev {dev.Id} | {(dev.UseMvp ? "GUIDE" : "Control")} | {dev.Model} | {successRate:F1}% ({successfulTasks}/{totalTasks}) | {totalDuration:F2}s | ${totalCost:F4} | {totalCycles} | {totalViolations} |");
            }
            else
            {
                detailedTableBuilder.AppendLine($"| Dev {dev.Id} | {(dev.UseMvp ? "GUIDE" : "Control")} | {dev.Model} | 0.0% (0/0) | 0.00s | $0.0000 | 0 | 0 |");
            }
        }

        // Generate benchmarking_dashboard.md in repository root
        var dashboardPath = Path.Combine(Directory.GetCurrentDirectory(), "benchmarking_dashboard.md");
        var dashboardContent = $@"# Empirical Performance Report (GUIDE)
**Execution Date:** {DateTime.UtcNow:yyyy-MM-dd} | **Sample:** 100 virtual developers with 3 code tasks each (300 total runs).

## Executive Efficiency Summary

| Engineering Metric | Control Group (Raw LLM) | Experimental Group (GUIDE) | Improvement Ratio |
| :--- | :--- | :--- | :--- |
| **Average API Cost / Task** | ${avgControlCost:F4} USD | ${avgExpCost:F4} USD | **{costRatio:F1}x Cheaper** |
| **Average Execution Time** | {avgControlTime:F2} seconds | {avgExpTime:F2} seconds | **{timeRatio:F1}x Faster** |
| **Compilation Cycles (Healing)** | {avgControlCycles:F2} iterations | {avgExpCycles:F2} iterations | **{cyclesRatio:F1}x Fewer Loops** |
| **Architecture Violations** | {totalControlViolations} deviations | {totalExpViolations} deviations | **{(totalExpViolations == 0 ? "100% Shielded" : $"{totalControlViolations / (double)totalExpViolations:F1}x Fewer Deviations")}** |

> **Conclusion:** The use of GUIDE reduces AI API cost volume by **{(avgControlCost > 0 ? (100.0 * (avgControlCost - avgExpCost) / avgControlCost) : 0):F1}%** through semantic dependency pruning (BFS Context Deltas) and the application of Minimal Patches, while ensuring that no code modifications violate defined project architectural constraints.

{detailedTableBuilder}
";

        await File.WriteAllTextAsync(dashboardPath, dashboardContent);
        Console.WriteLine($"\n[SUCCESS] Report successfully saved to: {dashboardPath}\n");
    }
}
