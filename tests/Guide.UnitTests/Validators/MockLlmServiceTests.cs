using System.Threading;
using System.Threading.Tasks;
using Guide.Validation;
using Xunit;

namespace Guide.UnitTests.Validators;

public class MockLlmServiceTests
{
    [Fact]
    public async Task GeneratePatchAsync_ReturnsEmpty_WhenNoViolationOrError()
    {
        // Arrange
        var service = new MockLlmService();
        string prompt = "This is a normal query about C# design.";

        // Act
        string patch = await service.GeneratePatchAsync(prompt, CancellationToken.None);

        // Assert
        Assert.Equal(string.Empty, patch);
    }

    [Fact]
    public async Task GeneratePatchAsync_ReturnsPatch_WhenMissingSemicolon()
    {
        // Arrange
        var service = new MockLlmService();
        string prompt = @"We have a compiler error: CS1002 (semicolon expected)
File content:
using System;
class Foo
{
    void Bar()
    {
        var x = 5
    }
}";

        // Act
        string patch = await service.GeneratePatchAsync(prompt, CancellationToken.None);

        // Assert
        Assert.Contains("<<<<<<< SEARCH", patch);
        Assert.Contains("var x = 5", patch);
        Assert.Contains("=======", patch);
        Assert.Contains("var x = 5;", patch);
        Assert.Contains(">>>>>>> REPLACE", patch);
    }

    [Fact]
    public async Task GeneratePatchAsync_ReturnsPatch_WhenArchitecturalViolation()
    {
        // Arrange
        var service = new MockLlmService();
        string prompt = @"Architectural drift violation detected!
Do not reference Guide.Validation in forbidden namespaces.
File content:
using System;
using Guide.Validation;

class Foo {}";

        // Act
        string patch = await service.GeneratePatchAsync(prompt, CancellationToken.None);

        // Assert
        Assert.Contains("<<<<<<< SEARCH", patch);
        Assert.Contains("using Guide.Validation;", patch);
        Assert.Contains("=======", patch);
        Assert.Contains(">>>>>>> REPLACE", patch);
    }

    [Fact]
    public async Task GeneratePatchAsync_ReturnsPatch_WhenTypeMismatchSyntaxError()
    {
        // Arrange
        var service = new MockLlmService();
        string prompt = @"Compiler syntax error: type mismatch
File content:
class Foo
{
    void Bar()
    {
        int x = ""hello""
    }
}";

        // Act
        string patch = await service.GeneratePatchAsync(prompt, CancellationToken.None);

        // Assert
        Assert.Contains("<<<<<<< SEARCH", patch);
        Assert.Contains("int x = \"hello\"", patch);
        Assert.Contains("=======", patch);
        Assert.Contains("string x = \"hello\";", patch);
        Assert.Contains(">>>>>>> REPLACE", patch);
    }
}
