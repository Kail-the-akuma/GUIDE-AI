using System.Collections.Generic;
using System.Threading.Tasks;

namespace Guide.Core.Interfaces;

public interface IContextEngine
{
    // Suporta Contexto Incremental calculando o delta contra o contexto anterior
    Task<ContextResult> BuildContextAsync(string anchorEntity, int depth, ContextResult? previousContext = null);
}

public record ContextResult(
    IEnumerable<string> TargetFiles,
    IEnumerable<string> SecondarySignatures,
    IEnumerable<ContextExplanation> Explanations
);

public record ContextExplanation(
    string TargetName,
    string Reason, // Ex: "Consumido por UserService via injeção IRepository"
    string RelationshipChain // Ex: "LoginController -> UserService -> UserRepository"
);
