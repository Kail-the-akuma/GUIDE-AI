using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Models;
using Guide.Semantic;

namespace Guide.Cli.Commands
{
    public static class IndexCommand
    {
        public static async Task<int> InvokeAsync(string path)
        {
            try
            {
                string repoRoot = Path.GetFullPath(path);
                DirectoryInfo? dir = new DirectoryInfo(repoRoot);
                bool gitFound = false;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    {
                        repoRoot = dir.FullName;
                        gitFound = true;
                        break;
                    }
                    dir = dir.Parent;
                }

                Console.WriteLine($"Indexing repository at: {repoRoot}");
                if (!gitFound)
                {
                    Console.WriteLine("Warning: No .git directory found in target path or parent directories.");
                }

                string guideDir = Path.Combine(repoRoot, ".guide");
                if (!Directory.Exists(guideDir))
                {
                    Directory.CreateDirectory(guideDir);
                }

                string solutionPath = FindSolutionPath(repoRoot);
                Console.WriteLine($"Solution/Project path to index: {solutionPath}");

                string dbPath = Path.Combine(guideDir, "project_graph.db");
                string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";

                SqliteKnowledgeStore store = new SqliteKnowledgeStore(connectionString);

                int latestVersion = await store.GetLatestGraphVersionAsync();
                int newVersion = latestVersion + 1;
                Console.WriteLine($"Current graph version: {latestVersion}. New target version: {newVersion}");

                ExtractedKnowledge? oldSnapshot = null;
                if (latestVersion > 0)
                {
                    oldSnapshot = await store.GetSnapshotAsync(latestVersion);
                }

                ProjectParser parser = new ProjectParser(store);
                Console.WriteLine("Parsing project sources...");
                
                IEnumerable<Microsoft.CodeAnalysis.SyntaxTree> parsedEnumerable = await parser.ParseProjectSourcesAsync(solutionPath, CancellationToken.None);
                List<Microsoft.CodeAnalysis.SyntaxTree> parsedTrees;
                if (parsedEnumerable != null)
                {
                    parsedTrees = parsedEnumerable.ToList();
                }
                else
                {
                    parsedTrees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
                }
                Console.WriteLine($"Parsed {parsedTrees.Count} changed or new C# files.");

                KnowledgeExtractor extractor = new KnowledgeExtractor(oldSnapshot?.Nodes);
                ExtractedKnowledge newKnowledge = extractor.Extract(parsedTrees);

                TypeScriptParser tsParser = new TypeScriptParser();
                DependencyGraph tsGraph = await tsParser.BuildGraphAsync(repoRoot);

                int maxCSharpId = newKnowledge.Nodes.Any() ? newKnowledge.Nodes.Max(n => n.Id ?? 0) : 0;
                List<RichNode> tsNodes = new List<RichNode>();
                List<RichEdge> tsEdges = new List<RichEdge>();
                Dictionary<int, int> tsIdMap = new Dictionary<int, int>();

                foreach (RichNode node in tsGraph.Nodes)
                {
                    if (node.Id.HasValue)
                    {
                        int newId = node.Id.Value + maxCSharpId;
                        tsIdMap[node.Id.Value] = newId;
                        tsNodes.Add(node with { Id = newId });
                    }
                }

                foreach (RichEdge edge in tsGraph.Edges)
                {
                    if (edge.FromNodeId.HasValue && edge.ToNodeId.HasValue)
                    {
                        int newFrom;
                        if (tsIdMap.TryGetValue(edge.FromNodeId.Value, out int f))
                        {
                            newFrom = f;
                        }
                        else
                        {
                            newFrom = edge.FromNodeId.Value + maxCSharpId;
                        }

                        int newTo;
                        if (tsIdMap.TryGetValue(edge.ToNodeId.Value, out int t))
                        {
                            newTo = t;
                        }
                        else
                        {
                            newTo = edge.ToNodeId.Value + maxCSharpId;
                        }

                        tsEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
                    }
                }

                newKnowledge = new ExtractedKnowledge(
                    newKnowledge.Nodes.Concat(tsNodes).ToList(),
                    newKnowledge.Edges.Concat(tsEdges).ToList()
                );

                ExtractedKnowledge mergedKnowledge;
                if (oldSnapshot != null)
                {
                    HashSet<string> modifiedFiles = parsedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (RichNode oldNode in oldSnapshot.Nodes)
                    {
                        if (tsParser.CanParse(Path.GetExtension(oldNode.FilePath)))
                        {
                            modifiedFiles.Add(oldNode.FilePath);
                        }
                    }

                    foreach (RichNode tsNode in tsNodes)
                    {
                        modifiedFiles.Add(tsNode.FilePath);
                    }

                    mergedKnowledge = MergeKnowledge(oldSnapshot, newKnowledge, modifiedFiles);
                }
                else
                {
                    mergedKnowledge = newKnowledge;
                }

                await store.SaveGraphSnapshotAsync(newVersion, mergedKnowledge);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Successfully indexed graph version {newVersion} ({mergedKnowledge.Nodes.Count()} nodes, {mergedKnowledge.Edges.Count()} edges).");
                Console.ResetColor();

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during indexing: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static string FindSolutionPath(string repoRoot)
        {
            string[] slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            string[] csprojFiles = Directory.GetFiles(repoRoot, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
            {
                return csprojFiles[0];
            }

            return repoRoot;
        }

        private static ExtractedKnowledge MergeKnowledge(ExtractedKnowledge oldKnowledge, ExtractedKnowledge newKnowledge, HashSet<string> modifiedFiles)
        {
            List<RichNode> remainingOldNodes = oldKnowledge.Nodes
                .Where(n => n.Id.HasValue && !modifiedFiles.Contains(n.FilePath))
                .ToList();

            HashSet<int> remainingOldNodeIds = remainingOldNodes.Select(n => n.Id!.Value).ToHashSet();

            List<RichEdge> remainingOldEdges = oldKnowledge.Edges
                .Where(e => e.FromNodeId.HasValue && e.ToNodeId.HasValue &&
                            remainingOldNodeIds.Contains(e.FromNodeId.Value) &&
                            remainingOldNodeIds.Contains(e.ToNodeId.Value))
                .ToList();

            List<RichNode> allNodes = new List<RichNode>();
            Dictionary<int, int> idMap = new Dictionary<int, int>();
            int newIdCounter = 1;

            foreach (RichNode node in remainingOldNodes)
            {
                int oldId = node.Id!.Value;
                RichNode reindexedNode = node with { Id = newIdCounter };
                idMap[oldId] = newIdCounter;
                allNodes.Add(reindexedNode);
                newIdCounter++;
            }

            Dictionary<int, int> newIdMap = new Dictionary<int, int>();
            foreach (RichNode node in newKnowledge.Nodes)
            {
                if (node.Id.HasValue)
                {
                    int tempId = node.Id.Value;
                    RichNode reindexedNode = node with { Id = newIdCounter };
                    newIdMap[tempId] = newIdCounter;
                    allNodes.Add(reindexedNode);
                    newIdCounter++;
                }
            }

            List<RichEdge> allEdges = new List<RichEdge>();

            foreach (RichEdge edge in remainingOldEdges)
            {
                int newFrom = idMap[edge.FromNodeId!.Value];
                int newTo = idMap[edge.ToNodeId!.Value];
                allEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
            }

            foreach (RichEdge edge in newKnowledge.Edges)
            {
                int? newFrom = null;
                if (edge.FromNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.FromNodeId.Value, out int valFrom))
                    {
                        newFrom = valFrom;
                    }
                    else if (idMap.TryGetValue(edge.FromNodeId.Value, out int oValFrom))
                    {
                        newFrom = oValFrom;
                    }
                }

                int? newTo = null;
                if (edge.ToNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.ToNodeId.Value, out int valTo))
                    {
                        newTo = valTo;
                    }
                    else if (idMap.TryGetValue(edge.ToNodeId.Value, out int oValTo))
                    {
                        newTo = oValTo;
                    }
                }

                if (newFrom.HasValue && newTo.HasValue)
                {
                    allEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
                }
            }

            return new ExtractedKnowledge(allNodes, allEdges);
        }
    }
}
