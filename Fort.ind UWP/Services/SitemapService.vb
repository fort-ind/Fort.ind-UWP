Imports System.Xml.Linq
Imports Windows.Storage

''' <summary>
''' Parses the bundled sitemap.xml and produces SearchItem entries for every URL
''' </summary>
Public Class SitemapService

    ' The sitemap ships inside the app package and the URL cache is already revalidated on a
    ' 24h TTL, so re-reading and re-parsing it for every caller is pure waste. Reference swap
    ' only, exactly like MainPage._allSearchItems: readers never observe a half-built list.
    Private Shared s_allItems As IReadOnlyList(Of SearchItem)
    Private Shared s_gameItems As IReadOnlyList(Of SearchItem)

    ' Serialises the first parse so two callers racing at startup - MainPage's constructor
    ' calls LoadSitemapItems, and GamesPage loads as soon as it is navigated to - cannot both
    ' hit the file system and both parse the XML.
    Private Shared ReadOnly s_loadGate As New Threading.SemaphoreSlim(1, 1)

    ''' <summary>
    ''' Reads sitemap.xml from the app package and returns SearchItem objects allowing for the
    ''' latest URLs to be searchable. Parsed at most once per process; an empty result (missing
    ''' file, malformed XML) is deliberately NOT memoized so a later caller retries.
    ''' </summary>
    Public Shared Async Function LoadSearchItemsAsync() As Task(Of IReadOnlyList(Of SearchItem))
        Dim cached = s_allItems
        If cached IsNot Nothing Then Return cached

        Await s_loadGate.WaitAsync()
        Try
            ' Re-check: a racing caller may have finished while we waited on the gate.
            If s_allItems IsNot Nothing Then Return s_allItems

            Dim parsed = Await ParseSitemapAsync()
            Dim result = parsed.AsReadOnly()
            If parsed.Count > 0 Then
                s_allItems = result
            End If
            Return result
        Finally
            s_loadGate.Release()
        End Try
    End Function

    ''' <summary>
    ''' The game subset of the sitemap, in sitemap order. Memoized separately so the Games page
    ''' does not re-filter every item each time it is shown.
    ''' </summary>
    Public Shared Async Function LoadGameItemsAsync() As Task(Of IReadOnlyList(Of SearchItem))
        Dim cachedGames = s_gameItems
        If cachedGames IsNot Nothing Then Return cachedGames

        ' Deliberately NOT inside s_loadGate: LoadSearchItemsAsync takes that gate and
        ' SemaphoreSlim is not reentrant, so taking it here would deadlock silently. Two
        ' callers racing through here just each build an equivalent list from the same
        ' memoized source and one of the two identical results wins - harmless.
        Dim all = Await LoadSearchItemsAsync()

        Dim games As New List(Of SearchItem)()
        For Each item In all
            If item.Category IsNot Nothing AndAlso
               item.Category.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal) Then
                games.Add(item)
            End If
        Next

        Dim result = games.AsReadOnly()
        If all.Count > 0 Then
            s_gameItems = result
        End If
        Return result
    End Function

    ''' <summary>
    ''' Parses the packaged sitemap (or the URL cache written from it) into SearchItems.
    ''' Callers go through LoadSearchItemsAsync, which memoizes this.
    ''' </summary>
    Private Shared Async Function ParseSitemapAsync() As Task(Of List(Of SearchItem))
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

                urlsToCache.Add(urlValue)

                Dim item = CreateSearchItemFromUrl(urlValue)
                If item IsNot Nothing Then
                    items.Add(item)
                End If
            Next

            If urlsToCache.Count > 0 Then
                Await SaveCachedUrlsAsync(urlsToCache)
            End If
        Catch ex As Exception
            Debug.WriteLine($"SitemapService: failed to load sitemap – {ex.Message}")
        End Try

        Return items
    End Function

    ''' <summary>
    ''' Creates a SearchItem instance from a URL string, or returns Nothing if the URL
    ''' is invalid or should be skipped (e.g. utility pages like 404).
    ''' </summary>
    ''' <param name="urlValue">The absolute URL string.</param>
    Private Shared Function CreateSearchItemFromUrl(urlValue As String) As SearchItem
        If String.IsNullOrWhiteSpace(urlValue) Then
            Return Nothing
        End If

        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(urlValue, UriKind.Absolute, uri) Then
            Return Nothing
        End If

        Dim path = uri.AbsolutePath.Trim("/"c)
        If String.IsNullOrEmpty(path) Then
            Return New SearchItem("Home", AppConstants.CategoryFortWebsite, Nothing, urlValue)
        End If

        ' Skip utility pages
        If path = "404" Then
            Return Nothing
        End If

        Dim category = GetCategory(path)
        Dim title = GetTitle(path)
        Return New SearchItem(title, category, Nothing, urlValue)
    End Function

    Private Shared Function BuildSearchItemsFromUrls(urls As IEnumerable(Of String)) As List(Of SearchItem)
        Dim items As New List(Of SearchItem)()

        For Each urlValue In urls
            Dim item = CreateSearchItemFromUrl(urlValue)
            If item IsNot Nothing Then
                items.Add(item)
            End If
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
            Catch ex As FormatException
                Return Nothing
            Catch ex As InvalidCastException
                Return Nothing
            Catch ex As OverflowException
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
        Catch ex As Exception
            Debug.WriteLine($"SitemapService: failed to load sitemap cache – {ex.Message}")
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

    ''' <summary>
    ''' Slug tokens that are acronyms rather than words - plain title-casing turns them into
    ''' "Cs" and "Fnaf", which reads wrong. Deliberately conservative: only tokens that are
    ''' never an ordinary English word, so a regenerated sitemap cannot trip it. Note "us"
    ''' (as in "amoung-us") is intentionally absent.
    ''' </summary>
    Private Shared ReadOnly s_upperCaseTokens As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "cs", "css", "dbz", "fnaf", "gba", "gbc", "gta", "hd", "html", "mlb", "mlg",
        "n64", "nba", "nds", "nes", "nfl", "nhl", "psp", "snes", "tmnt", "tv", "ufc",
        "ufo", "wwe"
    }

    ''' <summary>
    ''' Tokens that are the tail of a domain-style name - "diep-io" is diep.io, not "Diep Io".
    ''' Glued onto the preceding token with a dot and left lowercase.
    ''' </summary>
    Private Shared ReadOnly s_domainSuffixTokens As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "com", "gg", "io", "lol", "net", "org"
    }

    ''' <summary>
    ''' Turns the last path segment into a display name: "games/html/rynis-game" -> "Rynis Game".
    ''' Beyond plain title-casing, two generic rules clean up the shapes that show up in game
    ''' slugs - domain suffixes are re-glued ("diep-io" -> "Diep.io") and runs of numeric tokens
    ''' are treated as a version rather than separate words ("minecraft-1-8-8-fixed" ->
    ''' "Minecraft 1.8.8 Fixed"). Both are rules rather than a lookup table, so a regenerated or
    ''' extended sitemap gets the same treatment with nothing to maintain.
    ''' </summary>
    Private Shared Function GetTitle(path As String) As String
        Dim trimmed = path.TrimEnd("/"c)
        Dim lastSlash = trimmed.LastIndexOf("/"c)
        Dim slug = If(lastSlash >= 0, trimmed.Substring(lastSlash + 1), trimmed)

        If String.IsNullOrEmpty(slug) Then Return path

        Dim tokens = slug.Split({"-"c, "_"c}, StringSplitOptions.RemoveEmptyEntries)
        If tokens.Length = 0 Then Return path

        Dim sb As New System.Text.StringBuilder(slug.Length)
        For i = 0 To tokens.Length - 1
            Dim token = tokens(i)

            ' A domain suffix never starts a name, so index 0 is always an ordinary word.
            If i > 0 AndAlso s_domainSuffixTokens.Contains(token) Then
                sb.Append("."c)
                sb.Append(token.ToLowerInvariant())
                Continue For
            End If

            ' Two numbers in a row are a version ("1", "6" -> "1.6"), not two words. A number
            ' after a word still gets a space, so "2048 Cupcakes" and "FNAF 2" are unaffected.
            If i > 0 AndAlso IsAllDigits(token) AndAlso IsAllDigits(tokens(i - 1)) Then
                sb.Append("."c)
                sb.Append(token)
                Continue For
            End If

            If i > 0 Then sb.Append(" "c)
            sb.Append(FormatToken(token))
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Upper-cases a known acronym, otherwise title-cases the token. Invariant casing
    ''' throughout - these are URL slugs, not user text.
    ''' </summary>
    Private Shared Function FormatToken(token As String) As String
        If s_upperCaseTokens.Contains(token) Then Return token.ToUpperInvariant()

        Dim sb As New System.Text.StringBuilder(token.Length)
        sb.Append(Char.ToUpperInvariant(token(0)))
        For i = 1 To token.Length - 1
            sb.Append(Char.ToLowerInvariant(token(i)))
        Next
        Return sb.ToString()
    End Function

    Private Shared Function IsAllDigits(token As String) As Boolean
        If String.IsNullOrEmpty(token) Then Return False
        For i = 0 To token.Length - 1
            If Not Char.IsDigit(token(i)) Then Return False
        Next
        Return True
    End Function

End Class
