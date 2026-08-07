''' <summary>
''' Browse-all-games view: every game URL in the bundled sitemap, grouped A–Z (with a "#"
''' bucket for titles that start with a digit) inside a SemanticZoom, plus an in-page filter.
''' Page-backed rather than an inline panel because MainPage's ContentScrollViewer gives its
''' children an unconstrained vertical measure, and a SemanticZoom needs a bounded height -
''' without one the list would realise every row and the zoomed-out view would have nothing
''' to render into.
''' </summary>
Public NotInheritable Class GamesPage
    Inherits Page

    ''' <summary>
    ''' Bucket for every title that does not start with A–Z. Sorts before the letters.
    ''' </summary>
    Private Const DigitGroupKey As String = "#"

    Private Enum GamesPageState
        Loading
        Content
        Empty
        Failed
    End Enum

    ''' <summary>
    ''' The one collection the CollectionViewSource is bound to. Its identity never changes for
    ''' the life of the page - the filter mutates it in place - so cvs.View is created once and
    ''' both SemanticZoom views' bindings stay live.
    ''' </summary>
    Private ReadOnly _groups As New ObservableCollection(Of GameGroup)()

    Private ReadOnly _viewSource As CollectionViewSource

    ''' <summary>Every game, sorted once on load; the filter only ever reads from this.</summary>
    Private _allGames As IReadOnlyList(Of SearchItem) = Array.Empty(Of SearchItem)()

    ''' <summary>Cancels stale filter work while the user is still typing.</summary>
    Private _filterDebounceCts As Threading.CancellationTokenSource

    ''' <summary>
    ''' Guards the one-time load against UWP firing Loaded more than once, and against the page
    ''' being revisited while cached (NavigationCacheMode.Required).
    ''' </summary>
    Private _dataLoaded As Boolean = False

    ''' <summary>Prevents two overlapping loads if the user hammers Try again.</summary>
    Private _loadInProgress As Boolean = False

    Public Sub New()
        Me.InitializeComponent()

        ' Keep the parsed groups, scroll position and filter text when the user switches to
        ' another nav item and back, instead of rebuilding from scratch each time.
        NavigationCacheMode = NavigationCacheMode.Required

        ' Source is assigned here, AFTER InitializeComponent has applied IsSourceGrouped and
        ' ItemsPath from markup. Assigning Source before those two produces an ungrouped view
        ' with no error - empty headers and an empty jump grid, and nothing to diagnose from.
        _viewSource = DirectCast(Resources("GamesViewSource"), CollectionViewSource)
        _viewSource.Source = _groups
        EnsureItemsSources()

        AddHandler Loaded, AddressOf GamesPage_Loaded
        AddHandler Unloaded, AddressOf GamesPage_Unloaded
    End Sub

    ''' <summary>
    ''' Points both SemanticZoom views at the shared grouped view.
    ''' Done in code rather than as a XAML binding: {Binding Path=View} on a
    ''' CollectionViewSource resolves during InitializeComponent - before Source has been
    ''' assigned, so View is still Nothing - and View does not reliably re-notify afterwards,
    ''' which leaves both views permanently and silently empty.
    ''' Assigning imperatively is safe here precisely because there is nothing to refresh:
    ''' Source is set exactly once and _groups' identity never changes, so this view object
    ''' stays valid for the life of the page while the filter mutates the groups underneath it.
    ''' Idempotent, so it can be called again from Loaded as a safety net.
    ''' </summary>
    Private Sub EnsureItemsSources()
        Dim view = _viewSource.View
        If view Is Nothing Then Return

        If GamesList.ItemsSource Is Nothing Then
            GamesList.ItemsSource = view
        End If
        If GamesJumpGrid.ItemsSource Is Nothing Then
            GamesJumpGrid.ItemsSource = view.CollectionGroups
        End If
    End Sub

    Private Sub GamesPage_Loaded(sender As Object, e As RoutedEventArgs)
        ' If the view was not ready in the constructor, it certainly is by now.
        EnsureItemsSources()

        If _dataLoaded Then Return
        LoadGamesAsync()
    End Sub

    Private Sub GamesPage_Unloaded(sender As Object, e As RoutedEventArgs)
        CancelPendingFilter()
    End Sub

    Private Sub RetryButton_Click(sender As Object, e As RoutedEventArgs)
        LoadGamesAsync()
    End Sub

    ''' <summary>
    ''' Pulls the game subset out of the sitemap, sorts it once, and hands it to the filter.
    ''' Async Sub, so every path is wrapped - an unhandled exception here would crash the app.
    ''' </summary>
    Private Async Sub LoadGamesAsync()
        If _loadInProgress Then Return
        _loadInProgress = True

        Try
            SetState(GamesPageState.Loading)

            Dim games = Await SitemapService.LoadGameItemsAsync()

            Dim sorted As New List(Of SearchItem)(games)
            ' Ordinal so the sort order agrees with GroupKeyFor's A–Z test: a title that lands
            ' in the "#" bucket must also sort before every lettered one, or a group's items
            ' would not be contiguous in the sorted sequence.
            sorted.Sort(Function(left, right) String.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase))
            _allGames = sorted.AsReadOnly()
            _dataLoaded = True

            ApplyFilter(FilterBox.Text)
        Catch ex As Exception
            Debug.WriteLine($"GamesPage: Failed to load games - {ex.Message}")
            SetState(GamesPageState.Failed)
        Finally
            _loadInProgress = False
        End Try
    End Sub

    Private Sub FilterBox_TextChanged(sender As AutoSuggestBox, args As AutoSuggestBoxTextChangedEventArgs)
        If args.Reason <> AutoSuggestionBoxTextChangeReason.UserInput Then Return

        CancelPendingFilter()

        Dim cts As New Threading.CancellationTokenSource()
        _filterDebounceCts = cts
        ApplyFilterDebouncedAsync(sender.Text, cts.Token)
    End Sub

    ''' <summary>
    ''' Debounced so a fast typist does not rebuild every group on each keystroke.
    ''' Async Sub, so everything is wrapped.
    ''' </summary>
    Private Async Sub ApplyFilterDebouncedAsync(query As String, cancellationToken As Threading.CancellationToken)
        Try
            Await Task.Delay(AppConstants.SearchDebounceMilliseconds, cancellationToken)

            If cancellationToken.IsCancellationRequested Then Return
            If Not _dataLoaded Then Return

            ApplyFilter(query)
        Catch ex As OperationCanceledException
            ' Expected while typing quickly.
        Catch ex As Exception
            Debug.WriteLine($"GamesPage: Filter failed - {ex.Message}")
        End Try
    End Sub

    Private Sub ApplyFilter(query As String)
        Dim trimmed = If(query, String.Empty).Trim()

        Dim matches As List(Of SearchItem)
        If trimmed.Length = 0 Then
            matches = New List(Of SearchItem)(_allGames)
        Else
            matches = New List(Of SearchItem)()
            For Each item In _allGames
                If item.Title.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    matches.Add(item)
                End If
            Next
        End If

        RebuildGroups(matches)
        UpdateCountText(matches.Count)

        If _allGames.Count = 0 Then
            SetState(GamesPageState.Failed)
        ElseIf matches.Count = 0 Then
            EmptyText.Text = $"No games match '{trimmed}'"
            SetState(GamesPageState.Empty)
        Else
            SetState(GamesPageState.Content)
        End If
    End Sub

    ''' <summary>
    ''' Replaces the contents of the bound group collection with the groups for
    ''' <paramref name="matches"/>. This is an in-place mutation of the collection the
    ''' CollectionViewSource is bound to - cvs.Source is never reassigned, so cvs.View, and
    ''' therefore both SemanticZoom views' bindings, survive untouched.
    ''' A single Clear() raises one Reset, which both views handle by rebuilding wholesale.
    ''' Removing groups one at a time instead is the fragile path: the jump GridView holds
    ''' ICollectionViewGroup references that can be invalidated mid-transition.
    ''' </summary>
    Private Sub RebuildGroups(matches As IEnumerable(Of SearchItem))
        ' The jump grid points into the *current* grouping, so snap back to the list before the
        ' groups underneath it change - otherwise SemanticZoom can be asked to scroll to a
        ' group that no longer exists.
        If Not GamesZoom.IsZoomedInViewActive Then
            GamesZoom.IsZoomedInViewActive = True
        End If

        _groups.Clear()
        For Each group In BuildGroups(matches)
            _groups.Add(group)
        Next
    End Sub

    ''' <summary>
    ''' Buckets an already-sorted item sequence into alphabetical groups. Uses a dictionary
    ''' rather than a "did the first letter change" walk so a non-contiguous key could never
    ''' produce two groups sharing a header.
    ''' </summary>
    Private Shared Function BuildGroups(items As IEnumerable(Of SearchItem)) As List(Of GameGroup)
        Dim lookup As New Dictionary(Of String, GameGroup)(StringComparer.Ordinal)
        Dim ordered As New List(Of GameGroup)()

        For Each item In items
            Dim key = GroupKeyFor(item.Title)
            Dim group As GameGroup = Nothing
            If Not lookup.TryGetValue(key, group) Then
                group = New GameGroup(key)
                lookup.Add(key, group)
                ordered.Add(group)
            End If
            group.Items.Add(item)
        Next

        ordered.Sort(Function(left, right) CompareGroupKeys(left.Key, right.Key))
        Return ordered
    End Function

    ''' <summary>"#" for digits and anything outside A–Z, otherwise the uppercased first letter.</summary>
    Private Shared Function GroupKeyFor(title As String) As String
        If String.IsNullOrEmpty(title) Then Return DigitGroupKey
        Dim first = Char.ToUpperInvariant(title(0))
        If first >= "A"c AndAlso first <= "Z"c Then Return first.ToString()
        Return DigitGroupKey
    End Function

    ''' <summary>"#" always sorts first; everything else is plain ordinal.</summary>
    Private Shared Function CompareGroupKeys(left As String, right As String) As Integer
        If String.Equals(left, right, StringComparison.Ordinal) Then Return 0
        If String.Equals(left, DigitGroupKey, StringComparison.Ordinal) Then Return -1
        If String.Equals(right, DigitGroupKey, StringComparison.Ordinal) Then Return 1
        Return String.Compare(left, right, StringComparison.Ordinal)
    End Function

    Private Sub UpdateCountText(shownCount As Integer)
        Dim total = _allGames.Count
        If shownCount = total Then
            CountText.Text = If(total = 1, "1 game", $"{total} games")
        Else
            CountText.Text = $"{shownCount} of {total} games"
        End If
    End Sub

    ''' <summary>
    ''' Exactly one of the four states is on screen at a time. The filter box stays usable in
    ''' the Empty state so the user can back out of a filter that matched nothing.
    ''' </summary>
    Private Sub SetState(state As GamesPageState)
        GamesZoom.Visibility = If(state = GamesPageState.Content, Visibility.Visible, Visibility.Collapsed)
        LoadingPanel.Visibility = If(state = GamesPageState.Loading, Visibility.Visible, Visibility.Collapsed)
        EmptyPanel.Visibility = If(state = GamesPageState.Empty, Visibility.Visible, Visibility.Collapsed)
        ErrorPanel.Visibility = If(state = GamesPageState.Failed, Visibility.Visible, Visibility.Collapsed)

        LoadingRing.IsActive = (state = GamesPageState.Loading)
        FilterBox.IsEnabled = (state = GamesPageState.Content OrElse state = GamesPageState.Empty)
    End Sub

    Private Async Sub GamesList_ItemClick(sender As Object, e As ItemClickEventArgs)
        Try
            Dim item = TryCast(e.ClickedItem, SearchItem)
            If item Is Nothing OrElse String.IsNullOrEmpty(item.Url) Then Return

            ' Only http/https is launched - see AppConstants.TryCreateWebUri.
            Await AppConstants.LaunchWebUriAsync(item.Url)
        Catch ex As Exception
            ' Critical: catch exceptions in async void to prevent app crash
            Debug.WriteLine($"GamesPage: Failed to launch game - {ex.Message}")
        End Try
    End Sub

    Private Sub CancelPendingFilter()
        ' Clear the field before disposing so no later caller can reach the dead source.
        Dim cts = _filterDebounceCts
        _filterDebounceCts = Nothing
        If cts Is Nothing Then Return

        Try
            cts.Cancel()
        Catch ex As ObjectDisposedException
            ' Already torn down elsewhere - nothing left to cancel.
        End Try
        cts.Dispose()
    End Sub

End Class
