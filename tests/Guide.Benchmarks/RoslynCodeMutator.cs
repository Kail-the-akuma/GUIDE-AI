using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Guide.Benchmarks;

public class RoslynCodeMutator : CSharpSyntaxRewriter
{
    private readonly string _mutationType;

    public RoslynCodeMutator(string mutationType)
    {
        _mutationType = mutationType;
    }

    public string Mutate(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = syntaxTree.GetRoot();
        var mutatedRoot = Visit(root);
        return mutatedRoot.ToFullString();
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_mutationType == "SyntaxError" && (node.Identifier.Text == "RecordEntryAsync" || node.Identifier.Text == "Deposit" || node.Identifier.Text == "Handle"))
        {
            // Create a statement missing a semicolon
            var statement = SyntaxFactory.ParseStatement("Console.WriteLine(\"Missing semicolon\")");
            var newBody = SyntaxFactory.Block(statement);
            var bodyString = newBody.ToFullString().Replace(";", ""); // Remove the semicolon
            return node.WithBody((BlockSyntax)SyntaxFactory.ParseStatement(bodyString));
        }

        if (_mutationType == "ArchitectureDrift" && (node.Identifier.Text == "RecordEntryAsync" || node.Identifier.Text == "Deposit" || node.Identifier.Text == "AddAsync" || node.Identifier.Text == "GetByIdAsync"))
        {
            // Inject call to forbidden namespace (Lmp.Presentation.Api)
            var badStatement = SyntaxFactory.ParseStatement("var bad = typeof(Lmp.Presentation.Api.ShipmentController);");
            if (node.Body != null)
            {
                var bodyStatements = node.Body.Statements.Insert(0, badStatement);
                return node.WithBody(SyntaxFactory.Block(bodyStatements));
            }
        }

        return base.VisitMethodDeclaration(node);
    }
}
