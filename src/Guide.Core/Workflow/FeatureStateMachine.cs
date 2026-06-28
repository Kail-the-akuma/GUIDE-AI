using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Stateless;

namespace Guide.Core.Workflow;

public class FeatureStateMachine
{
    private readonly StateMachine<FeatureState, FeatureTrigger> _machine;
    private readonly string _stateFilePath;

    public FeatureState State => _machine.State;
    public string StateFilePath => _stateFilePath;

    public FeatureStateMachine(FeatureState initialState, string branchName, string? stateFilePath = null)
    {
        _machine = new StateMachine<FeatureState, FeatureTrigger>(initialState);

        if (!string.IsNullOrEmpty(stateFilePath))
        {
            _stateFilePath = stateFilePath;
        }
        else
        {
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            _stateFilePath = Path.Combine(".guide", "states", $"{safeBranchName}.json");
        }

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // 1. Requested -> ContextReady via Analyze
        _machine.Configure(FeatureState.Requested)
            .Permit(FeatureTrigger.Analyze, FeatureState.ContextReady);

        // 2. ContextReady -> CodeGenerated via ContextAssembled
        _machine.Configure(FeatureState.ContextReady)
            .Permit(FeatureTrigger.ContextAssembled, FeatureState.CodeGenerated);

        // 3. CodeGenerated -> StaticallyValid via CodeValidatedSuccessfully
        //    CodeGenerated -> Requested via CodeValidatedWithErrors (Auto-Healing Loop)
        _machine.Configure(FeatureState.CodeGenerated)
            .Permit(FeatureTrigger.CodeValidatedSuccessfully, FeatureState.StaticallyValid)
            .Permit(FeatureTrigger.CodeValidatedWithErrors, FeatureState.Requested);

        // 4. StaticallyValid -> FunctionallyValid via TestsPassed
        //    StaticallyValid -> Requested via TestsFailed (Auto-Healing Loop)
        _machine.Configure(FeatureState.StaticallyValid)
            .Permit(FeatureTrigger.TestsPassed, FeatureState.FunctionallyValid)
            .Permit(FeatureTrigger.TestsFailed, FeatureState.Requested);

        // 5. FunctionallyValid -> Completed via MergeCompleted
        _machine.Configure(FeatureState.FunctionallyValid)
            .Permit(FeatureTrigger.MergeCompleted, FeatureState.Completed);

        // 6. Completed is a terminal state in the linear FSM, so no outbound transitions configured.
    }

    public async Task FireAsync(FeatureTrigger trigger)
    {
        await _machine.FireAsync(trigger);
        await SaveStateAsync();
    }

    public bool CanFire(FeatureTrigger trigger)
    {
        return _machine.CanFire(trigger);
    }

    public async Task SaveStateAsync()
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var json = JsonSerializer.Serialize(new StateData { State = _machine.State });
        await File.WriteAllTextAsync(_stateFilePath, json);
    }

    public static async Task<FeatureStateMachine> LoadFromFileAsync(string branchName, string? stateFilePath = null)
    {
        string path;
        if (!string.IsNullOrEmpty(stateFilePath))
        {
            path = stateFilePath;
        }
        else
        {
            var safeBranchName = branchName.Replace("/", "_").Replace("\\", "_");
            path = Path.Combine(".guide", "states", $"{safeBranchName}.json");
        }

        FeatureState state = FeatureState.Requested;
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var doc = JsonSerializer.Deserialize<StateData>(json);
                if (doc != null)
                {
                    state = doc.State;
                }
            }
            catch
            {
                // Fallback to default Requested if deserialization fails
            }
        }
        return new FeatureStateMachine(state, branchName, path);
    }

    private class StateData
    {
        public FeatureState State { get; set; }
    }
}
