﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Analyzer.Utilities.FlowAnalysis.Analysis.TaintedDataAnalysis
{
    internal static class ZipSlipSanitizers
    {
        /// <summary>
        /// <see cref="SanitizerInfo"/>s for zip slip tainted data sanitizers.
        /// </summary>
        public static ImmutableHashSet<SanitizerInfo> SanitizerInfos { get; }

        static ZipSlipSanitizers()
        {
            var builder = PooledHashSet<SanitizerInfo>.GetInstance();

            builder.AddSanitizerInfo(
                WellKnownTypeNames.SystemIOPath,
                isInterface: false,
                isConstructorSanitizing: false,
                sanitizingMethods: new[] {
                    "GetFileName",
                });
            builder.AddSanitizerInfo(
                WellKnownTypeNames.SystemString,
                isInterface: false,
                isConstructorSanitizing: false,
                sanitizingMethods: new[] {
                    "Substring",
                },
                sanitizingInstanceMethods: new[] {
                    "StartsWith",
                });

            SanitizerInfos = builder.ToImmutableAndFree();
        }
    }
}
