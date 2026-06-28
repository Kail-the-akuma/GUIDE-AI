using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Models;
using Guide.Core.Workflow;
using Guide.Core.Interfaces;
using Guide.Semantic;
using Guide.Validation;
using Guide.Memory;

namespace Guide.Cli.Commands
{
    public static class ValidateCommand
    {
        public static async Task<int> InvokeAsync(string path, bool runTests, bool autoHeal = false, bool quiet = false)
        {
            try
            {
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

                if (!quiet)
                {
                    Console.WriteLine($"Validating repository at: {repoRoot}");
                }

                string solutionPath = FindSolutionPath(repoRoot);
                if (!quiet)
                {
                    Console.WriteLine($"Solution/Project path to validate: {solutionPath}");
                }

                string branchName = GetCurrentGitBranchName(repoRoot);
                if (!quiet)
                {
                    Console.WriteLine($"Active git branch: {branchName}");
                }

                string guideDir = Path.Combine(repoRoot, ".guide");
                if (!Directory.Exists(guideDir))
                {
                    Directory.CreateDirectory(guideDir);
                }
                string stateFileDirectory = Path.Combine(guideDir, "states");
                if (!Directory.Exists(stateFileDirectory))
                {
                    Directory.CreateDirectory(stateFileDirectory);
                }
                string safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
                string stateFilePath = Path.Combine(stateFileDirectory, $"{safeBranchName}.json");

                FeatureStateMachine fsm = await FeatureStateMachine.LoadFromFileAsync(branchName, stateFilePath);
                if (!quiet)
                {
                    Console.WriteLine($"Current feature state: {fsm.State}");
                }

                if (fsm.State == FeatureState.Requested)
                {
                    if (!quiet)
                    {
                        Console.WriteLine("Transitioning state: Requested -> ContextReady");
                    }
                    await fsm.FireAsync(FeatureTrigger.Analyze);
                }
                if (fsm.State == FeatureState.ContextReady)
                {
                    if (!quiet)
                    {
                        Console.WriteLine("Transitioning state: ContextReady -> CodeGenerated");
                    }
                    await fsm.FireAsync(FeatureTrigger.ContextAssembled);
                }

                CommandLineRunner runner = new CommandLineRunner();
                LanguageRegistry registry = LanguageRegistry.Detect(repoRoot, runner);

                if (!registry.Validators.Any())
                {
                    registry.RegisterValidator(new DotnetValidator(runner));
                }

                string dbPath = Path.Combine(guideDir, "project_graph.db");
                string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
                SqliteKnowledgeStore store = new SqliteKnowledgeStore(connectionString);

                string memoryDbPath = Path.Combine(guideDir, "engineering_memory.db");
                string memoryConnectionString = $"Data Source={memoryDbPath};Busy Timeout=5000;Cache=Shared;Mode=ReadWriteCreate;";
                EngineeringMemoryStore memoryStore = new EngineeringMemoryStore(memoryConnectionString);

                WorkflowEngine workflowEngine = new WorkflowEngine(
                    registry.Parsers,
                    registry.Validators,
                    store,
                    memoryStore,
                    new MockLlmService()
                );

                if (!quiet)
                {
                    Console.WriteLine("Running language-specific static validators...");
                }

                ValidationResult validationResult = new ValidationResult { IsSuccess = true, Errors = new List<string>() };
                foreach (ILanguageValidator validator in registry.Validators)
                {
                    if (!quiet)
                    {
                        Console.WriteLine($"Running static validator for {validator.Language}...");
                    }
                    ValidationResult res = await validator.ValidateAsync(solutionPath, runTests: false);
                    if (!res.IsSuccess)
                    {
                        validationResult.IsSuccess = false;
                        validationResult.Errors.AddRange(res.Errors);
                    }
                }

                if (!validationResult.IsSuccess)
                {
                    if (autoHeal)
                    {
                        if (!quiet)
                        {
                            Console.WriteLine("[Auto-Healer] Validation failed. Initiating AI-powered auto-healing loop...");
                        }

                        List<string> errorFiles = new List<string>();
                        foreach (string err in validationResult.Errors)
                        {
                            if (string.IsNullOrWhiteSpace(err))
                            {
                                continue;
                            }

                            string? filePathCandidate = null;
                            int parenIndex = err.IndexOf('(');
                            if (parenIndex != -1)
                            {
                                filePathCandidate = err.Substring(0, parenIndex).Trim();
                            }
                            else
                            {
                                int colonIndex = err.IndexOf(':');
                                if (colonIndex == 1 && err.Length > 2 && (err[2] == '\\' || err[2] == '/'))
                                {
                                    int nextColonIndex = err.IndexOf(':', 2);
                                    if (nextColonIndex != -1)
                                    {
                                        filePathCandidate = err.Substring(0, nextColonIndex).Trim();
                                    }
                                }
                                else if (colonIndex != -1)
                                {
                                    filePathCandidate = err.Substring(0, colonIndex).Trim();
                                }
                            }

                            if (!string.IsNullOrEmpty(filePathCandidate))
                            {
                                try
                                {
                                    string fullPath;
                                    if (Path.IsPathRooted(filePathCandidate))
                                    {
                                        fullPath = Path.GetFullPath(filePathCandidate);
                                    }
                                    else
                                    {
                                        fullPath = Path.GetFullPath(Path.Combine(repoRoot, filePathCandidate));
                                    }

                                    if (File.Exists(fullPath))
                                    {
                                        if (!errorFiles.Contains(fullPath))
                                        {
                                            errorFiles.Add(fullPath);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Ignore path resolution issues
                                }
                            }
                        }

                        bool allHealed = errorFiles.Count > 0;
                        List<string> currentErrors = new List<string>(validationResult.Errors);

                        foreach (string file in errorFiles)
                        {
                            WorkflowResult workflowRes = await workflowEngine.RunWorkflowAsync("Heal file", file, CancellationToken.None);
                            if (workflowRes.IsSuccess)
                            {
                                if (!quiet)
                                {
                                    Console.WriteLine($"[Auto-Healer] File {Path.GetFileName(file)} successfully healed after {workflowRes.HealingIterations} iterations!");
                                }
                                currentErrors = new List<string>(workflowRes.Errors);
                            }
                            else
                            {
                                allHealed = false;
                                currentErrors = new List<string>(workflowRes.Errors);
                                break;
                            }
                        }

                        if (allHealed)
                        {
                            validationResult = new ValidationResult
                            {
                                IsSuccess = true,
                                Errors = new List<string>()
                            };
                        }
                        else
                        {
                            if (!quiet)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Error.WriteLine("Static validation failed and auto-healing was unsuccessful. Final errors:");
                            }
                            foreach (string err in currentErrors)
                            {
                                if (quiet)
                                {
                                    Console.WriteLine(err);
                                }
                                else
                                {
                                    Console.Error.WriteLine($" - {err}");
                                }
                            }
                            if (!quiet)
                            {
                                Console.ResetColor();
                            }

                            await TransitionToRequestedAsync(fsm, branchName);
                            if (!quiet)
                            {
                                Console.WriteLine($"Feature state transitioned back to: {fsm.State}");
                            }
                            return 1;
                        }
                    }
                    else
                    {
                        if (!quiet)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine("Static validation failed with the following errors:");
                        }
                        foreach (string err in validationResult.Errors)
                        {
                            if (quiet)
                            {
                                Console.WriteLine(err);
                            }
                            else
                            {
                                Console.Error.WriteLine($" - {err}");
                            }
                        }
                        if (!quiet)
                        {
                            Console.ResetColor();
                        }

                        await TransitionToRequestedAsync(fsm, branchName);
                        if (!quiet)
                        {
                            Console.WriteLine($"Feature state transitioned back to: {fsm.State}");
                        }
                        return 1;
                    }
                }

                if (!quiet)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Static validation succeeded.");
                    Console.ResetColor();
                }

                if (fsm.State == FeatureState.CodeGenerated)
                {
                    if (!quiet)
                    {
                        Console.WriteLine("Transitioning state: CodeGenerated -> StaticallyValid");
                    }
                    await fsm.FireAsync(FeatureTrigger.CodeValidatedSuccessfully);
                }

                if (runTests)
                {
                    if (!quiet)
                    {
                        Console.WriteLine("Running tests for all active languages...");
                    }

                    ValidationResult testResult = new ValidationResult { IsSuccess = true, Errors = new List<string>() };
                    foreach (ILanguageValidator validator in registry.Validators)
                    {
                        if (!quiet)
                        {
                            Console.WriteLine($"Running tests for {validator.Language}...");
                        }
                        ValidationResult res = await validator.ValidateAsync(solutionPath, runTests: true);
                        if (!res.IsSuccess)
                        {
                            testResult.IsSuccess = false;
                            testResult.Errors.AddRange(res.Errors);
                        }
                    }

                    if (testResult.IsSuccess)
                    {
                        if (!quiet)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("All tests passed successfully.");
                            Console.ResetColor();
                        }

                        if (fsm.State == FeatureState.StaticallyValid)
                        {
                            if (!quiet)
                            {
                                Console.WriteLine("Transitioning state: StaticallyValid -> FunctionallyValid");
                            }
                            await fsm.FireAsync(FeatureTrigger.TestsPassed);
                        }
                    }
                    else
                    {
                        if (!quiet)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine("Test execution failed. Errors:");
                        }
                        foreach (string err in testResult.Errors)
                        {
                            if (quiet)
                            {
                                Console.WriteLine(err);
                            }
                            else
                            {
                                Console.Error.WriteLine($" - {err}");
                            }
                        }
                        await TransitionToRequestedAsync(fsm, branchName);
                        if (!quiet)
                        {
                            Console.WriteLine($"Feature state transitioned back to: {fsm.State}");
                        }
                        return 2;
                    }
                }

                if (quiet)
                {
                    Console.WriteLine("[SUCCESS] Validation completed.");
                }
                else
                {
                    Console.WriteLine($"Validation completed successfully. Final state: {fsm.State}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error during validation: {ex.Message}");
                    Console.ResetColor();
                }
                else
                {
                    Console.Error.WriteLine(ex.Message);
                }
                return 3;
            }
        }

        private static string FindSolutionPath(string repoRoot)
        {
            string[] slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            string[] csprojFiles = Directory.GetFiles(repoRoot, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
            {
                return csprojFiles[0];
            }

            return repoRoot;
        }

        private static string GetCurrentGitBranchName(string workingDir)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process? process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output;
                    }
                }
            }
            catch
            {
            }
            return "default";
        }

        private static async Task TransitionToRequestedAsync(FeatureStateMachine fsm, string branchName)
        {
            if (fsm.State == FeatureState.CodeGenerated && fsm.CanFire(FeatureTrigger.CodeValidatedWithErrors))
            {
                await fsm.FireAsync(FeatureTrigger.CodeValidatedWithErrors);
            }
            else if (fsm.State == FeatureState.StaticallyValid && fsm.CanFire(FeatureTrigger.TestsFailed))
            {
                await fsm.FireAsync(FeatureTrigger.TestsFailed);
            }
            else if (fsm.State != FeatureState.Requested)
            {
                FeatureStateMachine newFsm = new FeatureStateMachine(FeatureState.Requested, branchName, fsm.StateFilePath);
                await newFsm.SaveStateAsync();
            }
        }
    }
}
