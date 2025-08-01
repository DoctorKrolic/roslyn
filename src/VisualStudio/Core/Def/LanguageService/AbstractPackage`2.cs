﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Packaging;
using Microsoft.CodeAnalysis.SymbolSearch;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices.Implementation.Utilities;
using Microsoft.VisualStudio.LanguageServices.Packaging;
using Microsoft.VisualStudio.LanguageServices.SymbolSearch;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.LanguageService;

internal abstract partial class AbstractPackage<TPackage, TLanguageService> : AbstractPackage
    where TPackage : AbstractPackage<TPackage, TLanguageService>
    where TLanguageService : AbstractLanguageService<TPackage, TLanguageService>
{
    private TLanguageService? _languageService;

    private PackageInstallerService? _packageInstallerService;
    private VisualStudioSymbolSearchService? _symbolSearchService;
    private IVsShell? _shell;

    protected AbstractPackage()
    {
    }

    protected override void RegisterInitializeAsyncWork(PackageLoadTasks packageInitializationTasks)
    {
        base.RegisterInitializeAsyncWork(packageInitializationTasks);

        packageInitializationTasks.AddTask(isMainThreadTask: true, task: PackageInitializationMainThreadAsync);
        packageInitializationTasks.AddTask(isMainThreadTask: false, task: PackageInitializationBackgroundThreadAsync);
    }

    private async Task PackageInitializationMainThreadAsync(PackageLoadTasks packageInitializationTasks, CancellationToken cancellationToken)
    {
        // This code uses various main thread only services, so it must run completely on the main thread
        // (thus the CA(true) usage throughout)
        Contract.ThrowIfFalse(JoinableTaskFactory.Context.IsOnMainThread);

        var shell = (IVsShell7?)await GetServiceAsync(typeof(SVsShell)).ConfigureAwait(true);
        Assumes.Present(shell);

        _shell = (IVsShell?)shell;
        Assumes.Present(_shell);

        foreach (var editorFactory in CreateEditorFactories())
        {
            RegisterEditorFactory(editorFactory);
        }

        // awaiting an IVsTask guarantees to return on the captured context
        await shell.LoadPackageAsync(Guids.RoslynPackageId);
    }

    private Task PackageInitializationBackgroundThreadAsync(PackageLoadTasks packageInitializationTasks, CancellationToken cancellationToken)
    {
        RegisterLanguageService(typeof(TLanguageService), async cancellationToken =>
        {
            // Ensure we're on the BG when creating the language service.
            await TaskScheduler.Default;

            // Create the language service, tell it to set itself up, then store it in a field
            // so we can notify it that it's time to clean up.
            _languageService = CreateLanguageService();
            await _languageService.SetupAsync(cancellationToken).ConfigureAwait(false);

            return _languageService.ComAggregate!;
        });

        // Misc workspace has to be up and running by the time our package is usable so that it can track running
        // doc events and appropriately map files to/from it and other relevant workspaces (like the
        // metadata-as-source workspace).
        var miscellaneousFilesWorkspace = this.ComponentModel.GetService<MiscellaneousFilesWorkspace>();

        RegisterMiscellaneousFilesWorkspaceInformation(miscellaneousFilesWorkspace);

        return Task.CompletedTask;
    }

    protected override void RegisterOnAfterPackageLoadedAsyncWork(PackageLoadTasks afterPackageLoadedTasks)
    {
        base.RegisterOnAfterPackageLoadedAsyncWork(afterPackageLoadedTasks);

        afterPackageLoadedTasks.AddTask(
            isMainThreadTask: true,
            task: (packageLoadedTasks, cancellationToken) =>
            {
                if (_shell != null && !_shell.IsInCommandLineMode())
                {
                    // not every derived package support object browser and for those languages
                    // this is a no op
                    RegisterObjectBrowserLibraryManager();
                }

                LoadComponentsInUIContextOnceSolutionFullyLoadedAsync(cancellationToken).Forget();

                return Task.CompletedTask;
            });
    }

    protected override async Task LoadComponentsAsync(CancellationToken cancellationToken)
    {
        // Do the MEF loads and initialization in the BG explicitly.
        await TaskScheduler.Default;

        // Ensure the nuget package services are initialized. This initialization pass will only run
        // once our package is loaded indirectly through a legacy COM service we proffer (like the legacy project systems
        // loading us) or through something like the IVsEditorFactory or a debugger service. Right now it's fine
        // we only load this there, because we only use these to provide code fixes. But we only show code fixes in
        // open files, and so you would have had to open a file, which loads the editor factory, which loads our package,
        // which will run this.
        //
        // This code will have to be moved elsewhere once any of that load path is changed such that the package
        // no longer loads if a file is opened.
        var workspace = ComponentModel.GetService<VisualStudioWorkspace>();
        _packageInstallerService = workspace.Services.GetService<IPackageInstallerService>() as PackageInstallerService;
        _symbolSearchService = workspace.Services.GetService<ISymbolSearchService>() as VisualStudioSymbolSearchService;

        _packageInstallerService?.RegisterLanguage(this.RoslynLanguageName);
        _symbolSearchService?.RegisterLanguage(this.RoslynLanguageName);
    }

    protected abstract void RegisterMiscellaneousFilesWorkspaceInformation(MiscellaneousFilesWorkspace miscellaneousFilesWorkspace);

    protected abstract IEnumerable<IVsEditorFactory> CreateEditorFactories();
    protected abstract TLanguageService CreateLanguageService();

    // When registering a language service, we need to take its ComAggregate wrapper.
    protected void RegisterLanguageService(Type t, Func<CancellationToken, Task<object>> serviceCreator)
        => AddService(t, async (container, cancellationToken, type) => await serviceCreator(cancellationToken).ConfigureAwait(true), promote: true);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Per VS core team, Package.Dispose is called on the UI thread.
            Contract.ThrowIfFalse(JoinableTaskFactory.Context.IsOnMainThread);
            if (_shell != null && !_shell.IsInCommandLineMode())
            {
                UnregisterObjectBrowserLibraryManager();
            }

            // If we've created the language service then tell it it's time to clean itself up now.
            if (_languageService != null)
            {
                _languageService.TearDown();
                _languageService = null;
            }
        }

        base.Dispose(disposing);
    }

    protected abstract string RoslynLanguageName { get; }

    protected virtual void RegisterObjectBrowserLibraryManager()
    {
        // it is virtual rather than abstract to not break other languages which derived from our
        // base package implementations
    }

    protected virtual void UnregisterObjectBrowserLibraryManager()
    {
        // it is virtual rather than abstract to not break other languages which derived from our
        // base package implementations
    }
}
