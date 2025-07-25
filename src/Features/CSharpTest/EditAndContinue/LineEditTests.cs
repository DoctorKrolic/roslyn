﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable
#pragma warning disable IDE0055 // Collection expression formatting

using System;
using System.Linq;
using Microsoft.CodeAnalysis.Contracts.EditAndContinue;
using Microsoft.CodeAnalysis.CSharp.UnitTests;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.EditAndContinue.UnitTests;

[UseExportProvider]
public sealed class LineEditTests : EditingTestBase
{
    #region Top-level Code

    [Fact, WorkItem("https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1426286")]
    public void TopLevelCode_LineChange()
    {
        var src1 = """

            Console.ReadLine(1);

            """;
        var src2 = """


            Console.ReadLine(1);

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(1, 2)]);
    }

    [Fact, WorkItem("https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1426286")]
    public void TopLevelCode_LocalFunction_LineChange()
    {
        var src1 = """

            void F()
            {
                Console.ReadLine(1);
            }

            """;
        var src2 = """

            void F()
            {

                Console.ReadLine(1);
            }

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    #endregion

    #region Methods

    [Fact]
    public void Method_Reorder1()
    {
        var src1 = """

            class C
            {
                static void G()
                {
                    Console.ReadLine(1);
                }

                static void F()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void F()
                {
                    Console.ReadLine(2);
                }

                static void G()
                {
                    Console.ReadLine(1);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        // Consider: we could detect that the body of the method hasn't changed and avoid creating an update.
        edits.VerifyLineEdits(
            [
                new SourceLineUpdate(4, 9),
                new SourceLineUpdate(7, 7),
                new SourceLineUpdate(9, 4)
            ],
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"))]);
    }

    [Fact]
    public void Method_Reorder2()
    {
        var src1 = """

            class C
            {
                static void Main()
                {
                    F();
                    G();
                }

                static int G()
                {
                    return 1;
                }

                static int F()
                {
                    return 2;
                }
            }
            """;
        var src2 = """

            class C
            {
                static int F()
                {
                    return 1;
                }

                static void Main()
                {
                    F();
                    G();
                }

                static int G()
                {
                    return 2;
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        // Consider: we could detect that the body of the method hasn't changed and create line edits instead of an update.
        edits.VerifyLineEdits(
            [
                new SourceLineUpdate(4, 9),
            ],
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.G"))
            ]);
    }

    [Fact]
    public void Method_MultilineBreakpointSpans()
    {
        var src1 = """

            class C
            {
                void F()
                {
                    var x =
            1;
                }
            }

            """;
        var src2 = """

            class C
            {
                void F()
                {
                    var x =

            1;
                }
            }
            """;
        // We need to recompile the method since an active statement span [|var x = 1;|]
        // needs to be updated but can't be by a line update.
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"))]);
    }

    [Fact]
    public void Method_BlockBody_EntireBody1()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {


                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 6)]);
    }

    [Fact]
    public void Method_BlockBody_EntireBody2()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()

                {
                    Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 5)]);
    }

    [Fact]
    public void Method_BlockBody1()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {

                    Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact]
    public void Method_BlockBody2()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {

                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(6, 5)]);
    }

    [Fact]
    public void Method_BlockBody3()
    {
        var src1 = """

            class C
            {
                static void Bar()
                /*1*/
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                    /*2*/
                    Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 4)]);
    }

    [Fact]
    public void Method_BlockBody4()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);

                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(6, 7)]);
    }

    [Fact]
    public void Method_BlockBody5()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    if (F())
                    {
                        Console.ReadLine(2);
                    }
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                    if (F())
                    {
                        Console.ReadLine(2);

                    }
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(8, 9)]);
    }

    [Fact]
    public void Method_BlockBody_Recompile()
    {
        var src1 = """

            class C { static void Bar() { } }

            """;
        var src2 = """

            class C { /*--*/static void Bar() { } }
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"))]);
    }

    [Fact]
    public void Method_ExpressionBody_EntireBody()
    {
        var src1 = """

            class C
            {
                static int X() => 1;

                static int Y() => 1;
            }

            """;
        var src2 = """

            class C
            {

                static int X() => 1;
                static int Y() => 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [
                new SourceLineUpdate(3, 4),
                new SourceLineUpdate(4, 4)
            ]);
    }

    [Fact]
    public void Method_Statement_Recompile1()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                    /**/Console.ReadLine(2);
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"))]);
    }

    [Fact]
    public void Method_Statement_Recompile2()
    {
        var src1 = """

            class C
            {
                static void Bar()
                {
                    int <N:0.0>a = 1</N:0.0>;
                    int <N:0.1>b = 2</N:0.1>;
                    <AS:0>System.Console.WriteLine(1);</AS:0>
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar()
                {
                         int <N:0.0>a = 1</N:0.0>;
                    int <N:0.1>b = 2</N:0.1>;
                    <AS:0>System.Console.WriteLine(1);</AS:0>
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"))]);

        var active = GetActiveStatements(src1, src2);
        var syntaxMap = GetSyntaxMap(src1, src2);

        edits.VerifySemantics(
            active,
            [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"), syntaxMap[0])]);
    }

    [Fact]
    public void Method_GenericType_LineChange()
    {
        var src1 = """

            class C<T>
            {
                static void Bar()
                {
                    /*edit*/
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C<T>
            {
                static void Bar()
                {
                    Console.ReadLine(2);
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(6, 5)]);
    }

    [Fact]
    public void Method_GenericType_Recompile()
    {
        var src1 = """

            class C<T>
            {
                static void Bar()
                {
            /******/Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C<T>
            {
                static void Bar()
                {
            /******//*edit*/Console.ReadLine(2);
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.UpdatingGenericNotSupportedByRuntime, "/******//*edit*/", FeaturesResources.method)],
            capabilities: EditAndContinueCapabilities.Baseline);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"))],
            capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    [Fact]
    public void Method_GenericMethod_Recompile()
    {
        var src1 = """

            class C
            {
                static void Bar<T>()
                {
            /******//*edit*/Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static void Bar<T>()
                {
            /******/Console.ReadLine(2);
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.UpdatingGenericNotSupportedByRuntime, "/******/", FeaturesResources.method)],
            capabilities: EditAndContinueCapabilities.Baseline);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"))],
            capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    [Fact]
    public void Method_Async_Recompile()
    {
        var src1 = """

            class C
            {
                static async Task<int> Bar()
                {
                    Console.ReadLine(2);
                }
            }

            """;
        var src2 = """

            class C
            {
                static async Task<int> Bar()
                {
                    Console.ReadLine( 

            2);
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.Bar"), preserveLocalVariables: true)]);
    }

    [Fact, WorkItem("https://github.com/dotnet/roslyn/issues/69027")]
    public void Method_StackAlloc_LineChange()
    {
        var src1 = """

            class C
            {
                void F()
                {
                    Span<bool> x = stackalloc bool[64];
                }
            }

            """;
        var src2 = """

            class C
            {
                void F()
                {

                    Span<bool> x = stackalloc bool[64];
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact, WorkItem("https://github.com/dotnet/roslyn/issues/69027")]
    public void Method_StackAlloc_Recompile()
    {
        var src1 = """

            class C
            {
                void F()
                <AS:0>{</AS:0>
                    <N:0.0>Span<bool> x = stackalloc bool[64];</N:0.0>
                }
            }

            """;
        var src2 = """

            class C
            {
                void F()
                <AS:0>{</AS:0>
                    /**/<N:0.0>Span<bool> x = stackalloc bool[64];</N:0.0>
                }
            }
            """;

        // TODO: https://github.com/dotnet/roslyn/issues/67307
        // When we allow updating non-active bodies with stack alloc we will need to pass active statements to VerifyLineEdits
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.StackAllocUpdate, "stackalloc bool[64]", GetResource("method"))]);

        var active = GetActiveStatements(src1, src2);

        edits.VerifySemanticDiagnostics(
            active,
            [Diagnostic(RudeEditKind.StackAllocUpdate, "stackalloc bool[64]", GetResource("method"))]);
    }

    [Fact, WorkItem("https://github.com/dotnet/roslyn/issues/69027")]
    public void Method_StackAlloc_NonActive()
    {
        var src1 = """

            class C
            {
                void F()
                {
                    Span<bool> x = stackalloc bool[64];
                }
            }

            """;
        var src2 = """

            class C
            {
                void F()
                {
                    /**/Span<bool> x = stackalloc bool[64];
                }
            }
            """;

        // TODO: consider allowing change in non-active members
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.StackAllocUpdate, "stackalloc bool[64]", GetResource("method"))]);
    }

    [Fact]
    public void Lambda_Recompile()
    {
        var src1 = """

            class C
            {
                void F()
                {
                    var x = new System.Func<int>(
                        () => 1

                    );
                }
            }
            """;
        var src2 = """

            class C
            {
                void F()
                {
                    var x = new System.Func<int>(
                        () =>
                              1
                    );
                }
            }
            """;

        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"), preserveLocalVariables: true)]);
    }

    #endregion

    #region Constructors

    [Fact]
    public void Constructor_Reorder()
    {
        var src1 = """

            class C
            {
                public C(int a)
                {
                }

                public C(bool a)
                {
                }
            }

            """;
        var src2 = """

            class C
            {
                public C(bool a)
                {
                }

                public C(int a)
                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        // Consider: we could detect that the body of the method hasn't changed and avoid creating an update.
        edits.VerifyLineEdits(
            [
                new SourceLineUpdate(3, 7),
                new SourceLineUpdate(6, 6),
                new SourceLineUpdate(7, 3)
            ],
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").Constructors.Single(c => c.Parameters is [{ Type.SpecialType: SpecialType.System_Boolean }]), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_BlockBody_LineChange()
    {
        var src1 = """

            class C
            {
                public C(int a)

                {}
            }

            """;
        var src2 = """

            class C
            {

                public C(int a)
                {}
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
        [
            new SourceLineUpdate(3, 4),
            new SourceLineUpdate(4, 4)
        ]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_BlockBody_Recompile()
    {
        var src1 = """

            class C
            {
                public C(int a
                )
                {}
            }

            """;
        var src2 = """

            class C
            {
                public C(int a
                    )
                {}
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)]);

    }

    [Fact]
    public void Constructor_ImplicitInitializer_ExpressionBodied_LineChange1()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a) => 
                  _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a) =>

                  _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_ExpressionBodied_LineChange2()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a) 
                  => _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a)

                  => _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_ExpressionBodied_LineChange3()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a) => 
                  _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a) => 

                  _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_ExpressionBodied_LineChange4()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a) 
                  => 
                  _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a) 
                  
                  => 

                  _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(6, 8)]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_Primary_LineChange()
    {
        var src1 = """

            class C(int a);

            """;
        var src2 = """

            class
                  C(int a);
            """;

        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
        [
            new SourceLineUpdate(1, 2)
        ]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_Primary_Recompile1()
    {
        var src1 = """

            class C (int a);

            """;
        var src2 = """

            class  C(int a);

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true)]);

    }

    [Fact]
    public void Constructor_ImplicitInitializer_Primary_Recompile2()
    {
        var src1 = """

            class C(int a);

            """;
        var src2 = """

            class C(int a );

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true)]);

    }

    [Fact]
    public void Constructor_ImplicitInitializer_PrimaryRecord_Recompile1()
    {
        var src1 = """

            record C (int P);

            """;
        var src2 = """

            record  C(int P);

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetCopyConstructor("C")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true),
            ]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_PrimaryRecord_Recompile2()
    {
        var src1 = """

            record C(int P);

            """;
        var src2 = """

            record C(int P );

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true)
            ]);

    }

    [Fact]
    public void Constructor_ImplicitInitializer_PrimaryAndParameter_Recompile3()
    {
        var src1 = """

            record C(int P);

            """;
        var src2 = """

            record C( int P);

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.get_P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.set_P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true),
            ]);
    }

    [Fact]
    public void Constructor_ImplicitInitializer_PrimaryAndCopyCtorAndParameter_Recompile3()
    {
        var src1 = """

            record C<T>(int P);

            """;
        var src2 = """

            record C<T >(int P);

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetCopyConstructor("C")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.get_P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.set_P")),
                SemanticEdit(SemanticEditKind.Update, c => c.GetPrimaryConstructor("C"), preserveLocalVariables: true),
            ],
            capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    [Fact]
    public void Constructor_ExplicitInitializer_BlockBody_LineChange1()
    {
        var src1 = """

            class C
            {
                public C(int a)
                  : base()
                {
                }
            }

            """;
        var src2 = """

            class C
            {
                public C(int a) 

                  : base()
                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 5)]);
    }

    [Fact]
    public void Constructor_ExplicitInitializer_BlockBody_Recompile()
    {
        var src1 = """

            class C
            {
                public C(int a)
                  : base()
                {
                }
            }

            """;
        var src2 = """

            class C
            {
                public C(int a)
                      : base()
                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)]);
    }

    [Fact]
    public void Constructor_ExplicitInitializer_BlockBody_PartialBodyLineChange1()
    {
        var src1 = """

            class C
            {
                public C(int a)
                  : base()
                {
                }
            }

            """;
        var src2 = """

            class C
            {
                public C(int a)
                  : base()

                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            new SourceLineUpdate[] { new(5, 6) });
    }

    [Fact]
    public void Constructor_ExplicitInitializer_BlockBody_RudeRecompile1()
    {
        var src1 = """

            class C<T>
            {
                public C(int a)
                  : base()
                {
                }
            }

            """;
        var src2 = """

            class C<T>
            {
                public C(int a)
                      : base()
                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.UpdatingGenericNotSupportedByRuntime, "base", GetResource("constructor"))],
            capabilities: EditAndContinueCapabilities.Baseline);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)],
            capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    [Fact]
    public void Constructor_ExplicitInitializer_ExpressionBodied_LineChange1()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a)
                  : base() => _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a)

                  : base() => _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 6)]);
    }

    [Fact]
    public void Constructor_ExplicitInitializer_ExpressionBodied_LineChange2()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a)
                  : base() => 
                              _a = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a)

                  : base() => _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            new SourceLineUpdate[] { new(5, 6) });
    }

    [Fact]
    public void Constructor_ExplicitInitializer_ExpressionBodied_Recompile1()
    {
        var src1 = """

            class C
            {
                int _a;
                public C(int a)
                  : base() => _a 
                                 = a;
            }

            """;
        var src2 = """

            class C
            {
                int _a;
                public C(int a)
                  : base() => _a = a;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)]);
    }

    #endregion

    #region Destructors

    [Fact]
    public void Destructor_LineChange1()
    {
        var src1 = """

            class C
            {
                ~C()

                {
                }
            }

            """;
        var src2 = """

            class C
            {
                ~C()
                {
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(5, 4)]);
    }

    [Fact]
    public void Destructor_ExpressionBodied_LineChange1()
    {
        var src1 = """

            class C
            {
                ~C() => F();
            }

            """;
        var src2 = """

            class C
            {
                ~C() => 
                        F();
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Destructor_ExpressionBodied_LineChange2()
    {
        var src1 = """

            class C
            {
                ~C() => F();
            }

            """;
        var src2 = """

            class C
            {
                ~C() 
                     => F();
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    #endregion

    #region Field Initializers

    [Fact]
    public void ConstantField()
    {
        var src1 = """

            class C
            {
                const int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                const int Goo = 
                                1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>());
    }

    [Fact]
    public void NoInitializer()
    {
        var src1 = """

            class C
            {
                int Goo;
            }

            """;
        var src2 = """

            class C
            {
                int 
                    Goo;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>());
    }

    [Fact]
    public void Field_Reorder()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
                static int Bar = 2;
            }

            """;
        var src2 = """

            class C
            {
                static int Bar = 1;
                static int Goo = 2;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)],
            diagnostics: [Diagnostic(RudeEditKind.UpdateMightNotHaveAnyEffect, "Bar = 1", GetResource("field"))]);
    }

    [Fact]
    public void Field_LineChange1()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {



                static int Goo = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 6)]);
    }

    [Fact]
    public void Field_LineChange2()
    {
        var src1 = """

            class C
            {
                int Goo = 1, Bar = 2;
            }

            """;
        var src2 = """

            class C
            {
                int Goo = 1,
                             Bar = 2;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_LineChange3()
    {
        var src1 = """

            class C
            {
                [A]static int Goo = 1, Bar = 2;
            }

            """;
        var src2 = """

            class C
            {
                [A]
                   static int Goo = 1, Bar = 2;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Field_LineChange_Reloadable()
    {
        var src1 = ReloadableAttributeSrc + """

            [CreateNewOnMetadataUpdate]
            class C
            {
                int Goo = 1, Bar = 2;
            }

            """;
        var src2 = ReloadableAttributeSrc + """

            [CreateNewOnMetadataUpdate]
            class C
            {
                int Goo = 1,
                             Bar = 2;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Replace, c => c.GetMember("C"))
            ],
            capabilities: EditAndContinueCapabilities.NewTypeDefinition);
    }

    [Fact]
    public void Field_Recompile1a()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                static int Goo = 
                                 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_Recompile1b()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                static int Goo 
                               = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_Recompile1c()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                static int 
                           Goo = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_Recompile1d()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                static 
                       int Goo = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_Recompile1e()
    {
        var src1 = """

            class C
            {
                static int Goo = 1;
            }

            """;
        var src2 = """

            class C
            {
                static int Goo = 1
                                  ;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_Recompile2()
    {
        var src1 = """

            class C
            {
                static int Goo = 1 + 1;
            }

            """;
        var src2 = """

            class C
            {
                static int Goo = 1 +  1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ]);
    }

    [Fact]
    public void Field_RudeRecompile1()
    {
        var src1 = """

            class C<T>
            {
                static int Goo = 1 + 1;
            }

            """;
        var src2 = """

            class C<T>
            {
                static int Goo = 1 +/**/1;
            }
            """;
        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            diagnostics: [Diagnostic(RudeEditKind.UpdatingGenericNotSupportedByRuntime, "/**/", GetResource("field"))],
            capabilities: EditAndContinueCapabilities.Baseline);

        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").StaticConstructors.Single(), preserveLocalVariables: true)
            ],
            capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    [Fact]
    public void Field_Generic_Reloadable()
    {
        var src1 = ReloadableAttributeSrc + """

            [CreateNewOnMetadataUpdate]
            class C<T>
            {
                static int Goo = 1 + 1;
            }

            """;
        var src2 = ReloadableAttributeSrc + """

            [CreateNewOnMetadataUpdate]
            class C<T>
            {
                static int Goo = 1 +  1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Replace, c => c.GetMember("C"))
            ],
            capabilities: EditAndContinueCapabilities.NewTypeDefinition);
    }

    #endregion

    #region Properties

    [Fact]
    public void Property1()
    {
        var src1 = """

            class C
            {
                int P { get { return 1; } }
            }

            """;
        var src2 = """

            class C
            {
                int P { get { return 
                                     1; } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<IPropertySymbol>("C.P").GetMethod)]);
    }

    [Fact]
    public void Property2()
    {
        var src1 = """

            class C
            {
                int P { get { return 1; } }
            }

            """;
        var src2 = """

            class C
            {
                int P { get 
                            { return 1; } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits([new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property3()
    {
        var src1 = """

            class C
            {
                int P { get { return 1; } set { } }
            }

            """;
        var src2 = """

            class C
            {
                
                int P { get { return 1; } set { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property_ExpressionBody1()
    {
        var src1 = """

            class C
            {
                int P => 1;
            }

            """;
        var src2 = """

            class C
            {
                int P => 
                         1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property_GetterExpressionBody1()
    {
        var src1 = """

            class C
            {
                int P { get => 1; }
            }

            """;
        var src2 = """

            class C
            {
                int P { get => 
                               1; }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property_SetterExpressionBody1()
    {
        var src1 = """

            class C
            {
                int P { set => F(); }
            }

            """;
        var src2 = """

            class C
            {
                int P { set => 
                               F(); }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property_Initializer1()
    {
        var src1 = """

            class C
            {
                int P { get; } = 1;
            }

            """;
        var src2 = """

            class C
            {
                int P { 
                        get; } = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Property_Initializer2()
    {
        var src1 = """

            class C
            {
                int P { get; } = 1;
            }

            """;
        var src2 = """

            class C
            {
                int P { get; } = 
                                 1;
            }
            """;
        // We can only apply one delta per line, but that affects both getter and initializer. So we need to recompile one of them.
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            lineEdits: [new SourceLineUpdate(3, 4)],
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.get_P"))]);
    }

    [Fact]
    public void Property_Initializer3()
    {
        var src1 = """

            class C
            {
                int P { get; } = 1;
            }

            """;
        var src2 = """

            class C
            {
                int P
                      { get; } = 
                                 1;
            }
            """;
        // We can only apply one delta per line, but that affects both getter and initializer. So we need to recompile one of them.
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            lineEdits: [new SourceLineUpdate(3, 5)],
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.get_P"))]);
    }

    [Fact]
    public void Property_Initializer4()
    {
        var src1 = """

            class C
            {
                int P { get; } = 1;
            }

            """;
        var src2 = """

            class C
            {
                int P { get; } =  1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<INamedTypeSymbol>("C").InstanceConstructors.Single(), preserveLocalVariables: true)]);
    }

    #endregion

    #region Properties

    [Fact]
    public void Indexer1()
    {
        var src1 = """

            class C
            {
                int this[int a] { get { return 1; } }
            }

            """;
        var src2 = """

            class C
            {
                int this[int a] { get { return 
                                               1; } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<IPropertySymbol>("C.this[]").GetMethod)]);
    }

    [Fact]
    public void Indexer2()
    {
        var src1 = """

            class C
            {
                int this[int a] { get { return 1; } }
            }

            """;
        var src2 = """

            class C
            {
                int this[int a] { get 
                                      { return 1; } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits([new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Indexer3()
    {
        var src1 = """

            class C
            {
                int this[int a] { get { return 1; } set { } }
            }

            """;
        var src2 = """

            class C
            {
                
                int this[int a] { get { return 1; } set { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits([new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Indexer_ExpressionBody1()
    {
        var src1 = """

            class C
            {
                int this[int a] => 1;
            }

            """;
        var src2 = """

            class C
            {
                int this[int a] => 
                                   1;
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Indexer_GetterExpressionBody1()
    {
        var src1 = """

            class C
            {
                int this[int a] { get => 1; }
            }

            """;
        var src2 = """

            class C
            {
                int this[int a] { get => 
                                         1; }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Indexer_SetterExpressionBody1()
    {
        var src1 = """

            class C
            {
                int this[int a] { set => F(); }
            }

            """;
        var src2 = """

            class C
            {
                int this[int a] { set => 
                                         F(); }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    #endregion

    #region Events

    [Fact]
    public void Event_LineChange1()
    {
        var src1 = """

            class C
            {
                event Action E { add { } remove { } }
            }

            """;
        var src2 = """

            class C
            {

                event Action E { add { } remove { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)]);
    }

    [Fact]
    public void Event_LineChange2()
    {
        var src1 = """

            class C
            {
                event Action E { add 
                                     { } remove { } }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add { } remove { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 3)]);
    }

    [Fact]
    public void Event_LineChange3()
    {
        var src1 = """

            class C
            {
                event Action E { add {
                                       } remove { } }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add { } remove { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 3)]);
    }

    [Fact]
    public void Event_LineChange4()
    {
        var src1 = """

            class C
            {
                event Action E { add { } remove {
                                                  } }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add { } remove { } }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 3)]);
    }

    [Fact]
    public void Event_Recompile1()
    {
        var src1 = """

            class C
            {
                event Action E { add { } remove { } }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add { } remove 
                                                { } }
            }
            """;
        // we can only apply one delta per line, but that would affect add and remove differently, so need to recompile
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<IEventSymbol>("C.E").RemoveMethod)]);
    }

    [Fact]
    public void Event_Recompile2()
    {
        var src1 = """

            class C
            {
                event Action E { add { } remove { } }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add { } remove {
                                                  } }
            }
            """;
        // we can only apply one delta per line, but that would affect add and remove differently, so need to recompile
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<IEventSymbol>("C.E").RemoveMethod)]);
    }

    [Fact, WorkItem("https://github.com/dotnet/roslyn/issues/53263")]
    public void Event_ExpressionBody_MultipleBodiesOnTheSameLine1()
    {
        var src1 = """

            class C
            {
                event Action E { add => F(); remove => F(); }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add => 
                                        F(); remove => 
                                                       F(); }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(3, 4)],
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember<IEventSymbol>("C.E").RemoveMethod)]);
    }

    [Fact]
    public void Event_ExpressionBody()
    {
        var src1 = """

            class C
            {
                event Action E { add 
                                     => F(); remove 
                                                    => F(); }
            }

            """;
        var src2 = """

            class C
            {
                event Action E { add => F(); remove => F(); }
            }
            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [new SourceLineUpdate(4, 3), new SourceLineUpdate(5, 3)]);
    }

    #endregion

    #region Types

    [Fact]
    public void Type_Reorder1()
    {
        var src1 = """

            class C
            {
                static int F1() => 1;
                static int F2() => 1;
            }

            class D
            {
                static int G1() => 1;
                static int G2() => 1;
            }

            """;
        var src2 = """

            class D
            {
                static int G1() => 1;
                static int G2() => 1;
            }

            class C
            {
                static int F1() => 1;
                static int F2() => 1;
            }

            """;
        var edits = GetTopEdits(src1, src2);
        edits.VerifyLineEdits(
            [
                new SourceLineUpdate(3, 9),
                new SourceLineUpdate(5, 5),
                new SourceLineUpdate(9, 3)
            ]);
    }

    #endregion

    #region Line Mappings

    [Fact]
    public void LineMapping_ChangeLineNumber_WithinMethod_NoSequencePointImpact()
    {
        var src1 = """

            class C
            {
                static void F()
                {
                    G(
            #line 2 "c"
                        123
            #line default
                    );
                }
            }
            """;
        var src2 = """

            class C
            {
                static void F()
                {
                    G(
            #line 3 "c"
                        123
            #line default
                    );
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        // Line deltas can't be applied on the whole breakpoint span hence recompilation.
        edits.VerifyLineEdits(
            Array.Empty<SequencePointUpdates>(),
            semanticEdits: [SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"))]);
    }

    /// <summary>
    /// Validates that changes in #line directives produce semantic updates of the containing method.
    /// </summary>
    [Fact]
    public void LineMapping_ChangeLineNumber_OutsideOfMethod()
    {
        var src1 = """

            #line 1 "a"
            class C
            {
                int x = 1;
                static int y = 1;
                void F1() { }
                void F2() { }
            }
            class D
            {
                public D() {}

            #line 5 "a"
                void F3() {}

            #line 6 "a"
                void F4() {}
            }
            """;
        var src2 = """

            #line 11 "a"
            class C
            {
                int x = 1;
                static int y = 1;
                void F1() { }
                void F2() { }
            }
            class D
            {
                public D() {}

            #line 5 "a"
                void F3() {}
                void F4() {}
            }

            """;
        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            new SequencePointUpdates[]
            {
                new("a", [new SourceLineUpdate(2, 12), new SourceLineUpdate(6, 6), new SourceLineUpdate(9, 19)]) // D ctor
            },
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("D.F3")), // overlaps with "void F1() { }"
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("D.F4")), // overlaps with "void F2() { }"
            ]);
    }

    [Fact]
    public void LineMapping_LineDirectivesAndWhitespace()
    {
        var src1 = """

            class C
            {
            #line 5 "a"
            #line 6 "a"



                static void F() { } // line 9
            }
            """;
        var src2 = """

            class C
            {
            #line 9 "a"
                static void F() { }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        edits.VerifySemantics();
    }

    [Fact]
    public void LineMapping_MultipleFiles()
    {
        var src1 = """

            class C
            {
                static void F()
                {
            #line 1 "a"
                    A();
            #line 1 "b"
                    B();
            #line default
                }
            }
            """;
        var src2 = """

            class C
            {
                static void F()
                {
            #line 2 "a"
                    A();
            #line 2 "b"
                    B();
            #line default
                }
            }
            """;
        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            new SequencePointUpdates[]
            {
                new("a", [new SourceLineUpdate(0, 1)]),
                new("b", [new SourceLineUpdate(0, 1)]),
            });
    }

    [Fact]
    public void LineMapping_FileChange_Recompile()
    {
        var src1 = """

            class C
            {
                static void F()
                {
                    A();
            #line 1 "a"
                    B();
            #line 3 "a"
                    C();
                }


                int x = 1;
            }
            """;
        var src2 = """

            class C
            {
                static void F()
                {
                    A();
            #line 1 "b"
                    B();
            #line 2 "a"
                    C();
                }

                int x = 1;
            }
            """;
        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
            new SequencePointUpdates[]
            {
                new("a", [new SourceLineUpdate(6, 4)]),
            },
            semanticEdits:
            [
                SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"))
            ]);
    }

    [Fact]
    public void LineMapping_FileChange_RudeEdit()
    {
        var src1 = """

            #line 1 "a"
            class C { static void F<T>() { } }

            """;
        var src2 = """

            #line 1 "b"
            class C { static void F<T>() { } }
            """;

        var edits = GetTopEdits(src1, src2);

        edits.VerifyLineEdits(
             Array.Empty<SequencePointUpdates>(),
             diagnostics:
             [
                 Diagnostic(RudeEditKind.UpdatingGenericNotSupportedByRuntime, "{", GetResource("method"))
             ],
             capabilities: EditAndContinueCapabilities.Baseline);

        edits.VerifyLineEdits(
             Array.Empty<SequencePointUpdates>(),
             semanticEdits:
             [
                 SemanticEdit(SemanticEditKind.Update, c => c.GetMember("C.F"))
             ],
             capabilities: EditAndContinueCapabilities.GenericUpdateMethod);
    }

    #endregion
}
