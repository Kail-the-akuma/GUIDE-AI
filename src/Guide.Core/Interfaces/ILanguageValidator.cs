using System.Threading.Tasks;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface ILanguageValidator
{
    string Language { get; }
    Task<ValidationResult> ValidateAsync(string projectPath, bool runTests);
}
