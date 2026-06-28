using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Validation;
using Xunit;

namespace Guide.UnitTests.Validators;

public class ParallelValidatorTests
{
    private class TestValidator : IValidator
    {
        private readonly bool _isSuccess;
        private readonly string[] _violations;
        public string Name { get; }

        public TestValidator(string name, bool isSuccess, params string[] violations)
        {
            Name = name;
            _isSuccess = isSuccess;
            _violations = violations;
        }

        public Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct)
        {
            return Task.FromResult(new ValidationPluginResult(_isSuccess, _violations));
        }
    }

    [Fact]
    public async Task ValidateProjectAsync_ShouldReturnSuccess_WhenAllPluginsSucceed()
    {
        // Arrange
        var v1 = new TestValidator("V1", true);
        var v2 = new TestValidator("V2", true);
        var validator = new ParallelValidator(new[] { v1, v2 });

        // Act
        var result = await validator.ValidateProjectAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateProjectAsync_ShouldReturnFailure_WhenAnyPluginFails()
    {
        // Arrange
        var v1 = new TestValidator("V1", true);
        var v2 = new TestValidator("V2", false, "Violation 1", "Violation 2");
        var validator = new ParallelValidator(new[] { v1, v2 });

        // Act
        var result = await validator.ValidateProjectAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Violation 1", result.Errors);
        Assert.Contains("Violation 2", result.Errors);
    }

    [Fact]
    public async Task ValidateProjectAsync_ShouldAggregateAllViolations_WhenMultiplePluginsFail()
    {
        // Arrange
        var v1 = new TestValidator("V1", false, "Err from V1");
        var v2 = new TestValidator("V2", false, "Err from V2");
        var validator = new ParallelValidator(new[] { v1, v2 });

        // Act
        var result = await validator.ValidateProjectAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Err from V1", result.Errors);
        Assert.Contains("Err from V2", result.Errors);
    }

    [Fact]
    public async Task ValidateProjectAsync_ShouldHandlePluginExceptionsGracefully()
    {
        // Arrange
        var v1 = new TestValidator("V1", true);
        var badPlugin = new ExceptionThrowingValidator();
        var validator = new ParallelValidator(new IValidator[] { v1, badPlugin });

        // Act
        var result = await validator.ValidateProjectAsync("test.sln", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Errors);
        Assert.Contains("Plugin 'BadPlugin' threw exception", violation);
        Assert.Contains("Simulated failure", violation);
    }

    private class ExceptionThrowingValidator : IValidator
    {
        public string Name => "BadPlugin";

        public Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }
}
