﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.VisualStudio.LanguageServices.Implementation.Venus;
using VsTextSpan = Microsoft.VisualStudio.TextManager.Interop.TextSpan;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Extensions;

internal static class VsTextSpanExtensions
{
    public static async Task<VsTextSpan?> MapSpanFromSecondaryBufferToPrimaryBufferAsync(
        this VsTextSpan spanInSecondaryBuffer,
        IThreadingContext threadingContext,
        DocumentId documentId,
        CancellationToken cancellationToken)
    {
        var containedDocument = ContainedDocument.TryGetContainedDocument(documentId);
        if (containedDocument == null)
            return null;

        await threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        return MapSpanFromSecondaryBufferToPrimaryBuffer(
            spanInSecondaryBuffer, threadingContext, documentId, containedDocument);
    }

    /// <summary>
    /// Only call this from the UI thread.  See <see cref="MapSpanFromSecondaryBufferToPrimaryBufferAsync"/> for the async version.
    /// </summary>
    public static VsTextSpan? MapSpanFromSecondaryBufferToPrimaryBuffer(
        this VsTextSpan spanInSecondaryBuffer,
        IThreadingContext threadingContext,
        DocumentId documentId,
        ContainedDocument? containedDocument = null)
    {
        threadingContext.ThrowIfNotOnUIThread();

        containedDocument ??= ContainedDocument.TryGetContainedDocument(documentId);
        if (containedDocument == null)
            return null;
        var bufferCoordinator = containedDocument.BufferCoordinator;

        var primary = new VsTextSpan[1];
        var hresult = bufferCoordinator.MapSecondaryToPrimarySpan(spanInSecondaryBuffer, primary);

        var result = primary[0];

        return ErrorHandler.Succeeded(hresult) ? result : null;
    }
}
