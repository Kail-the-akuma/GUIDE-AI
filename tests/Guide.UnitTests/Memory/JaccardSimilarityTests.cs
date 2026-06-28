using Xunit;
using Guide.Memory;

namespace Guide.UnitTests.Memory;

public class JaccardSimilarityTests
{
    [Theory]
    [InlineData("hello world", "hello world", 1.0)]
    [InlineData("hello world", "world hello", 0.57)]
    [InlineData("hello", "world", 0.0)]
    [InlineData("", "", 1.0)]
    public void Compute_CalculatesExpectedSimilarity(string s1, string s2, double expected)
    {
        double score = JaccardSimilarity.Compute(s1, s2);
        Assert.Equal(expected, score, 2);
    }

    [Fact]
    public void Compute_CalculatesPartialSimilarity()
    {
        double score = JaccardSimilarity.Compute("abc", "abcd");
        Assert.Equal(0.3333, score, 4);
    }
}
