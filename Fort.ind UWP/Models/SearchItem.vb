''' <summary>
''' Represents a searchable item in the app search bar
''' </summary>
Public Class SearchItem

    ''' <summary>
    ''' Display text shown in the suggestion list
    ''' </summary>
    Public Property Title As String

    ''' <summary>
    ''' Category label (e.g. "Menu", "Settings", "Profile", "fort1nd.com")
    ''' </summary>
    Public Property Category As String

    ''' <summary>
    ''' Navigation tag or URL used when the item is selected
    ''' </summary>
    Public Property NavigationTag As String

    ''' <summary>
    ''' Optional URL for external items from fort1nd.com
    ''' </summary>
    Public Property Url As String

    Public Sub New(title As String, category As String, navigationTag As String, Optional url As String = Nothing)
        Me.Title = title
        Me.Category = category
        Me.NavigationTag = navigationTag
        Me.Url = url
    End Sub

    Public Overrides Function ToString() As String
        Return $"{Title}  —  {Category}"
    End Function

End Class
