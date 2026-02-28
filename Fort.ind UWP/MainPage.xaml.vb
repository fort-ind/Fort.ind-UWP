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

    ' All searchable items across menus, settings, profiles, and fort1nd.com
    Private ReadOnly _searchItems As New List(Of SearchItem) From {
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
        Dim sitemapItems = Await SitemapService.LoadSearchItemsAsync()
        _searchItems.AddRange(sitemapItems)
    End Sub

    Private Sub MainPage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Async Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            Sub() UpdateProfileNavItem())
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

        ' Button colors - transparent with subtle hover
        titleBar.ButtonBackgroundColor = Colors.Transparent
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255)
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255)

        ' Button foreground colors
        titleBar.ButtonForegroundColor = Colors.White
        titleBar.ButtonHoverForegroundColor = Colors.White
        titleBar.ButtonPressedForegroundColor = Colors.White
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(128, 255, 255, 255)
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
                If Not TypeOf ContentFrame.Content Is ProfilePage Then
                    ContentFrame.Navigate(GetType(ProfilePage))
                End If
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
        Dim aboutDialog As New ContentDialog()
        aboutDialog.Title = "About"
        aboutDialog.Content = $"Fort.ind desktop for UWP{vbCrLf}Version 0.5 beta{vbCrLf}{vbCrLf}Storage: Local JSON Files"
        aboutDialog.PrimaryButtonText = "OK"
        aboutDialog.DefaultButton = ContentDialogButton.Primary
        aboutDialog.XamlRoot = Me.XamlRoot

        Await aboutDialog.ShowAsync()
    End Sub

    Private Sub RefreshTileButton_Click(sender As Object, e As RoutedEventArgs)
        UpdateLiveTile()
    End Sub

    Private Sub ClearTileButton_Click(sender As Object, e As RoutedEventArgs)
        LiveTileService.ClearTile()
        LiveTileService.ClearBadge()
    End Sub

    Private Async Function ShowWelcomeDialogAsync() As Task
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
    End Function

    Private Async Sub ResetWelcomeButton_Click(sender As Object, e As RoutedEventArgs)
        Dim localSettings = ApplicationData.Current.LocalSettings
        localSettings.Values("HideWelcomeDialog") = False
        Await ShowWelcomeDialogAsync()
    End Sub

    ' ── Search bar handlers ──

    Private Sub NavSearchBox_TextChanged(sender As AutoSuggestBox, args As AutoSuggestBoxTextChangedEventArgs)
        If args.Reason = AutoSuggestionBoxTextChangeReason.UserInput Then
            Dim query = sender.Text.Trim()
            If String.IsNullOrEmpty(query) Then
                sender.ItemsSource = Nothing
                Return
            End If

            ' Add profile-specific item if logged in
            Dim dynamicItems As New List(Of SearchItem)(_searchItems)
            If ProfileService.CurrentUser IsNot Nothing Then
                Dim name = If(String.IsNullOrWhiteSpace(ProfileService.CurrentUser.DisplayName),
                              ProfileService.CurrentUser.Username,
                              ProfileService.CurrentUser.DisplayName)
                dynamicItems.Add(New SearchItem($"Profile: {name}", "Profile", "Profile"))
            End If

            Dim filtered = dynamicItems.Where(
                Function(item) item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                               item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList()

            sender.ItemsSource = filtered
        End If
    End Sub

    Private Async Sub NavSearchBox_QuerySubmitted(sender As AutoSuggestBox, args As AutoSuggestBoxQuerySubmittedEventArgs)
        If args.ChosenSuggestion IsNot Nothing Then
            Dim item = TryCast(args.ChosenSuggestion, SearchItem)
            If item IsNot Nothing Then
                Await NavigateToSearchItem(item)
            End If
        Else
            ' User pressed Enter without picking a suggestion – navigate to first match
            Dim query = args.QueryText.Trim()
            If Not String.IsNullOrEmpty(query) Then
                Dim match = _searchItems.FirstOrDefault(
                    Function(i) i.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                i.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                If match IsNot Nothing Then
                    Await NavigateToSearchItem(match)
                End If
            End If
        End If
    End Sub

    Private Async Sub NavSearchBox_SuggestionChosen(sender As AutoSuggestBox, args As AutoSuggestBoxSuggestionChosenEventArgs)
        Dim item = TryCast(args.SelectedItem, SearchItem)
        If item IsNot Nothing Then
            sender.Text = item.Title
        End If
    End Sub

    Private Async Function NavigateToSearchItem(item As SearchItem) As Task
        If Not String.IsNullOrEmpty(item.Url) Then
            ' External link – open in default browser
            Await Windows.System.Launcher.LaunchUriAsync(New Uri(item.Url))
        ElseIf Not String.IsNullOrEmpty(item.NavigationTag) Then
            ShowPanel(item.NavigationTag)
        End If
    End Function

End Class
