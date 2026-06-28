using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Guide.Core.Interfaces;

public interface IProjectParser
{
    // Lê o projeto via MSBuild ou faz fallback recursivo seguro para AST walker
    Task<IEnumerable<SyntaxTree>> ParseProjectSourcesAsync(string solutionPath, CancellationToken ct);
}
