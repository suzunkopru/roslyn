﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.ComponentModel.Composition
Imports System.Diagnostics.CodeAnalysis
Imports System.Threading
Imports Microsoft.CodeAnalysis.Editor.Implementation.Highlighting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.Editor.VisualBasic.KeywordHighlighting
    <ExportHighlighter(LanguageNames.VisualBasic)>
    Friend Class XmlElementHighlighter
        Inherits AbstractKeywordHighlighter(Of XmlNodeSyntax)

        <ImportingConstructor>
        <SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification:="Used in test code: https://github.com/dotnet/roslyn/issues/42814")>
        Public Sub New()
        End Sub

        Protected Overloads Overrides Sub AddHighlights(node As XmlNodeSyntax, highlights As List(Of TextSpan), cancellationToken As CancellationToken)
            Dim xmlElement = node.GetAncestor(Of XmlElementSyntax)()
            With xmlElement
                If xmlElement IsNot Nothing AndAlso
                   Not .ContainsDiagnostics AndAlso
                   Not .HasAncestor(Of DocumentationCommentTriviaSyntax)() Then

                    With .StartTag
                        If .Attributes.Count = 0 Then
                            highlights.Add(.Span)
                        Else
                            highlights.Add(TextSpan.FromBounds(.LessThanToken.SpanStart, .Name.Span.End))
                            highlights.Add(.GreaterThanToken.Span)
                        End If
                    End With
                    highlights.Add(.EndTag.Span)
                End If

            End With
        End Sub
    End Class
End Namespace
