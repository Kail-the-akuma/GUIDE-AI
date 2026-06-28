using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation.Plugins;

public class BuildValidatorPlugin : IValidator
{
    private readonly ICommandLineRunner _runner;
    private static readonly Regex BuildErrorRegex = new(
        @"^(.+)\((\d+),(\d+)\): error (\w+): (.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "BuildValidator";

    public BuildValidatorPlugin(ICommandLineRunner runner)
    {
        _runner = runner;
    }

    public async Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct)
    {
        var (exitCode, output) = await _runner.ExecuteAsync("dotnet", $"build \"{solutionPath}\"", ct: ct);

        if (exitCode == 0)
        {
            return new ValidationPluginResult(true, Array.Empty<string>());
        }

        var violations = new List<string>();
        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var match = BuildErrorRegex.Match(trimmed);
            if (match.Success)
            {
                violations.Add(trimmed);
            }
        }

        if (violations.Count == 0)
        {
            violations.Add($"Build failed with exit code {exitCode}. Output: {output.Trim()}");
        }

        return new ValidationPluginResult(false, violations);
    }
}
