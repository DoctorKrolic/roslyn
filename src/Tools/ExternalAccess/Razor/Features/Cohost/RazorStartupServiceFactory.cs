﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageServer;
using Microsoft.CodeAnalysis.LanguageServer.Handler;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.VisualStudio.Threading;
using Roslyn.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost;

[ExportCSharpVisualBasicLspServiceFactory(typeof(RazorStartupService), WellKnownLspServerKinds.Any), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class RazorStartupServiceFactory(
    [Import(AllowDefault = true)] IUIContextActivationService? uIContextActivationService,
    [Import(AllowDefault = true)] Lazy<ICohostStartupService>? cohostStartupService,
    [Import(AllowDefault = true)] AbstractRazorCohostLifecycleService? razorCohostLifecycleService) : ILspServiceFactory
{
    public ILspService CreateILspService(LspServices lspServices, WellKnownLspServerKinds serverKind)
    {
        return new RazorStartupService(uIContextActivationService, cohostStartupService, razorCohostLifecycleService);
    }

    private class RazorStartupService(
        IUIContextActivationService? uIContextActivationService,
#pragma warning disable CS0618 // Type or member is obsolete
        Lazy<ICohostStartupService>? cohostStartupService,
#pragma warning restore CS0618 // Type or member is obsolete
        AbstractRazorCohostLifecycleService? razorCohostLifecycleService) : ILspService, IOnInitialized, IDisposable
    {
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private IDisposable? _activation;

        public void Dispose()
        {
            razorCohostLifecycleService?.Dispose();

            _activation?.Dispose();
            _activation = null;
            _disposalTokenSource.Cancel();
        }

        public async Task OnInitializedAsync(ClientCapabilities clientCapabilities, RequestContext context, CancellationToken cancellationToken)
        {
            if (context.ServerKind is not (WellKnownLspServerKinds.AlwaysActiveVSLspServer or WellKnownLspServerKinds.CSharpVisualBasicLspServer))
            {
                // We have to register this class for Any server, but only want to run in the C# server in VS or VS Code
                return;
            }

            if (cohostStartupService is null && razorCohostLifecycleService is null)
            {
                return;
            }

            if (razorCohostLifecycleService is not null)
            {
                // If we have a cohost lifecycle service, fire pre-initialization, which happens when the LSP server starts up, but before
                // the UIContext is activated.
                await razorCohostLifecycleService.LspServerIntializedAsync(cancellationToken).ConfigureAwait(false);
            }

            if (uIContextActivationService is null)
            {
                // Outside of VS, we want to initialize immediately.. I think?
                InitializeRazor();
            }
            else
            {
                _activation = uIContextActivationService.ExecuteWhenActivated(Constants.RazorCohostingUIContext, InitializeRazor);
            }

            return;

            void InitializeRazor()
            {
                this.InitializeRazorAsync(clientCapabilities, context, _disposalTokenSource.Token).ReportNonFatalErrorAsync();
            }
        }

        private async Task InitializeRazorAsync(ClientCapabilities clientCapabilities, RequestContext context, CancellationToken cancellationToken)
        {
            // The LSP server will dispose us when the server exits, but VS could decide to activate us later.
            // If a new instance of the server is created, a new instance of this class will be created and the
            // UIContext will already be active, so this method will be immediately called on the new instance.
            if (cancellationToken.IsCancellationRequested) return;

            await TaskScheduler.Default.SwitchTo(alwaysYield: true);

            using var languageScope = context.Logger.CreateLanguageContext(Constants.RazorLanguageName);

            var requestContext = new RazorCohostRequestContext(context);

            if (razorCohostLifecycleService is not null)
            {
                // If we have a cohost lifecycle service, fire post-initialization, which happens when the UIContext is activated.
                await razorCohostLifecycleService.RazorActivatedAsync(clientCapabilities, requestContext, cancellationToken).ConfigureAwait(false);
            }

            if (cohostStartupService is not null)
            {
                // We use a string to pass capabilities to/from Razor to avoid version issues with the Protocol DLL
                var serializedClientCapabilities = JsonSerializer.Serialize(clientCapabilities, ProtocolConversions.LspJsonSerializerOptions);
                await cohostStartupService.Value.StartupAsync(serializedClientCapabilities, requestContext, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
