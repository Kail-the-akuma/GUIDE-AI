using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Guide.Semantic;

namespace Guide.Cli.Commands
{
    public static class QueryContextCommand
    {
        public static async Task<int> InvokeAsync(string path, string anchor, int depth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(anchor))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Error: The --anchor option is required and cannot be empty.");
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

                Console.WriteLine($"Querying context for anchor entity '{anchor}' up to depth {depth}...");
                Guide.Core.Interfaces.ContextResult contextResult = await contextEngine.BuildContextAsync(anchor, depth);

                System.Collections.Generic.List<string> targetFiles = contextResult.TargetFiles.ToList();
                System.Collections.Generic.List<Guide.Core.Interfaces.ContextExplanation> explanations = contextResult.Explanations.ToList();

                if (!targetFiles.Any() && !explanations.Any())
                {
                    Console.WriteLine($"No context entries found for anchor '{anchor}' at depth {depth}.");
                    return 0;
                }

                Console.WriteLine();
                Console.WriteLine("Related Files:");
                foreach (string file in targetFiles)
                {
                    Console.WriteLine($"- {file}");
                }

                Console.WriteLine();
                Console.WriteLine("Explanations:");
                foreach (Guide.Core.Interfaces.ContextExplanation exp in explanations)
                {
                    Console.WriteLine($"- {exp.TargetName}: {exp.Reason} (Relationship chain: {exp.RelationshipChain})");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during context query: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }
    }
}
