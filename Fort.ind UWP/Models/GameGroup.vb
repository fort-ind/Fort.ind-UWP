''' <summary>
''' One alphabetical bucket of games – "A" through "Z", plus "#" for every title that starts
''' with a digit or anything else outside A–Z. Bound through a CollectionViewSource with
''' IsSourceGrouped=True and ItemsPath="Items", so this type is deliberately NOT a collection
''' itself: keeping Items strongly typed means no DirectCast(…, SearchItem) anywhere in the
''' page under Option Strict On.
''' </summary>
Public NotInheritable Class GameGroup

    Public Sub New(key As String)
        Me.Key = key
        Me.Items = New ObservableCollection(Of SearchItem)()
    End Sub

    ''' <summary>
    ''' Header text and jump-grid label: a single letter, or "#".
    ''' </summary>
    Public ReadOnly Property Key As String

    ''' <summary>
    ''' Observable so the filter can replace the rows in a group without swapping the
    ''' collection instance the CollectionViewSource is watching.
    ''' </summary>
    Public ReadOnly Property Items As ObservableCollection(Of SearchItem)

    ''' <summary>
    ''' Narrator fallback for the jump tile if a template ever omits an automation name.
    ''' </summary>
    Public Overrides Function ToString() As String
        Return Key
    End Function

End Class
