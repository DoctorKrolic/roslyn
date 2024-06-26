﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CodeAnalysis.ExternalAccess.Razor
{
    /// <summary>
    /// A capabilities provider that should only be used from Razor tests
    /// </summary>
    internal interface IRazorTestCapabilitiesProvider
    {
        string GetServerCapabilitiesJson(string clientCapabilitiesJson);
    }
}
