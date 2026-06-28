using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface IKnowledgeExtractor
{
    // Extrai entidades ricas e as suas arestas de dependência a partir do código analisado
    ExtractedKnowledge Extract(IEnumerable<SyntaxTree> syntaxTrees);
}
