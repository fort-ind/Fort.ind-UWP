' The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

Imports Windows.UI
Imports Windows.UI.ViewManagement
Imports Windows.ApplicationModel.Core
Imports Windows.Storage

''' <summary>
''' An empty page that can be used on its own or navigated to within a Frame.
''' </summary>
Public NotInheritable Class MainPage
    Inherits Page

    ' Static menu/settings items (never changes)
    Private Shared ReadOnly s_staticSearchItems As SearchItem() = {
        New SearchItem("Home", "Menu", "LatestNews"),
        New SearchItem("Latest News", "Menu", "LatestNews"),
        New SearchItem("Games", "Menu", "Games"),
        New SearchItem("Beta Programs", "Menu", "Betas"),
        New SearchItem("Your Profile", "Menu", "Profile"),
        New SearchItem("Social", "Menu", "Social"),
        New SearchItem("About", "Menu", "About"),
        New SearchItem("Settings", "Menu", "Settings"),
        New SearchItem("Data Storage", "Settings", "Settings"),
        New SearchItem("Local JSON Storage", "Settings", "Settings"),
        New SearchItem("Live Tile", "Settings", "Settings"),
        New SearchItem("Refresh Live Tile", "Settings", "Settings"),
        New SearchItem("Clear Live Tile", "Settings", "Settings"),
        New SearchItem("Welcome Dialog", "Settings", "Settings"),
        New SearchItem("Show Welcome Dialog Again", "Settings", "Settings"),
        New SearchItem("Account", "Profile", "Profile"),
        New SearchItem("Login", "Profile", "Profile"),
        New SearchItem("Register", "Profile", "Profile")
    }

    ' All searchable items – volatile reference swapped once when sitemap loads (no lock needed for reads)
    Private _allSearchItems As IReadOnlyList(Of SearchItem) = s_staticSearchItems

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _isDialogOpen As Boolean = False

    Public Sub New()
        Me.InitializeComponent()
        SetupTitleBar()
        UpdateLiveTile()
        UpdateProfileNavItem()
        LoadSitemapItems()

        ' Listen for auth state changes
        AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
        AddHandler Unloaded, AddressOf MainPage_Unloaded
    End Sub

    Private Async Sub LoadSitemapItems()
        Try
            Dim sitemapItems = Await SitemapService.LoadSearchItemsAsync()
            ' Build a new combined list and swap the reference (atomic, no lock needed)
            Dim combined As New List(Of SearchItem)(s_staticSearchItems.Length + sitemapItems.Count)
            combined.AddRange(s_staticSearchItems)
            combined.AddRange(sitemapItems)
            _allSearchItems = combined
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Failed to load sitemap items – {ex.Message}")
        End Try
    End Sub

    Private Sub MainPage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Async Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        Try
            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                Sub() UpdateProfileNavItem())
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Auth state change handler failed – {ex.Message}")
        End Try
    End Sub

    Private Sub UpdateProfileNavItem()
        ' Update profile nav item based on login state
        If ProfileService.CurrentUser IsNot Nothing Then
            ProfileNavItem.Content = ProfileService.CurrentUser.DisplayName
            If String.IsNullOrWhiteSpace(ProfileService.CurrentUser.DisplayName) Then
                ProfileNavItem.Content = ProfileService.CurrentUser.Username
            End If
        Else
            ProfileNavItem.Content = "Your Profile"
        End If
    End Sub

    Private Sub SetupTitleBar()
        ' Extend view into title bar for seamless acrylic
        Dim coreTitleBar = CoreApplication.GetCurrentView().TitleBar
        coreTitleBar.ExtendViewIntoTitleBar = True

        ' Set the draggable title bar region
        Window.Current.SetTitleBar(AppTitleBar)

        ' Make title bar buttons transparent to match acrylic
        Dim titleBar = ApplicationView.GetForCurrentView().TitleBar

        ' Determine colors based on current theme
        Dim isDark = Application.Current.RequestedTheme = ApplicationTheme.Dark
        Dim fgColor = If(isDark, Colors.White, Colors.Black)
        Dim inactiveFg = If(isDark, Color.FromArgb(128, 255, 255, 255), Color.FromArgb(128, 0, 0, 0))
        Dim hoverBg = If(isDark, Color.FromArgb(30, 255, 255, 255), Color.FromArgb(30, 0, 0, 0))
        Dim pressedBg = If(isDark, Color.FromArgb(50, 255, 255, 255), Color.FromArgb(50, 0, 0, 0))

        ' Button colors - transparent with subtle hover
        titleBar.ButtonBackgroundColor = Colors.Transparent
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent
        titleBar.ButtonHoverBackgroundColor = hoverBg
        titleBar.ButtonPressedBackgroundColor = pressedBg

        ' Button foreground colors
        titleBar.ButtonForegroundColor = fgColor
        titleBar.ButtonHoverForegroundColor = fgColor
        titleBar.ButtonPressedForegroundColor = fgColor
        titleBar.ButtonInactiveForegroundColor = inactiveFg
    End Sub

    Private Sub UpdateLiveTile()
        ' Update Live Tile with latest news
        Dim newsItems As New List(Of NewsItem) From {
            New NewsItem("Whats new?", "2026.1 has been released for web go to fort1nd.com to see whats new", "welcome"),
            New NewsItem("Get Started", "Hello! fort.uwp is now ready to use. :3", "features")
        }

        ' Update tile with cycling news
        LiveTileService.UpdateTileWithMultipleNews(newsItems)

        ' Show badge indicating new content
        LiveTileService.UpdateBadgeGlyph("newMessage")
    End Sub

    Private Async Sub NavView_Loaded(sender As Object, e As RoutedEventArgs)
        Try
            ' Select the first item (Latest News) by default
            NavView.SelectedItem = NavView.MenuItems(0)
            ' Ensure pane starts closed
            NavView.IsPaneOpen = False

            ' Show welcome dialog on first launch
            Dim localSettings = ApplicationData.Current.LocalSettings
            Dim hideWelcome As Boolean = False
            If localSettings.Values.ContainsKey("HideWelcomeDialog") Then
                hideWelcome = CBool(localSettings.Values("HideWelcomeDialog"))
            End If
            If Not hideWelcome Then
                Await ShowWelcomeDialogAsync()
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: NavView_Loaded failed – {ex.Message}")
        End Try
    End Sub

    Private Sub NavView_ItemInvoked(sender As NavigationView, args As NavigationViewItemInvokedEventArgs)
        If args.IsSettingsInvoked Then
            ShowPanel("Settings")
        Else
            Dim invokedItem = TryCast(args.InvokedItemContainer, NavigationViewItem)
            If invokedItem IsNot Nothing Then
                Dim tag = If(invokedItem.Tag?.ToString(), "LatestNews")
                ShowPanel(tag)
            End If
        End If

        ' Always close the pane after navigation
        NavView.IsPaneOpen = False
    End Sub

    Private Sub ShowPanel(panelName As String)
        ' Hide all panels and frame
        LatestNewsPanel.Visibility = Visibility.Collapsed
        GamesPanel.Visibility = Visibility.Collapsed
        BetasPanel.Visibility = Visibility.Collapsed
        SocialPanel.Visibility = Visibility.Collapsed
        AboutPanel.Visibility = Visibility.Collapsed
        SettingsPanel.Visibility = Visibility.Collapsed
        ContentFrame.Visibility = Visibility.Collapsed
        ContentScrollViewer.Visibility = Visibility.Visible

        ' Show the selected panel
        Select Case panelName
            Case "LatestNews"
                LatestNewsPanel.Visibility = Visibility.Visible
            Case "Games"
                GamesPanel.Visibility = Visibility.Visible
            Case "Betas"
                BetasPanel.Visibility = Visibility.Visible
            Case "Profile"
                ' Navigate to ProfilePage in the frame (skip if already there)
                ContentScrollViewer.Visibility = Visibility.Collapsed
                ContentFrame.Visibility = Visibility.Visible
                Try
                    If Not TypeOf ContentFrame.Content Is ProfilePage Then
                        ContentFrame.Navigate(GetType(ProfilePage))
                    End If
                Catch ex As Exception
                    ' Navigation failed – fall back to home
                    Debug.WriteLine($"MainPage: Profile navigation failed – {ex.Message}")
                    ContentFrame.Visibility = Visibility.Collapsed
                    ContentScrollViewer.Visibility = Visibility.Visible
                    LatestNewsPanel.Visibility = Visibility.Visible
                End Try
            Case "Social"
                SocialPanel.Visibility = Visibility.Visible
            Case "About"
                AboutPanel.Visibility = Visibility.Visible
            Case "Settings"
                SettingsPanel.Visibility = Visibility.Visible
                UpdateStorageInfo()
            Case Else
                LatestNewsPanel.Visibility = Visibility.Visible
        End Select
    End Sub

    Private Async Sub UpdateStorageInfo()
        Try
            StoragePathText.Text = $"Location: {LocalStorageService.DataPath}"
            Dim userCount = Await LocalStorageService.GetUserCountAsync()
            UserCountText.Text = $"Registered users: {userCount}"
        Catch ex As Exception
            StoragePathText.Text = ""
            UserCountText.Text = ""
        End Try
    End Sub

    Private Async Sub AboutButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _isDialogOpen Then Return
            _isDialogOpen = True

            Dim aboutDialog As New ContentDialog()
            aboutDialog.Title = "About"
            aboutDialog.Content = $"Fort.ind desktop for UWP{vbCrLf}Version 0.5 Beta{vbCrLf}{vbCrLf}Storage: Local JSON Files"
            aboutDialog.PrimaryButtonText = "OK"
            aboutDialog.DefaultButton = ContentDialogButton.Primary
            aboutDialog.XamlRoot = Me.XamlRoot

            Await aboutDialog.ShowAsync()
        Catch ex As Exception
            Debug.WriteLine($"MainPage: About dialog failed – {ex.Message}")
        Finally
            _isDialogOpen = False
        End Try
    End Sub

    Private Sub RefreshTileButton_Click(sender As Object, e As RoutedEventArgs)
        UpdateLiveTile()
    End Sub

    Private Sub ClearTileButton_Click(sender As Object, e As RoutedEventArgs)
        LiveTileService.ClearTile()
        LiveTileService.ClearBadge()
    End Sub

    ' ── Settings row expand/collapse ──

    Private Sub StorageHeader_Tapped(sender As Object, e As TappedRoutedEventArgs)
        ToggleSettingsRow(StorageContent, StorageChevronRotation)
    End Sub

    Private Sub TileHeader_Tapped(sender As Object, e As TappedRoutedEventArgs)
        ToggleSettingsRow(TileContent, TileChevronRotation)
    End Sub

    Private Sub WelcomeHeader_Tapped(sender As Object, e As TappedRoutedEventArgs)
        ToggleSettingsRow(WelcomeContent, WelcomeChevronRotation)
    End Sub

    Private Sub AboutHeader_Tapped(sender As Object, e As TappedRoutedEventArgs)
        ToggleSettingsRow(AboutContent, AboutChevronRotation)
    End Sub

    Private Sub ToggleSettingsRow(content As StackPanel, chevronTransform As RotateTransform)
        If content.Visibility = Visibility.Collapsed Then
            content.Visibility = Visibility.Visible
            chevronTransform.Angle = 90
        Else
            content.Visibility = Visibility.Collapsed
            chevronTransform.Angle = 0
        End If
    End Sub

    Private Sub SettingsRow_PointerEntered(sender As Object, e As PointerRoutedEventArgs)
        Dim grid = TryCast(sender, Grid)
        If grid IsNot Nothing Then
            grid.Opacity = 0.85
        End If
    End Sub

    Private Sub SettingsRow_PointerExited(sender As Object, e As PointerRoutedEventArgs)
        Dim grid = TryCast(sender, Grid)
        If grid IsNot Nothing Then
            grid.Opacity = 1.0
        End If
    End Sub

    Private Async Function ShowWelcomeDialogAsync() As Task
        If _isDialogOpen Then Return
        _isDialogOpen = True

        Try
            Dim dontShowCheckBox As New CheckBox()
            dontShowCheckBox.Content = "Don't show this again"
            dontShowCheckBox.Margin = New Thickness(0, 16, 0, 0)

            Dim contentPanel As New StackPanel()
            contentPanel.Spacing = 12

            ' Icon row
            Dim iconPanel As New StackPanel()
            iconPanel.Orientation = Orientation.Horizontal
            iconPanel.HorizontalAlignment = HorizontalAlignment.Center
            iconPanel.Spacing = 24
            iconPanel.Margin = New Thickness(0, 8, 0, 8)

            Dim starIcon As New FontIcon()
            starIcon.Glyph = ChrW(&HE734)
            starIcon.FontSize = 32

            Dim testTubeIcon As New FontIcon()
            testTubeIcon.Glyph = ChrW(&HE9A1)
            testTubeIcon.FontSize = 32

            Dim webIcon As New FontIcon()
            webIcon.Glyph = ChrW(&HE774)
            webIcon.FontSize = 32

            iconPanel.Children.Add(starIcon)
            iconPanel.Children.Add(testTubeIcon)
            iconPanel.Children.Add(webIcon)

            contentPanel.Children.Add(iconPanel)

            Dim descText As New TextBlock()
            descText.Text = "Welcome to the beta version of fort.desktop, there's still a lot missing right now and some things may be broken. we hope you enjoy the beta as much as we do! "
            descText.TextWrapping = TextWrapping.Wrap
            descText.FontSize = 14
            descText.Opacity = 0.9

            contentPanel.Children.Add(descText)
            contentPanel.Children.Add(dontShowCheckBox)

            Dim welcomeDialog As New ContentDialog()
            welcomeDialog.Title = "Hi :)"
            welcomeDialog.Content = contentPanel
            welcomeDialog.PrimaryButtonText = "got it"
            welcomeDialog.DefaultButton = ContentDialogButton.Primary
            welcomeDialog.XamlRoot = Me.XamlRoot

            Await welcomeDialog.ShowAsync()

            If dontShowCheckBox.IsChecked.GetValueOrDefault(False) Then
                Dim localSettings = ApplicationData.Current.LocalSettings
                localSettings.Values("HideWelcomeDialog") = True
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Welcome dialog failed – {ex.Message}")
        Finally
            _isDialogOpen = False
        End Try
    End Function

    Private Async Sub ResetWelcomeButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim localSettings = ApplicationData.Current.LocalSettings
            localSettings.Values("HideWelcomeDialog") = False
            Await ShowWelcomeDialogAsync()
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Reset welcome failed – {ex.Message}")
        End Try
    End Sub

    ' ── Search bar handlers ──

    Private Sub NavSearchBox_TextChanged(sender As AutoSuggestBox, args As AutoSuggestBoxTextChangedEventArgs)
        If args.Reason = AutoSuggestionBoxTextChangeReason.UserInput Then
            Dim query = sender.Text.Trim()
            If String.IsNullOrEmpty(query) Then
                sender.ItemsSource = Nothing
                Return
            End If

            ' Read volatile reference – no lock or copy needed
            Dim items = _allSearchItems

            Dim filtered As New List(Of SearchItem)()
            For Each item In items
                If item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    filtered.Add(item)
                End If
            Next

            ' Add profile-specific item if logged in and matches
            If ProfileService.CurrentUser IsNot Nothing Then
                Dim name = If(String.IsNullOrWhiteSpace(ProfileService.CurrentUser.DisplayName),
                              ProfileService.CurrentUser.Username,
                              ProfileService.CurrentUser.DisplayName)
                Dim profileTitle = $"Profile: {name}"
                If profileTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   "Profile".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    filtered.Add(New SearchItem(profileTitle, "Profile", "Profile"))
                End If
            End If

            sender.ItemsSource = filtered
        End If
    End Sub

    Private Async Sub NavSearchBox_QuerySubmitted(sender As AutoSuggestBox, args As AutoSuggestBoxQuerySubmittedEventArgs)
        Try
            If args.ChosenSuggestion IsNot Nothing Then
                Dim item = TryCast(args.ChosenSuggestion, SearchItem)
                If item IsNot Nothing Then
                    Await NavigateToSearchItem(item)
                End If
            Else
                ' User pressed Enter without picking a suggestion – navigate to first match
                Dim query = args.QueryText.Trim()
                If Not String.IsNullOrEmpty(query) Then
                    Dim items = _allSearchItems

                    Dim match As SearchItem = Nothing
                    For Each i In items
                        If i.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                           i.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                            match = i
                            Exit For
                        End If
                    Next
                    If match IsNot Nothing Then
                        Await NavigateToSearchItem(match)
                    End If
                End If
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Search query failed – {ex.Message}")
        End Try
    End Sub

    ' Fixed: removed unnecessary Async keyword (no Await in this method)
    Private Sub NavSearchBox_SuggestionChosen(sender As AutoSuggestBox, args As AutoSuggestBoxSuggestionChosenEventArgs)
        Dim item = TryCast(args.SelectedItem, SearchItem)
        If item IsNot Nothing Then
            sender.Text = item.Title
        End If
    End Sub

    Private Async Function NavigateToSearchItem(item As SearchItem) As Task
        If Not String.IsNullOrEmpty(item.Url) Then
            ' Validate URL before launching to avoid UriFormatException
            Dim uri As Uri = Nothing
            If Uri.TryCreate(item.Url, UriKind.Absolute, uri) Then
                Await Windows.System.Launcher.LaunchUriAsync(uri)
            Else
                Debug.WriteLine($"MainPage: Invalid URL in search item – {item.Url}")
            End If
        ElseIf Not String.IsNullOrEmpty(item.NavigationTag) Then
            ShowPanel(item.NavigationTag)
        End If
    End Function

End Class
