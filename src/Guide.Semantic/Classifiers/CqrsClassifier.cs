using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class CqrsClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.Feature;

        public bool Classify(SyntaxNode node, out double confidence, out string detectionMethod, out Dictionary<string, string> properties)
        {
            confidence = 0.0;
            detectionMethod = string.Empty;
            properties = new Dictionary<string, string>();

            if (node is not ClassDeclarationSyntax classDecl)
            {
                return false;
            }

            var name = classDecl.Identifier.Text;

            // Rule 1: Suffix "Command", "Query", "Handler"
            if (name.EndsWith("Command", StringComparison.Ordinal) ||
                name.EndsWith("Query", StringComparison.Ordinal) ||
                name.EndsWith("Handler", StringComparison.Ordinal))
            {
                confidence = 1.0;
                detectionMethod = nameof(CqrsClassifier);
                properties["Rule"] = "SuffixCqrs";
                return true;
            }

            // Rule 2: Inherits from a command/query/request interface
            if (ClassifierHelper.InheritsFromMatching(classDecl, baseTypeName =>
                baseTypeName.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
                baseTypeName.Contains("Query", StringComparison.OrdinalIgnoreCase) ||
                baseTypeName.Contains("Request", StringComparison.OrdinalIgnoreCase)))
            {
                confidence = 1.0;
                detectionMethod = nameof(CqrsClassifier);
                properties["Rule"] = "InheritsCqrsInterface";
                return true;
            }

            return false;
        }
    }
}
