using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Guide.Cli.Commands;
using Guide.Core.Workflow;
using Xunit;

namespace Guide.IntegrationTests
{
    public class CliIntegrationTests : IDisposable
    {
        private readonly string _tempRepoPath;

        public CliIntegrationTests()
        {
            // Create a unique temporary directory for each test
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "Guide_TestRepo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRepoPath);
            // Simulate a git repository by creating a .git folder
            Directory.CreateDirectory(Path.Combine(_tempRepoPath, ".git"));
        }

        [Fact]
        public async Task InitCommand_ShouldInitializeRepositoryCorrectly()
        {
            // Act
            int exitCode = await InitCommand.InvokeAsync(_tempRepoPath);

            // Assert
            Assert.Equal(0, exitCode);

            // Verify .guide directory and SQLite database
            var peaIaDir = Path.Combine(_tempRepoPath, ".guide");
            Assert.True(Directory.Exists(peaIaDir));
            Assert.True(File.Exists(Path.Combine(peaIaDir, "project_graph.db")));

            // Verify AI templates
            Assert.True(File.Exists(Path.Combine(_tempRepoPath, ".cursorrules")));
            Assert.True(File.Exists(Path.Combine(_tempRepoPath, ".windsurfrules")));
            Assert.True(File.Exists(Path.Combine(_tempRepoPath, ".agents", "AGENTS.md")));
            Assert.True(File.Exists(Path.Combine(_tempRepoPath, ".github", "copilot-instructions.md")));

            // Verify template content holds architectural rules
            var cursorrulesContent = await File.ReadAllTextAsync(Path.Combine(_tempRepoPath, ".cursorrules"));
            Assert.Contains("Guide.Semantic", cursorrulesContent);
            Assert.Contains("Guide.Core", cursorrulesContent);
        }

        [Fact]
        public async Task HookCommand_ShouldInstallPrePushHookCorrectly()
        {
            // Act
            int exitCode = await HookCommand.InvokeAsync(_tempRepoPath);

            // Assert
            Assert.Equal(0, exitCode);

            var prePushPath = Path.Combine(_tempRepoPath, ".git", "hooks", "pre-push");
            Assert.True(File.Exists(prePushPath));

            var hookContent = await File.ReadAllTextAsync(prePushPath);
            Assert.Contains("GUIDE", hookContent);
            Assert.Contains("validate", hookContent);
        }

        [Fact]
        public async Task ValidateCommand_ShouldRunValidatorsAndTransitionFsm()
        {
            // 1. Setup a dummy C# project in our temporary directory to validate
            await SetupDummyProjectAsync(_tempRepoPath);

            // 2. Initialize the repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // Find current git branch name of the temporary directory (defaults to "default" if git command fails)
            string branchName = GetCurrentGitBranchName(_tempRepoPath);
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            var stateFilePath = Path.Combine(_tempRepoPath, ".guide", "states", $"{safeBranchName}.json");

            // 3. Run validation
            int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: false);

            // Assert
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(stateFilePath));

            // Verify state in file is transitioned to StaticallyValid
            var json = await File.ReadAllTextAsync(stateFilePath);
            var stateDoc = JsonSerializer.Deserialize<StateData>(json);
            Assert.NotNull(stateDoc);
            Assert.Equal(FeatureState.StaticallyValid, stateDoc.State);
        }

        [Fact]
        public async Task IndexAndQueryContextCommands_ShouldWorkCorrectly()
        {
            // 1. Setup dummy C# project and initialize
            await SetupDummyProjectAsync(_tempRepoPath);
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // Add classes with relationships to test indexing
            var classAPath = Path.Combine(_tempRepoPath, "ClassA.cs");
            await File.WriteAllTextAsync(classAPath, @"
namespace Dummy
{
    public class ClassA
    {
        private readonly ClassB _b;
        public ClassA(ClassB b)
        {
            _b = b;
        }
    }
}");
            var classBPath = Path.Combine(_tempRepoPath, "ClassB.cs");
            await File.WriteAllTextAsync(classBPath, @"
namespace Dummy
{
    public class ClassB
    {
        public void DoSomething() {}
    }
}");

            // 2. Run IndexCommand
            int indexExitCode = await IndexCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, indexExitCode);

            // 3. Verify SQLite DB has version 1 with expected nodes
            var dbPath = Path.Combine(_tempRepoPath, ".guide", "project_graph.db");
            Assert.True(File.Exists(dbPath));

            var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
            var store = new Guide.Semantic.SqliteKnowledgeStore(connectionString);
            int latestVersion = await store.GetLatestGraphVersionAsync();
            Assert.Equal(1, latestVersion);

            var snapshot = await store.GetSnapshotAsync(1);
            var nodes = snapshot.Nodes.ToList();
            Assert.Contains(nodes, n => n.Name == "ClassA");
            Assert.Contains(nodes, n => n.Name == "ClassB");

            // 4. Run IndexCommand again to verify version increments (even without changes)
            int indexExitCode2 = await IndexCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, indexExitCode2);
            int latestVersion2 = await store.GetLatestGraphVersionAsync();
            Assert.Equal(2, latestVersion2);

            // 5. Run QueryContextCommand
            // Output is printed to Console/Stdout, we check that the command executes without error (exit code 0)
            int queryExitCode = await QueryContextCommand.InvokeAsync(_tempRepoPath, "ClassA", 1);
            Assert.Equal(0, queryExitCode);

            // Verify with an empty/invalid anchor (should fail or handle gracefully)
            int queryInvalidExitCode = await QueryContextCommand.InvokeAsync(_tempRepoPath, "", 1);
            Assert.Equal(1, queryInvalidExitCode);
        }

        [Fact]
        public async Task SearchAndWhyCommands_ShouldWorkCorrectly()
        {
            // 1. Setup dummy C# project and initialize
            await SetupDummyProjectAsync(_tempRepoPath);
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // 2. Create the .agents/knowledge/ folder and a sample rule file
            var agentsDir = Path.Combine(_tempRepoPath, ".agents");
            var knowledgeDir = Path.Combine(agentsDir, "knowledge");
            Directory.CreateDirectory(knowledgeDir);

            var rulePath = Path.Combine(knowledgeDir, "rule-002.md");
            await File.WriteAllTextAsync(rulePath, @"---
Tags: testing, csharp
Priority: High
Status: Active
AppliesTo: OrderService
Deprecated: false
Version: 1.0
Owner: Architect
---
# ADR-002: Integrity Directive

This is a rule governing OrderService.");

            // 3. Create classes with relationships to test why command
            var servicePath = Path.Combine(_tempRepoPath, "OrderService.cs");
            await File.WriteAllTextAsync(servicePath, @"
namespace Dummy
{
    public class OrderService
    {
    }
}");
            var controllerPath = Path.Combine(_tempRepoPath, "OrderController.cs");
            await File.WriteAllTextAsync(controllerPath, @"
namespace Dummy
{
    public class OrderController
    {
        private readonly OrderService _service;
        public OrderController(OrderService service)
        {
            _service = service;
        }
    }
}");
            var testPath = Path.Combine(_tempRepoPath, "OrderServiceTests.cs");
            await File.WriteAllTextAsync(testPath, @"
namespace Dummy
{
    public class OrderServiceTests
    {
        private readonly OrderService _service;
        public OrderServiceTests()
        {
            _service = new OrderService();
        }
    }
}");

            // 4. Run IndexCommand to populate semantic graph
            int indexExitCode = await IndexCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, indexExitCode);

            // 5. Test SearchCommand
            int searchExitCode = await SearchCommand.InvokeAsync(_tempRepoPath, "Integrity");
            Assert.Equal(0, searchExitCode);

            // Test SearchCommand with missing/empty query
            int searchEmptyExitCode = await SearchCommand.InvokeAsync(_tempRepoPath, "");
            Assert.Equal(1, searchEmptyExitCode);

            // 6. Test WhyCommand
            int whyExitCode = await WhyCommand.InvokeAsync(_tempRepoPath, "OrderService");
            Assert.Equal(0, whyExitCode);

            // Test WhyCommand with missing/empty anchor
            int whyEmptyExitCode = await WhyCommand.InvokeAsync(_tempRepoPath, "");
            Assert.Equal(1, whyEmptyExitCode);

            // Test WhyCommand with non-existent anchor (should return 1 since node not found)
            int whyNotFoundExitCode = await WhyCommand.InvokeAsync(_tempRepoPath, "NonExistentService");
            Assert.Equal(1, whyNotFoundExitCode);
        }

        [Fact]
        public async Task ValidateCommand_ShouldSkipTestsWhenNoChanges()
        {
            // 1. Setup a dummy C# project in our temporary directory to validate
            await SetupDummyProjectAsync(_tempRepoPath);

            // 2. Initialize real git repository to track files
            await RunGitCommandAsync("init", _tempRepoPath);
            await RunGitCommandAsync("config user.email test@example.com", _tempRepoPath);
            await RunGitCommandAsync("config user.name Test", _tempRepoPath);
            await RunGitCommandAsync("add .", _tempRepoPath);
            await RunGitCommandAsync("commit -m \"initial\"", _tempRepoPath);

            // 3. Initialize Guide repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // 4. Run validation with runTests: true
            int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: true);

            // Assert
            Assert.Equal(0, exitCode);

            // Verify state in file is transitioned to FunctionallyValid
            string branchName = GetCurrentGitBranchName(_tempRepoPath);
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            var stateFilePath = Path.Combine(_tempRepoPath, ".guide", "states", $"{safeBranchName}.json");
            Assert.True(File.Exists(stateFilePath));

            var json = await File.ReadAllTextAsync(stateFilePath);
            var stateDoc = JsonSerializer.Deserialize<StateData>(json);
            Assert.NotNull(stateDoc);
            Assert.Equal(FeatureState.FunctionallyValid, stateDoc.State);
        }

        [Fact]
        public async Task ValidateCommand_WithAutoHeal_ShouldSuccessfullyHealSemicolonError()
        {
            // 1. Setup a dummy C# project
            await SetupDummyProjectAsync(_tempRepoPath);

            // 2. Introduce a syntax error (missing semicolon) in Program.cs
            var brokenProgram = @"using System;
namespace Dummy
{
    public static class Program
    {
        public static void Main()
        {
            var x = 5
            Console.WriteLine(""Hello from dummy!"");
        }
    }
}";
            await File.WriteAllTextAsync(Path.Combine(_tempRepoPath, "Program.cs"), brokenProgram);

            // 3. Initialize Guide repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // 4. Run validation with runTests: false, autoHeal: true
            int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: false, autoHeal: true);

            // Assert
            Assert.Equal(0, exitCode);

            // Verify Program.cs was modified/healed
            var fixedContent = await File.ReadAllTextAsync(Path.Combine(_tempRepoPath, "Program.cs"));
            Assert.Contains("var x = 5;", fixedContent);

            // Verify state in file is transitioned to StaticallyValid
            string branchName = GetCurrentGitBranchName(_tempRepoPath);
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            var stateFilePath = Path.Combine(_tempRepoPath, ".guide", "states", $"{safeBranchName}.json");
            Assert.True(File.Exists(stateFilePath));

            var json = await File.ReadAllTextAsync(stateFilePath);
            var stateDoc = JsonSerializer.Deserialize<StateData>(json);
            Assert.NotNull(stateDoc);
            Assert.Equal(FeatureState.StaticallyValid, stateDoc.State);
        }

        [Fact]
        public async Task ValidateCommand_WithoutAutoHeal_ShouldFailForSemicolonError()
        {
            // 1. Setup a dummy C# project
            await SetupDummyProjectAsync(_tempRepoPath);

            // 2. Introduce a syntax error
            var brokenProgram = @"using System;
namespace Dummy
{
    public static class Program
    {
        public static void Main()
        {
            var x = 5
            Console.WriteLine(""Hello from dummy!"");
        }
    }
}";
            await File.WriteAllTextAsync(Path.Combine(_tempRepoPath, "Program.cs"), brokenProgram);

            // 3. Initialize Guide repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // 4. Run validation with runTests: false, autoHeal: false
            int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: false, autoHeal: false);

            // Assert
            Assert.Equal(1, exitCode); // Should fail

            // Verify Program.cs was NOT healed
            var content = await File.ReadAllTextAsync(Path.Combine(_tempRepoPath, "Program.cs"));
            Assert.Contains("var x = 5", content);
            Assert.DoesNotContain("var x = 5;", content);

            // Verify state in file is NOT StaticallyValid (should be transitioned back to Requested or default)
            string branchName = GetCurrentGitBranchName(_tempRepoPath);
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            var stateFilePath = Path.Combine(_tempRepoPath, ".guide", "states", $"{safeBranchName}.json");
            Assert.True(File.Exists(stateFilePath));

            var json = await File.ReadAllTextAsync(stateFilePath);
            var stateDoc = JsonSerializer.Deserialize<StateData>(json);
            Assert.NotNull(stateDoc);
            Assert.Equal(FeatureState.Requested, stateDoc.State);
        }

        [Fact]
        public async Task ValidateCommand_QuietMode_Success_ShouldPrintOnlySuccessMessage()
        {
            // 1. Setup a dummy C# project in our temporary directory to validate
            await SetupDummyProjectAsync(_tempRepoPath);

            // 2. Initialize the repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // Redirect Console.Out
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                // 3. Run validation with quiet: true
                int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: false, autoHeal: false, quiet: true);

                // Assert
                Assert.Equal(0, exitCode);
                var output = sw.ToString().Trim();
                Assert.Equal("[SUCCESS] Validation completed.", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public async Task ValidateCommand_QuietMode_Failure_ShouldPrintOnlyCleanErrors()
        {
            // 1. Setup a dummy C# project
            await SetupDummyProjectAsync(_tempRepoPath);

            // Break Program.cs so that it fails to compile
            var brokenProgram = @"
using System;
namespace Dummy
{
    public static class Program
    {
        public static void Main()
        {
            var x = 5
        }
    }
}";
            await File.WriteAllTextAsync(Path.Combine(_tempRepoPath, "Program.cs"), brokenProgram);

            // 2. Initialize Guide repository
            int initExitCode = await InitCommand.InvokeAsync(_tempRepoPath);
            Assert.Equal(0, initExitCode);

            // Redirect Console.Out
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                // 3. Run validation with quiet: true
                int exitCode = await ValidateCommand.InvokeAsync(_tempRepoPath, runTests: false, autoHeal: false, quiet: true);

                // Assert
                Assert.Equal(1, exitCode); // Should fail
                var output = sw.ToString().Trim();
                Assert.NotEmpty(output);
                Assert.DoesNotContain("[SUCCESS] Validation completed.", output);
                Assert.DoesNotContain("Validating repository", output);
                Assert.DoesNotContain("Active git branch", output);
                // Compiler error lines should be printed
                Assert.Contains("CS1002", output); // semicolon expected
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        private async Task RunGitCommandAsync(string arguments, string workingDir)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }

        private async Task SetupDummyProjectAsync(string directory)
        {
            // Create dummy .csproj
            var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
            await File.WriteAllTextAsync(Path.Combine(directory, "Dummy.csproj"), csprojContent);

            // Create dummy Program.cs
            var programContent = @"using System;
namespace Dummy
{
    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine(""Hello from dummy!"");
        }
    }
}";
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), programContent);

            // Create architecture-rules.json
            var archRulesContent = @"{
  ""Rules"": []
}";
            await File.WriteAllTextAsync(Path.Combine(directory, "architecture-rules.json"), archRulesContent);
        }

        private string GetCurrentGitBranchName(string workingDir)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
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
                // Ignore process exceptions
            }
            return "default";
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRepoPath))
                {
                    Directory.Delete(_tempRepoPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors in test run
            }
        }

        private class StateData
        {
            public FeatureState State { get; set; }
        }
    }
}
