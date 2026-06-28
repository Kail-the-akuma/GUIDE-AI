using System;
using Guide.Validation;
using Xunit;

namespace Guide.UnitTests.Validators;

public class PatchApplierTests
{
    [Fact]
    public void ApplyPatches_ShouldApplySinglePatch()
    {
        // Arrange
        string targetContent = "line1\nline2\nline3";
        string patch = @"
<<<<<<< SEARCH
line2
=======
newLine2
>>>>>>> REPLACE
";

        // Act
        string result = PatchApplier.ApplyPatches(targetContent, patch);

        // Assert
        Assert.Equal("line1\nnewLine2\nline3", result);
    }

    [Fact]
    public void ApplyPatches_ShouldApplyMultiplePatches()
    {
        // Arrange
        string targetContent = "line1\nline2\nline3\nline4";
        string patch = @"
<<<<<<< SEARCH
line2
=======
newLine2
>>>>>>> REPLACE

<<<<<<< SEARCH
line4
=======
newLine4
>>>>>>> REPLACE
";

        // Act
        string result = PatchApplier.ApplyPatches(targetContent, patch);

        // Assert
        Assert.Equal("line1\nnewLine2\nline3\nnewLine4", result);
    }

    [Fact]
    public void ApplyPatches_ShouldHandleWindowsNewlines()
    {
        // Arrange
        string targetContent = "line1\r\nline2\r\nline3";
        string patch = "<<<<<<< SEARCH\r\nline2\r\n=======\r\nnewLine2\r\n>>>>>>> REPLACE";

        // Act
        string result = PatchApplier.ApplyPatches(targetContent, patch);

        // Assert
        Assert.Equal("line1\r\nnewLine2\r\nline3", result);
    }

    [Fact]
    public void ApplyPatches_ShouldHandleMixedNewlinesInPatch()
    {
        // Arrange
        string targetContent = "line1\r\nline2\r\nline3";
        string patch = "<<<<<<< SEARCH\nline2\n=======\nnewLine2\n>>>>>>> REPLACE"; // patch has Unix newlines

        // Act
        string result = PatchApplier.ApplyPatches(targetContent, patch);

        // Assert
        Assert.Equal("line1\r\nnewLine2\r\nline3", result);
    }

    [Fact]
    public void ApplyPatches_ShouldThrowException_WhenSearchBlockNotFound()
    {
        // Arrange
        string targetContent = "line1\nline2\nline3";
        string patch = @"
<<<<<<< SEARCH
nonexistentLine
=======
newLine
>>>>>>> REPLACE
";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => PatchApplier.ApplyPatches(targetContent, patch));
        Assert.Contains("Search content not found", ex.Message);
    }
}
