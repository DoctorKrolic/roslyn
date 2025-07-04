﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Editor.[Shared].Extensions
Imports Microsoft.CodeAnalysis.Editor.Shared.Utilities
Imports Microsoft.CodeAnalysis.Editor.UnitTests.Utilities
Imports Microsoft.CodeAnalysis.Editor.UnitTests.Utilities.GoToHelpers
Imports Microsoft.CodeAnalysis.GoToDefinition
Imports Microsoft.CodeAnalysis.Navigation
Imports Microsoft.CodeAnalysis.Shared.TestHooks
Imports Microsoft.VisualStudio.Text
Imports Microsoft.VisualStudio.Text.Editor.Commanding.Commands

Namespace Microsoft.CodeAnalysis.Editor.UnitTests.GoToDefinition
    <UseExportProvider, Trait(Traits.Feature, Traits.Features.GoToDefinition)>
    Public NotInheritable Class GoToDefinitionCommandHandlerTests
        <WpfFact>
        Public Async Function TestInLinkedFiles() As Task
            Dim definition =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSProj" PreprocessorSymbols="Proj1">
        <Document FilePath="C.cs">
class C
{
    void M()
    {
        M1$$(5);
    }
#if Proj1
    void M1(int x) { }
#endif
#if Proj2
    void M1(int x) { }
#endif
}
        </Document>
    </Project>
    <Project Language="C#" CommonReferences="true" PreprocessorSymbols="Proj2">
        <Document IsLinkFile="true" LinkAssemblyName="CSProj" LinkFilePath="C.cs"/>
    </Project>
</Workspace>

            Using workspace = EditorTestWorkspace.Create(definition, composition:=GoToTestHelpers.Composition)

                Dim baseDocument = workspace.Documents.First(Function(d) Not d.IsLinkFile)
                Dim linkDocument = workspace.Documents.First(Function(d) d.IsLinkFile)
                Dim view = baseDocument.GetTextView()

                Dim mockDocumentNavigationService =
                    DirectCast(workspace.Services.GetService(Of IDocumentNavigationService)(), MockDocumentNavigationService)

                Dim provider = workspace.GetService(Of IAsynchronousOperationListenerProvider)()
                Dim waiter = provider.GetWaiter(FeatureAttribute.GoToDefinition)
                Dim handler = New GoToDefinitionCommandHandler(
                    workspace.GetService(Of IThreadingContext),
                    provider)

                handler.ExecuteCommand(New GoToDefinitionCommandArgs(view, baseDocument.GetTextBuffer()), TestCommandExecutionContext.Create())
                Await waiter.ExpeditedWaitAsync()

                Assert.True(mockDocumentNavigationService._triedNavigationToPosition)
                Assert.Equal(78, mockDocumentNavigationService._position)

                workspace.SetDocumentContext(linkDocument.Id)

                handler.ExecuteCommand(New GoToDefinitionCommandArgs(view, baseDocument.GetTextBuffer()), TestCommandExecutionContext.Create())
                Await waiter.ExpeditedWaitAsync()

                Assert.True(mockDocumentNavigationService._triedNavigationToPosition)
                Assert.Equal(121, mockDocumentNavigationService._position)
            End Using
        End Function

        <WpfFact>
        Public Async Function TestAtEndOfFile() As Task
            Dim definition =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSProj">
        <Document FilePath="C.cs">int x = 0;
int y = x$$</Document>
    </Project>
</Workspace>

            Using workspace = EditorTestWorkspace.Create(definition, composition:=GoToTestHelpers.Composition)

                Dim document = workspace.Documents.First()
                Dim view = document.GetTextView()

                Dim mockDocumentNavigationService =
                    DirectCast(workspace.Services.GetService(Of IDocumentNavigationService)(), MockDocumentNavigationService)

                Dim provider = workspace.GetService(Of IAsynchronousOperationListenerProvider)()
                Dim waiter = provider.GetWaiter(FeatureAttribute.GoToDefinition)
                Dim handler = New GoToDefinitionCommandHandler(
                    workspace.GetService(Of IThreadingContext),
                    provider)

                handler.ExecuteCommand(New GoToDefinitionCommandArgs(view, document.GetTextBuffer()), TestCommandExecutionContext.Create())
                Await waiter.ExpeditedWaitAsync()

                Assert.True(mockDocumentNavigationService._triedNavigationToPosition)
                Assert.Equal(4, mockDocumentNavigationService._position)
                Assert.Equal(document.Id, mockDocumentNavigationService._documentId)
            End Using
        End Function

        <WpfTheory, WorkItem("https://github.com/dotnet/roslyn/issues/43183")>
        <CombinatorialData>
        Public Async Function TestWithSelection(reversedSelection As Boolean) As Task
            Dim definition =
<Workspace>
    <Project Language="C#" CommonReferences="true" AssemblyName="CSProj">
        <Document FilePath="C.cs">
class C
{
    int X;

    void M()
    {
        _ = X%2; // Press F12 with caret between X and %
    }
}
        </Document>
    </Project>
</Workspace>

            Using workspace = EditorTestWorkspace.Create(definition, composition:=GoToTestHelpers.Composition)

                Dim document = workspace.Documents.First()
                Dim view = document.GetTextView()

                Dim mockDocumentNavigationService =
                    DirectCast(workspace.Services.GetService(Of IDocumentNavigationService)(), MockDocumentNavigationService)

                Dim provider = workspace.GetService(Of IAsynchronousOperationListenerProvider)()
                Dim waiter = provider.GetWaiter(FeatureAttribute.GoToDefinition)
                Dim handler = New GoToDefinitionCommandHandler(
                    workspace.GetService(Of IThreadingContext),
                    provider)

                Dim snapshot = document.GetTextBuffer().CurrentSnapshot
                Dim index = snapshot.GetText().IndexOf("X%")

                view.SetSelection(New SnapshotSpan(snapshot, New Span(index, 1)), isReversed:=reversedSelection)

                handler.ExecuteCommand(New GoToDefinitionCommandArgs(view, document.GetTextBuffer()), TestCommandExecutionContext.Create())
                Await waiter.ExpeditedWaitAsync()

                Assert.True(mockDocumentNavigationService._triedNavigationToPosition)
                Assert.Equal(22, mockDocumentNavigationService._position)
                Assert.Equal(document.Id, mockDocumentNavigationService._documentId)
            End Using
        End Function
    End Class
End Namespace
