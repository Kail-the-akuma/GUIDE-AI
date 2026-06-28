using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic
{
    public class TypeScriptParser : ISemanticParser
    {
        private static readonly Regex StaticImportRegex = new(
            @"\b(import|export)\s+(?:(?:(?!import|export|;)[\s\S])*?\s+from\s+)?['""`]([^'""`\r\n]+)['""`]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex DynamicImportRegex = new(
            @"\bimport\s*\(\s*['""`]([^'""`\r\n]+)['""`](?:(?!import|;)[\s\S])*?\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public bool CanParse(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension))
            {
                return false;
            }
            string ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            return ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ExtractImports(string content)
        {
            HashSet<string> imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in StaticImportRegex.Matches(content))
            {
                if (match.Groups.Count > 2)
                {
                    imports.Add(match.Groups[2].Value);
                }
            }

            foreach (Match match in DynamicImportRegex.Matches(content))
            {
                if (match.Groups.Count > 1)
                {
                    imports.Add(match.Groups[1].Value);
                }
            }

            return imports;
        }

        private static bool IsTestFile(string relativePath)
        {
            string normalized = relativePath.Replace("\\", "/");
            bool hasTestExtension =
                normalized.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".test.jsx", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".spec.tsx", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".spec.js", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".spec.jsx", StringComparison.OrdinalIgnoreCase);

            if (hasTestExtension)
            {
                return true;
            }

            bool isInTestFolder =
                normalized.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/test/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("__tests__/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("test/", StringComparison.OrdinalIgnoreCase);

            bool isSourceFile =
                normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

            return isInTestFolder && isSourceFile;
        }

        public async Task<DependencyGraph> BuildGraphAsync(string projectPath)
        {
            DependencyGraph graph = new DependencyGraph();
            string directory = projectPath;
            if (File.Exists(projectPath))
            {
                directory = Path.GetDirectoryName(projectPath) ?? projectPath;
            }

            if (!Directory.Exists(directory))
            {
                return graph;
            }

            List<string> files = new List<string>();
            SafeFindTsFiles(directory, files);

            Dictionary<string, RichNode> nodeMap = new Dictionary<string, RichNode>(StringComparer.OrdinalIgnoreCase);
            int idCounter = 1;

            foreach (string file in files)
            {
                string moduleName = Path.GetFileNameWithoutExtension(file);
                string relativePath = Path.GetRelativePath(directory, file).Replace("\\", "/");

                string nodeType = IsTestFile(relativePath) ? "UnitTest" : "TypeScriptModule";

                RichNode node = new RichNode(
                    idCounter++,
                    relativePath,
                    "",
                    moduleName,
                    nodeType,
                    ""
                );
                nodeMap[relativePath] = node;
                graph.Nodes.Add(node);
            }

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(directory, file).Replace("\\", "/");
                if (!nodeMap.TryGetValue(relativePath, out RichNode? sourceNode))
                {
                    continue;
                }

                try
                {
                    string content = await File.ReadAllTextAsync(file);
                    IEnumerable<string> imports = ExtractImports(content);
                    foreach (string importPath in imports)
                    {
                        if (importPath.StartsWith("."))
                        {
                            string? fileDir = Path.GetDirectoryName(file);
                            if (fileDir != null)
                            {
                                string targetFullPath = Path.GetFullPath(Path.Combine(fileDir, importPath));
                                string? matchedRelativePath = ResolveImportFile(targetFullPath, directory);
                                if (matchedRelativePath != null && nodeMap.TryGetValue(matchedRelativePath, out RichNode? targetNode))
                                {
                                    graph.Edges.Add(new RichEdge(sourceNode.Id, targetNode.Id, "DependsOn"));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip unreadable files
                }
            }

            return graph;
        }

        private static void SafeFindTsFiles(string path, List<string> files)
        {
            try
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    string ext = Path.GetExtension(file);
                    if (ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file);
                    }
                }

                foreach (string dir in Directory.GetDirectories(path))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".guide", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    SafeFindTsFiles(dir, files);
                }
            }
            catch
            {
                // Ignore directory read issues
            }
        }

        private static string? ResolveImportFile(string baseFullPath, string projectRoot)
        {
            string[] extensions = new string[] { ".ts", ".tsx", ".js", ".jsx" };
            foreach (string ext in extensions)
            {
                string pathWithExt = baseFullPath + ext;
                if (File.Exists(pathWithExt))
                {
                    return Path.GetRelativePath(projectRoot, pathWithExt).Replace("\\", "/");
                }
            }

            foreach (string ext in extensions)
            {
                string indexPath = Path.Combine(baseFullPath, "index" + ext);
                if (File.Exists(indexPath))
                {
                    return Path.GetRelativePath(projectRoot, indexPath).Replace("\\", "/");
                }
            }

            return null;
        }

        public async Task<CodeContext> GetContextAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new CodeContext { FilePath = filePath };
            }

            string content = await File.ReadAllTextAsync(filePath);
            List<string> imports = ExtractImports(content).ToList();
            List<string> classes = new List<string>();
            List<string> signatures = new List<string>();

            Regex classRegex = new Regex(@"class\s+(\w+)", RegexOptions.Compiled);
            foreach (Match match in classRegex.Matches(content))
            {
                classes.Add(match.Groups[1].Value);
            }

            Regex functionRegex = new Regex(@"(?:export\s+)?(?:async\s+)?function\s+(\w+)\s*\(", RegexOptions.Compiled);
            foreach (Match match in functionRegex.Matches(content))
            {
                signatures.Add(match.Value.Trim().TrimEnd('('));
            }

            return new CodeContext
            {
                FilePath = filePath,
                Language = "TypeScript",
                RawContent = content,
                Imports = imports,
                Classes = classes,
                Signatures = signatures
            };
        }
    }
}
