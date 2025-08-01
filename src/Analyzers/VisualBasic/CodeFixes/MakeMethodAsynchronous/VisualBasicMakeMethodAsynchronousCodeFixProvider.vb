﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.Composition
Imports System.Diagnostics.CodeAnalysis
Imports System.Threading
Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.MakeMethodAsynchronous
Imports Microsoft.CodeAnalysis.Simplification
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.MakeMethodAsynchronous
    <ExportCodeFixProvider(LanguageNames.VisualBasic, Name:=PredefinedCodeFixProviderNames.AddAsync), [Shared]>
    Friend Class VisualBasicMakeMethodAsynchronousCodeFixProvider
        Inherits AbstractMakeMethodAsynchronousCodeFixProvider

        Friend Const BC36937 As String = "BC36937" ' error BC36937: 'Await' can only be used when contained within a method or lambda expression marked with the 'Async' modifier.
        Friend Const BC37057 As String = "BC37057" ' error BC37057: 'Await' can only be used within an Async method. Consider marking this method with the 'Async' modifier and changing its return type to 'Task'.
        Friend Const BC37058 As String = "BC37058" ' error BC37058: 'Await' can only be used within an Async method. Consider marking this method with the 'Async' modifier and changing its return type to 'Task'.
        Friend Const BC37059 As String = "BC37059" ' error BC37059: 'Await' can only be used within an Async lambda expression. Consider marking this expression with the 'Async' modifier and changing its return type to 'Task'.

        Private Shared ReadOnly s_diagnosticIds As ImmutableArray(Of String) = ImmutableArray.Create(
            BC36937, BC37057, BC37058, BC37059)

        Private Shared ReadOnly s_asyncToken As SyntaxToken = SyntaxFactory.Token(SyntaxKind.AsyncKeyword)

        <ImportingConstructor>
        <SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification:="Used in test code: https://github.com/dotnet/roslyn/issues/42814")>
        Public Sub New()
        End Sub

        Protected Overrides Function IsSupportedDiagnostic(diagnostic As Diagnostic, cancellationToken As CancellationToken) As Boolean
            Return True
        End Function

        Public Overrides ReadOnly Property FixableDiagnosticIds As ImmutableArray(Of String)
            Get
                Return s_diagnosticIds
            End Get
        End Property

        Protected Overrides Function GetMakeAsyncTaskFunctionResource() As String
            Return VisualBasicCodeFixesResources.Make_Async_Function
        End Function

        Protected Overrides Function GetMakeAsyncVoidFunctionResource() As String
            Return VisualBasicCodeFixesResources.Make_Async_Sub
        End Function

        Protected Overrides Function IsAsyncSupportingFunctionSyntax(node As SyntaxNode) As Boolean
            Return node.IsAsyncSupportedFunctionSyntax()
        End Function

        Protected Overrides Function IsAsyncReturnType(type As ITypeSymbol, knownTypes As KnownTaskTypes) As Boolean
            Return knownTypes.IsTaskLike(type)
        End Function

        Protected Overrides Function FixMethodSignature(
                addAsyncModifier As Boolean,
                keepVoid As Boolean,
                methodSymbolOpt As IMethodSymbol,
                node As SyntaxNode,
                knownTypes As KnownTaskTypes) As SyntaxNode

            ' This flag can only be false when updating partial definition method signature.
            ' Since partial methods cannot be async in VB, it cannot be false here
            Debug.Assert(addAsyncModifier)

            If node.IsKind(SyntaxKind.SingleLineSubLambdaExpression) OrElse
               node.IsKind(SyntaxKind.SingleLineFunctionLambdaExpression) Then

                Return FixSingleLineLambdaExpression(DirectCast(node, SingleLineLambdaExpressionSyntax))
            ElseIf node.IsKind(SyntaxKind.MultiLineSubLambdaExpression) OrElse
                   node.IsKind(SyntaxKind.MultiLineFunctionLambdaExpression) Then

                Return FixMultiLineLambdaExpression(DirectCast(node, MultiLineLambdaExpressionSyntax))
            ElseIf node.IsKind(SyntaxKind.SubBlock) Then
                Return FixSubBlock(keepVoid, DirectCast(node, MethodBlockSyntax), knownTypes.TaskType)
            Else
                Return FixFunctionBlock(
                    methodSymbolOpt, DirectCast(node, MethodBlockSyntax), knownTypes)
            End If
        End Function

        Private Shared Function FixFunctionBlock(methodSymbol As IMethodSymbol, node As MethodBlockSyntax, knownTypes As KnownTaskTypes) As SyntaxNode
            Dim functionStatement = node.SubOrFunctionStatement
            Dim newFunctionStatement = AddAsyncKeyword(functionStatement)

            If Not knownTypes.IsTaskLike(methodSymbol.ReturnType) Then
                ' if the current return type is not already task-list, then wrap it in Task(of ...)
                Dim returnType = knownTypes.TaskOfTType.Construct(methodSymbol.ReturnType).GenerateTypeSyntax().WithAdditionalAnnotations(Simplifier.AddImportsAnnotation)
                newFunctionStatement = newFunctionStatement.WithAsClause(
                newFunctionStatement.AsClause.WithType(returnType))
            End If

            Return node.WithSubOrFunctionStatement(newFunctionStatement)
        End Function

        Private Shared Function FixSubBlock(
                keepVoid As Boolean, node As MethodBlockSyntax, taskType As INamedTypeSymbol) As SyntaxNode

            If keepVoid Then
                ' User wants to keep this a void method, so keep this as a sub.
                Dim newSubStatement = AddAsyncKeyword(node.SubOrFunctionStatement)
                Return node.WithSubOrFunctionStatement(newSubStatement)
            End If

            ' Have to convert this sub into a func. 
            Dim subStatement = node.SubOrFunctionStatement
            Dim asClause =
                SyntaxFactory.SimpleAsClause(taskType.GenerateTypeSyntax()).
                              WithTrailingTrivia(
                                If(subStatement.ParameterList?.GetTrailingTrivia(),
                                   subStatement.GetTrailingTrivia()))

            Dim functionStatement = SyntaxFactory.FunctionStatement(
                subStatement.AttributeLists,
                subStatement.Modifiers.Add(s_asyncToken),
                SyntaxFactory.Token(SyntaxKind.FunctionKeyword).WithTriviaFrom(subStatement.SubOrFunctionKeyword),
                subStatement.Identifier.WithTrailingTrivia(),
                subStatement.TypeParameterList?.WithoutTrailingTrivia(),
                subStatement.ParameterList?.WithoutTrailingTrivia(),
                asClause,
                subStatement.HandlesClause,
                subStatement.ImplementsClause)

            Dim endFunctionStatement = SyntaxFactory.EndFunctionStatement(
                node.EndSubOrFunctionStatement.EndKeyword,
                SyntaxFactory.Token(SyntaxKind.FunctionKeyword).WithTriviaFrom(node.EndSubOrFunctionStatement.BlockKeyword))

            Dim block = SyntaxFactory.FunctionBlock(
                functionStatement,
                node.Statements,
                endFunctionStatement)

            Return block
        End Function

        Private Shared Function AddAsyncKeyword(subOrFunctionStatement As MethodStatementSyntax) As MethodStatementSyntax
            Dim modifiers = subOrFunctionStatement.Modifiers
            Dim newModifiers = modifiers.Add(s_asyncToken)
            Return subOrFunctionStatement.WithModifiers(newModifiers)
        End Function

        Private Shared Function FixMultiLineLambdaExpression(node As MultiLineLambdaExpressionSyntax) As SyntaxNode
            Dim header As LambdaHeaderSyntax = GetNewHeader(node)
            Return node.WithSubOrFunctionHeader(header).WithLeadingTrivia(node.GetLeadingTrivia())
        End Function

        Private Shared Function FixSingleLineLambdaExpression(node As SingleLineLambdaExpressionSyntax) As SingleLineLambdaExpressionSyntax
            Dim header As LambdaHeaderSyntax = GetNewHeader(node)
            Return node.WithSubOrFunctionHeader(header).WithLeadingTrivia(node.GetLeadingTrivia())
        End Function

        Private Shared Function GetNewHeader(node As LambdaExpressionSyntax) As LambdaHeaderSyntax
            Dim header = DirectCast(node.SubOrFunctionHeader, LambdaHeaderSyntax)
            Dim newModifiers = header.Modifiers.Add(s_asyncToken)
            Dim newHeader = header.WithModifiers(newModifiers)
            Return newHeader
        End Function
    End Class
End Namespace
