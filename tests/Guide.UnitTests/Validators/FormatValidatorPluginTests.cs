using System;
using System.Threading;
using System.Threading.Tasks;
using Guide.Validation;
using Guide.Validation.Plugins;
using Xunit;

namespace Guide.UnitTests.Validators;

public class FormatValidatorPluginTests
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
    public async Task ValidateAsync_ShouldReturnSuccess_WhenFormatSucceeds()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, "No formatting changes detected."))
        };
        var plugin = new FormatValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task ValidateAsync_ShouldParseViolations_WhenFormatFails()
    {
        // Arrange
        var output = "Formatting violation in Program.cs:\r\n  warning: Fix naming style\r\n  error: Fix brace style";
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((2, output))
        };
        var plugin = new FormatValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Violations);
        Assert.Contains(result.Violations, v => v.Contains("warning: Fix naming style"));
        Assert.Contains(result.Violations, v => v.Contains("error: Fix brace style"));
    }

    [Fact]
    public async Task ValidateAsync_ShouldFallbackToGenericError_WhenFormatFailsWithoutKeywords()
    {
        // Arrange
        var output = "Unexpected exit from tool process.";
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((1, output))
        };
        var plugin = new FormatValidatorPlugin(mockRunner);

        // Act
        var result = await plugin.ValidateAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        var violations = Assert.Single(result.Violations);
        Assert.Contains("Format validation failed with exit code 1", violations);
        Assert.Contains("Unexpected exit", violations);
    }
}
