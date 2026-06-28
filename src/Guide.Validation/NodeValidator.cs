using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Semantic;

namespace Guide.Validation
{
    public class NodeValidator : ILanguageValidator
    {
        private readonly ICommandLineRunner _runner;

        public string Language
        {
            get
            {
                return "TypeScript";
            }
        }

        public NodeValidator(ICommandLineRunner runner)
        {
            _runner = runner;
        }

        public async Task<ValidationResult> ValidateAsync(string projectPath, bool runTests)
        {
            string workingDir = projectPath;
            if (File.Exists(projectPath))
            {
                workingDir = Path.GetDirectoryName(projectPath) ?? projectPath;
            }

            try
            {
                (int exitCode, string output) = await _runner.ExecuteAsync("npm", "run build", workingDirectory: workingDir);

                if (exitCode == 0 && runTests)
                {
                    string repoRoot = Path.GetFullPath(projectPath);
                    if (File.Exists(repoRoot))
                    {
                        repoRoot = Path.GetDirectoryName(repoRoot) ?? repoRoot;
                    }
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

                    string peaIaDir = Path.Combine(repoRoot, ".guide");
                    string dbPath = Path.Combine(peaIaDir, "project_graph.db");
                    string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
                    SqliteKnowledgeStore store = new SqliteKnowledgeStore(connectionString);
                    SmartTestSkipper skipper = new SmartTestSkipper(_runner, store);

                    List<string>? testPaths = await skipper.GetImpactedFrontendTestsAsync(repoRoot, CancellationToken.None);

                    if (testPaths == null)
                    {
                        Console.WriteLine("[Smart Test Skipper] No frontend tests impacted. Skipping frontend test execution.");
                    }
                    else
                    {
                        int testExitCode;
                        string testOutput;

                        if (testPaths.Count == 0)
                        {
                            Console.WriteLine("[Smart Test Skipper] Running all frontend tests (fallback)...");
                            (testExitCode, testOutput) = await _runner.ExecuteAsync("npm", "test", workingDirectory: workingDir);
                        }
                        else
                        {
                            IEnumerable<string> relativePaths = testPaths.Select(tp =>
                            {
                                string abs = Path.GetFullPath(Path.Combine(repoRoot, tp));
                                return Path.GetRelativePath(workingDir, abs).Replace("\\", "/");
                            });
                            string args = "vitest run " + string.Join(" ", relativePaths.Select(p => $"\"{p}\""));
                            Console.WriteLine($"[Smart Test Skipper] Running only impacted frontend tests: npx {args}");
                            (testExitCode, testOutput) = await _runner.ExecuteAsync("npx", args, workingDirectory: workingDir);
                        }

                        if (testExitCode != 0)
                        {
                            return new ValidationResult
                            {
                                IsSuccess = false,
                                Errors = new List<string> { $"npm test failed with exit code {testExitCode}. Output: {testOutput}" }
                            };
                        }
                    }
                }

                if (exitCode != 0)
                {
                    return new ValidationResult
                    {
                        IsSuccess = false,
                        Errors = new List<string> { $"npm run build failed with exit code {exitCode}. Output: {output}" }
                    };
                }

                return new ValidationResult { IsSuccess = true };
            }
            catch (Win32Exception)
            {
                Console.WriteLine("Warning: npm/tsc not found in system PATH. Falling back to lightweight C#-based syntax checker for HTML/CSS.");
                return RunLightweightSyntaxChecker(workingDir);
            }
            catch (Exception ex) when (ex.InnerException is Win32Exception)
            {
                Console.WriteLine("Warning: npm/tsc not found in system PATH. Falling back to lightweight C#-based syntax checker for HTML/CSS.");
                return RunLightweightSyntaxChecker(workingDir);
            }
        }

        private ValidationResult RunLightweightSyntaxChecker(string directory)
        {
            ValidationResult result = new ValidationResult { IsSuccess = true };

            IEnumerable<string> htmlFiles = Directory.EnumerateFiles(directory, "*.html", SearchOption.AllDirectories);
            IEnumerable<string> cssFiles = Directory.EnumerateFiles(directory, "*.css", SearchOption.AllDirectories);

            foreach (string file in htmlFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    int openBrackets = 0;
                    for (int i = 0; i < content.Length; i++)
                    {
                        if (content[i] == '<')
                        {
                            openBrackets++;
                        }
                        else if (content[i] == '>')
                        {
                            openBrackets--;
                            if (openBrackets < 0)
                            {
                                result.IsSuccess = false;
                                result.Errors.Add($"HTML syntax error in {file}: Unmatched closing bracket '>' at position {i}.");
                                openBrackets = 0;
                            }
                        }
                    }
                    if (openBrackets > 0)
                    {
                        result.IsSuccess = false;
                        result.Errors.Add($"HTML syntax error in {file}: Unmatched opening bracket '<' at the end of the file.");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error reading HTML file {file}: {ex.Message}");
                }
            }

            foreach (string file in cssFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    int openBraces = 0;
                    for (int i = 0; i < content.Length; i++)
                    {
                        if (content[i] == '{')
                        {
                            openBraces++;
                        }
                        else if (content[i] == '}')
                        {
                            openBraces--;
                            if (openBraces < 0)
                            {
                                result.IsSuccess = false;
                                result.Errors.Add($"CSS syntax error in {file}: Unmatched closing brace '}}' at position {i}.");
                                openBraces = 0;
                            }
                        }
                    }
                    if (openBraces > 0)
                    {
                        result.IsSuccess = false;
                        result.Errors.Add($"CSS syntax error in {file}: Unmatched opening brace '{{' at the end of the file.");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error reading CSS file {file}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
