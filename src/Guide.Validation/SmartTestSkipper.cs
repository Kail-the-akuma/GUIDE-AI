using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation;

public class SmartTestSkipper
{
    private readonly ICommandLineRunner _runner;
    private readonly IKnowledgeStore _store;

    public SmartTestSkipper(ICommandLineRunner runner, IKnowledgeStore store)
    {
        _runner = runner;
        _store = store;
    }

    public async Task<string?> GetTestFilterAsync(string solutionPath, string repoRoot, CancellationToken ct)
    {
        try
        {
            var (exitCode, output) = await _runner.ExecuteAsync("git", "status --porcelain", repoRoot, ct);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Git status failed with exit code {exitCode}: {output}");
            }

            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var gitModifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                if (line.Length <= 3) continue;

                var status = line.Substring(0, 2);
                bool isModified = status.Contains('M');
                bool isAdded = status.Contains('A');
                bool isUntracked = status.StartsWith("??");

                if (isModified || isAdded || isUntracked)
                {
                    var filePath = line.Substring(3).Trim().Trim('"');
                    if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        var normalizedPath = NormalizePath(filePath);
                        gitModifiedFiles.Add(normalizedPath);

                        var absolutePath = NormalizePath(Path.GetFullPath(Path.Combine(repoRoot, filePath)));
                        gitModifiedFiles.Add(absolutePath);
                    }
                }
            }

            if (gitModifiedFiles.Count == 0)
            {
                return null;
            }

            int latestVersion = await _store.GetLatestGraphVersionAsync();
            if (latestVersion <= 0)
            {
                return "";
            }

            var snapshot = await _store.GetSnapshotAsync(latestVersion);
            if (snapshot == null || snapshot.Nodes == null)
            {
                return "";
            }

            var nodeMap = new Dictionary<int, RichNode>();
            foreach (var node in snapshot.Nodes)
            {
                if (node.Id.HasValue)
                {
                    nodeMap[node.Id.Value] = node;
                }
            }

            var affectedNodeIds = new HashSet<int>();
            var affectedTestFullyQualifiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in snapshot.Nodes)
            {
                if (string.IsNullOrEmpty(node.FilePath)) continue;

                var normNodePath = NormalizePath(node.FilePath);
                var absNodePath = Path.IsPathRooted(node.FilePath)
                    ? NormalizePath(Path.GetFullPath(node.FilePath))
                    : NormalizePath(Path.GetFullPath(Path.Combine(repoRoot, node.FilePath)));

                bool isModified = gitModifiedFiles.Contains(normNodePath) || gitModifiedFiles.Contains(absNodePath);
                if (isModified)
                {
                    if (node.Id.HasValue)
                    {
                        affectedNodeIds.Add(node.Id.Value);
                    }

                    if (node.NodeType == "UnitTest" || node.NodeType == "PlaywrightTest")
                    {
                        var fqName = string.IsNullOrEmpty(node.Namespace)
                            ? node.Name
                            : $"{node.Namespace}.{node.Name}";
                        affectedTestFullyQualifiedNames.Add(fqName);
                    }
                }
            }

            var incomingEdges = new Dictionary<int, List<int>>();
            if (snapshot.Edges != null)
            {
                foreach (var edge in snapshot.Edges)
                {
                    if (edge.FromNodeId.HasValue && edge.ToNodeId.HasValue)
                    {
                        var from = edge.FromNodeId.Value;
                        var to = edge.ToNodeId.Value;

                        if (!incomingEdges.TryGetValue(to, out var list))
                        {
                            list = new List<int>();
                            incomingEdges[to] = list;
                        }
                        list.Add(from);
                    }
                }
            }

            var queue = new Queue<int>(affectedNodeIds);
            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();

                if (incomingEdges.TryGetValue(currentId, out var predecessors))
                {
                    foreach (var predId in predecessors)
                    {
                        if (affectedNodeIds.Add(predId))
                        {
                            queue.Enqueue(predId);
                        }
                    }
                }
            }

            foreach (var nodeId in affectedNodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node))
                {
                    if (node.NodeType == "UnitTest" || node.NodeType == "PlaywrightTest")
                    {
                        var fqName = string.IsNullOrEmpty(node.Namespace)
                            ? node.Name
                            : $"{node.Namespace}.{node.Name}";
                        affectedTestFullyQualifiedNames.Add(fqName);
                    }
                }
            }

            if (affectedTestFullyQualifiedNames.Count == 0)
            {
                return null;
            }

            if (affectedTestFullyQualifiedNames.Count > 15)
            {
                return "";
            }

            var filterString = string.Join("|", affectedTestFullyQualifiedNames.Select(name => $"FullyQualifiedName~{name}"));
            return filterString;
        }
        catch (Exception)
        {
            return "";
        }
    }

    public async Task<List<string>?> GetImpactedFrontendTestsAsync(string repoRoot, CancellationToken ct)
    {
        try
        {
            var (exitCode, output) = await _runner.ExecuteAsync("git", "status --porcelain", repoRoot, ct);
            if (exitCode != 0)
            {
                return new List<string>();
            }

            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var gitModifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasFrontendChanges = false;

            foreach (var line in lines)
            {
                if (line.Length <= 3) continue;

                var status = line.Substring(0, 2);
                bool isModified = status.Contains('M');
                bool isAdded = status.Contains('A');
                bool isUntracked = status.StartsWith("??");

                if (isModified || isAdded || isUntracked)
                {
                    var filePath = line.Substring(3).Trim().Trim('"');
                    var ext = Path.GetExtension(filePath);
                    if (ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
                    {
                        var normalizedPath = NormalizePath(filePath);
                        gitModifiedFiles.Add(normalizedPath);

                        var absolutePath = NormalizePath(Path.GetFullPath(Path.Combine(repoRoot, filePath)));
                        gitModifiedFiles.Add(absolutePath);

                        hasFrontendChanges = true;
                    }
                }
            }

            if (!hasFrontendChanges)
            {
                return null;
            }

            int latestVersion = await _store.GetLatestGraphVersionAsync();
            if (latestVersion <= 0)
            {
                return new List<string>();
            }

            var snapshot = await _store.GetSnapshotAsync(latestVersion);
            if (snapshot == null || snapshot.Nodes == null)
            {
                return new List<string>();
            }

            var nodeMap = new Dictionary<int, RichNode>();
            foreach (var node in snapshot.Nodes)
            {
                if (node.Id.HasValue)
                {
                    nodeMap[node.Id.Value] = node;
                }
            }

            var affectedNodeIds = new HashSet<int>();
            var affectedTestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in snapshot.Nodes)
            {
                if (string.IsNullOrEmpty(node.FilePath)) continue;

                var normNodePath = NormalizePath(node.FilePath);
                var absNodePath = Path.IsPathRooted(node.FilePath)
                    ? NormalizePath(Path.GetFullPath(node.FilePath))
                    : NormalizePath(Path.GetFullPath(Path.Combine(repoRoot, node.FilePath)));

                bool isModified = gitModifiedFiles.Contains(normNodePath) || gitModifiedFiles.Contains(absNodePath);
                if (isModified)
                {
                    if (node.Id.HasValue)
                    {
                        affectedNodeIds.Add(node.Id.Value);
                    }

                    if (node.NodeType == "UnitTest" || node.NodeType == "PlaywrightTest")
                    {
                        affectedTestPaths.Add(node.FilePath);
                    }
                }
            }

            var incomingEdges = new Dictionary<int, List<int>>();
            if (snapshot.Edges != null)
            {
                foreach (var edge in snapshot.Edges)
                {
                    if (edge.FromNodeId.HasValue && edge.ToNodeId.HasValue)
                    {
                        var from = edge.FromNodeId.Value;
                        var to = edge.ToNodeId.Value;

                        if (!incomingEdges.TryGetValue(to, out var list))
                        {
                            list = new List<int>();
                            incomingEdges[to] = list;
                        }
                        list.Add(from);
                    }
                }
            }

            var queue = new Queue<int>(affectedNodeIds);
            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();

                if (incomingEdges.TryGetValue(currentId, out var predecessors))
                {
                    foreach (var predId in predecessors)
                    {
                        if (affectedNodeIds.Add(predId))
                        {
                            queue.Enqueue(predId);
                        }
                    }
                }
            }

            foreach (var nodeId in affectedNodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node))
                {
                    if (node.NodeType == "UnitTest" || node.NodeType == "PlaywrightTest")
                    {
                        affectedTestPaths.Add(node.FilePath);
                    }
                }
            }

            if (affectedTestPaths.Count == 0)
            {
                return null;
            }

            if (affectedTestPaths.Count > 15)
            {
                return new List<string>();
            }

            return affectedTestPaths.ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("\\", "/").Trim();
    }
}
