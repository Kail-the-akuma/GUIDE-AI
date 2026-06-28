using System.Threading;
using System.Threading.Tasks;

namespace Guide.Validation;

public interface ILlmService
{
    Task<string> GeneratePatchAsync(string prompt, CancellationToken ct);
}
