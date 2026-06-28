using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class RepositoryClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.Repository;

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

            // Rule 1: Suffix "Repository"
            if (name.EndsWith("Repository", StringComparison.Ordinal))
            {
                confidence = 1.0;
                detectionMethod = nameof(RepositoryClassifier);
                properties["Rule"] = "SuffixRepository";
                return true;
            }

            // Rule 2: Inherits from "IRepository"
            if (ClassifierHelper.InheritsFrom(classDecl, "IRepository"))
            {
                confidence = 1.0;
                detectionMethod = nameof(RepositoryClassifier);
                properties["Rule"] = "InheritsIRepository";
                return true;
            }

            return false;
        }
    }
}
