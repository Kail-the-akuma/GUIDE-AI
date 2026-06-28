using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Guide.Semantic;

namespace Guide.Cli.Commands
{
    public static class InitCommand
    {
        public static async Task<int> InvokeAsync(string path)
        {
            try
            {
                // 1. Determine repository root by looking upwards for a .git directory
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

                Console.WriteLine($"Initializing GUIDE repository at: {repoRoot}");
                if (!gitFound)
                {
                    Console.WriteLine("Warning: No .git directory found in target path or parent directories.");
                }

                // 2. Create the .guide directory
                string guideDir = Path.Combine(repoRoot, ".guide");
                if (!Directory.Exists(guideDir))
                {
                    Directory.CreateDirectory(guideDir);
                }

                // 3. Create/initialize the SQLite database
                string dbPath = Path.Combine(guideDir, "project_graph.db");
                string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;";

                // Instantiating SqliteKnowledgeStore automatically runs the DB schema creation
                SqliteKnowledgeStore store = new SqliteKnowledgeStore(connectionString);
                Console.WriteLine($"Initialized SQLite database at: {dbPath}");

                // 4. Write Embedded templates for configurations
                await WriteTemplateAsync(repoRoot, ".cursorrules", "cursorrules.txt");
                await WriteTemplateAsync(repoRoot, ".windsurfrules", "windsurfrules.txt");

                string agentsDir = Path.Combine(repoRoot, ".agents");
                if (!Directory.Exists(agentsDir))
                {
                    Directory.CreateDirectory(agentsDir);
                }
                await WriteTemplateAsync(agentsDir, "AGENTS.md", "agents.txt");

                string githubDir = Path.Combine(repoRoot, ".github");
                if (!Directory.Exists(githubDir))
                {
                    Directory.CreateDirectory(githubDir);
                }
                await WriteTemplateAsync(githubDir, "copilot-instructions.md", "copilot.txt");

                Console.WriteLine("GUIDE initialization completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during initialization: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task WriteTemplateAsync(string targetDirectory, string fileName, string resourceName)
        {
            string targetPath = Path.Combine(targetDirectory, fileName);
            Assembly assembly = typeof(InitCommand).Assembly;
            string fullResourceName = $"Guide.Cli.Templates.{resourceName}";

            using Stream? stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Could not find embedded template resource: {fullResourceName}");
            }

            using StreamReader reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            await File.WriteAllTextAsync(targetPath, content);
            Console.WriteLine($"Created: {targetPath}");
        }
    }
}
