using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class EntityClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.Entity;

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

            // Rule 1: Suffix "Entity"
            if (name.EndsWith("Entity", StringComparison.Ordinal))
            {
                confidence = 1.0;
                detectionMethod = nameof(EntityClassifier);
                properties["Rule"] = "SuffixEntity";
                return true;
            }

            // Rule 2: Inherits from "Entity" or "AggregateRoot"
            if (ClassifierHelper.InheritsFrom(classDecl, "Entity") || ClassifierHelper.InheritsFrom(classDecl, "AggregateRoot"))
            {
                confidence = 1.0;
                detectionMethod = nameof(EntityClassifier);
                properties["Rule"] = "InheritsEntityOrAggregateRoot";
                return true;
            }

            return false;
        }
    }
}
