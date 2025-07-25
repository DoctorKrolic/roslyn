﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Collections;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Host.Mef;

internal static class LayeredServiceUtilities
{
    /// <summary>
    /// Layers in the priority order. <see cref="ServiceLayer.Host"/> services override <see cref="ServiceLayer.Editor"/> services, etc.
    /// </summary>
    private static readonly ImmutableArray<string> s_orderedProductLayers = [ServiceLayer.Host, ServiceLayer.Editor, ServiceLayer.Desktop, ServiceLayer.Default];

    public static string GetAssemblyQualifiedServiceTypeName(Type type, string argName)
        => (type ?? throw new ArgumentNullException(argName)).AssemblyQualifiedName ?? throw new ArgumentException("Invalid service type", argName);

    public static (Lazy<TServiceInterface, TMetadata>? lazyService, bool usesFactory) PickService<TServiceInterface, TMetadata>(
        Type serviceType,
        string? workspaceKind,
        IEnumerable<(Lazy<TServiceInterface, TMetadata>? lazyService, bool usesFactory)> services)
        where TMetadata : ILayeredServiceMetadata
    {
        (Lazy<TServiceInterface, TMetadata>? lazyService, bool usesFactory) service;
        TemporaryArray<(Lazy<TServiceInterface, TMetadata>? lazyService, bool usesFactory)> servicesOfMatchingType = new();

        // PERF: Hoist AssemblyQualifiedName out of the loop to avoid repeated string allocations.
        var assemblyQualifiedName = serviceType.AssemblyQualifiedName;

        foreach (var entry in services)
        {
            if (entry.lazyService?.Metadata.ServiceType == assemblyQualifiedName)
            {
                servicesOfMatchingType.Add(entry);
            }
        }

#if WORKSPACE
        // test layer overrides all other layers and workspace kinds:
        service = servicesOfMatchingType.SingleOrDefault(static lz => lz.lazyService?.Metadata.Layer == ServiceLayer.Test);
        if (service.lazyService != null)
        {
            return service;
        }
#endif
        // If a service is exported for specific workspace kinds and the current workspace kind is among them, use it.
        if (workspaceKind != null)
        {
            service = servicesOfMatchingType.SingleOrDefault(static (lz, workspaceKind) => lz.lazyService?.Metadata.WorkspaceKinds.Contains(workspaceKind) ?? false, workspaceKind);
            if (service.lazyService != null)
            {
                return service;
            }

            // For backward compat check workspace kind specific service.
            // Workspace kind specific service should specify supported kinds in WorkspaceKinds.
            service = TryGetServiceByLayer(workspaceKind);
            if (service.lazyService != null)
            {
                return service;
            }
        }

        foreach (var layer in s_orderedProductLayers)
        {
            service = TryGetServiceByLayer(layer);
            if (service.lazyService != null)
            {
                return service;
            }
        }

        // no service.
        return default;

        (Lazy<TServiceInterface, TMetadata>? lazyService, bool usesFactory) TryGetServiceByLayer(string layer)
            => servicesOfMatchingType.SingleOrDefault(static (lz, layer) => lz.lazyService?.Metadata.WorkspaceKinds is [] && lz.lazyService.Metadata.Layer == layer, layer);
    }
}
