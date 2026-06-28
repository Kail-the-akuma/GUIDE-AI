using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Validation;
using Xunit;

namespace Guide.UnitTests.Validators;

public class AutoHealerTests
{
    private class TestCommandLineRunner : ICommandLineRunner
    {
        public Func<string, string, string?, Task<(int ExitCode, string Output)>>? OnExecute { get; set; }

        public Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default)
        {
            if (OnExecute != null)
            {
                return OnExecute(command, arguments, workingDirectory);
            }
            return Task.FromResult((0, ""));
        }
    }

    private class TestKnowledgeStore : IKnowledgeStore
    {
        public Task SaveGraphSnapshotAsync(int version, ExtractedKnowledge knowledge) => Task.CompletedTask;
        public Task<int> GetLatestGraphVersionAsync() => Task.FromResult(0);
        public Task<ExtractedKnowledge> GetSnapshotAsync(int version) => Task.FromResult(new ExtractedKnowledge(Array.Empty<RichNode>(), Array.Empty<RichEdge>()));
        public Task MapFeatureFlowAsync(string featureName, IEnumerable<string> relatedNodes) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetFeatureFlowAsync(string featureName) => Task.FromResult(Enumerable.Empty<string>());
    }

    private class TestLlmService : ILlmService
    {
        public Func<string, Task<string>>? OnGeneratePatch { get; set; }

        public Task<string> GeneratePatchAsync(string prompt, CancellationToken ct)
        {
            if (OnGeneratePatch != null)
            {
                return OnGeneratePatch(prompt);
            }
            return Task.FromResult(string.Empty);
        }
    }

    private class MockEngineeringMemory : IEngineeringMemory
    {
        public Func<string, string, double, Task<IEnumerable<MemoryMatch>>>? OnFindSimilar { get; set; }
        public Func<string, string, string, string, bool, Task>? OnRecordCorrection { get; set; }

        public Task<IEnumerable<MemoryMatch>> FindSimilarCorrectionsAsync(string errorCode, string errorLog, double similarityThreshold = 0.7, string? fileExtension = null)
        {
            if (OnFindSimilar != null)
            {
                return OnFindSimilar(errorCode, errorLog, similarityThreshold);
            }
            return Task.FromResult(Enumerable.Empty<MemoryMatch>());
        }

        public Task RecordCorrectionAsync(string errorCode, string errorLog, string originalSnippet, string patchedSnippet, bool isSuccess, string? fileExtension = null)
        {
            if (OnRecordCorrection != null)
            {
                return OnRecordCorrection(errorCode, errorLog, originalSnippet, patchedSnippet, isSuccess);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HealAsync_ShouldHealSyntaxErrorInOneIteration()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new MockLlmService();
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) =>
                {
                    string content = File.ReadAllText(tempFile);
                    if (content.Contains("var x = 5;"))
                    {
                        return Task.FromResult((0, "Build succeeded"));
                    }
                    else
                    {
                        return Task.FromResult((1, "Foo.cs(1,26): error CS1002: ; expected"));
                    }
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Iterations);
            Assert.Empty(result.Errors);
            string finalContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("var x = 5;", finalContent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldFailAfterFiveIterations_WhenErrorsPersist()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                // Always return a patch that replaces x = 5 with x = 5; but the compiler will still fail
                OnGeneratePatch = (p) => Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE")
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                // Always fail
                OnExecute = (cmd, args, wd) => Task.FromResult((1, "Foo.cs(1,26): error CS1002: ; expected"))
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(5, result.Iterations);
            Assert.NotEmpty(result.Errors);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldAbortEarly_WhenLlmReturnsEmptyPatch()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) => Task.FromResult(string.Empty)
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner();

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "Some error" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(1, result.Iterations);
            Assert.Contains("LLM service returned an empty patch.", result.Errors);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldAbortEarly_WhenPatchApplicationThrowsException()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                // Patch references non-existent code
                OnGeneratePatch = (p) => Task.FromResult("<<<<<<< SEARCH\nnonexistent\n=======\nreplacement\n>>>>>>> REPLACE")
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner();

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "Some error" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(1, result.Iterations);
            Assert.Contains(result.Errors, e => e.Contains("Failed to apply patch"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldHealUsingMemoryMatch_AndNotCallLlm()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var llmCalled = false;
            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) =>
                {
                    llmCalled = true;
                    return Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE");
                }
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) =>
                {
                    string content = File.ReadAllText(tempFile);
                    if (content.Contains("var x = 5;"))
                    {
                        return Task.FromResult((0, "Build succeeded"));
                    }
                    else
                    {
                        return Task.FromResult((1, "Foo.cs(1,26): error CS1002: ; expected"));
                    }
                }
            };

            var memoryCalled = false;
            var mockMemory = new MockEngineeringMemory
            {
                OnFindSimilar = (code, log, threshold) =>
                {
                    memoryCalled = true;
                    Assert.Equal("CS1002", code);
                    var match = new MemoryMatch(
                        "var x = 5",
                        "<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE",
                        1.0
                    );
                    return Task.FromResult<IEnumerable<MemoryMatch>>(new[] { match });
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm, mockMemory);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Iterations); // Success immediately, no iterations
            Assert.True(memoryCalled);
            Assert.False(llmCalled);
            string finalContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("var x = 5;", finalContent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldFallbackToLlm_WhenMemoryMatchFailsValidation()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var llmCalled = false;
            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) =>
                {
                    llmCalled = true;
                    return Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE");
                }
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) =>
                {
                    string content = File.ReadAllText(tempFile);
                    if (content.Contains("var x = 5;"))
                    {
                        return Task.FromResult((0, "Build succeeded"));
                    }
                    else
                    {
                        return Task.FromResult((1, "Foo.cs(1,26): error CS1002: ; expected"));
                    }
                }
            };

            var mockMemory = new MockEngineeringMemory
            {
                OnFindSimilar = (code, log, threshold) =>
                {
                    // Memory returns a bad patch that doesn't actually compile or isn't right
                    var match = new MemoryMatch(
                        "var x = 5",
                        "<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5_bad;\n>>>>>>> REPLACE",
                        1.0
                    );
                    return Task.FromResult<IEnumerable<MemoryMatch>>(new[] { match });
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm, mockMemory);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Iterations); // Handled by LLM
            Assert.True(llmCalled);
            string finalContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("var x = 5;", finalContent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldRecordSuccess_WhenLlmSucceeds()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) => Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE")
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) =>
                {
                    string content = File.ReadAllText(tempFile);
                    if (content.Contains("var x = 5;"))
                    {
                        return Task.FromResult((0, "Build succeeded"));
                    }
                    else
                    {
                        return Task.FromResult((1, "Foo.cs(1,26): error CS1002: ; expected"));
                    }
                }
            };

            string? recordedCode = null;
            string? recordedLog = null;
            string? recordedOriginal = null;
            string? recordedPatched = null;
            bool? recordedSuccess = null;

            var mockMemory = new MockEngineeringMemory
            {
                OnRecordCorrection = (code, log, original, patched, success) =>
                {
                    recordedCode = code;
                    recordedLog = log;
                    recordedOriginal = original;
                    recordedPatched = patched;
                    recordedSuccess = success;
                    return Task.CompletedTask;
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm, mockMemory);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("CS1002", recordedCode);
            Assert.Equal("Foo.cs(1,26): error CS1002: ; expected", recordedLog);
            Assert.Equal("var x = 5", recordedOriginal);
            Assert.Equal("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE", recordedPatched);
            Assert.True(recordedSuccess);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldCompressErrors()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) => Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE")
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) => Task.FromResult((0, "Build succeeded"))
            };

            string? recordedCode = null;
            string? recordedLog = null;
            var mockMemory = new MockEngineeringMemory
            {
                OnRecordCorrection = (code, log, original, patched, success) =>
                {
                    recordedCode = code;
                    recordedLog = log;
                    return Task.CompletedTask;
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm, mockMemory);
            var initialErrors = new[] { @"C:\Path\To\Foo.cs(1,26): error CS1002: ; expected [C:\Path\To\Project.csproj]" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("CS1002", recordedCode);
            Assert.Equal("Foo.cs(1,26): error CS1002: ; expected", recordedLog);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldCatchSyntaxErrorsInSyntaxGate_AndNotRunBuild()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "class Foo\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            var mockLlm = new TestLlmService
            {
                // Generate a patch that introduces a syntax error (missing semicolon and invalid code)
                OnGeneratePatch = (p) => Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5 invalid_syntax\n>>>>>>> REPLACE")
            };
            var mockStore = new TestKnowledgeStore();
            bool runnerExecuted = false;
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) =>
                {
                    runnerExecuted = true;
                    return Task.FromResult((0, "Build succeeded"));
                }
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "Foo.cs(1,26): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.False(runnerExecuted); // Crucial: Validator run was bypassed
            Assert.Contains(result.Errors, e => e.Contains("error CS1002") || e.Contains("error CS1513") || e.Contains("error CS1003"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldPruneErrorsFromOtherFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, "MyTargetFile.cs");
        try
        {
            await File.WriteAllTextAsync(tempFile, "class MyTargetFile\n{\n    void Bar()\n    {\n        var x = 5\n    }\n}");

            string? receivedPrompt = null;
            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) =>
                {
                    receivedPrompt = p;
                    return Task.FromResult("<<<<<<< SEARCH\nvar x = 5\n=======\nvar x = 5;\n>>>>>>> REPLACE");
                }
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) => Task.FromResult((0, "Build succeeded"))
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);

            var initialErrors = new[]
            {
                "MyTargetFile.cs(5,15): error CS1002: ; expected",
                "OtherFile.cs(12,8): error CS0103: The name 'y' does not exist in the current context"
            };

            // Act
            var result = await healer.HealAsync("solution.sln", "C:/repo", tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(receivedPrompt);
            Assert.Contains("MyTargetFile.cs(5,15): error CS1002: ; expected", receivedPrompt);
            Assert.DoesNotContain("OtherFile.cs", receivedPrompt);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task HealAsync_ShouldStripCommentsFromPrompt_ButKeepOriginalFileUnmodifiedUntilPatchApplied()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, "TargetForStripping.cs");
        try
        {
            // Create a file with comments
            await File.WriteAllTextAsync(tempFile, @"
// This is a leading comment that should be stripped.
class TargetForStripping
{
    void Run()
    {
        // Inside comment
        var x = 5
    }
}");

            string? receivedPrompt = null;
            var mockLlm = new TestLlmService
            {
                OnGeneratePatch = (p) =>
                {
                    receivedPrompt = p;
                    // The LLM generates a patch against the comment-stripped content
                    return Task.FromResult(@"<<<<<<< SEARCH
class TargetForStripping
{
    void Run()
    {
        
        var x = 5
    }
}
=======
class TargetForStripping
{
    void Run()
    {
        var x = 5;
    }
}
>>>>>>> REPLACE");
                }
            };
            var mockStore = new TestKnowledgeStore();
            var mockRunner = new TestCommandLineRunner
            {
                OnExecute = (cmd, args, wd) => Task.FromResult((0, "Build succeeded"))
            };

            var healer = new AutoHealer(mockRunner, mockStore, mockLlm);
            var initialErrors = new[] { "TargetForStripping.cs(7,15): error CS1002: ; expected" };

            // Act
            var result = await healer.HealAsync("solution.sln", tempDir, tempFile, initialErrors, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(receivedPrompt);
            // Verify comments are stripped in the prompt
            Assert.DoesNotContain("This is a leading comment that should be stripped.", receivedPrompt);
            Assert.DoesNotContain("Inside comment", receivedPrompt);
            
            // Verify the file was updated with the patch
            string finalContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("var x = 5;", finalContent);
            Assert.DoesNotContain("This is a leading comment", finalContent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
