using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Core.Helpers;

namespace Stryker.Core.Instrumentation
{
    internal class ExpressionToBodyEngine : BaseEngine<BaseMethodDeclarationSyntax>
    {
        public T ConvertToBody<T>(T method) where T: BaseMethodDeclarationSyntax
        {
            if (method.ExpressionBody == null || method.Body != null)
            {
                // can't convert
                return method;
            }

            StatementSyntax statementLine;
            switch (method)
            {
                case MethodDeclarationSyntax actualMethod when actualMethod.NeedsReturn():
                    statementLine = SyntaxFactory.ReturnStatement(method.ExpressionBody.Expression.WithLeadingTrivia(SyntaxFactory.Space));
                    break;

                case ConversionOperatorDeclarationSyntax _:
                case OperatorDeclarationSyntax _:
                    statementLine = SyntaxFactory.ReturnStatement(method.ExpressionBody.Expression.WithLeadingTrivia(SyntaxFactory.Space));
                    break;

                default:
                    statementLine = SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression);
                    break;
            }

            // do we need add return to the expression body?
            var statement = SyntaxFactory.Block(statementLine);

            BaseMethodDeclarationSyntax result = method switch
            {
                MethodDeclarationSyntax actualMethod => actualMethod.WithBody(statement).
                    WithExpressionBody(null).
                    WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)),
                OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.WithBody(statement).
                    WithExpressionBody(null).
                    WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)),
                ConversionOperatorDeclarationSyntax conversion => conversion.WithBody(statement).
                    WithExpressionBody(null).
                    WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)),
                DestructorDeclarationSyntax destructor => destructor.WithBody(statement).
                    WithExpressionBody(null).
                    WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)),
                ConstructorDeclarationSyntax constructor => constructor.WithBody(statement).
                    WithExpressionBody(null).
                    WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)),
                _ => method
            };

            return result.WithAdditionalAnnotations(Marker) as T;
        }

        protected override SyntaxNode Revert(BaseMethodDeclarationSyntax node)
        {
            // get expression
            var expression = SyntaxFactory.ArrowExpressionClause(node.Body?.Statements[0] switch
            {
                ReturnStatementSyntax returnStatement => returnStatement.Expression,
                ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
                _ => throw new InvalidOperationException($"Can't extract original expression from {node.Body}")
            });

            return node switch
            {
                MethodDeclarationSyntax actualMethod => actualMethod.WithExpressionBody(expression).
                    WithBody(null).
                    WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.WithExpressionBody(expression).
                    WithBody(null).
                    WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                ConversionOperatorDeclarationSyntax conversion => conversion.WithExpressionBody(expression).
                    WithBody(null).
                    WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                DestructorDeclarationSyntax destructor => destructor.WithExpressionBody(expression).
                    WithBody(null).
                    WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                ConstructorDeclarationSyntax constructor => constructor.WithExpressionBody(expression).
                    WithBody(null).
                    WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                _ => node
            };
        }

        public ExpressionToBodyEngine(string markerId) : base(markerId, "ExpressionToBodyEngine")
        {
        }
    }
}
