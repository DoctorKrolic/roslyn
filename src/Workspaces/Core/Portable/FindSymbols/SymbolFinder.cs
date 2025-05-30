﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.LanguageService;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FindSymbols;

public static partial class SymbolFinder
{
    /// <summary>
    /// Obsolete.  Use <see cref="FindSymbolAtPositionAsync(SemanticModel, int, Workspace, CancellationToken)"/>.
    /// </summary>
    [Obsolete("Use FindSymbolAtPositionAsync instead.")]
    public static ISymbol FindSymbolAtPosition(
        SemanticModel semanticModel,
        int position,
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return FindSymbolAtPositionAsync(semanticModel, position, workspace, cancellationToken).WaitAndGetResult(cancellationToken);
    }

    /// <summary>
    /// Finds the symbol that is associated with a position in the text of a document.
    /// </summary>
    /// <param name="semanticModel">The semantic model associated with the document.</param>
    /// <param name="position">The character position within the document.</param>
    /// <param name="workspace">A workspace to provide context.</param>
    /// <param name="cancellationToken">A CancellationToken.</param>
    public static Task<ISymbol> FindSymbolAtPositionAsync(
        SemanticModel semanticModel,
        int position,
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        if (workspace is null)
            throw new ArgumentNullException(nameof(workspace));

        return FindSymbolAtPositionAsync(semanticModel, position, workspace.Services.SolutionServices, cancellationToken);
    }

    /// <summary>
    /// Finds the symbol that is associated with a position in the text of a document.
    /// </summary>
    /// <param name="semanticModel">The semantic model associated with the document.</param>
    /// <param name="position">The character position within the document.</param>
    /// <param name="cancellationToken">A CancellationToken.</param>
    internal static Task<ISymbol> FindSymbolAtPositionAsync(
        SemanticModel semanticModel,
        int position,
        SolutionServices services,
        CancellationToken cancellationToken)
    {
        return FindSymbolAtPositionAsync(semanticModel, position, services, includeType: false, cancellationToken);
    }

    /// <summary>
    /// Finds the symbol that is associated with a position in the text of a document.
    /// </summary>
    /// <param name="semanticModel">The semantic model associated with the document.</param>
    /// <param name="position">The character position within the document.</param>
    /// <param name="includeType">True to include the type of the symbol in the search.</param>
    /// <param name="cancellationToken">A CancellationToken.</param>
    internal static async Task<ISymbol> FindSymbolAtPositionAsync(
        SemanticModel semanticModel,
        int position,
        SolutionServices services,
        bool includeType,
        CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            throw new ArgumentNullException(nameof(semanticModel));
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var semanticInfo = await GetSemanticInfoAtPositionAsync(
            semanticModel, position, services, cancellationToken).ConfigureAwait(false);
        return semanticInfo.GetAnySymbol(includeType);
    }

    internal static async Task<TokenSemanticInfo> GetSemanticInfoAtPositionAsync(
        SemanticModel semanticModel,
        int position,
        SolutionServices services,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAtPositionAsync(semanticModel, position, services, cancellationToken).ConfigureAwait(false);

        if (token != default &&
            token.Span.IntersectsWith(position))
        {
            return semanticModel.GetSemanticInfo(token, services, cancellationToken);
        }

        return TokenSemanticInfo.Empty;
    }

    private static Task<SyntaxToken> GetTokenAtPositionAsync(
        SemanticModel semanticModel,
        int position,
        SolutionServices services,
        CancellationToken cancellationToken)
    {
        var syntaxTree = semanticModel.SyntaxTree;
        var syntaxFacts = services.GetRequiredLanguageService<ISyntaxFactsService>(semanticModel.Language);

        return syntaxTree.GetTouchingTokenAsync(
            semanticModel, position, syntaxFacts.IsBindableToken, cancellationToken, findInsideTrivia: true);
    }

    public static async Task<ISymbol> FindSymbolAtPositionAsync(
        Document document,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        return await FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Services, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the definition symbol declared in source code for a corresponding reference symbol. 
    /// Returns null if no such symbol can be found in the specified solution.
    /// </summary>
    public static Task<ISymbol?> FindSourceDefinitionAsync(ISymbol? symbol, Solution solution, CancellationToken cancellationToken = default)
        => SymbolFinderInternal.FindSourceDefinitionAsync(symbol, solution, cancellationToken).AsTask();

    /// <summary>
    /// Finds symbols in the given compilation that are similar to the specified symbol.
    /// 
    /// A found symbol may be the exact same symbol instance if the compilation is the origin of the specified symbol, 
    /// or it may be a different symbol instance if the compilation is not the originating compilation.
    /// 
    /// Multiple symbols may be returned if there are ambiguous matches.
    /// No symbols may be returned if the compilation does not define or have access to a similar symbol.
    /// </summary>
    /// <param name="symbol">The symbol to find corresponding matches for.</param>
    /// <param name="compilation">A compilation to find the corresponding symbol within. The compilation may or may not be the origin of the symbol.</param>
    /// <param name="cancellationToken">A CancellationToken.</param>
    public static IEnumerable<TSymbol> FindSimilarSymbols<TSymbol>(TSymbol symbol, Compilation compilation, CancellationToken cancellationToken = default)
        where TSymbol : ISymbol
    {
        if (symbol is null)
            throw new ArgumentNullException(nameof(symbol));

        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));

        var key = symbol.GetSymbolKey(cancellationToken);

        // We may be talking about different compilations.  So do not try to resolve locations.
        var result = new HashSet<TSymbol>();
        var resolution = key.Resolve(compilation, cancellationToken: cancellationToken);
        foreach (var current in resolution.OfType<TSymbol>())
        {
            result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// If <paramref name="symbol"/> is declared in a linked file, then this function returns all the symbols that
    /// are defined by the same symbol's syntax in the all projects that the linked file is referenced from.
    /// <para/>
    /// In order to be returned the other symbols must have the same <see cref="ISymbol.Name"/> and <see
    /// cref="ISymbol.Kind"/> as <paramref name="symbol"/>.  This matches general user intuition that these are all
    /// the 'same' symbol, and should be examined, regardless of the project context and <see cref="ISymbol"/> they
    /// originally started with.
    /// </summary>
    internal static async Task<ImmutableArray<ISymbol>> FindLinkedSymbolsAsync(
        ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        // Add the original symbol to the result set.
        var linkedSymbols = new HashSet<ISymbol> { symbol };

        foreach (var location in symbol.DeclaringSyntaxReferences)
        {
            var originalDocument = solution.GetDocument(location.SyntaxTree);

            // GetDocument will return null for locations in #load'ed trees. TODO:  Remove this check and add logic
            // to fetch the #load'ed tree's Document once https://github.com/dotnet/roslyn/issues/5260 is fixed.
            if (originalDocument == null)
            {
                Debug.Assert(solution.WorkspaceKind is WorkspaceKind.Interactive or WorkspaceKind.MiscellaneousFiles);
                continue;
            }

            var originalRoot = await location.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            foreach (var linkedDocumentId in originalDocument.GetLinkedDocumentIds())
            {
                var linkedDocument = solution.GetRequiredDocument(linkedDocumentId);

                // It's possible for us to have a solution snapshot where only part of a linked set of documents has
                // been updated.  As such, the other linked docs may have different contents/sizes than the original
                // doc we started with.  Skip those files as there's no sensible way to say that we have linked
                // symbols here when the contents are not the same.
                var linkedSyntaxRoot = await linkedDocument.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (originalRoot.FullSpan != linkedSyntaxRoot.FullSpan)
                    continue;

                var linkedNode = linkedSyntaxRoot.FindNode(location.Span, getInnermostNodeForTie: true);

                var semanticModel = await linkedDocument.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var linkedSymbol = semanticModel.GetDeclaredSymbol(linkedNode, cancellationToken);
                if (linkedSymbol is null)
                    continue;

                if (linkedSymbol.Kind != symbol.Kind)
                {
                    // With primary constructors, the declaring node of the primary constructor is the type
                    // declaration node itself.  So, see if we're in that situation, and try to find the
                    // corresponding primary constructor in the linked file.
                    if (linkedSymbol is INamedTypeSymbol linkedNamedType &&
                        symbol.IsConstructor())
                    {
                        linkedSymbol = linkedNamedType.Constructors.FirstOrDefault(
                            c => c.DeclaringSyntaxReferences.Any(r => linkedNode.Equals(r.GetSyntax(cancellationToken))));
                        if (linkedSymbol is null)
                            continue;
                    }
                }

                if (linkedSymbol.Name == symbol.Name)
                    linkedSymbols.Add(linkedSymbol);
            }
        }

        return [.. linkedSymbols];
    }
}
