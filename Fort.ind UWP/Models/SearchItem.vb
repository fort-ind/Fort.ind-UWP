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

    ''' <summary>
    ''' Segoe MDL2 Assets glyph character for the category icon
    ''' </summary>
    Public Property Icon As String

    Public Sub New(title As String, category As String, navigationTag As String, Optional url As String = Nothing)
        Me.Title = title
        Me.Category = category
        Me.NavigationTag = navigationTag
        Me.Url = url
        Me.Icon = GetIconGlyph(category)
    End Sub

    Private Shared Function GetIconGlyph(category As String) As String
        Select Case True
            Case category = "Menu" : Return ChrW(&HE700)          ' GlobalNavButton
            Case category = "Settings" : Return ChrW(&HE713)      ' Setting (gear)
            Case category = "Profile" : Return ChrW(&HE77B)       ' Contact (person)
            Case category.StartsWith("Games") : Return ChrW(&HE768) ' Play
            Case category = "Social" : Return ChrW(&HE716)        ' People
            Case category = "Emulators" : Return ChrW(&HE768)     ' Play (gaming)
            Case category.StartsWith("Apps") : Return ChrW(&HE71D) ' Apps
            Case category = "Extras" : Return ChrW(&HE734)        ' Favorite (star)
            Case category = "Labs & Betas" : Return ChrW(&HE9D9)  ' Beaker
            Case Else : Return ChrW(&HE774)                        ' Globe (fort1nd.com + default)
        End Select
    End Function

    Public Overrides Function ToString() As String
        Return $"{Title}  —  {Category}"
    End Function

End Class
