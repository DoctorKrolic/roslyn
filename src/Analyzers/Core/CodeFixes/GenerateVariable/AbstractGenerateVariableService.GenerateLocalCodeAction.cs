﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.GenerateMember.GenerateVariable;

internal abstract partial class AbstractGenerateVariableService<TService, TSimpleNameSyntax, TExpressionSyntax>
{
    private sealed class GenerateLocalCodeAction(TService service, Document document, State state) : CodeAction
    {
        private readonly TService _service = service;
        private readonly Document _document = document;
        private readonly State _state = state;

        public override string Title
        {
            get
            {
                var text = CodeFixesResources.Generate_local_0;

                return string.Format(
                    text,
                    _state.IdentifierToken.ValueText);
            }
        }

        protected override async Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
        {
            var newRoot = await GetNewRootAsync(cancellationToken).ConfigureAwait(false);
            var newDocument = _document.WithSyntaxRoot(newRoot);

            return newDocument;
        }

        private async Task<SyntaxNode> GetNewRootAsync(CancellationToken cancellationToken)
        {
            var semanticModel = await _document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (_service.TryConvertToLocalDeclaration(_state.LocalType, _state.IdentifierToken, semanticModel, cancellationToken, out var newRoot))
            {
                return newRoot;
            }

            var syntaxFactory = _document.GetRequiredLanguageService<SyntaxGenerator>();
            var initializer = _state.IsOnlyWrittenTo
                ? null
                : syntaxFactory.DefaultExpression(_state.LocalType);

            var type = _state.LocalType;
            var localStatement = syntaxFactory.LocalDeclarationStatement(type, _state.IdentifierToken.ValueText, initializer);
            localStatement = localStatement.WithAdditionalAnnotations(Formatter.Annotation);

            var root = _state.IdentifierToken.GetAncestors<SyntaxNode>().Last();
            var context = new CodeGenerationContext(beforeThisLocation: _state.IdentifierToken.GetLocation());
            var info = await _document.GetCodeGenerationInfoAsync(context, cancellationToken).ConfigureAwait(false);

            return info.Service.AddStatements(root, [localStatement], info, cancellationToken);
        }
    }
}
