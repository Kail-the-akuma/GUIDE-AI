using System;
using System.Threading;
using System.Threading.Tasks;
using Guide.Validation;
using Guide.Validation.Plugins;
using Xunit;

namespace Guide.UnitTests.Validators;

public class BuildValidatorPluginTests
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

    [Fact]
    public async Task ValidateAsync_ShouldReturnSuccess_WhenBuildSucceeds()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, "Build succeeded."))
        };
        var plugin = new BuildValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task ValidateAsync_ShouldParseErrors_WhenBuildFailsWithErrors()
    {
        // Arrange
        var output = "Program.cs(12,5): error CS1002: ; expected\r\nSome other log line";
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((1, output))
        };
        var plugin = new BuildValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        var violations = Assert.Single(result.Violations);
        Assert.Contains("Program.cs(12,5): error CS1002: ; expected", violations);
    }

    [Fact]
    public async Task ValidateAsync_ShouldFallbackToGenericError_WhenBuildFailsWithoutRegexMatches()
    {
        // Arrange
        var output = "MSBuild failed critically without standard error format.";
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((2, output))
        };
        var plugin = new BuildValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        var violations = Assert.Single(result.Violations);
        Assert.Contains("Build failed with exit code 2", violations);
        Assert.Contains("MSBuild failed critically", violations);
    }
}
