﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Composition
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Editing
Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.Host.Mef
Imports Microsoft.CodeAnalysis.ImplementInterface
Imports Microsoft.CodeAnalysis.PooledObjects
Imports Microsoft.CodeAnalysis.VisualBasic.CodeGeneration
Imports Microsoft.CodeAnalysis.VisualBasic.Formatting
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.ImplementInterface
    <ExportLanguageService(GetType(IImplementInterfaceService), LanguageNames.VisualBasic), [Shared]>
    Partial Friend NotInheritable Class VisualBasicImplementInterfaceService
        Inherits AbstractImplementInterfaceService(Of TypeBlockSyntax)

        <ImportingConstructor>
        <Obsolete(MefConstruction.ImportingConstructorMessage, True)>
        Public Sub New()
        End Sub

        Protected Overrides ReadOnly Property SyntaxGeneratorInternal As SyntaxGeneratorInternal = VisualBasicSyntaxGeneratorInternal.Instance

        Protected Overrides Function ToDisplayString(disposeImplMethod As IMethodSymbol, format As SymbolDisplayFormat) As String
            Return SymbolDisplay.ToDisplayString(disposeImplMethod, format)
        End Function

        Protected Overrides ReadOnly Property CanImplementImplicitly As Boolean
        Protected Overrides ReadOnly Property HasHiddenExplicitImplementation As Boolean

        Protected Overrides Function AllowDelegateAndEnumConstraints(options As ParseOptions) As Boolean
            Return False
        End Function

        Protected Overrides Function IsTypeInInterfaceBaseList(type As SyntaxNode) As Boolean
            Return TypeOf type?.Parent Is ImplementsStatementSyntax
        End Function

        Protected Overrides Sub AddInterfaceTypes(typeDeclaration As TypeBlockSyntax, result As ArrayBuilder(Of SyntaxNode))
            For Each implementsStatement In typeDeclaration.Implements
                For Each interfaceType In implementsStatement.Types
                    result.Add(interfaceType)
                Next
            Next
        End Sub

        Protected Overrides Function TryInitializeState(
                document As Document, model As SemanticModel, node As SyntaxNode, cancellationToken As CancellationToken,
                ByRef classOrStructDecl As SyntaxNode, ByRef classOrStructType As INamedTypeSymbol,
                ByRef interfaceTypes As ImmutableArray(Of INamedTypeSymbol)) As Boolean
            If cancellationToken.IsCancellationRequested Then
                Return False
            End If

            Dim implementsStatement As ImplementsStatementSyntax
            Dim interfaceNode As TypeSyntax
            If TypeOf node Is ImplementsStatementSyntax Then
                interfaceNode = Nothing
                implementsStatement = DirectCast(node, ImplementsStatementSyntax)
            ElseIf TypeOf node Is TypeSyntax AndAlso TypeOf node.Parent Is ImplementsStatementSyntax Then
                interfaceNode = DirectCast(node, TypeSyntax)
                implementsStatement = DirectCast(node.Parent, ImplementsStatementSyntax)
            Else
                Return False
            End If

            If implementsStatement.IsParentKind(SyntaxKind.ClassBlock) OrElse
               implementsStatement.IsParentKind(SyntaxKind.StructureBlock) Then

                If interfaceNode IsNot Nothing Then
                    interfaceTypes = ImmutableArray.Create(GetInterfaceType(model, interfaceNode, cancellationToken))
                Else
                    interfaceTypes = implementsStatement.Types.SelectAsArray(
                        Function(t) GetInterfaceType(model, t, cancellationToken))
                End If

                interfaceTypes = interfaceTypes.WhereNotNull().Where(Function(t) t.TypeKind = TypeKind.Interface).ToImmutableArray()
                If interfaceTypes.Any() Then
                    cancellationToken.ThrowIfCancellationRequested()

                    classOrStructDecl = implementsStatement.Parent
                    Dim classOrStructBlock = TryCast(classOrStructDecl, TypeBlockSyntax)
                    classOrStructType = model.GetDeclaredSymbol(classOrStructBlock.BlockStatement, cancellationToken)

                    Return classOrStructType IsNot Nothing
                End If
            End If

            classOrStructDecl = Nothing
            classOrStructType = Nothing
            interfaceTypes = Nothing
            Return False
        End Function

        Private Shared Function GetInterfaceType(semanticModel As SemanticModel,
                                          node As SyntaxNode,
                                          cancellationToken As CancellationToken) As INamedTypeSymbol
            Dim symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken)
            If symbolInfo.CandidateReason = CandidateReason.WrongArity Then
                Return Nothing
            End If

            Return TryCast(symbolInfo.GetAnySymbol(), INamedTypeSymbol)
        End Function

        Protected Overrides Function AddCommentInsideIfStatement(ifStatement As SyntaxNode, trivia As SyntaxTriviaList) As SyntaxNode
            Return ifStatement.ReplaceNode(
                ifStatement.ChildNodes().Last(),
                ifStatement.ChildNodes().Last().WithPrependedLeadingTrivia(trivia))
        End Function

        Protected Overrides Function CreateFinalizer(
                g As SyntaxGenerator,
                classType As INamedTypeSymbol,
                disposeMethodDisplayString As String) As SyntaxNode

            ' ' Do not change this code...
            ' Dispose(False)
            Dim disposeStatement = AddComment(
                String.Format(CodeFixesResources.Do_not_change_this_code_Put_cleanup_code_in_0_method, disposeMethodDisplayString),
                g.ExpressionStatement(g.InvocationExpression(
                    g.IdentifierName(NameOf(IDisposable.Dispose)),
                    g.Argument(DisposingName, RefKind.None, g.FalseLiteralExpression()))))

            ' MyBase.Finalize()
            Dim finalizeStatement =
                g.ExpressionStatement(g.InvocationExpression(
                    g.MemberAccessExpression(g.BaseExpression(), g.IdentifierName(NameOf(Finalize)))))

            Dim methodDecl = g.MethodDeclaration(
                NameOf(Finalize),
                accessibility:=Accessibility.Protected,
                modifiers:=DeclarationModifiers.Override,
                statements:={disposeStatement, finalizeStatement})

            Return AddComment(
                String.Format(CodeFixesResources.TODO_colon_override_finalizer_only_if_0_has_code_to_free_unmanaged_resources, disposeMethodDisplayString),
                methodDecl)
        End Function
    End Class
End Namespace
