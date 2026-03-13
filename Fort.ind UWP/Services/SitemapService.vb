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
            Dim cachedUrls = Await TryLoadCachedUrlsAsync()
            If cachedUrls IsNot Nothing AndAlso cachedUrls.Count > 0 Then
                Return BuildSearchItemsFromUrls(cachedUrls)
            End If

            Dim file = Await StorageFile.GetFileFromApplicationUriAsync(New Uri("ms-appx:///sitemap.xml"))
            Dim text = Await FileIO.ReadTextAsync(file)
            
            ' Protect against malformed XML
            Dim doc As XDocument = Nothing
            Try
                doc = XDocument.Parse(text)
            Catch xmlEx As Exception
                Debug.WriteLine($"SitemapService: XML parsing failed – {xmlEx.Message}")
                Return items ' Return empty list if XML is malformed
            End Try
            
            Dim ns As XNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9"
            Dim urlsToCache As New List(Of String)()

            For Each urlElement In doc.Descendants(ns + "url")
                Dim urlValue = urlElement.Element(ns + "loc")?.Value
                If String.IsNullOrEmpty(urlValue) Then Continue For

                Dim uri As Uri = Nothing
                If Not Uri.TryCreate(urlValue, UriKind.Absolute, uri) Then Continue For

                urlsToCache.Add(urlValue)

                Dim path = uri.AbsolutePath.Trim("/"c)
                If String.IsNullOrEmpty(path) Then
                    items.Add(New SearchItem("Home", AppConstants.CategoryFortWebsite, Nothing, urlValue))
                    Continue For
                End If

                ' Skip utility pages
                If path = "404" Then Continue For

                Dim category = GetCategory(path)
                Dim title = GetTitle(path)

                items.Add(New SearchItem(title, category, Nothing, urlValue))
            Next

            If urlsToCache.Count > 0 Then
                Await SaveCachedUrlsAsync(urlsToCache)
            End If
        Catch ex As Exception
            Debug.WriteLine($"SitemapService: failed to load sitemap – {ex.Message}")
        End Try

        Return items
    End Function

    Private Shared Function BuildSearchItemsFromUrls(urls As IEnumerable(Of String)) As List(Of SearchItem)
        Dim items As New List(Of SearchItem)()

        For Each urlValue In urls
            If String.IsNullOrWhiteSpace(urlValue) Then Continue For

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(urlValue, UriKind.Absolute, uri) Then Continue For

            Dim path = uri.AbsolutePath.Trim("/"c)
            If String.IsNullOrEmpty(path) Then
                items.Add(New SearchItem("Home", AppConstants.CategoryFortWebsite, Nothing, urlValue))
                Continue For
            End If

            If path = "404" Then Continue For

            Dim category = GetCategory(path)
            Dim title = GetTitle(path)
            items.Add(New SearchItem(title, category, Nothing, urlValue))
        Next

        Return items
    End Function

    Private Shared Async Function TryLoadCachedUrlsAsync() As Task(Of List(Of String))
        Try
            Dim settings = ApplicationData.Current.LocalSettings
            If Not settings.Values.ContainsKey(AppConstants.SitemapCacheTimestampKey) Then
                Return Nothing
            End If

            Dim rawTimestamp = settings.Values(AppConstants.SitemapCacheTimestampKey)
            Dim cacheUnixSeconds As Long
            Try
                cacheUnixSeconds = Convert.ToInt64(rawTimestamp)
            Catch
                Return Nothing
            End Try

            Dim nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            Dim maxAgeSeconds = CLng(AppConstants.SitemapCacheTtlHours) * 60L * 60L
            If (nowUnixSeconds - cacheUnixSeconds) > maxAgeSeconds Then
                Return Nothing
            End If

            Dim cacheFile = Await ApplicationData.Current.LocalFolder.GetFileAsync(AppConstants.SitemapCacheFileName)
            Dim content = Await FileIO.ReadTextAsync(cacheFile)
            If String.IsNullOrWhiteSpace(content) Then
                Return Nothing
            End If

            Dim urls As New List(Of String)()
            Dim lines = content.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            For Each line In lines
                Dim value = line.Trim()
                If Not String.IsNullOrWhiteSpace(value) Then
                    urls.Add(value)
                End If
            Next

            If urls.Count = 0 Then
                Return Nothing
            End If

            Return urls
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Async Function SaveCachedUrlsAsync(urls As IEnumerable(Of String)) As Task
        Try
            Dim lines As New List(Of String)()
            For Each url In urls
                If Not String.IsNullOrWhiteSpace(url) Then
                    lines.Add(url)
                End If
            Next

            If lines.Count = 0 Then
                Return
            End If

            Dim cacheFile = Await ApplicationData.Current.LocalFolder.CreateFileAsync(
                AppConstants.SitemapCacheFileName,
                CreationCollisionOption.ReplaceExisting)

            Await FileIO.WriteTextAsync(cacheFile, String.Join(Environment.NewLine, lines))
            ApplicationData.Current.LocalSettings.Values(AppConstants.SitemapCacheTimestampKey) = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        Catch ex As Exception
            Debug.WriteLine($"SitemapService: failed to save sitemap cache – {ex.Message}")
        End Try
    End Function

    Private Shared Function GetCategory(path As String) As String
        If path.StartsWith("games/html/") Then Return "Games — HTML"
        If path.StartsWith("games/flash/") Then Return "Games — Flash"
        If path.StartsWith("games/codepen/") Then Return "Games — CodePen"
        If path.StartsWith("games/retroclassic-mostly-emulated/") Then Return "Games — Retro"
        If path.StartsWith("games/minecraft/") Then Return "Games — Minecraft"
        If path.StartsWith("games/") Then Return "Games"
        If path.StartsWith("social/") Then Return "Social"
        If path.StartsWith("emulators/") Then Return "Emulators"
        If path.StartsWith("apps/appstone/") Then Return "Apps — AppStone"
        If path.StartsWith("apps/") Then Return "Apps"
        If path.StartsWith("extras/") Then Return "Extras"
        If path.StartsWith("labs-betas/") Then Return "Labs & Betas"
        Return AppConstants.CategoryFortWebsite
    End Function

    Private Shared Function GetTitle(path As String) As String
        ' Use the last segment of the path as the display name when showing results! example: "games/html/rynis-game" -> "Rynis Game"
        Dim trimmed = path.TrimEnd("/"c)
        Dim lastSlash = trimmed.LastIndexOf("/"c)
        Dim slug = If(lastSlash >= 0, trimmed.Substring(lastSlash + 1), trimmed)

        If String.IsNullOrEmpty(slug) Then Return path

        ' Title-case in a single pass with one StringBuilder
        Dim sb As New System.Text.StringBuilder(slug.Length)
        Dim capitalizeNext As Boolean = True
        For i = 0 To slug.Length - 1
            Dim c = slug(i)
            If c = "-"c OrElse c = "_"c Then
                sb.Append(" "c)
                capitalizeNext = True
            ElseIf capitalizeNext Then
                sb.Append(Char.ToUpper(c))
                capitalizeNext = False
            Else
                sb.Append(Char.ToLower(c))
            End If
        Next
        Return sb.ToString()
    End Function

End Class
