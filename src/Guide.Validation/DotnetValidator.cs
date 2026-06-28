using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Semantic;

namespace Guide.Validation
{
    public class DotnetValidator : ILanguageValidator
    {
        private readonly ICommandLineRunner _runner;

        public string Language => "CSharp";

        public DotnetValidator(ICommandLineRunner runner)
        {
            _runner = runner;
        }

        public async Task<ValidationResult> ValidateAsync(string projectPath, bool runTests)
        {
            // 1. Run static validation (ParallelValidator)
            var staticValidator = new ParallelValidator(_runner);
            var staticResult = await staticValidator.ValidateProjectAsync(projectPath, CancellationToken.None);

            if (!staticResult.IsSuccess)
            {
                return staticResult;
            }

            // 2. Run tests if requested
            if (runTests)
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
                var dbPath = Path.Combine(peaIaDir, "project_graph.db");
                var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
                var store = new SqliteKnowledgeStore(connectionString);
                var skipper = new SmartTestSkipper(_runner, store);

                string? filter = await skipper.GetTestFilterAsync(projectPath, repoRoot, CancellationToken.None);

                if (filter == null)
                {
                    Console.WriteLine("[Smart Test Skipper] No tests impacted by the current changes. Skipping test execution.");
                    return new ValidationResult { IsSuccess = true };
                }

                int testExitCode;
                string testOutput;

                if (filter == "")
                {
                    Console.WriteLine("[Smart Test Skipper] Running all tests (fallback or too many tests impacted)...");
                    (testExitCode, testOutput) = await _runner.ExecuteAsync("dotnet", $"test \"{projectPath}\"", workingDirectory: repoRoot, ct: CancellationToken.None);
                }
                else
                {
                    Console.WriteLine($"[Smart Test Skipper] Running only impacted tests with filter: {filter}");
                    (testExitCode, testOutput) = await _runner.ExecuteAsync("dotnet", $"test \"{projectPath}\" --filter \"{filter}\"", workingDirectory: repoRoot, ct: CancellationToken.None);
                }

                if (testExitCode != 0)
                {
                    var errors = new List<string> { $"dotnet test failed with exit code {testExitCode}. Output:\n{testOutput}" };
                    return new ValidationResult
                    {
                        IsSuccess = false,
                        Errors = errors
                    };
                }
            }

            return new ValidationResult { IsSuccess = true };
        }
    }
}
