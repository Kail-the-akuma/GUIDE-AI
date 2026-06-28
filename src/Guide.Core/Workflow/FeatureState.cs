namespace Guide.Core.Workflow;

public enum FeatureState
{
    Requested,
    ContextReady,
    CodeGenerated,
    StaticallyValid,
    FunctionallyValid,
    Completed
}

public enum FeatureTrigger
{
    Analyze,
    ContextAssembled,
    CodeValidatedWithErrors,
    CodeValidatedSuccessfully,
    TestsPassed,
    TestsFailed,
    MergeCompleted
}
