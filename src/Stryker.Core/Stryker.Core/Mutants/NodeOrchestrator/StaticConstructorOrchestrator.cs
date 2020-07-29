﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Stryker.Core.Mutants.NodeOrchestrator
{
    internal class StaticConstructorOrchestrator : NodeSpecificOrchestrator<ConstructorDeclarationSyntax>
    {
        protected override bool CanHandleThis(ConstructorDeclarationSyntax t)
        {
            return t.Modifiers.Any(x => x.Kind() == SyntaxKind.StaticKeyword);
        }

        internal override SyntaxNode OrchestrateMutation(ConstructorDeclarationSyntax node, MutationContext context)
        {
            if (context.MustInjectCoverageLogic)
            {
                var trackedConstructor = node.TrackNodes((SyntaxNode) node.Body ?? node.ExpressionBody);
                if (node.ExpressionBody != null)
                {
                    var bodyBlock =
                        SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(node.ExpressionBody.Expression));
                    var markedBlock = MutantPlacer.PlaceStaticContextMarker((BlockSyntax) context.Mutate(bodyBlock));
                    trackedConstructor = trackedConstructor.Update(
                        trackedConstructor.AttributeLists,
                        trackedConstructor.Modifiers,
                        trackedConstructor.Identifier,
                        trackedConstructor.ParameterList,
                        trackedConstructor.Initializer,
                        markedBlock,
                        null,
                        SyntaxFactory.Token(SyntaxKind.None));
                }
                else if (node.Body != null)
                {
                    var markedBlock = MutantPlacer.PlaceStaticContextMarker((BlockSyntax) context.Mutate(node.Body));
                    trackedConstructor =
                        trackedConstructor.ReplaceNode(trackedConstructor.GetCurrentNode(node.Body), markedBlock);
                }
                return trackedConstructor;
            }

            return context.EnterStatic().MutateChildren(node);
        }
    }
}