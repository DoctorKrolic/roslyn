﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Roslyn.LanguageServer.Protocol;

using System.Text.Json.Serialization;

/// <summary>
/// Class which represents the result returned by the initialize request.
///
/// See the <see href="https://microsoft.github.io/language-server-protocol/specifications/specification-current/#initializeResult">Language Server Protocol specification</see> for additional information.
/// </summary>
internal sealed class InitializeResult
{
    /// <summary>
    /// Gets or sets the server capabilities.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [JsonRequired]
    public ServerCapabilities Capabilities
    {
        get;
        set;
    }

    /// <summary>
    /// Information about the server name and version
    /// </summary>
    /// <remarks>Since LSP 3.15</remarks>
    [JsonPropertyName("serverInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerInfo? ServerInfo { get; init; }
}
