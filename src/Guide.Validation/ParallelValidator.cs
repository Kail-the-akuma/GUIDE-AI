using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Validation.Plugins;

namespace Guide.Validation;

public class ParallelValidator
{
    private readonly IEnumerable<IValidator> _validators;

    public ParallelValidator(IEnumerable<IValidator> validators)
    {
        _validators = validators;
    }

    public ParallelValidator(ICommandLineRunner runner)
        : this(new IValidator[]
        {
            new BuildValidatorPlugin(runner),
            new FormatValidatorPlugin(runner),
            new ArchValidatorPlugin()
        })
    {
    }

    public async Task<ValidationResult> ValidateProjectAsync(string solutionPath, CancellationToken ct)
    {
        var results = new List<ValidationPluginResult>();
        foreach (var v in _validators)
        {
            try
            {
                var res = await v.ValidateAsync(solutionPath, ct);
                results.Add(res);
            }
            catch (System.Exception ex)
            {
                results.Add(new ValidationPluginResult(false, new[] { $"Plugin '{v.Name}' threw exception: {ex.Message}" }));
            }
        }

        var finalResult = new ValidationResult
        {
            IsSuccess = true,
            Errors = new List<string>()
        };

        foreach (var res in results)
        {
            if (!res.IsSuccess)
            {
                finalResult.IsSuccess = false;
                foreach (var violation in res.Violations)
                {
                    finalResult.Errors.Add(violation);
                }
            }
        }

        return finalResult;
    }
}
