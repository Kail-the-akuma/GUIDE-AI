using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;
using Guide.Core.Interfaces;

namespace Guide.Semantic
{
    public class ProjectParser : IProjectParser
    {
        private readonly SqliteKnowledgeStore _store;
        private readonly FallbackAstParser _fallbackParser;

        public ProjectParser(SqliteKnowledgeStore store)
        {
            _store = store;
            _fallbackParser = new FallbackAstParser(store);
        }

        public async Task<IEnumerable<SyntaxTree>> ParseProjectSourcesAsync(string solutionPath, CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = solutionPath; // Fallback if solutionPath is a directory
            }

            try
            {
                if (!MSBuildLocator.IsRegistered)
                {
                    MSBuildLocator.RegisterDefaults();
                }

                using (var workspace = MSBuildWorkspace.Create())
                {
                    Solution solution;
                    if (solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var project = await workspace.OpenProjectAsync(solutionPath, null, ct);
                        solution = project.Solution;
                    }
                    else
                    {
                        solution = await workspace.OpenSolutionAsync(solutionPath, null, ct);
                    }

                    var syntaxTrees = new List<SyntaxTree>();
                    foreach (var project in solution.Projects)
                    {
                        foreach (var document in project.Documents)
                        {
                            if (document.SupportsSyntaxTree)
                            {
                                var tree = await document.GetSyntaxTreeAsync(ct);
                                if (tree != null)
                                {
                                    syntaxTrees.Add(tree);
                                }
                            }
                        }
                    }

                    if (syntaxTrees.Any())
                    {
                        return syntaxTrees;
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback silently or log warning
                Console.WriteLine($"MSBuildWorkspace failed to load: {ex.Message}. Falling back to AST crawler.");
            }

            return await _fallbackParser.ParseDirectoryAsync(directory, ct);
        }
    }
}
