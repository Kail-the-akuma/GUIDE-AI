using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Guide.Validation;

public class CommandLineRunner : ICommandLineRunner
{
    public async Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(ct);

        var fullOutput = outputTask.Result + Environment.NewLine + errorTask.Result;
        return (process.ExitCode, fullOutput);
    }
}
