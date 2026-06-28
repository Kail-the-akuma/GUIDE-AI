using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetArchTest.Rules;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation.Plugins;

public class ArchValidatorPlugin : IValidator
{
    public string Name => "ArchValidator";

    private class ArchRule
    {
        public string FromAssembly { get; set; } = string.Empty;
        public List<string> ShouldNotDependOn { get; set; } = new();
    }

    private class ArchConfig
    {
        public List<ArchRule> Rules { get; set; } = new();
    }

    public Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct)
    {
        var violations = new List<string>();
        var assemblies = LoadSolutionAssemblies(solutionPath);

        // Try to load architecture-rules.json
        string? configDir = Path.GetDirectoryName(solutionPath);
        string configPath = string.Empty;
        if (!string.IsNullOrEmpty(configDir))
        {
            configPath = Path.Combine(configDir, "architecture-rules.json");
        }

        bool rulesLoadedFromFile = false;

        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            try
            {
                var jsonContent = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<ArchConfig>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config?.Rules != null && config.Rules.Count > 0)
                {
                    rulesLoadedFromFile = true;
                    foreach (var rule in config.Rules)
                    {
                        if (string.IsNullOrWhiteSpace(rule.FromAssembly)) continue;

                        var targetAssembly = assemblies.FirstOrDefault(a =>
                            string.Equals(a.GetName().Name, rule.FromAssembly, StringComparison.OrdinalIgnoreCase));

                        if (targetAssembly == null)
                        {
                            continue;
                        }

                        var ruleResult = Types.InAssembly(targetAssembly)
                            .ShouldNot()
                            .HaveDependencyOnAny(rule.ShouldNotDependOn.ToArray())
                            .GetResult();

                        if (!ruleResult.IsSuccessful && ruleResult.FailingTypes != null)
                        {
                            foreach (var type in ruleResult.FailingTypes)
                            {
                                violations.Add($"[FileRule] Type '{type.FullName}' in assembly '{rule.FromAssembly}' has forbidden dependency on one of: {string.Join(", ", rule.ShouldNotDependOn)}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                violations.Add($"Failed to parse architecture-rules.json: {ex.Message}");
            }
        }

        // Fallback: Enforce default rules in code if rules weren't successfully loaded from file
        if (!rulesLoadedFromFile)
        {
            EnforceDefaultRules(assemblies, violations);
        }

        var isSuccess = violations.Count == 0;
        return Task.FromResult(new ValidationPluginResult(isSuccess, violations));
    }

    private void EnforceDefaultRules(IEnumerable<Assembly> assemblies, List<string> violations)
    {
        // Rule 1: Guide.Semantic should not depend on Guide.Validation or Guide.Knowledge
        var semanticEngineAssembly = assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, "Guide.Semantic", StringComparison.OrdinalIgnoreCase));
        if (semanticEngineAssembly != null)
        {
            var result = Types.InAssembly(semanticEngineAssembly)
                .ShouldNot()
                .HaveDependencyOnAny("Guide.Validation", "Guide.Knowledge")
                .GetResult();

            if (!result.IsSuccessful && result.FailingTypes != null)
            {
                foreach (var type in result.FailingTypes)
                {
                    violations.Add($"[DefaultRule] Type '{type.FullName}' in Guide.Semantic depends on Guide.Validation or Guide.Knowledge.");
                }
            }
        }

        // Rule 2: Guide.Core should not depend on Guide.Semantic, Guide.Validation, or Guide.Knowledge
        var coreAssembly = assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, "Guide.Core", StringComparison.OrdinalIgnoreCase));
        if (coreAssembly != null)
        {
            var result = Types.InAssembly(coreAssembly)
                .ShouldNot()
                .HaveDependencyOnAny("Guide.Semantic", "Guide.Validation", "Guide.Knowledge")
                .GetResult();

            if (!result.IsSuccessful && result.FailingTypes != null)
            {
                foreach (var type in result.FailingTypes)
                {
                    violations.Add($"[DefaultRule] Type '{type.FullName}' in Guide.Core depends on Guide.Semantic, Guide.Validation, or Guide.Knowledge.");
                }
            }
        }

        // Rule 3: Guide.Validation should not depend on Guide.Knowledge
        var validatorsAssembly = assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, "Guide.Validation", StringComparison.OrdinalIgnoreCase));
        if (validatorsAssembly != null)
        {
            var result = Types.InAssembly(validatorsAssembly)
                .ShouldNot()
                .HaveDependencyOnAny("Guide.Knowledge")
                .GetResult();

            if (!result.IsSuccessful && result.FailingTypes != null)
            {
                foreach (var type in result.FailingTypes)
                {
                    violations.Add($"[DefaultRule] Type '{type.FullName}' in Guide.Validation depends on Guide.Knowledge.");
                }
            }
        }
    }

    private IEnumerable<Assembly> LoadSolutionAssemblies(string solutionPath)
    {
        var assemblies = new List<Assembly>();
        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (!string.IsNullOrEmpty(solutionDir) && Directory.Exists(solutionDir))
        {
            try
            {
                var dllFiles = Directory.GetFiles(solutionDir, "Guide.*.dll", SearchOption.AllDirectories);
                foreach (var file in dllFiles)
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    {
                        try
                        {
                            var assembly = Assembly.LoadFrom(file);
                            if (assembly != null && !assemblies.Any(a => a.FullName == assembly.FullName))
                            {
                                assemblies.Add(assembly);
                            }
                        }
                        catch
                        {
                            // Ignore assembly load failures
                        }
                    }
                }
            }
            catch
            {
                // Ignore search/IO errors
            }
        }

        // Fallback/add currently loaded assemblies if not already present
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.FullName != null && asm.FullName.StartsWith("Guide.", StringComparison.OrdinalIgnoreCase))
            {
                if (!assemblies.Any(a => a.FullName == asm.FullName))
                {
                    assemblies.Add(asm);
                }
            }
        }

        return assemblies;
    }
}
