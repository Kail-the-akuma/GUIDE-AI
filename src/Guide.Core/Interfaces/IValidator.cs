using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface IValidator
{
    string Name { get; }
    Task<ValidationPluginResult> ValidateAsync(string solutionPath, CancellationToken ct);
}
