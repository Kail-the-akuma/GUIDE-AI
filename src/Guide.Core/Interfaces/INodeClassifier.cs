using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface INodeClassifier
{
    NodeType TargetType { get; }
    bool Classify(SyntaxNode node, out double confidence, out string detectionMethod, out Dictionary<string, string> properties);
}
