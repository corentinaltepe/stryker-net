using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stryker.Core.Instrumentation
{
    internal class DefaultOutParEngine: BaseEngine<MethodDeclarationSyntax>
    {
        public DefaultOutParEngine(string markerId) : base(markerId, "DefaultParEngine")
        {
        }

        protected override SyntaxNode Revert(MethodDeclarationSyntax node)
        {
            if (node.Body == null)
            {
                return node;
            }
            return node.WithBody(node.Body.Statements.Last() as BlockSyntax);
        }

        public MethodDeclarationSyntax InjectDefaultInitializerForOutParam(
            MethodDeclarationSyntax methodDeclarationSyntax)
        {
            var outArgs = methodDeclarationSyntax.ParameterList.Parameters.Where(arg =>
                arg.Modifiers.Any(m => m.ToString() == "out"));
            if (!outArgs.Any())
            {
                return methodDeclarationSyntax;
            }

            var statements = outArgs.Select(parameterSyntax => SyntaxFactory
                .ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(parameterSyntax.Identifier),
                    SyntaxFactory.DefaultExpression(parameterSyntax.Type.WithTrailingTrivia())))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));

            var newBody = methodDeclarationSyntax.Body.WithStatements(
                SyntaxFactory.List(statements.Append<StatementSyntax>(methodDeclarationSyntax.Body)));
            return methodDeclarationSyntax.ReplaceNode(methodDeclarationSyntax.Body, newBody)
                .WithAdditionalAnnotations(Marker);
        }
    }
}
