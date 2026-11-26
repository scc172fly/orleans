Imports Orleans

''' <summary>
''' Orleans grain 执行类
''' </summary>
Public Class HelloGrain
    Inherits Grain
    Implements Interfaces.IHelloGrain

    Public Function SayHello(greeting As String) As Task(Of String) Implements Interfaces.IHelloGrain.SayHello
        Return Task.FromResult("Hello, this is Visual Basic speaking!")
    End Function

End Class
