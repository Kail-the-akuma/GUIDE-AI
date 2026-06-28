using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Guide.Core.Workflow;
using Xunit;

namespace Guide.UnitTests.Core;

public class FeatureStateMachineTests
{
    [Fact]
    public void Constructor_SetsInitialStateAndStateFilePath()
    {
        // Arrange
        var initialState = FeatureState.Requested;
        var branchName = "feature/test-fsm";

        // Act
        var fsm = new FeatureStateMachine(initialState, branchName);

        // Assert
        Assert.Equal(initialState, fsm.State);
        Assert.Contains("feature_test-fsm.json", fsm.StateFilePath);
    }

    [Fact]
    public async Task FireAsync_TransitionsStateAndSavesToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var fsm = new FeatureStateMachine(FeatureState.Requested, "test-branch", tempFile);

            // Act
            await fsm.FireAsync(FeatureTrigger.Analyze);

            // Assert
            Assert.Equal(FeatureState.ContextReady, fsm.State);
            Assert.True(File.Exists(tempFile));

            var content = await File.ReadAllTextAsync(tempFile);
            var data = JsonSerializer.Deserialize<StateData>(content);
            Assert.NotNull(data);
            Assert.Equal(FeatureState.ContextReady, data.State);
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
    public async Task FireAsync_ThrowsException_OnInvalidTransition()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var fsm = new FeatureStateMachine(FeatureState.Requested, "test-branch", tempFile);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => fsm.FireAsync(FeatureTrigger.MergeCompleted));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Theory]
    [InlineData(FeatureState.Requested, FeatureTrigger.Analyze, FeatureState.ContextReady)]
    [InlineData(FeatureState.ContextReady, FeatureTrigger.ContextAssembled, FeatureState.CodeGenerated)]
    [InlineData(FeatureState.CodeGenerated, FeatureTrigger.CodeValidatedSuccessfully, FeatureState.StaticallyValid)]
    [InlineData(FeatureState.CodeGenerated, FeatureTrigger.CodeValidatedWithErrors, FeatureState.Requested)]
    [InlineData(FeatureState.StaticallyValid, FeatureTrigger.TestsPassed, FeatureState.FunctionallyValid)]
    [InlineData(FeatureState.StaticallyValid, FeatureTrigger.TestsFailed, FeatureState.Requested)]
    [InlineData(FeatureState.FunctionallyValid, FeatureTrigger.MergeCompleted, FeatureState.Completed)]
    public async Task LinearAndLoopTransitions_AreValid(FeatureState startState, FeatureTrigger trigger, FeatureState expectedState)
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var fsm = new FeatureStateMachine(startState, "test-branch", tempFile);

            // Act
            await fsm.FireAsync(trigger);

            // Assert
            Assert.Equal(expectedState, fsm.State);
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
    public async Task LoadFromFileAsync_LoadsExistingState()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new StateData { State = FeatureState.StaticallyValid };
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(tempFile, json);

            // Act
            var fsm = await FeatureStateMachine.LoadFromFileAsync("test-branch", tempFile);

            // Assert
            Assert.Equal(FeatureState.StaticallyValid, fsm.State);
            Assert.Equal(tempFile, fsm.StateFilePath);
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
    public async Task LoadFromFileAsync_DefaultsToRequested_IfFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");

        // Act
        var fsm = await FeatureStateMachine.LoadFromFileAsync("test-branch", nonExistentFile);

        // Assert
        Assert.Equal(FeatureState.Requested, fsm.State);
        Assert.Equal(nonExistentFile, fsm.StateFilePath);
    }

    [Fact]
    public void CanFire_ReturnsCorrectValue()
    {
        // Arrange
        var fsm = new FeatureStateMachine(FeatureState.Requested, "test-branch");

        // Assert
        Assert.True(fsm.CanFire(FeatureTrigger.Analyze));
        Assert.False(fsm.CanFire(FeatureTrigger.MergeCompleted));
    }

    private class StateData
    {
        public FeatureState State { get; set; }
    }
}
