using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation.Plugins;

public class FormatValidatorPlugin : IValidator
{
    private readonly ICommandLineRunner _runner;

    public string Name => "FormatValidator";

    public FormatValidatorPlugin(ICommandLineRunner runner)
    {
        _runner = runner;
    }

    public async Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct)
    {
        var (exitCode, output) = await _runner.ExecuteAsync("dotnet", $"format \"{solutionPath}\" --verify-no-changes", ct: ct);

        if (exitCode == 0)
        {
            return new ValidationPluginResult(true, Array.Empty<string>());
        }

        var violations = new List<string>();
        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) &&
                (trimmed.Contains("format", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("change", StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(trimmed);
            }
        }

        if (violations.Count == 0)
        {
            violations.Add($"Format validation failed with exit code {exitCode}. Output: {output.Trim()}");
        }

        return new ValidationPluginResult(false, violations);
    }
}
