using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic
{
    public class ContextEngine : IContextEngine
    {
        private readonly SqliteKnowledgeStore _store;

        public ContextEngine(SqliteKnowledgeStore store)
        {
            _store = store;
        }

        public async Task<ContextResult> BuildContextAsync(string anchorEntity, int depth, ContextResult? previousContext = null)
        {
            int latestVersion = await _store.GetLatestGraphVersionAsync();
            if (latestVersion == 0)
            {
                return new ContextResult(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<ContextExplanation>()
                );
            }

            var snapshot = await _store.GetSnapshotAsync(latestVersion);
            var nodes = snapshot.Nodes.ToList();
            var edges = snapshot.Edges.ToList();

            var nodeById = nodes.ToDictionary(n => n.Id!.Value);

            // Build adjacency lists
            var outgoing = edges.GroupBy(e => e.FromNodeId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
            var incoming = edges.GroupBy(e => e.ToNodeId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Find start nodes matching anchorEntity (case-insensitive)
            var startNodes = nodes
                .Where(n => string.Equals(n.Name, anchorEntity, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!startNodes.Any())
            {
                return new ContextResult(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<ContextExplanation>()
                );
            }

            var targetFiles = new List<string>();
            var secondarySignatures = new List<string>();
            var explanations = new List<ContextExplanation>();

            var visited = new HashSet<int>();
            var queue = new Queue<BfsState>();

            foreach (var startNode in startNodes)
            {
                visited.Add(startNode.Id!.Value);
                queue.Enqueue(new BfsState(
                    startNode,
                    0,
                    new List<RichNode> { startNode },
                    "Anchor Entity"
                ));
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current.Depth > depth)
                {
                    continue;
                }

                // Add all visited files and signatures to target/secondary context
                if (!string.IsNullOrEmpty(current.Node.FilePath) && !targetFiles.Contains(current.Node.FilePath))
                {
                    targetFiles.Add(current.Node.FilePath);
                }

                if (!string.IsNullOrEmpty(current.Node.PublicSignatures) && !secondarySignatures.Contains(current.Node.PublicSignatures))
                {
                    secondarySignatures.Add(current.Node.PublicSignatures);
                }

                if (current.Depth > 0)
                {
                    var relationshipChain = string.Join(" -> ", current.Path.Select(p => p.Name));
                    explanations.Add(new ContextExplanation(
                        current.Node.Name,
                        current.Reason,
                        relationshipChain
                    ));
                }

                if (current.Depth < depth)
                {
                    // 1. Visit outgoing dependencies (dependencies of current node)
                    if (outgoing.TryGetValue(current.Node.Id!.Value, out var outEdges))
                    {
                        foreach (var edge in outEdges)
                        {
                            if (edge.ToNodeId.HasValue && nodeById.TryGetValue(edge.ToNodeId.Value, out var neighbor))
                            {
                                if (!visited.Contains(neighbor.Id!.Value))
                                {
                                    visited.Add(neighbor.Id.Value);
                                    var newPath = new List<RichNode>(current.Path) { neighbor };
                                    var reason = GetOutgoingReason(current.Node.Name, neighbor.Name, edge.RelationType);
                                    queue.Enqueue(new BfsState(neighbor, current.Depth + 1, newPath, reason));
                                }
                            }
                        }
                    }

                    // 2. Visit incoming dependencies (usages of current node)
                    if (incoming.TryGetValue(current.Node.Id!.Value, out var inEdges))
                    {
                        foreach (var edge in inEdges)
                        {
                            if (edge.FromNodeId.HasValue && nodeById.TryGetValue(edge.FromNodeId.Value, out var neighbor))
                            {
                                if (!visited.Contains(neighbor.Id!.Value))
                                {
                                    visited.Add(neighbor.Id.Value);
                                    var newPath = new List<RichNode>(current.Path) { neighbor };
                                    var reason = GetIncomingReason(current.Node.Name, neighbor.Name, edge.RelationType);
                                    queue.Enqueue(new BfsState(neighbor, current.Depth + 1, newPath, reason));
                                }
                            }
                        }
                    }
                }
            }

            // Calculate delta if previousContext is provided
            if (previousContext != null)
            {
                var prevFiles = new HashSet<string>(previousContext.TargetFiles, StringComparer.OrdinalIgnoreCase);
                var prevSigs = new HashSet<string>(previousContext.SecondarySignatures, StringComparer.Ordinal);
                var prevExpls = new HashSet<string>(previousContext.Explanations.Select(e => $"{e.TargetName}:{e.RelationshipChain}"), StringComparer.OrdinalIgnoreCase);

                var deltaFiles = targetFiles.Where(f => !prevFiles.Contains(f)).ToList();
                var deltaSigs = secondarySignatures.Where(s => !prevSigs.Contains(s)).ToList();
                var deltaExpls = explanations.Where(e => !prevExpls.Contains($"{e.TargetName}:{e.RelationshipChain}")).ToList();

                return new ContextResult(deltaFiles, deltaSigs, deltaExpls);
            }

            return new ContextResult(targetFiles, secondarySignatures, explanations);
        }

        private static string GetOutgoingReason(string source, string target, string relationType)
        {
            return relationType switch
            {
                "Injects" => $"{source} depende de {target} via injeção de construtor",
                "Implements" => $"{source} implementa a interface {target}",
                "Inherits" => $"{source} herda de {target}",
                "DependsOn" => $"{source} refere-se a {target}",
                _ => $"{source} está relacionado com {target} via {relationType}"
            };
        }

        private static string GetIncomingReason(string current, string neighbor, string relationType)
        {
            return relationType switch
            {
                "Injects" => $"{neighbor} consome {current} via injeção",
                "Implements" => $"{neighbor} implementa {current}",
                "Inherits" => $"{neighbor} herda de {current}",
                "DependsOn" => $"{neighbor} depende de {current}",
                _ => $"{neighbor} refere-se a {current} via {relationType}"
            };
        }

        private class BfsState
        {
            public RichNode Node { get; }
            public int Depth { get; }
            public List<RichNode> Path { get; }
            public string Reason { get; }

            public BfsState(RichNode node, int depth, List<RichNode> path, string reason)
            {
                Node = node;
                Depth = depth;
                Path = path;
                Reason = reason;
            }
        }
    }
}
