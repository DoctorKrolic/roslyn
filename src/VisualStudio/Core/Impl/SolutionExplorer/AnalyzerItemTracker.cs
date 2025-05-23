﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer;

/// <summary>
/// This class listens to selection change events, and tracks which, if any, of our
/// <see cref="AnalyzerItem"/> or <see cref="AnalyzersFolderItem"/> is selected.
/// </summary>
[Export]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class AnalyzerItemsTracker(IThreadingContext threadingContext) : IVsSelectionEvents
{
    private readonly IThreadingContext _threadingContext = threadingContext;

    private IVsMonitorSelection? _vsMonitorSelection = null;
    private uint _selectionEventsCookie = 0;

    public event EventHandler? SelectedHierarchyItemChanged;

    public async Task RegisterAsync(IAsyncServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        _vsMonitorSelection ??= await serviceProvider.GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>(throwOnFailure: false, cancellationToken).ConfigureAwait(false);
        await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _vsMonitorSelection?.AdviseSelectionEvents(this, out _selectionEventsCookie);
    }

    public void Unregister()
    {
        _vsMonitorSelection?.UnadviseSelectionEvents(_selectionEventsCookie);
    }

    public IVsHierarchy? SelectedHierarchy { get; private set; }
    public uint SelectedItemId { get; private set; } = VSConstants.VSITEMID_NIL;
    public AnalyzersFolderItem? SelectedFolder { get; private set; }
    public ImmutableArray<AnalyzerItem> SelectedAnalyzerItems { get; private set; } = [];
    public ImmutableArray<DiagnosticItem> SelectedDiagnosticItems { get; private set; } = [];

    int IVsSelectionEvents.OnCmdUIContextChanged(uint dwCmdUICookie, int fActive)
    {
        return VSConstants.S_OK;
    }

    int IVsSelectionEvents.OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
    {
        return VSConstants.S_OK;
    }

    int IVsSelectionEvents.OnSelectionChanged(
        IVsHierarchy pHierOld,
        uint itemidOld,
        IVsMultiItemSelect pMISOld,
        ISelectionContainer pSCOld,
        IVsHierarchy pHierNew,
        uint itemidNew,
        IVsMultiItemSelect pMISNew,
        ISelectionContainer pSCNew)
    {
        var oldSelectedHierarchy = this.SelectedHierarchy;
        var oldSelectedItemId = this.SelectedItemId;

        this.SelectedHierarchy = pHierNew;
        this.SelectedItemId = itemidNew;

        var selectedObjects = GetSelectedObjects(pSCNew);

        this.SelectedAnalyzerItems = [.. selectedObjects
            .OfType<AnalyzerItem.BrowseObject>()
            .Select(b => b.AnalyzerItem)];

        this.SelectedFolder = selectedObjects
            .OfType<AnalyzersFolderItem.BrowseObject>()
            .Select(b => b.Folder)
            .FirstOrDefault();

        this.SelectedDiagnosticItems = [.. selectedObjects
            .OfType<DiagnosticItem.BrowseObject>()
            .Select(b => b.DiagnosticItem)];

        if (!object.ReferenceEquals(oldSelectedHierarchy, this.SelectedHierarchy) ||
            oldSelectedItemId != this.SelectedItemId)
        {
            this.SelectedHierarchyItemChanged?.Invoke(this, EventArgs.Empty);
        }

        return VSConstants.S_OK;
    }

    private static object[] GetSelectedObjects(ISelectionContainer? selectionContainer)
    {
        if (selectionContainer == null)
        {
            return [];
        }

        if (selectionContainer.CountObjects((uint)Constants.GETOBJS_SELECTED, out var selectedObjectCount) < 0 || selectedObjectCount == 0)
        {
            return [];
        }

        var selectedObjects = new object[selectedObjectCount];
        if (selectionContainer.GetObjects((uint)Constants.GETOBJS_SELECTED, selectedObjectCount, selectedObjects) < 0)
        {
            return [];
        }

        return selectedObjects;
    }
}
