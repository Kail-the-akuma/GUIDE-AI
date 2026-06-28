using System.Collections.Generic;

namespace Guide.Core.Models;

public class ValidationResult
{
    public bool IsSuccess { get; set; }
    public List<string> Errors { get; set; } = new();
}

public record ValidationPluginResult(
    bool IsSuccess,
    IEnumerable<string> Violations
);
