using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic
{
    public class CSharpParser : ISemanticParser
    {
        public bool CanParse(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension)) return false;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<DependencyGraph> BuildGraphAsync(string projectPath)
        {
            string repoRoot = Path.GetFullPath(projectPath);
            if (File.Exists(repoRoot))
            {
                repoRoot = Path.GetDirectoryName(repoRoot) ?? repoRoot;
            }
            var dir = new DirectoryInfo(repoRoot);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    repoRoot = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }
            var peaIaDir = Path.Combine(repoRoot, ".guide");
            if (!Directory.Exists(peaIaDir))
            {
                Directory.CreateDirectory(peaIaDir);
            }
            var dbPath = Path.Combine(peaIaDir, "project_graph.db");
            var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
            var store = new SqliteKnowledgeStore(connectionString);

            int latestVersion = await store.GetLatestGraphVersionAsync();
            int newVersion = latestVersion + 1;

            ExtractedKnowledge? oldSnapshot = null;
            if (latestVersion > 0)
            {
                oldSnapshot = await store.GetSnapshotAsync(latestVersion);
            }

            var parser = new ProjectParser(store);
            var parsedTrees = (await parser.ParseProjectSourcesAsync(projectPath, CancellationToken.None)).ToList();

            var extractor = new KnowledgeExtractor(oldSnapshot?.Nodes);
            var newKnowledge = extractor.Extract(parsedTrees);

            ExtractedKnowledge mergedKnowledge;
            if (oldSnapshot != null)
            {
                var modifiedFiles = parsedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                mergedKnowledge = MergeKnowledge(oldSnapshot, newKnowledge, modifiedFiles);
            }
            else
            {
                mergedKnowledge = newKnowledge;
            }

            await store.SaveGraphSnapshotAsync(newVersion, mergedKnowledge);

            var dependencyGraph = new DependencyGraph();
            dependencyGraph.Nodes.AddRange(mergedKnowledge.Nodes);
            dependencyGraph.Edges.AddRange(mergedKnowledge.Edges);

            return dependencyGraph;
        }

        public async Task<CodeContext> GetContextAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new CodeContext { FilePath = filePath };
            }

            string content = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(content);
            var root = await syntaxTree.GetRootAsync();

            var imports = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name?.ToString() ?? string.Empty)
                .ToList();

            var classes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Select(c => c.Identifier.Text)
                .ToList();

            var signatures = new List<string>();
            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var type in types)
            {
                var publicMembers = type.Members
                    .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
                    .Select(m => m.ToString().Trim());
                signatures.AddRange(publicMembers);
            }

            return new CodeContext
            {
                FilePath = filePath,
                Language = "CSharp",
                RawContent = content,
                Imports = imports,
                Classes = classes,
                Signatures = signatures
            };
        }

        private static ExtractedKnowledge MergeKnowledge(ExtractedKnowledge oldKnowledge, ExtractedKnowledge newKnowledge, HashSet<string> modifiedFiles)
        {
            var remainingOldNodes = oldKnowledge.Nodes
                .Where(n => n.Id.HasValue && !modifiedFiles.Contains(n.FilePath))
                .ToList();

            var remainingOldNodeIds = remainingOldNodes.Select(n => n.Id!.Value).ToHashSet();

            var remainingOldEdges = oldKnowledge.Edges
                .Where(e => e.FromNodeId.HasValue && e.ToNodeId.HasValue &&
                            remainingOldNodeIds.Contains(e.FromNodeId.Value) &&
                            remainingOldNodeIds.Contains(e.ToNodeId.Value))
                .ToList();

            var allNodes = new List<RichNode>();
            var idMap = new Dictionary<int, int>();
            int newIdCounter = 1;

            foreach (var node in remainingOldNodes)
            {
                int oldId = node.Id!.Value;
                var reindexedNode = node with { Id = newIdCounter };
                idMap[oldId] = newIdCounter;
                allNodes.Add(reindexedNode);
                newIdCounter++;
            }

            var newIdMap = new Dictionary<int, int>();
            foreach (var node in newKnowledge.Nodes)
            {
                if (node.Id.HasValue)
                {
                    int tempId = node.Id.Value;
                    var reindexedNode = node with { Id = newIdCounter };
                    newIdMap[tempId] = newIdCounter;
                    allNodes.Add(reindexedNode);
                    newIdCounter++;
                }
            }

            var allEdges = new List<RichEdge>();

            foreach (var edge in remainingOldEdges)
            {
                int newFrom = idMap[edge.FromNodeId!.Value];
                int newTo = idMap[edge.ToNodeId!.Value];
                allEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
            }

            foreach (var edge in newKnowledge.Edges)
            {
                int? newFrom = null;
                if (edge.FromNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.FromNodeId.Value, out var valFrom))
                    {
                        newFrom = valFrom;
                    }
                    else if (idMap.TryGetValue(edge.FromNodeId.Value, out var oValFrom))
                    {
                        newFrom = oValFrom;
                    }
                }

                int? newTo = null;
                if (edge.ToNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.ToNodeId.Value, out var valTo))
                    {
                        newTo = valTo;
                    }
                    else if (idMap.TryGetValue(edge.ToNodeId.Value, out var oValTo))
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
