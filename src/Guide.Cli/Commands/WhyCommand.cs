using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Semantic;

namespace Guide.Cli.Commands
{
    public static class WhyCommand
    {
        public static async Task<int> InvokeAsync(string path, string anchor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(anchor))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Error: The anchor entity name is required.");
                    Console.ResetColor();
                    return 1;
                }

                // 1. Locate repository root
                string repoRoot = Path.GetFullPath(path);
                DirectoryInfo? dir = new DirectoryInfo(repoRoot);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    {
                        repoRoot = dir.FullName;
                        break;
                    }
                    dir = dir.Parent;
                }

                string guideDir = Path.Combine(repoRoot, ".guide");
                string dbPath = Path.Combine(guideDir, "project_graph.db");

                if (!File.Exists(dbPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: SQLite database does not exist at {dbPath}. Please run the 'init' and 'index' commands first.");
                    Console.ResetColor();
                    return 1;
                }

                // 2. Initialize database store & context engine
                string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
                SqliteKnowledgeStore store = new SqliteKnowledgeStore(connectionString);
                ContextEngine contextEngine = new ContextEngine(store);

                int latestVersion = await store.GetLatestGraphVersionAsync();
                if (latestVersion == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: Graph is empty. Please run 'index' first.");
                    Console.ResetColor();
                    return 0;
                }

                ExtractedKnowledge snapshot = await store.GetSnapshotAsync(latestVersion);
                List<RichNode> nodes = snapshot.Nodes.ToList();
                List<RichEdge> edges = snapshot.Edges.ToList();

                // Find anchor node
                List<RichNode> anchorNodes = nodes
                    .Where(n => string.Equals(n.Name, anchor, StringComparison.OrdinalIgnoreCase) && n.NodeType != "ADR")
                    .ToList();

                if (!anchorNodes.Any())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: Anchor node '{anchor}' not found in the semantic graph.");
                    Console.ResetColor();
                    return 1;
                }

                foreach (RichNode anchorNode in anchorNodes)
                {
                    Console.WriteLine("================================================================================");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"EXPLANATION FOR: {anchorNode.Name}");
                    Console.ResetColor();
                    Console.WriteLine("================================================================================");
                    Console.WriteLine();
                    Console.WriteLine($"{anchorNode.Name} is a {anchorNode.NodeType} (Confidence: {anchorNode.Confidence:F2})");
                    Console.WriteLine($"  File Path: {anchorNode.FilePath}");
                    if (!string.IsNullOrEmpty(anchorNode.Namespace))
                    {
                        Console.WriteLine($"  Namespace: {anchorNode.Namespace}");
                    }
                    Console.WriteLine();

                    // Find exposing nodes (API/Controller)
                    List<(RichNode Node, RichEdge Edge)> exposing = new List<(RichNode Node, RichEdge Edge)>();
                    IEnumerable<RichEdge> incomingEdges = edges.Where(e => e.ToNodeId == anchorNode.Id);
                    foreach (RichEdge edge in incomingEdges)
                    {
                        RichNode? source = nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
                        if (source != null && (source.NodeType == "API" || source.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)))
                        {
                            exposing.Add((source, edge));
                        }
                    }

                    // Find testing nodes
                    List<(RichNode Node, RichEdge Edge)> testing = new List<(RichNode Node, RichEdge Edge)>();
                    foreach (RichEdge edge in incomingEdges)
                    {
                        RichNode? source = nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
                        if (source != null && (source.NodeType == "UnitTest" || source.NodeType == "PlaywrightTest" || source.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)))
                        {
                            testing.Add((source, edge));
                        }
                    }

                    // Find governing ADRs
                    List<RichNode> governing = new List<RichNode>();
                    foreach (RichNode node in nodes.Where(n => n.NodeType == "ADR"))
                    {
                        if (node.Properties.TryGetValue("AppliesTo", out string? appliesTo) && !string.IsNullOrWhiteSpace(appliesTo))
                        {
                            if (appliesTo.Contains(anchorNode.Name, StringComparison.OrdinalIgnoreCase) ||
                                appliesTo.Contains(anchorNode.NodeType, StringComparison.OrdinalIgnoreCase) ||
                                (anchorNode.FilePath != null && appliesTo.Contains(Path.GetFileNameWithoutExtension(anchorNode.FilePath), StringComparison.OrdinalIgnoreCase)))
                            {
                                governing.Add(node);
                            }
                        }
                    }

                    // Print structured relationships
                    Console.WriteLine("Detected Relations:");

                    if (exposing.Any())
                    {
                        foreach ((RichNode node, RichEdge edge) in exposing)
                        {
                            Console.WriteLine($"  ├── Exposed by: {node.Name} ({node.NodeType}, Node Confidence: {node.Confidence:F2}, Edge Confidence: {edge.Confidence:F2})");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  ├── Exposed by: None detected");
                    }

                    if (testing.Any())
                    {
                        foreach ((RichNode node, RichEdge edge) in testing)
                        {
                            Console.WriteLine($"  ├── Tested by:  {node.Name} ({node.NodeType}, Node Confidence: {node.Confidence:F2}, Edge Confidence: {edge.Confidence:F2})");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  ├── Tested by:  None detected");
                    }

                    if (governing.Any())
                    {
                        foreach (RichNode adr in governing)
                        {
                            Console.WriteLine($"  └── Governed by: {adr.Name} (ADR, Confidence: {adr.Confidence:F2})");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  └── Governed by: None detected");
                    }

                    Console.WriteLine();

                    // Print BFS contexts
                    Console.WriteLine("Context BFS Traversal Path details:");
                    ContextResult contextResult = await contextEngine.BuildContextAsync(anchorNode.Name, 3);
                    if (contextResult.Explanations.Any())
                    {
                        foreach (ContextExplanation exp in contextResult.Explanations)
                        {
                            Console.WriteLine($"  - {exp.TargetName}: {exp.Reason} (Chain: {exp.RelationshipChain})");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  - No BFS context details found.");
                    }

                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Structured Explanatory Chain:");
                    Console.ResetColor();

                    List<string> chainParts = new List<string> { $"{anchorNode.Name} ({anchorNode.NodeType}, Conf: {anchorNode.Confidence:F2})" };

                    if (exposing.Any())
                    {
                        string expNames = string.Join(", ", exposing.Select(x => $"{x.Node.Name} (Conf: {x.Edge.Confidence:F2})"));
                        chainParts.Add($"exposed by {expNames}");
                    }
                    if (testing.Any())
                    {
                        string testNames = string.Join(", ", testing.Select(x => $"{x.Node.Name} (Conf: {x.Edge.Confidence:F2})"));
                        chainParts.Add($"tested by {testNames}");
                    }
                    if (governing.Any())
                    {
                        string adrNames = string.Join(", ", governing.Select(x => $"{x.Name} (Conf: {x.Confidence:F2})"));
                        chainParts.Add($"governed by {adrNames}");
                    }

                    Console.WriteLine("  " + string.Join(" ──> ", chainParts));
                    Console.WriteLine();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during why explanation: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }
    }
}
