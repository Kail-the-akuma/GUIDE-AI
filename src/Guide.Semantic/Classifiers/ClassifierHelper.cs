using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Guide.Semantic.Classifiers
{
    public static class ClassifierHelper
    {
        public static string GetSimpleTypeName(TypeSyntax? type)
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

        public static bool InheritsFrom(TypeDeclarationSyntax typeDecl, string baseName)
        {
            if (typeDecl.BaseList == null) return false;
            foreach (var baseType in typeDecl.BaseList.Types)
            {
                var simpleName = GetSimpleTypeName(baseType.Type);
                if (string.Equals(simpleName, baseName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool InheritsFromMatching(TypeDeclarationSyntax typeDecl, Func<string, bool> predicate)
        {
            if (typeDecl.BaseList == null) return false;
            foreach (var baseType in typeDecl.BaseList.Types)
            {
                var simpleName = GetSimpleTypeName(baseType.Type);
                if (predicate(simpleName))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasAttribute(TypeDeclarationSyntax typeDecl, string attributeName)
        {
            var nameWithAttr = attributeName.EndsWith("Attribute", StringComparison.Ordinal) ? attributeName : attributeName + "Attribute";
            var nameWithoutAttr = attributeName.EndsWith("Attribute", StringComparison.Ordinal) ? attributeName.Substring(0, attributeName.Length - 9) : attributeName;

            // Check type attributes
            foreach (var attrList in typeDecl.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var attrSimpleName = GetSimpleTypeName(attr.Name);
                    if (string.Equals(attrSimpleName, nameWithAttr, StringComparison.Ordinal) ||
                        string.Equals(attrSimpleName, nameWithoutAttr, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            // Check member attributes (methods/properties/etc.)
            foreach (var member in typeDecl.Members)
            {
                foreach (var attrList in member.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var attrSimpleName = GetSimpleTypeName(attr.Name);
                        if (string.Equals(attrSimpleName, nameWithAttr, StringComparison.Ordinal) ||
                            string.Equals(attrSimpleName, nameWithoutAttr, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
