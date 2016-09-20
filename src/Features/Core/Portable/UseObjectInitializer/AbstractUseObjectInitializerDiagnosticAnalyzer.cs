﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.UseObjectInitializer
{
    internal abstract class AbstractUseObjectInitializerDiagnosticAnalyzer<
        TSyntaxKind,
        TExpressionSyntax,
        TStatementSyntax,
        TObjectCreationExpressionSyntax,
        TMemberAccessExpressionSyntax,
        TVariableDeclarator>
        : DiagnosticAnalyzer, IBuiltInAnalyzer
        where TSyntaxKind : struct
        where TExpressionSyntax : SyntaxNode
        where TObjectCreationExpressionSyntax : TExpressionSyntax
        where TMemberAccessExpressionSyntax : TExpressionSyntax
        where TStatementSyntax : SyntaxNode
        where TVariableDeclarator : SyntaxNode
    {
        private static readonly string Id = IDEDiagnosticIds.UseObjectInitializerDiagnosticId;

        private static readonly DiagnosticDescriptor s_descriptor =
            CreateDescriptor(Id, DiagnosticSeverity.Hidden);

        private static readonly DiagnosticDescriptor s_unnecessaryWithSuggestionDescriptor =
            CreateDescriptor(Id, DiagnosticSeverity.Hidden, DiagnosticCustomTags.Unnecessary);

        private static readonly DiagnosticDescriptor s_unnecessaryWithoutSuggestionDescriptor =
            CreateDescriptor(Id + "WithoutSuggestion",
                DiagnosticSeverity.Hidden, DiagnosticCustomTags.Unnecessary);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(s_descriptor, s_unnecessaryWithoutSuggestionDescriptor, s_unnecessaryWithSuggestionDescriptor);

        public bool OpenFileOnly(Workspace workspace) => false;

        private static DiagnosticDescriptor CreateDescriptor(string id, DiagnosticSeverity severity, params string[] customTags)
            => new DiagnosticDescriptor(
                id,
                FeaturesResources.Object_initialization_can_be_simplified,
                FeaturesResources.Object_initialization_can_be_simplified,
                DiagnosticCategory.Style,
                severity,
                isEnabledByDefault: true,
                customTags: customTags);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeNode, GetObjectCreationSyntaxKind());
        }

        protected abstract TSyntaxKind GetObjectCreationSyntaxKind();

        private void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var optionSet = context.Options.GetOptionSet();
            var option = optionSet.GetOption(CodeStyleOptions.PreferObjectInitializer, LanguageNames.CSharp);
            if (!option.Value)
            {
                // not point in analyzing if the option is off.
                return;
            }

            var objectCreationExpression = (TObjectCreationExpressionSyntax)context.Node;

            var syntaxFacts = GetSyntaxFactsService();
            var analyzer = new Analyzer<TExpressionSyntax, TStatementSyntax, TObjectCreationExpressionSyntax, TMemberAccessExpressionSyntax, TVariableDeclarator>(
                syntaxFacts,
                objectCreationExpression);
            var matches = analyzer.Analyze();
            if (matches == null)
            {
                return;
            }

            var locations = ImmutableArray.Create(objectCreationExpression.GetLocation());

            var severity = option.Notification.Value;
            context.ReportDiagnostic(Diagnostic.Create(
                CreateDescriptor(Id, severity),
                objectCreationExpression.GetLocation(),
                additionalLocations: locations));

            var syntaxTree = objectCreationExpression.SyntaxTree;

            foreach (var match in matches)
            {
                var location1 = Location.Create(syntaxTree, TextSpan.FromBounds(
                    match.MemberAccessExpression.SpanStart, 
                    syntaxFacts.GetOperatorTokenOfMemberAccessExpression(match.MemberAccessExpression).Span.End));

                context.ReportDiagnostic(Diagnostic.Create(
                    s_unnecessaryWithSuggestionDescriptor, location1, additionalLocations: locations));

                if (match.Statement.Span.End > match.Initializer.FullSpan.End)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_unnecessaryWithoutSuggestionDescriptor,
                        Location.Create(syntaxTree, TextSpan.FromBounds(
                            match.Initializer.FullSpan.End,
                            match.Statement.Span.End)),
                        additionalLocations: locations));
                }
            }
        }

        protected abstract ISyntaxFactsService GetSyntaxFactsService();

        public DiagnosticAnalyzerCategory GetAnalyzerCategory()
        {
            return DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;
        }
    }

    internal struct Match<TStatementSyntax, TMemberAccessExpressionSyntax, TExpressionSyntax>
        where TExpressionSyntax : SyntaxNode
        where TMemberAccessExpressionSyntax : TExpressionSyntax
        where TStatementSyntax : SyntaxNode
    {
        public readonly TStatementSyntax Statement;
        public readonly TMemberAccessExpressionSyntax MemberAccessExpression;
        public readonly TExpressionSyntax Initializer;

        public Match(
            TStatementSyntax statement,
            TMemberAccessExpressionSyntax memberAccessExpression,
            TExpressionSyntax initializer)
        {
            Statement = statement;
            MemberAccessExpression = memberAccessExpression;
            Initializer = initializer;
        }
    }

    internal struct Analyzer<
            TExpressionSyntax,
            TStatementSyntax, 
            TObjectCreationExpressionSyntax, 
            TMemberAccessExpressionSyntax,
            TVariableDeclaratorSyntax>
        where TExpressionSyntax : SyntaxNode
        where TStatementSyntax : SyntaxNode
        where TObjectCreationExpressionSyntax : TExpressionSyntax
        where TMemberAccessExpressionSyntax : TExpressionSyntax
        where TVariableDeclaratorSyntax : SyntaxNode
    {
        private readonly ISyntaxFactsService _syntaxFacts;
        private readonly TObjectCreationExpressionSyntax _objectCreationExpression;

        private TStatementSyntax _containingStatement;
        private SyntaxNodeOrToken _valuePattern;

        public Analyzer(
            ISyntaxFactsService syntaxFacts, 
            TObjectCreationExpressionSyntax objectCreationExpression) : this()
        {
            _syntaxFacts = syntaxFacts;
            _objectCreationExpression = objectCreationExpression;
        }

        internal List<Match<TStatementSyntax, TMemberAccessExpressionSyntax, TExpressionSyntax>> Analyze()
        {
            if (_syntaxFacts.GetObjectCreationInitializer(_objectCreationExpression) != null)
            {
                // Don't bother if this already has an initializer.
                return null;
            }

            _containingStatement = _objectCreationExpression.FirstAncestorOrSelf<TStatementSyntax>();
            if (_containingStatement == null)
            {
                return null;
            }

            if (!TryInitializeVariableDeclarationCase() &&
                !TryInitializeAssignmentCase())
            {
                return null;
            }

            var containingBlock = _containingStatement.Parent;
            var foundStatement = false;

            List<Match<TStatementSyntax, TMemberAccessExpressionSyntax, TExpressionSyntax>> matches = null;
            HashSet<string> seenNames = null;

            foreach (var child in containingBlock.ChildNodesAndTokens())
            {
                if (!foundStatement)
                {
                    if (child == _containingStatement)
                    {
                        foundStatement = true;
                    }

                    continue;
                }

                if (child.IsToken)
                {
                    break;
                }

                var statement = child.AsNode() as TStatementSyntax;
                if (statement == null)
                {
                    break;
                }

                if (!_syntaxFacts.IsSimpleAssignmentStatement(statement))
                {
                    break;
                }

                SyntaxNode left, right;
                _syntaxFacts.GetPartsOfAssignmentStatement(statement, out left, out right);

                var rightExpression = right as TExpressionSyntax;
                var leftMemberAccess = left as TMemberAccessExpressionSyntax;
                if (!_syntaxFacts.IsSimpleMemberAccessExpression(leftMemberAccess))
                {
                    break;
                }

                var expression = (TExpressionSyntax)_syntaxFacts.GetExpressionOfMemberAccessExpression(leftMemberAccess);
                if (!ValuePatternMatches(expression))
                {
                    break;
                }

                // found a match!
                seenNames = seenNames ?? new HashSet<string>();
                matches = matches ?? new List<Match<TStatementSyntax, TMemberAccessExpressionSyntax, TExpressionSyntax>>();

                // If we see an assignment to the same property/field, we can't convert it
                // to an initializer.
                var name = _syntaxFacts.GetNameOfMemberAccessExpression(leftMemberAccess);
                var identifier = _syntaxFacts.GetIdentifierOfSimpleName(name);
                if (!seenNames.Add(identifier.ValueText))
                {
                    break;
                }

                matches.Add(new Match<TStatementSyntax, TMemberAccessExpressionSyntax, TExpressionSyntax>(
                    statement, leftMemberAccess, rightExpression));
            }

            return matches;
        }

        private bool ValuePatternMatches(TExpressionSyntax expression)
        {
            if (_valuePattern.IsToken)
            {
                return _syntaxFacts.IsIdentifierName(expression) &&
                    _syntaxFacts.AreEquivalent(
                        _valuePattern.AsToken(),
                        _syntaxFacts.GetIdentifierOfSimpleName(expression));
            }
            else
            {
                return _syntaxFacts.AreEquivalent(
                    _valuePattern.AsNode(), expression);
            }
        }

        private bool TryInitializeAssignmentCase()
        {
            if (!_syntaxFacts.IsSimpleAssignmentStatement(_containingStatement))
            {
                return false;
            }

            SyntaxNode left, right;
            _syntaxFacts.GetPartsOfAssignmentStatement(_containingStatement, out left, out right);
            if (right != _objectCreationExpression)
            {
                return false;
            }

            _valuePattern = left;
            return true;
        }

        private bool TryInitializeVariableDeclarationCase()
        {
            if (!_syntaxFacts.IsLocalDeclarationStatement(_containingStatement))
            {
                return false;
            }

            var containingDeclarator = _objectCreationExpression.FirstAncestorOrSelf<TVariableDeclaratorSyntax>();
            if (containingDeclarator == null)
            {
                return false;
            }

            if (!_syntaxFacts.IsDeclaratorOfLocalDeclarationStatement(containingDeclarator, _containingStatement))
            {
                return false;
            }

            _valuePattern = _syntaxFacts.GetIdentifierOfVariableDeclarator(containingDeclarator);
            return true;
        }
    }
}