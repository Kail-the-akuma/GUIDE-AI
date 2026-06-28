namespace Guide.Benchmarks;

public class TestSkipperSimulator
{
    public static double RunTests(bool useSkipper, int totalTestsCount, int modifiedEntitiesCount)
    {
        double averageTestDurationMs = 150.0; // 150ms per integration/unit test

        if (!useSkipper)
        {
            // Execute whole test suite sequentially (Control)
            return (totalTestsCount * averageTestDurationMs) / 1000.0; // in seconds
        }
        else
        {
            // Execute only tests affected by modified classes (GUIDE)
            int impactedTestsCount = modifiedEntitiesCount * 3; // Ratio of 3 tests per class
            if (impactedTestsCount > totalTestsCount)
            {
                impactedTestsCount = totalTestsCount;
            }
            return (impactedTestsCount * averageTestDurationMs) / 1000.0; // in seconds
        }
    }
}
