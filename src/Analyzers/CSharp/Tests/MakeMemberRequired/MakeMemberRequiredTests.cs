﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.CodeFixes.MakeMemberRequired;
using Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Testing;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Analyzers.UnitTests.MakeMemberRequired;

using VerifyCS = CSharpCodeFixVerifier<
    EmptyDiagnosticAnalyzer,
    CSharpMakeMemberRequiredCodeFixProvider>;

[Trait(Traits.Feature, Traits.Features.CodeActionsMakeMemberRequired)]
public sealed class MakeMemberRequiredTests
{
    public static IEnumerable<object[]> MemberAccessibilityModifierCombinationsWhereShouldProvideFix()
    {
        yield return new[] { "public", "public", "public" };
        yield return new[] { "public", "private", "public" };
        yield return new[] { "public", "private", "internal" };
        yield return new[] { "public", "private", "protected internal" };
        yield return new[] { "public", "protected", "public" };
        yield return new[] { "public", "internal", "public" };
        yield return new[] { "public", "internal", "internal" };
        yield return new[] { "public", "internal", "protected internal" };
        yield return new[] { "public", "private protected", "public" };
        yield return new[] { "public", "private protected", "internal" };
        yield return new[] { "public", "private protected", "protected internal" };
        yield return new[] { "public", "protected internal", "public" };
        yield return new[] { "internal", "public", "public" };
        yield return new[] { "internal", "public", "internal" };
        yield return new[] { "internal", "public", "protected internal" };
        yield return new[] { "internal", "private", "public" };
        yield return new[] { "internal", "private", "internal" };
        yield return new[] { "internal", "private", "protected internal" };
        yield return new[] { "internal", "protected", "public" };
        yield return new[] { "internal", "protected", "internal" };
        yield return new[] { "internal", "protected", "protected internal" };
        yield return new[] { "internal", "internal", "public" };
        yield return new[] { "internal", "internal", "internal" };
        yield return new[] { "internal", "internal", "protected internal" };
        yield return new[] { "internal", "private protected", "public" };
        yield return new[] { "internal", "private protected", "internal" };
        yield return new[] { "internal", "private protected", "protected internal" };
        yield return new[] { "internal", "protected internal", "public" };
        yield return new[] { "internal", "protected internal", "internal" };
        yield return new[] { "internal", "protected internal", "protected internal" };
    }

    public static IEnumerable<object[]> MemberAccessibilityModifierCombinationsWhereShouldNotProvideFix()
    {
        yield return new[] { "public", "public", "private" };
        yield return new[] { "public", "public", "protected" };
        yield return new[] { "public", "public", "internal" };
        yield return new[] { "public", "public", "private protected" };
        yield return new[] { "public", "public", "protected internal" };
        yield return new[] { "public", "private", "private" };
        yield return new[] { "public", "private", "protected" };
        yield return new[] { "public", "private", "private protected" };
        yield return new[] { "public", "protected", "private" };
        yield return new[] { "public", "protected", "protected" };
        yield return new[] { "public", "protected", "internal" };
        yield return new[] { "public", "protected", "private protected" };
        yield return new[] { "public", "protected", "protected internal" };
        yield return new[] { "public", "internal", "private" };
        yield return new[] { "public", "internal", "protected" };
        yield return new[] { "public", "internal", "private protected" };
        yield return new[] { "public", "private protected", "private" };
        yield return new[] { "public", "private protected", "protected" };
        yield return new[] { "public", "private protected", "private protected" };
        yield return new[] { "public", "protected internal", "private" };
        yield return new[] { "public", "protected internal", "protected" };
        yield return new[] { "public", "protected internal", "internal" };
        yield return new[] { "public", "protected internal", "private protected" };
        yield return new[] { "public", "protected internal", "protected internal" };
        yield return new[] { "internal", "public", "private" };
        yield return new[] { "internal", "public", "protected" };
        yield return new[] { "internal", "public", "private protected" };
        yield return new[] { "internal", "private", "private" };
        yield return new[] { "internal", "private", "protected" };
        yield return new[] { "internal", "private", "private protected" };
        yield return new[] { "internal", "protected", "private" };
        yield return new[] { "internal", "protected", "protected" };
        yield return new[] { "internal", "protected", "private protected" };
        yield return new[] { "internal", "internal", "private" };
        yield return new[] { "internal", "internal", "protected" };
        yield return new[] { "internal", "internal", "private protected" };
        yield return new[] { "internal", "private protected", "private" };
        yield return new[] { "internal", "private protected", "protected" };
        yield return new[] { "internal", "private protected", "private protected" };
        yield return new[] { "internal", "protected internal", "private" };
        yield return new[] { "internal", "protected internal", "protected" };
        yield return new[] { "internal", "protected internal", "private protected" };
    }

    public static IEnumerable<object[]> AccessorAccessibilityModifierCombinationsWhereShouldProvideFix()
    {
        yield return new[] { "public", "" };
        yield return new[] { "internal", "" };
        yield return new[] { "internal", "internal" };
        yield return new[] { "internal", "protected internal" };
    }

    public static IEnumerable<object[]> AccessorAccessibilityModifierCombinationsWhereShouldNotProvideFix()
    {
        yield return new[] { "public", "internal" };
        yield return new[] { "public", "protected" };
        yield return new[] { "public", "private" };
        yield return new[] { "public", "private protected" };
        yield return new[] { "public", "protected internal" };
        yield return new[] { "internal", "protected" };
        yield return new[] { "internal", "private" };
        yield return new[] { "internal", "private protected" };
    }

    [Fact, WorkItem("https://github.com/dotnet/roslyn/issues/68478")]
    public Task SimpleSetPropertyMissingRequiredAttribute()
        => new VerifyCS.Test
        {
            TestCode = """
            #nullable enable
            class MyClass
            {
                public string {|CS8618:MyProperty|} { get; set; }
            }
            """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net60,
        }.RunAsync();

    [Fact]
    public Task SimpleSetProperty()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; set; }
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string MyProperty { get; set; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task SimpleInitProperty()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; init; }
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string MyProperty { get; init; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task NotOnGetOnlyProperty()
        => new VerifyCS.Test
        {
            TestCode = """
            #nullable enable
            
            class MyClass
            {
                public string {|CS8618:MyProperty|} { get; }
            }
            """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(MemberAccessibilityModifierCombinationsWhereShouldProvideFix))]
    public Task TestEffectivePropertyAccessibilityWhereShouldProvideFix(string outerClassAccessibility, string containingTypeAccessibility, string propertyAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{propertyAccessibility}} string {|CS8618:MyProperty|} { get; set; }
                    }
                }
                """,
            FixedCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{propertyAccessibility}} required string MyProperty { get; set; }
                    }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(MemberAccessibilityModifierCombinationsWhereShouldNotProvideFix))]
    public Task TestEffectivePropertyAccessibilityWhereShouldNotProvideFix(string outerClassAccessibility, string containingTypeAccessibility, string propertyAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{propertyAccessibility}} string {|CS8618:MyProperty|} { get; set; }
                    }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(AccessorAccessibilityModifierCombinationsWhereShouldProvideFix))]
    public Task TestSetAccessorAccessibilityWhereShouldProvideFix(string containingTypeAccessibility, string setAccessorAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; {{setAccessorAccessibility}} set; }
                }
                """,
            FixedCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public required string MyProperty { get; {{setAccessorAccessibility}} set; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(AccessorAccessibilityModifierCombinationsWhereShouldNotProvideFix))]
    public Task TestSetAccessorAccessibilityWhereShouldNotProvideFix(string containingTypeAccessibility, string setAccessorAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; {{setAccessorAccessibility}} set; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(AccessorAccessibilityModifierCombinationsWhereShouldProvideFix))]
    public Task TestInitAccessorAccessibilityWhereShouldProvideFix(string containingTypeAccessibility, string setAccessorAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; {{setAccessorAccessibility}} init; }
                }
                """,
            FixedCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public required string MyProperty { get; {{setAccessorAccessibility}} init; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(AccessorAccessibilityModifierCombinationsWhereShouldNotProvideFix))]
    public Task TestInitAccessorAccessibilityWhereShouldNotProvideFix(string containingTypeAccessibility, string setAccessorAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{containingTypeAccessibility}} class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; {{setAccessorAccessibility}} init; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task SimpleField()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
            
                class MyClass
                {
                    public string {|CS8618:_myField|};
                }
                """,
            FixedCode = """
                #nullable enable
            
                class MyClass
                {
                    public required string _myField;
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(MemberAccessibilityModifierCombinationsWhereShouldProvideFix))]
    public Task TestEffectiveFieldAccessibilityWhereShouldProvideFix(string outerClassAccessibility, string containingTypeAccessibility, string fieldAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{fieldAccessibility}} string {|CS8618:_myField|};
                    }
                }
                """,
            FixedCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{fieldAccessibility}} required string _myField;
                    }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Theory]
    [MemberData(nameof(MemberAccessibilityModifierCombinationsWhereShouldNotProvideFix))]
    public Task TestEffectiveFieldAccessibilityWhereShouldNotProvideFix(string outerClassAccessibility, string containingTypeAccessibility, string fieldAccessibility)
        => new VerifyCS.Test
        {
            TestCode = $$"""
                #nullable enable
            
                {{outerClassAccessibility}} class C
                {
                    {{containingTypeAccessibility}} class MyClass
                    {
                        {{fieldAccessibility}} string {|CS8618:_myField|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task NotForLowerVersionOfCSharp()
        => new VerifyCS.Test
        {
            TestCode = """
            #nullable enable
            
            class MyClass
            {
                public string {|CS8618:MyProperty|} { get; set; }
            }
            """,
            LanguageVersion = LanguageVersion.CSharp10,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task NotOnConstructorDeclaration()
        => new VerifyCS.Test
        {
            TestCode = """
            #nullable enable
            
            class MyClass
            {
                public string MyProperty { get; set; }
                public {|CS8618:MyClass|}() { }
            }
            """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task NotOnEventDeclaration()
        => new VerifyCS.Test
        {
            TestCode = """
            #nullable enable
            
            class MyClass
            {
                public event System.EventHandler {|CS8618:MyEvent|};
            }
            """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task FixAll1()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:MyProperty|} { get; set; }
                    public string {|CS8618:MyProperty1|} { get; set; }
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string MyProperty { get; set; }
                    public required string MyProperty1 { get; set; }
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task FixAll2()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:_myField|};
                    public string {|CS8618:_myField1|};
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string _myField;
                    public required string _myField1;
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task FixAll3()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:_myField|}, {|CS8618:_myField1|};
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string _myField, _myField1;
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task FixAll4()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:_myField|}, {|CS8618:_myField1|}, {|CS8618:_myField2|};
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string _myField, _myField1, _myField2;
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task FixAll5()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:_myField|}, {|CS8618:_myField1|};
                    public string {|CS8618:_myField2|}, {|CS8618:_myField3|};
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string _myField, _myField1;
                    public required string _myField2, _myField3;
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();

    [Fact]
    public Task TwoFieldDeclaratorsWithOneIssue()
        => new VerifyCS.Test
        {
            TestCode = """
                #nullable enable
                class MyClass
                {
                    public string {|CS8618:_myField|}, _myField1 = "";
                }
                """,
            FixedCode = """
                #nullable enable
                class MyClass
                {
                    public required string _myField, _myField1 = "";
                }
                """,
            LanguageVersion = LanguageVersion.CSharp11,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net70
        }.RunAsync();
}
