using System.Threading.Tasks;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface ISemanticParser
{
    bool CanParse(string fileExtension);
    Task<DependencyGraph> BuildGraphAsync(string projectPath);
    Task<CodeContext> GetContextAsync(string filePath);
}
