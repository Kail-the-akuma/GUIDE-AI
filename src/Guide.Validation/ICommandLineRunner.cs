using System.Threading;
using System.Threading.Tasks;

namespace Guide.Validation;

public interface ICommandLineRunner
{
    Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default);
}
