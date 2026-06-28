using System.Collections.Generic;

namespace Guide.Core.Models;

public class CodeContext
{
    public string FilePath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public List<string> Imports { get; set; } = new();
    public List<string> Classes { get; set; } = new();
    public List<string> Signatures { get; set; } = new();
}
