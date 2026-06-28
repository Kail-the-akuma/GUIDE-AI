using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Guide.Validation;

public static class SemanticCompactor
{
    public static string CompactCode(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot();
        var rewriter = new CommentRemoverRewriter();
        var rewrittenRoot = rewriter.Visit(root);
        return rewrittenRoot.ToFullString();
    }

    private class CommentRemoverRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var cleanedToken = token;

            if (cleanedToken.HasLeadingTrivia)
            {
                var cleanedLeading = cleanedToken.LeadingTrivia.Where(t => !IsComment(t));
                cleanedToken = cleanedToken.WithLeadingTrivia(cleanedLeading);
            }

            if (cleanedToken.HasTrailingTrivia)
            {
                var cleanedTrailing = cleanedToken.TrailingTrivia.Where(t => !IsComment(t));
                cleanedToken = cleanedToken.WithTrailingTrivia(cleanedTrailing);
            }

            return base.VisitToken(cleanedToken);
        }

        private static bool IsComment(SyntaxTrivia trivia)
        {
            var kind = trivia.Kind();
            return kind == SyntaxKind.SingleLineCommentTrivia ||
                   kind == SyntaxKind.MultiLineCommentTrivia ||
                   kind == SyntaxKind.SingleLineDocumentationCommentTrivia ||
                   kind == SyntaxKind.MultiLineDocumentationCommentTrivia;
        }
    }
}
