using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class TestClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.UnitTest;

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

            // Rule 1: Suffix "Tests"
            if (name.EndsWith("Tests", StringComparison.Ordinal))
            {
                confidence = 1.0;
                detectionMethod = nameof(TestClassifier);
                properties["Rule"] = "SuffixTests";
                return true;
            }

            // Rule 2: Has test attributes
            if (ClassifierHelper.HasAttribute(classDecl, "Fact") ||
                ClassifierHelper.HasAttribute(classDecl, "Theory") ||
                ClassifierHelper.HasAttribute(classDecl, "Test") ||
                ClassifierHelper.HasAttribute(classDecl, "TestMethod"))
            {
                confidence = 1.0;
                detectionMethod = nameof(TestClassifier);
                properties["Rule"] = "TestAttributes";
                return true;
            }

            return false;
        }
    }
}
