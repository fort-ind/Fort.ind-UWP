Imports System.Xml.Linq
Imports Windows.Storage

''' <summary>
''' Parses the bundled sitemap.xml and produces SearchItem entries for every URL
''' </summary>
Public Class SitemapService

    ''' <summary>
    ''' Reads sitemap.xml from the app package and returns SearchItem objects allowing for the latest URLs to be searchable 
    ''' </summary>
    Public Shared Async Function LoadSearchItemsAsync() As Task(Of List(Of SearchItem))
        Dim items As New List(Of SearchItem)

        Try
            Dim file = Await StorageFile.GetFileFromApplicationUriAsync(New Uri("ms-appx:///sitemap.xml"))
            Dim text = Await FileIO.ReadTextAsync(file)
            Dim doc = XDocument.Parse(text)
            Dim ns As XNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9"

            For Each urlElement In doc.Descendants(ns + "url")
                Dim loc = urlElement.Element(ns + "loc")?.Value
                If String.IsNullOrEmpty(loc) Then Continue For

                Dim uri As Uri = Nothing
                If Not Uri.TryCreate(loc, UriKind.Absolute, uri) Then Continue For

                Dim path = uri.AbsolutePath.Trim("/"c)
                If String.IsNullOrEmpty(path) Then
                    items.Add(New SearchItem("Home", "fort1nd.com", Nothing, loc))
                    Continue For
                End If

                ' Skip utility pages
                If path = "404" Then Continue For

                Dim category = GetCategory(path)
                Dim title = GetTitle(path)

                items.Add(New SearchItem(title, category, Nothing, loc))
            Next
        Catch
            ' If sitemap can't be loaded, return empty list gracefully
        End Try

        Return items
    End Function

    Private Shared Function GetCategory(path As String) As String
        If path.StartsWith("games/html/") Then Return "Games — HTML"
        If path.StartsWith("games/flash/") Then Return "Games — Flash"
        If path.StartsWith("games/codepen/") Then Return "Games — CodePen"
        If path.StartsWith("games/retroclassic-mostly-emulated/") Then Return "Games — Retro"
        If path.StartsWith("games/minecraft/") Then Return "Games — Minecraft"
        If path.StartsWith("games/doodles") Then Return "Games"
        If path.StartsWith("games/") Then Return "Games"
        If path.StartsWith("social/") Then Return "Social"
        If path.StartsWith("emulators/") Then Return "Emulators"
        If path.StartsWith("apps/appstone/") Then Return "Apps — AppStone"
        If path.StartsWith("apps/") Then Return "Apps"
        If path.StartsWith("extras/") Then Return "Extras"
        If path.StartsWith("labs-betas/") Then Return "Labs & Betas"
        Return "fort1nd.com"
    End Function

    Private Shared Function GetTitle(path As String) As String
        ' Use the last segment of the path as the display name
        Dim segments = path.Split("/"c)
        Dim slug = segments.Last()

        ' Replace hyphens/underscores with spaces and title-case it
        Dim words = slug.Replace("-", " ").Replace("_", " ").Split(" "c)
        For i = 0 To words.Length - 1
            If words(i).Length > 0 Then
                words(i) = Char.ToUpper(words(i)(0)) & words(i).Substring(1)
            End If
        Next
        Return String.Join(" ", words)
    End Function

End Class
