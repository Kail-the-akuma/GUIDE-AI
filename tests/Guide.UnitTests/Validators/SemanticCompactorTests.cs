using Xunit;
using Guide.Validation;

namespace Guide.UnitTests.Validators;

public class SemanticCompactorTests
{
    [Fact]
    public void CompactCode_NullOrEmpty_ReturnsOriginal()
    {
        Assert.Null(SemanticCompactor.CompactCode(null!));
        Assert.Equal(string.Empty, SemanticCompactor.CompactCode(string.Empty));
    }

    [Fact]
    public void CompactCode_RemovesSingleLineComments()
    {
        var input = @"
public class Foo
{
    // This is a comment
    public void Bar()
    {
        var x = 1; // Inline comment
    }
}";
        var result = SemanticCompactor.CompactCode(input);

        Assert.DoesNotContain("This is a comment", result);
        Assert.DoesNotContain("Inline comment", result);
        Assert.Contains("public class Foo", result);
        Assert.Contains("var x = 1;", result);
    }

    [Fact]
    public void CompactCode_RemovesMultiLineComments()
    {
        var input = @"
public class Foo
{
    /* This is a 
       multi-line comment */
    public void Bar()
    {
        /* Inline multi-line */ var x = 1;
    }
}";
        var result = SemanticCompactor.CompactCode(input);

        Assert.DoesNotContain("This is a", result);
        Assert.DoesNotContain("multi-line comment", result);
        Assert.DoesNotContain("Inline multi-line", result);
        Assert.Contains("public void Bar()", result);
        Assert.Contains("var x = 1;", result);
    }

    [Fact]
    public void CompactCode_RemovesDocumentationComments()
    {
        var input = @"
/// <summary>
/// This is a doc comment
/// </summary>
public class Foo
{
    /** <summary>
        Another doc comment
        </summary> */
    public void Bar()
    {
    }
}";
        var result = SemanticCompactor.CompactCode(input);

        Assert.DoesNotContain("summary", result);
        Assert.DoesNotContain("This is a doc comment", result);
        Assert.DoesNotContain("Another doc comment", result);
        Assert.Contains("public class Foo", result);
        Assert.Contains("public void Bar()", result);
    }
}
