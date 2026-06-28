using System;
using System.Threading;
using System.Threading.Tasks;

namespace Guide.Validation;

public class MockLlmService : ILlmService
{
    public Task<string> GeneratePatchAsync(string prompt, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            return Task.FromResult(string.Empty);
        }

        // Check for architectural drift/violation warning
        bool isArchViolation = prompt.Contains("Guide.Validation", StringComparison.OrdinalIgnoreCase) ||
                              prompt.Contains("Lmp.Presentation", StringComparison.OrdinalIgnoreCase) ||
                              prompt.Contains("architectural drift", StringComparison.OrdinalIgnoreCase) ||
                              prompt.Contains("violation", StringComparison.OrdinalIgnoreCase);

        // Check for compiler/syntax error
        bool isCompilerError = prompt.Contains("semicolon", StringComparison.OrdinalIgnoreCase) ||
                               prompt.Contains("CS1002", StringComparison.OrdinalIgnoreCase) ||
                               prompt.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                               prompt.Contains("compiler", StringComparison.OrdinalIgnoreCase) ||
                               prompt.Contains("mismatch", StringComparison.OrdinalIgnoreCase);

        var lines = prompt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (isArchViolation)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if ((trimmed.StartsWith("using ") && (trimmed.Contains("Guide.Validation") || trimmed.Contains("Lmp.Presentation"))) ||
                    (trimmed.Contains("new Guide.Validation") || trimmed.Contains("new Lmp.Presentation")))
                {
                    return Task.FromResult($"<<<<<<< SEARCH\n{line}\n=======\n>>>>>>> REPLACE");
                }
            }
        }

        if (isCompilerError)
        {
            // 1. Check type mismatch: int x = "hello"
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("int ") && trimmed.Contains("= \"") && (trimmed.EndsWith("\"") || trimmed.EndsWith("\";")))
                {
                    var corrected = line.Replace("int ", "string ");
                    if (!corrected.EndsWith(";")) corrected += ";";
                    return Task.FromResult($"<<<<<<< SEARCH\n{line}\n=======\n{corrected}\n>>>>>>> REPLACE");
                }
            }

            // 2. Check missing closing parenthesis: public void Foo(int x
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("(") && !trimmed.Contains(")") &&
                    (trimmed.Contains("void ") || trimmed.Contains("class ") || trimmed.Contains("public ") || trimmed.Contains("private ")))
                {
                    return Task.FromResult($"<<<<<<< SEARCH\n{line}\n=======\n{line})\n>>>>>>> REPLACE");
                }
            }

            // 3. Check missing semicolon: var x = 5
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains('=') &&
                    !trimmed.EndsWith(";") &&
                    !trimmed.EndsWith("{") &&
                    !trimmed.EndsWith("}") &&
                    !trimmed.EndsWith(")") &&
                    !trimmed.Contains("SEARCH") &&
                    !trimmed.Contains("REPLACE") &&
                    !trimmed.Contains("=======") &&
                    (trimmed.StartsWith("var ") || trimmed.StartsWith("int ") || trimmed.StartsWith("string ") || trimmed.StartsWith("double ") || trimmed.StartsWith("bool ")))
                {
                    return Task.FromResult($"<<<<<<< SEARCH\n{line}\n=======\n{line};\n>>>>>>> REPLACE");
                }
            }
        }

        return Task.FromResult(string.Empty);
    }
}
