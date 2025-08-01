﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.LanguageServer.Handler.Diagnostics;

internal sealed class NonLocalDocumentDiagnosticSource(
    TextDocument document, Func<DiagnosticAnalyzer, bool>? shouldIncludeAnalyzer)
    : AbstractDocumentDiagnosticSource<TextDocument>(document)
{
    private readonly Func<DiagnosticAnalyzer, bool>? _shouldIncludeAnalyzer = shouldIncludeAnalyzer;

    public override async Task<ImmutableArray<DiagnosticData>> GetDiagnosticsAsync(
        RequestContext context,
        CancellationToken cancellationToken)
    {
        // We call GetDiagnosticsForIdsAsync as we want to ensure we get the full set of non-local diagnostics for this
        // document including those reported as a compilation end diagnostic.  These are not included in document pull
        // (uses GetDiagnosticsForSpan) due to cost.
        var service = this.Solution.Services.GetRequiredService<IDiagnosticAnalyzerService>();
        var diagnostics = await service.GetDiagnosticsForIdsAsync(
            Document.Project, Document.Id, diagnosticIds: null, _shouldIncludeAnalyzer,
            includeLocalDocumentDiagnostics: false, includeNonLocalDocumentDiagnostics: true, cancellationToken).ConfigureAwait(false);

        // TODO(cyrusn): In the future we could consider reporting these, but with a flag on the diagnostic mentioning
        // that it is suppressed and should be hidden from the task list by default.
        return diagnostics.WhereAsArray(d => !d.IsSuppressed);
    }
}
