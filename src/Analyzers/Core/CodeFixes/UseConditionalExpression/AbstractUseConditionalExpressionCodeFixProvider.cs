﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Formatting.Rules;
using Microsoft.CodeAnalysis.LanguageService;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.UseConditionalExpression;

using static UseConditionalExpressionCodeFixHelpers;
using static UseConditionalExpressionHelpers;

internal abstract class AbstractUseConditionalExpressionCodeFixProvider<
    TStatementSyntax,
    TIfStatementSyntax,
    TExpressionSyntax,
    TConditionalExpressionSyntax> : SyntaxEditorBasedCodeFixProvider
    where TStatementSyntax : SyntaxNode
    where TIfStatementSyntax : TStatementSyntax
    where TExpressionSyntax : SyntaxNode
    where TConditionalExpressionSyntax : TExpressionSyntax
{
    protected abstract ISyntaxFacts SyntaxFacts { get; }
    protected abstract AbstractFormattingRule GetMultiLineFormattingRule();

    protected abstract ISyntaxFormatting SyntaxFormatting { get; }

    protected abstract TExpressionSyntax ConvertToExpression(IThrowOperation throwOperation);
    protected abstract TStatementSyntax WrapWithBlockIfAppropriate(TIfStatementSyntax ifStatement, TStatementSyntax statement);

    protected abstract Task FixOneAsync(
        Document document, Diagnostic diagnostic,
        SyntaxEditor editor, SyntaxFormattingOptions formattingOptions, CancellationToken cancellationToken);

    protected override async Task FixAllAsync(
        Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor,
        CancellationToken cancellationToken)
    {
        var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var formattingOptions = await document.GetSyntaxFormattingOptionsAsync(cancellationToken).ConfigureAwait(false);

        // Defer to our callback to actually make the edits for each diagnostic. In turn, it
        // will return 'true' if it made a multi-line conditional expression. In that case,
        // we'll need to explicitly format this node so we can get our special multi-line
        // formatting in VB and C#.
        var nestedEditor = new SyntaxEditor(root, document.Project.Solution.Services);
        foreach (var diagnostic in diagnostics)
        {
            await FixOneAsync(
                document, diagnostic, nestedEditor, formattingOptions, cancellationToken).ConfigureAwait(false);
        }

        var changedRoot = nestedEditor.GetChangedRoot();

        // Get the language specific rule for formatting this construct and call into the
        // formatted to explicitly format things.  Note: all we will format is the new
        // conditional expression as that's the only node that has the appropriate
        // annotation on it.
        var rules = ImmutableArray.Create(GetMultiLineFormattingRule());
        var spansToFormat = FormattingExtensions.GetAnnotatedSpans(changedRoot, SpecializedFormattingAnnotation);

        var formattedRoot = SyntaxFormatting.GetFormattingResult(changedRoot, spansToFormat, formattingOptions, rules, cancellationToken).GetFormattedRoot(cancellationToken);
        changedRoot = formattedRoot;

        editor.ReplaceNode(root, changedRoot);
    }

    /// <summary>
    /// Helper to create a conditional expression out of two original IOperation values
    /// corresponding to the whenTrue and whenFalse parts. The helper will add the appropriate
    /// annotations and casts to ensure that the conditional expression preserves semantics, but
    /// is also properly simplified and formatted.
    /// </summary>
    protected async Task<TExpressionSyntax> CreateConditionalExpressionAsync(
        Document document, IConditionalOperation ifOperation,
        IOperation trueStatement, IOperation falseStatement,
        IOperation trueValue, IOperation falseValue,
        bool isRef, SyntaxFormattingOptions formattingOptions, CancellationToken cancellationToken)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        var generatorInternal = document.GetRequiredLanguageService<SyntaxGeneratorInternal>();
        var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        var condition = WrapIfStatementIfNecessary(ifOperation);
        if (CanSimplify(trueValue, falseValue, isRef, out var negate))
        {
            return negate
                ? (TExpressionSyntax)generator.Negate(generatorInternal, condition, semanticModel, cancellationToken).WithoutTrivia()
                : condition.WithoutTrivia();
        }

        var trueExpression = MakeRef(generatorInternal, isRef, CastValueIfNecessary(generator, trueStatement, trueValue));
        var falseExpression = MakeRef(generatorInternal, isRef, CastValueIfNecessary(generator, falseStatement, falseValue));
        trueExpression = WrapReturnExpressionIfNecessary(trueExpression, trueStatement);
        falseExpression = WrapReturnExpressionIfNecessary(falseExpression, falseStatement);

        var initialExpression = (TConditionalExpressionSyntax)generator.ConditionalExpression(
            condition.WithoutTrivia(),
            trueExpression,
            falseExpression);
        var (conditionalExpression, makeMultiLine) = UpdateConditionalExpression(ifOperation, initialExpression);

        conditionalExpression = conditionalExpression.WithAdditionalAnnotations(Simplifier.Annotation);
        makeMultiLine = makeMultiLine || await MakeMultiLineAsync(
            document, condition,
            trueValue.Syntax, falseValue.Syntax, formattingOptions, cancellationToken).ConfigureAwait(false);
        if (makeMultiLine)
        {
            conditionalExpression = conditionalExpression.WithAdditionalAnnotations(
                SpecializedFormattingAnnotation);
        }

        return MakeRef(generatorInternal, isRef, conditionalExpression);
    }

    protected virtual (TConditionalExpressionSyntax conditional, bool makeMultiLine) UpdateConditionalExpression(
        IConditionalOperation originalIfStatement, TConditionalExpressionSyntax conditionalExpression)
    {
        return (conditionalExpression, makeMultiLine: false);
    }

    protected virtual TExpressionSyntax WrapIfStatementIfNecessary(IConditionalOperation operation)
        => (TExpressionSyntax)operation.Condition.Syntax;

    protected virtual TExpressionSyntax WrapReturnExpressionIfNecessary(TExpressionSyntax returnExpression, IOperation returnOperation)
        => returnExpression;

    private static TExpressionSyntax MakeRef(SyntaxGeneratorInternal generator, bool isRef, TExpressionSyntax syntaxNode)
        => isRef ? (TExpressionSyntax)generator.RefExpression(syntaxNode) : syntaxNode;

    /// <summary>
    /// Checks if we should wrap the conditional expression over multiple lines.
    /// </summary>
    private static async Task<bool> MakeMultiLineAsync(
        Document document, SyntaxNode condition, SyntaxNode trueSyntax, SyntaxNode falseSyntax, SyntaxFormattingOptions formattingOptions,
        CancellationToken cancellationToken)
    {
        var sourceText = await document.GetValueTextAsync(cancellationToken).ConfigureAwait(false);
        if (!sourceText.AreOnSameLine(condition.GetFirstToken(), condition.GetLastToken()) ||
            !sourceText.AreOnSameLine(trueSyntax.GetFirstToken(), trueSyntax.GetLastToken()) ||
            !sourceText.AreOnSameLine(falseSyntax.GetFirstToken(), falseSyntax.GetLastToken()))
        {
            return true;
        }

        if (condition.Span.Length + trueSyntax.Span.Length + falseSyntax.Span.Length > formattingOptions.ConditionalExpressionWrappingLength)
        {
            return true;
        }

        return false;
    }

    private TExpressionSyntax CastValueIfNecessary(
        SyntaxGenerator generator, IOperation statement, IOperation value)
    {
        if (statement is IThrowOperation throwOperation)
            return ConvertToExpression(throwOperation);

        var suppressKind = this.SyntaxFacts.SyntaxKinds.SuppressNullableWarningExpression;
        var sourceSyntax = value.Syntax;
        while (sourceSyntax is { Parent.RawKind: var kind } && kind == suppressKind)
            sourceSyntax = sourceSyntax.Parent;

        sourceSyntax = sourceSyntax.WithoutTrivia();

        // If there was an implicit conversion generated by the compiler, then convert that to an
        // explicit conversion inside the condition.  This is needed as there is no type
        // inference in conditional expressions, so we need to ensure that the same conversions
        // that were occurring previously still occur after conversion. Note: the simplifier
        // will remove any of these casts that are unnecessary.
        if (value is IConversionOperation conversion &&
            conversion.IsImplicit &&
            conversion.Type != null &&
            conversion.Type.TypeKind != TypeKind.Error)
        {
            // Note we only add the cast if the source had no type (like the null literal), or a
            // non-error type itself.  We don't want to insert lots of casts in error code.
            if (conversion.Operand.Type == null || conversion.Operand.Type.TypeKind != TypeKind.Error)
            {
                return (TExpressionSyntax)generator.CastExpression(conversion.Type, sourceSyntax);
            }
        }

        return (TExpressionSyntax)sourceSyntax;
    }
}
