﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Serialization;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics;

internal sealed class TestAnalyzerReferenceByLanguage :
    AnalyzerReference,
    SerializerService.TestAccessor.IAnalyzerReferenceWithGuid
{
    private readonly IReadOnlyDictionary<string, ImmutableArray<DiagnosticAnalyzer>> _analyzersMap;

    public TestAnalyzerReferenceByLanguage(IReadOnlyDictionary<string, ImmutableArray<DiagnosticAnalyzer>> analyzersMap, string? fullPath = null)
    {
        _analyzersMap = analyzersMap;
        FullPath = fullPath;

        // Make up a checksum so we can calculate Project checksums containing these references
        var checksumArray = Guid.NewGuid().ToByteArray();
        Array.Resize(ref checksumArray, Checksum.HashSize);
        this.Checksum = Checksum.From(checksumArray);
    }

    public override string? FullPath { get; }
    public override string Display => nameof(TestAnalyzerReferenceByLanguage);
    public override object Id => Display;
    public Guid Guid { get; } = Guid.NewGuid();

    public Checksum Checksum;

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
        => _analyzersMap.SelectManyAsArray(kvp => kvp.Value);

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
    {
        if (_analyzersMap.TryGetValue(language, out var analyzers))
        {
            return analyzers;
        }

        return [];
    }

    public TestAnalyzerReferenceByLanguage WithAdditionalAnalyzers(string language, IEnumerable<DiagnosticAnalyzer> analyzers)
    {
        var newAnalyzersMap = ImmutableDictionary.CreateRange(
            _analyzersMap.Select(kvp => KeyValuePair.Create(
                kvp.Key, kvp.Key == language ? kvp.Value.AddRange(analyzers) : kvp.Value)));
        return new(newAnalyzersMap);
    }
}
