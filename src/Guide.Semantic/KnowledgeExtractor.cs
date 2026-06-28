using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic
{
    public class KnowledgeExtractor : IKnowledgeExtractor
    {
        private readonly IEnumerable<RichNode>? _existingNodes;
        private readonly IEnumerable<INodeClassifier> _classifiers;

        public KnowledgeExtractor(IEnumerable<RichNode>? existingNodes = null, IEnumerable<INodeClassifier>? classifiers = null)
        {
            _existingNodes = existingNodes;
            _classifiers = classifiers ?? GetDefaultClassifiers();
        }

        private static IEnumerable<INodeClassifier> GetDefaultClassifiers()
        {
            return new INodeClassifier[]
            {
                new Classifiers.ApiControllerClassifier(),
                new Classifiers.RepositoryClassifier(),
                new Classifiers.CqrsClassifier(),
                new Classifiers.EntityClassifier(),
                new Classifiers.PlaywrightTestClassifier(),
                new Classifiers.TestClassifier()
            };
        }

        public ExtractedKnowledge Extract(IEnumerable<SyntaxTree> syntaxTrees)
        {
            var nodes = new List<RichNode>();
            var typeDeclarations = new List<(TypeDeclarationSyntax Syntax, string Namespace, string Name, string NodeType)>();

            int idCounter = 1;
            var nodeMap = new Dictionary<string, RichNode>(StringComparer.Ordinal); // Key: Namespace.Name

            // Seed with existing nodes to enable cross-file resolution of unmodified types
            if (_existingNodes != null)
            {
                foreach (var node in _existingNodes)
                {
                    var key = string.IsNullOrEmpty(node.Namespace) ? node.Name : $"{node.Namespace}.{node.Name}";
                    if (!nodeMap.ContainsKey(key))
                    {
                        nodeMap[key] = node;
                        if (node.Id >= idCounter)
                        {
                            idCounter = node.Id.Value + 1;
                        }
                    }
                }
            }

            // 1. First pass: Find all type declarations in the input syntax trees and create RichNode entries
            foreach (var tree in syntaxTrees)
            {
                var root = tree.GetRoot();
                var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
                foreach (var type in types)
                {
                    var ns = GetNamespace(type);
                    var name = type.Identifier.Text;
                    var key = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    var nodeType = type switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax => "Record",
                        _ => "Type"
                    };

                    double confidence = 1.0;
                    string detectionMethod = "Manual";
                    Dictionary<string, string>? properties = null;

                    foreach (var classifier in _classifiers)
                    {
                        if (classifier.Classify(type, out var conf, out var detMethod, out var props))
                        {
                            nodeType = classifier.TargetType.ToString();
                            confidence = conf;
                            detectionMethod = detMethod;
                            properties = props;
                            break;
                        }
                    }

                    // Extract public signatures
                    var publicMembers = type.Members
                        .Where(m => IsPublic(m.Modifiers))
                        .Select(GetMemberSignature);
                    var publicSignatures = string.Join("\n", publicMembers);

                    var node = new RichNode(
                        idCounter++,
                        tree.FilePath,
                        ns,
                        name,
                        nodeType,
                        publicSignatures,
                        confidence,
                        detectionMethod,
                        properties
                    );

                    nodeMap[key] = node;
                    nodes.Add(node);
                    typeDeclarations.Add((type, ns, name, nodeType));
                }
            }

            // 2. Second pass: Extract relationships (Edges)
            var edges = new List<RichEdge>();
            var edgeSet = new HashSet<(int, int, string)>(); // Prevent duplicates

            void AddEdge(int fromId, int toId, string relationType)
            {
                if (fromId == toId) return; // No self loops
                if (edgeSet.Add((fromId, toId, relationType)))
                {
                    edges.Add(new RichEdge(fromId, toId, relationType));
                }
            }

            foreach (var (type, ns, name, nodeType) in typeDeclarations)
            {
                var key = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                var sourceNode = nodeMap[key];
                var sourceId = sourceNode.Id!.Value;

                // Base types (Inheritance/Implementation)
                var baseTypes = new HashSet<string>(StringComparer.Ordinal);
                if (type.BaseList != null)
                {
                    foreach (var baseType in type.BaseList.Types)
                    {
                        var baseTypeName = GetSimpleTypeName(baseType.Type);
                        if (!string.IsNullOrEmpty(baseTypeName))
                        {
                            baseTypes.Add(baseTypeName);
                            var targetNode = FindNodeByName(baseTypeName, ns, nodeMap);
                            if (targetNode != null)
                            {
                                var relType = nodeType == "Interface" ? "Inherits" :
                                              (targetNode.NodeType == "Interface" ? "Implements" : "Inherits");
                                AddEdge(sourceId, targetNode.Id!.Value, relType);
                            }
                        }
                    }
                }

                // Constructor injections
                var injectedTypes = new HashSet<string>(StringComparer.Ordinal);
                var ctors = type.Members.OfType<ConstructorDeclarationSyntax>();
                foreach (var ctor in ctors)
                {
                    if (ctor.ParameterList != null)
                    {
                        foreach (var param in ctor.ParameterList.Parameters)
                        {
                            var paramTypeName = GetSimpleTypeName(param.Type);
                            if (!string.IsNullOrEmpty(paramTypeName))
                            {
                                injectedTypes.Add(paramTypeName);
                                var targetNode = FindNodeByName(paramTypeName, ns, nodeMap);
                                if (targetNode != null)
                                {
                                    AddEdge(sourceId, targetNode.Id!.Value, "Injects");
                                }
                            }
                        }
                    }
                }

                // Other references inside the class body
                var walker = new ReferenceWalker();
                walker.Visit(type);
                foreach (var refTypeName in walker.ReferencedTypes)
                {
                    if (refTypeName == name) continue; // Skip self reference
                    if (baseTypes.Contains(refTypeName)) continue;
                    if (injectedTypes.Contains(refTypeName)) continue;

                    var targetNode = FindNodeByName(refTypeName, ns, nodeMap);
                    if (targetNode != null)
                    {
                        AddEdge(sourceId, targetNode.Id!.Value, "DependsOn");
                    }
                }
            }

            return new ExtractedKnowledge(nodes, edges);
        }

        private static string GetNamespace(SyntaxNode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (current is BaseNamespaceDeclarationSyntax ns)
                {
                    return ns.Name.ToString();
                }
                current = current.Parent;
            }
            return string.Empty;
        }

        private static bool IsPublic(SyntaxTokenList modifiers)
        {
            return modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
        }

        private static string GetMemberSignature(MemberDeclarationSyntax member)
        {
            if (member is MethodDeclarationSyntax method)
            {
                return $"{method.Modifiers} {method.ReturnType} {method.Identifier}{method.ParameterList}".Trim();
            }
            if (member is ConstructorDeclarationSyntax ctor)
            {
                return $"{ctor.Modifiers} {ctor.Identifier}{ctor.ParameterList}".Trim();
            }
            if (member is PropertyDeclarationSyntax prop)
            {
                string accessors = "get; set;";
                if (prop.AccessorList != null)
                {
                    accessors = string.Join(" ", prop.AccessorList.Accessors.Select(a => $"{a.Modifiers} {a.Keyword};".Trim()));
                }
                return $"{prop.Modifiers} {prop.Type} {prop.Identifier} {{ {accessors} }}".Trim();
            }
            if (member is FieldDeclarationSyntax field)
            {
                return $"{field.Modifiers} {field.Declaration}".Trim();
            }
            return member.ToString().Trim();
        }

        private static string GetSimpleTypeName(TypeSyntax? type)
        {
            if (type == null) return string.Empty;
            if (type is IdentifierNameSyntax id)
            {
                return id.Identifier.Text;
            }
            if (type is GenericNameSyntax gen)
            {
                return gen.Identifier.Text;
            }
            if (type is QualifiedNameSyntax qual)
            {
                return GetSimpleTypeName(qual.Right);
            }
            if (type is NullableTypeSyntax nullType)
            {
                return GetSimpleTypeName(nullType.ElementType);
            }
            return type.ToString();
        }

        private static RichNode? FindNodeByName(string name, string currentNamespace, Dictionary<string, RichNode> nodeMap)
        {
            // Try current namespace first
            var key = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
            if (nodeMap.TryGetValue(key, out var node))
            {
                return node;
            }

            // Fallback: search by Name only
            var matches = nodeMap.Values.Where(n => n.Name == name).ToList();
            if (matches.Count == 1)
            {
                return matches[0];
            }
            if (matches.Count > 1)
            {
                var closest = matches.FirstOrDefault(n => currentNamespace.StartsWith(n.Namespace));
                if (closest != null) return closest;
                return matches[0];
            }

            return null;
        }

        private class ReferenceWalker : CSharpSyntaxWalker
        {
            public HashSet<string> ReferencedTypes { get; } = new(StringComparer.Ordinal);

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                ReferencedTypes.Add(node.Identifier.Text);
                base.VisitIdentifierName(node);
            }

            public override void VisitGenericName(GenericNameSyntax node)
            {
                ReferencedTypes.Add(node.Identifier.Text);
                base.VisitGenericName(node);
            }
        }
    }
}
