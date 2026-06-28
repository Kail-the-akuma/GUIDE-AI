using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class PlaywrightTestClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.PlaywrightTest;

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

            // Rule 1: Suffix
            if (name.EndsWith("UiTests", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("E2eTests", StringComparison.OrdinalIgnoreCase))
            {
                confidence = 1.0;
                detectionMethod = nameof(PlaywrightTestClassifier);
                properties["Rule"] = "SuffixUiOrE2eTests";
                return true;
            }

            // Rule 2: Inheritance
            if (classDecl.BaseList != null)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    var simpleName = ClassifierHelper.GetSimpleTypeName(baseType.Type);
                    if (string.Equals(simpleName, "PageTest", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(simpleName, "PlaywrightTest", StringComparison.OrdinalIgnoreCase))
                    {
                        confidence = 1.0;
                        detectionMethod = nameof(PlaywrightTestClassifier);
                        properties["Rule"] = "InheritsPageTest";
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
