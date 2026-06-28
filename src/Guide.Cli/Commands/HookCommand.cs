using System;
using System.IO;
using System.Threading.Tasks;

namespace Guide.Cli.Commands
{
    public static class HookCommand
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

                if (!gitFound)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Error: No .git directory found. Cannot install git hooks outside a git repository.");
                    Console.ResetColor();
                    return 1;
                }

                string hooksDir = Path.Combine(repoRoot, ".git", "hooks");
                if (!Directory.Exists(hooksDir))
                {
                    Directory.CreateDirectory(hooksDir);
                }

                string prePushPath = Path.Combine(hooksDir, "pre-push");

                // Write the pre-push hook with Unix line endings (\n)
                string hookContent = "#!/bin/sh\n" +
                                  "echo \"A correr validadores locais GUIDE...\"\n" +
                                  "dotnet run --project src/Guide.Cli -- validate\n" +
                                  "if [ $? -ne 0 ]; then\n" +
                                  "    echo \"Erro: A validação GUIDE falhou. Commit rejeitado!\"\n" +
                                  "    exit 1\n" +
                                  "fi\n" +
                                  "exit 0\n";

                await File.WriteAllTextAsync(prePushPath, hookContent);
                Console.WriteLine($"Git pre-push hook installed successfully at: {prePushPath}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during hook installation: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }
    }
}
