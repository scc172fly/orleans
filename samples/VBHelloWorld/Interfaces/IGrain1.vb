''' <summary>
''' Orleans grain 通信接口 IGrain1
''' </summary>
Public Interface IHelloGrain
    Inherits Orleans.IGrainWithIntegerKey

    Function SayHello(greeting As String) As Task(Of String)

End Interface
