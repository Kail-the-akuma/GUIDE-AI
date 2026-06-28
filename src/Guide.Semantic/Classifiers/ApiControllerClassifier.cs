using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic.Classifiers
{
    public class ApiControllerClassifier : INodeClassifier
    {
        public NodeType TargetType => NodeType.API;

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

            // Rule 1: Suffix "Controller"
            if (name.EndsWith("Controller", StringComparison.Ordinal))
            {
                confidence = 1.0;
                detectionMethod = nameof(ApiControllerClassifier);
                properties["Rule"] = "SuffixController";
                return true;
            }

            // Rule 2: Inherits from "ControllerBase" or "Controller"
            if (ClassifierHelper.InheritsFrom(classDecl, "ControllerBase") || ClassifierHelper.InheritsFrom(classDecl, "Controller"))
            {
                confidence = 1.0;
                detectionMethod = nameof(ApiControllerClassifier);
                properties["Rule"] = "InheritsControllerBase";
                return true;
            }

            // Rule 3: Contains route attributes like [Route], [HttpGet]
            if (ClassifierHelper.HasAttribute(classDecl, "Route") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpGet") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpPost") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpPut") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpDelete") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpPatch") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpHead") ||
                ClassifierHelper.HasAttribute(classDecl, "HttpOptions"))
            {
                confidence = 1.0;
                detectionMethod = nameof(ApiControllerClassifier);
                properties["Rule"] = "RouteAttributes";
                return true;
            }

            return false;
        }
    }
}
