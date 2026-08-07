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
        New SearchItem("Home", AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
        New SearchItem("Latest News", AppConstants.CategoryMenu, AppConstants.NavigationLatestNews),
        New SearchItem("Games", AppConstants.CategoryMenu, AppConstants.NavigationGames),
        New SearchItem("Beta Programs", AppConstants.CategoryMenu, AppConstants.NavigationBetas),
        New SearchItem("Your Profile", AppConstants.CategoryMenu, AppConstants.NavigationProfile),
        New SearchItem("Social", AppConstants.CategoryMenu, AppConstants.NavigationSocial),
        New SearchItem("Settings", AppConstants.CategoryMenu, AppConstants.NavigationSettings),
        New SearchItem("Data Storage", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Local JSON Storage", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Refresh Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Clear Live Tile", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Welcome Dialog", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Show Welcome Dialog Again", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Appearance", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Theme", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Dark Mode", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Light Mode", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Background Color", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Background Tint", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Custom Background Tint", AppConstants.CategorySettings, AppConstants.NavigationSettings),
        New SearchItem("Account", AppConstants.CategoryProfile, AppConstants.NavigationProfile),
        New SearchItem("Sign In", AppConstants.CategoryProfile, AppConstants.NavigationProfile)
    }

    ' All searchable items – volatile reference swapped once when sitemap loads (no lock needed for reads)
    Private _allSearchItems As IReadOnlyList(Of SearchItem) = s_staticSearchItems

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _dialogSemaphore As New Threading.SemaphoreSlim(1, 1)

    ' Guard to suppress appearance control event handlers during settings load
    Private _loadingSettings As Boolean = False

    ' Cancels stale search work while the user is still typing.
    Private _searchDebounceCts As Threading.CancellationTokenSource

    ' Tracks whether the CoreWindow KeyDown handler is attached, so repeated Loaded/Unloaded
    ' cycles (which UWP can fire more than once) don't attach it multiple times.
    Private _keyHandlerAttached As Boolean = False

    ' Tracks whether the AuthStateChanged handler is attached, for the same reason - and so
    ' the handler is reliably reattached after an Unloaded/Loaded pair instead of leaving
    ' this page permanently deaf to sign-in/out.
    Private _authHandlerAttached As Boolean = False

    ' Guards NavView_Loaded's one-time startup initialization (selecting Home, closing the
    ' pane, showing the welcome dialog) against UWP firing Loaded more than once - without
    ' this, a second firing would silently snap the user back to the Home tab and re-close
    ' the pane no matter what they were looking at.
    Private _navViewInitialized As Boolean = False

    ' Light-mode equivalents for each dark preset tint color. Custom colors aren't in here -
    ' they fall back to LightenForLightTheme(), which computes an approximation of the same
    ' pastel treatment these hand-picked values use.
    Private Shared ReadOnly s_lightTintMap As New Dictionary(Of String, String) From {
        {"#1E3A5F", "#C8E0F5"},
        {"#2D1B69", "#DDD0F5"},
        {"#0F3D2E", "#C5E8D5"},
        {"#3D1515", "#F5CECE"},
        {"#1A1A2E", "#D0D0EA"},
        {"#0E3A3A", "#C5E8E8"},
        {"#3D2A0F", "#F5E3C0"},
        {"#3D1533", "#F5CEE9"},
        {"#2E3D0F", "#DEEBC0"},
        {"#232323", "#DCDCDC"}
    }

    Public Sub New()
        Me.InitializeComponent()
        AboutVersionText.Text = $"Version {AppConstants.AppVersionDisplay}"
        SetupTitleBar()
        UpdateProfileNavItem()
        LoadSitemapItems()
        LoadAppearanceSettings()

        ' Building the adaptive tile payloads and pushing them to the shell is off the critical
        ' path for showing the window, so it runs at Low priority - after layout and first
        ' render - rather than inline in the constructor.
        Dim ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                          Sub() UpdateLiveTile())

        AddHandler Unloaded, AddressOf MainPage_Unloaded
        AddHandler Loaded, AddressOf MainPage_Loaded
    End Sub

    Private Sub MainPage_Loaded(sender As Object, e As RoutedEventArgs)
        ' Attach keyboard handler only when loaded to prevent memory leaks.
        ' Guard against Loaded firing more than once, which would attach duplicate
        ' handlers and run the Ctrl+F / Escape logic multiple times per keypress.
        If Not _keyHandlerAttached Then
            AddHandler Window.Current.CoreWindow.KeyDown, AddressOf OnCoreKeyDown
            _keyHandlerAttached = True
        End If

        If Not _authHandlerAttached Then
            AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
            _authHandlerAttached = True
        End If

        ' Re-read the current user *after* the handler is attached. Session restore now runs
        ' in the background (App.RestoreSessionInBackground), so it can finish either side of
        ' this point - if it landed before the handler existed, its AuthStateChanged was
        ' raised into the void and only this call puts the account name on the nav item.
        UpdateProfileNavItem()
    End Sub

    ''' <summary>
    ''' Loads the sitemap into the search index. Called from the constructor, so it starts on
    ''' the UI thread and every continuation resumes there too - the indicator is therefore
    ''' touched directly rather than through Dispatcher.RunAsync, which only added two queued
    ''' round-trips to reach the thread we were already on.
    ''' </summary>
    Private Async Sub LoadSitemapItems()
        Try
            SetSitemapLoadingIndicator(True)

            Dim sitemapItems = Await SitemapService.LoadSearchItemsAsync()
            ' Build a new combined list and swap the reference (atomic, no lock needed)
            Dim combined As New List(Of SearchItem)(s_staticSearchItems.Length + sitemapItems.Count)
            combined.AddRange(s_staticSearchItems)
            combined.AddRange(sitemapItems)
            _allSearchItems = combined
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Failed to load sitemap items – {ex.Message}")
        Finally
            SetSitemapLoadingIndicator(False)
        End Try
    End Sub

    Private Sub SetSitemapLoadingIndicator(active As Boolean)
        If LoadingIndicator Is Nothing Then Return
        LoadingIndicator.IsActive = active
        LoadingIndicator.Visibility = If(active, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub MainPage_Unloaded(sender As Object, e As RoutedEventArgs)
        If _authHandlerAttached Then
            RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
            _authHandlerAttached = False
        End If

        ' Remove keyboard handler to prevent memory leaks
        If _keyHandlerAttached Then
            Try
                RemoveHandler Window.Current.CoreWindow.KeyDown, AddressOf OnCoreKeyDown
            Catch ex As Exception
                Debug.WriteLine($"MainPage: Failed to remove KeyDown handler - {ex.Message}")
            End Try
            _keyHandlerAttached = False
        End If

        CancelPendingSearch()
    End Sub

    Private Async Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        Try
            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                Sub()
                    Try
                        UpdateProfileNavItem()
                    Catch ex As Exception
                        Debug.WriteLine($"MainPage: UpdateProfileNavItem failed - {ex.Message}")
                    End Try
                End Sub)
        Catch ex As Exception
            ' Critical: Catch exceptions in async void to prevent app crash
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
        UpdateTitleBarColors()
    End Sub

    Private Sub UpdateTitleBarColors()
        Dim titleBar = ApplicationView.GetForCurrentView().TitleBar

        Dim isDark = IsEffectiveThemeDark()

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
        Try
            ' Update Live Tile with latest news
            Dim newsItems As New List(Of NewsItem) From {
                New NewsItem("What's new?", "2026.7 has been released for web go to fort1nd.com to see whats new", "welcome"),
                New NewsItem("Get Started", "Hello! fort.uwp is now ready to use. :3", "features")
            }

            ' Update tile with cycling news
            LiveTileService.UpdateTileWithMultipleNews(newsItems)

            ' Show badge indicating new content
            LiveTileService.UpdateBadgeGlyph("newMessage")
        Catch ex As Exception
            Debug.WriteLine($"MainPage: UpdateLiveTile failed – {ex.Message}")
        End Try
    End Sub

    Private Async Sub NavView_Loaded(sender As Object, e As RoutedEventArgs)
        If _navViewInitialized Then Return
        _navViewInitialized = True

        Try
            ' Select the first item (Latest News) by default
            If NavView.MenuItems.Count > 0 Then
                NavView.SelectedItem = NavView.MenuItems(0)
            End If

            ' Assigning SelectedItem raises SelectionChanged, not ItemInvoked, so the
            ' initial view has to be set up by hand. Without this the header is never
            ' given a title, and a null header collapses the header row entirely -
            ' leaving the floating pane toggle button to overlap the content.
            ShowContent(AppConstants.NavigationLatestNews)

            ' Ensure pane starts closed
            ClosePaneUnlessExpanded()

            ' DisplayModeChanged does not fire for the mode the control starts in.
            UpdateContentPadding(NavView.DisplayMode)

            ' Clear the badge now that the user has opened the app
            LiveTileService.ClearBadge()

            ' Show welcome dialog on first launch
            Dim localSettings = ApplicationData.Current.LocalSettings
            Dim hideWelcome As Boolean = False
            If localSettings.Values.ContainsKey(AppConstants.SettingHideWelcomeDialog) Then
                hideWelcome = CBool(localSettings.Values(AppConstants.SettingHideWelcomeDialog))
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
            ShowContent(AppConstants.NavigationSettings)
        Else
            Dim invokedItem = TryCast(args.InvokedItemContainer, NavigationViewItem)
            If invokedItem IsNot Nothing Then
                Dim tag = If(invokedItem.Tag?.ToString(), AppConstants.NavigationLatestNews)
                ShowContent(tag)
            End If
        End If

        ' Close the pane after navigation
        ClosePaneUnlessExpanded()
    End Sub

    ''' <summary>
    ''' Closes the navigation pane, except in Expanded display mode where the pane is
    ''' docked beside the content rather than overlaying it, and is meant to stay open.
    ''' </summary>
    Private Sub ClosePaneUnlessExpanded()
        If NavView.DisplayMode <> NavigationViewDisplayMode.Expanded Then
            NavView.IsPaneOpen = False
        End If
    End Sub

    ''' <summary>
    ''' Page title for a panel, shown in the NavigationView header.
    ''' </summary>
    Private Shared Function HeaderFor(panelName As String) As String
        Select Case panelName
            Case AppConstants.NavigationGames
                Return "Games"
            Case AppConstants.NavigationBetas
                Return "Beta Programs"
            Case AppConstants.NavigationProfile
                Return "Your Profile"
            Case AppConstants.NavigationSocial
                Return "Social"
            Case AppConstants.NavigationSettings
                Return "Settings"
            Case Else
                ' Home and unknown tags all show the Home panel.
                Return "Welcome to Fort.ind"
        End Select
    End Function

    Private Sub NavView_DisplayModeChanged(sender As NavigationView, args As NavigationViewDisplayModeChangedEventArgs)
        UpdateContentPadding(args.DisplayMode)
    End Sub

    ''' <summary>
    ''' Minimal mode leaves less room for content, so it uses the tighter 12px margin the
    ''' design guidance recommends; Compact and Expanded get the standard 24px.
    ''' </summary>
    Private Sub UpdateContentPadding(mode As NavigationViewDisplayMode)
        Dim inset As Double = If(mode = NavigationViewDisplayMode.Minimal, 12, 24)
        ContentPanel.Padding = New Thickness(inset)
    End Sub

    ''' <summary>
    ''' Central content switch. This is the single place that owns the decision between the
    ''' app's two content hosts: page-backed views (Profile and Games) navigate the
    ''' ContentFrame, while every other view is a lightweight inline panel shown in the
    ''' ContentScrollViewer. Adding a new page-backed view is a one-line Case here.
    ''' </summary>
    Private Sub ShowContent(tag As String)
        NavView.Header = HeaderFor(tag)

        Select Case tag
            Case AppConstants.NavigationProfile
                ' The one page-backed view: hosted in the Frame, not as an inline panel.
                ShowProfilePage()
            Case AppConstants.NavigationGames
                ' Second page-backed view: the grouped list needs a bounded height, which the
                ' inline ContentScrollViewer cannot give it.
                ShowGamesPage()
            Case AppConstants.NavigationBetas
                ShowInlinePanel(BetasPanel)
            Case AppConstants.NavigationSocial
                ShowInlinePanel(SocialPanel)
            Case AppConstants.NavigationSettings
                ShowInlinePanel(SettingsPanel)
                UpdateStorageInfo()
            Case Else
                ' Home, Latest News, and any unknown tag fall back to the Home panel.
                ShowInlinePanel(LatestNewsPanel)
        End Select
    End Sub

    ''' <summary>
    ''' Shows the inline content host (the ScrollViewer) and makes exactly one panel visible.
    ''' Collapses the Frame so the two hosts are never on screen at the same time.
    ''' </summary>
    Private Sub ShowInlinePanel(panel As UIElement)
        ContentFrame.Visibility = Visibility.Collapsed
        ContentScrollViewer.Visibility = Visibility.Visible

        LatestNewsPanel.Visibility = If(panel Is LatestNewsPanel, Visibility.Visible, Visibility.Collapsed)
        BetasPanel.Visibility = If(panel Is BetasPanel, Visibility.Visible, Visibility.Collapsed)
        SocialPanel.Visibility = If(panel Is SocialPanel, Visibility.Visible, Visibility.Collapsed)
        SettingsPanel.Visibility = If(panel Is SettingsPanel, Visibility.Visible, Visibility.Collapsed)
    End Sub

    ''' <summary>
    ''' Shows the Frame content host and navigates it to ProfilePage (or refreshes the UI if
    ''' it is already there). Falls back to the Home panel if navigation fails.
    ''' </summary>
    Private Sub ShowProfilePage()
        ContentScrollViewer.Visibility = Visibility.Collapsed
        ContentFrame.Visibility = Visibility.Visible
        Try
            If ContentFrame IsNot Nothing Then
                If TypeOf ContentFrame.Content Is ProfilePage Then
                    ' Already on profile page – refresh the UI instead of re-navigating
                    DirectCast(ContentFrame.Content, ProfilePage).RefreshUI()
                Else
                    ContentFrame.Navigate(GetType(ProfilePage))
                    TrimContentBackStack()
                End If
            End If
        Catch ex As Exception
            ' Navigation failed – fall back to home
            Debug.WriteLine($"MainPage: Profile navigation failed – {ex.Message}")
            NavView.Header = HeaderFor(AppConstants.NavigationLatestNews)
            ShowInlinePanel(LatestNewsPanel)
        End Try
    End Sub

    ''' <summary>
    ''' Shows the Frame content host and navigates it to GamesPage. The page sets
    ''' NavigationCacheMode.Required and owns its own loading/empty/error states, so re-entering
    ''' it is a no-op rather than a reload. Falls back to the Home panel if navigation fails.
    ''' </summary>
    Private Sub ShowGamesPage()
        ContentScrollViewer.Visibility = Visibility.Collapsed
        ContentFrame.Visibility = Visibility.Visible
        Try
            If ContentFrame IsNot Nothing Then
                If Not (TypeOf ContentFrame.Content Is GamesPage) Then
                    ContentFrame.Navigate(GetType(GamesPage))
                    TrimContentBackStack()
                End If
            End If
        Catch ex As Exception
            ' Navigation failed – fall back to home
            Debug.WriteLine($"MainPage: Games navigation failed – {ex.Message}")
            NavView.Header = HeaderFor(AppConstants.NavigationLatestNews)
            ShowInlinePanel(LatestNewsPanel)
        End Try
    End Sub

    ''' <summary>
    ''' Drops everything but the immediately previous entry from the content Frame's back stack.
    '''
    ''' Switching between the Profile and Games nav items pushes a PageStackEntry every time,
    ''' and nothing ever popped them - a session spent flipping between the two grew the stack
    ''' (and the strong references it holds) without bound. One entry is kept because LoginPage
    ''' relies on Frame.GoBack to return to ProfilePage after signing in.
    ''' </summary>
    Private Sub TrimContentBackStack()
        Try
            Dim stack = ContentFrame.BackStack
            While stack.Count > 1
                stack.RemoveAt(0)
            End While
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Failed to trim content back stack - {ex.Message}")
        End Try
    End Sub

    Private Sub UpdateStorageInfo()
        Try
            StoragePathText.Text = $"Location: {LocalStorageService.DataPath}"

            Dim user = ProfileService.CurrentUser
            If user IsNot Nothing Then
                CacheDescriptionText.Text = "You are logged in using fort.social. We cached your login details so we can quickly log you in :p"
                UserCountText.Text = $"Signed in as @{user.Username}@{If(String.IsNullOrWhiteSpace(user.Host), MisskeyAuthService.InstanceHost, user.Host)}"
                ClearLoginInfoButton.Visibility = Visibility.Visible
            Else
                CacheDescriptionText.Text = "Not signed in... why dont you go do that?"
                UserCountText.Text = ""
                ClearLoginInfoButton.Visibility = Visibility.Collapsed
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: UpdateStorageInfo failed - {ex.Message}")
            StoragePathText.Text = ""
            CacheDescriptionText.Text = ""
            UserCountText.Text = ""
            ClearLoginInfoButton.Visibility = Visibility.Collapsed
        End Try
    End Sub

    Private Async Sub ClearLoginInfoButton_Click(sender As Object, e As RoutedEventArgs)
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

        Try
            Dim dialog As New ContentDialog()
            dialog.Title = "remove your account"
            dialog.Content = "this will remove the login data for your fort.social account, beware! this does not deauthorize your account from fort.social go to your profile > service integration and unlink your account from there if you dont want to use this account in fort.desktop"
            dialog.PrimaryButtonText = "Clear"
            dialog.CloseButtonText = "Cancel"
            dialog.DefaultButton = ContentDialogButton.Close
            AppConstants.ApplyXamlRoot(dialog, Me)

            Dim result = Await dialog.ShowAsync()

            If result = ContentDialogResult.Primary Then
                Await ProfileService.LogoutAsync()
                UpdateStorageInfo()
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Clear login info dialog failed – {ex.Message}")
        Finally
            _dialogSemaphore.Release()
        End Try
    End Sub

    ''' <summary>
    ''' Wipes all local app data. Destructive and irreversible, so it's gated behind two
    ''' separate confirmations rather than one - the first explains what will happen, the
    ''' second is a final "are you sure" with no way to back out afterward. Once the wipe is
    ''' done, offers to restart the app immediately (via CoreApplication.RequestRestartAsync)
    ''' since a handful of things - the appearance settings just re-read from LocalSettings
    ''' during this same session, but anything cached only in memory elsewhere - are only
    ''' guaranteed consistent after a fresh process start.
    ''' </summary>
    Private Async Sub ResetAppButton_Click(sender As Object, e As RoutedEventArgs)
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

        Try
            Dim explainDialog As New ContentDialog()
            explainDialog.Title = "Reset fort.desktop"
            explainDialog.Content = "This signs you out and deletes everything the app has saved locally - your cached profile, the sitemap cache, and all preferences (theme, tint color, panel states). It resets the app to a fresh install. This does not affect your fort.social account."
            explainDialog.PrimaryButtonText = "Continue"
            explainDialog.CloseButtonText = "Cancel"
            explainDialog.DefaultButton = ContentDialogButton.Close
            AppConstants.ApplyXamlRoot(explainDialog, Me)

            If Await explainDialog.ShowAsync() <> ContentDialogResult.Primary Then Return

            Dim confirmDialog As New ContentDialog()
            confirmDialog.Title = "Are you absolutely sure?"
            confirmDialog.Content = "This is permanent - everything fort.desktop has saved will be deleted and cannot be recovered."
            confirmDialog.PrimaryButtonText = "Yes, Reset Everything"
            confirmDialog.CloseButtonText = "Cancel"
            confirmDialog.DefaultButton = ContentDialogButton.Close
            AppConstants.ApplyXamlRoot(confirmDialog, Me)

            If Await confirmDialog.ShowAsync() <> ContentDialogResult.Primary Then Return

            Await ProfileService.ResetAppDataAsync()
            LoadAppearanceSettings()
            UpdateStorageInfo()

            Dim restartDialog As New ContentDialog()
            restartDialog.Title = "Reset complete"
            restartDialog.Content = "fort.desktop has been reset to a fresh install. Restart the app now for everything to take full effect."
            restartDialog.PrimaryButtonText = "Restart Now"
            restartDialog.CloseButtonText = "Later"
            restartDialog.DefaultButton = ContentDialogButton.Primary
            AppConstants.ApplyXamlRoot(restartDialog, Me)

            If Await restartDialog.ShowAsync() = ContentDialogResult.Primary Then
                Await RequestAppRestartAsync()
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Reset app dialog failed – {ex.Message}")
        Finally
            _dialogSemaphore.Release()
        End Try
    End Sub

    ''' <summary>
    ''' Asks the OS to terminate and relaunch this app. On success the process is torn down
    ''' before this call returns, so any code after the Await here only runs if the restart
    ''' could NOT be started (e.g. the app isn't in the foreground) - in which case we just
    ''' leave the (already-reset) app running and let the user restart it manually.
    ''' </summary>
    Private Async Function RequestAppRestartAsync() As Task
        Try
            Dim failureReason = Await Windows.ApplicationModel.Core.CoreApplication.RequestRestartAsync("")
            Debug.WriteLine($"MainPage: App restart request did not restart the app - {failureReason}")
        Catch ex As Exception
            Debug.WriteLine($"MainPage: App restart request threw - {ex.Message}")
        End Try
    End Function

    Private Sub RefreshTileButton_Click(sender As Object, e As RoutedEventArgs)
        UpdateLiveTile()
    End Sub

    Private Sub ClearTileButton_Click(sender As Object, e As RoutedEventArgs)
        LiveTileService.ClearTile()
        LiveTileService.ClearBadge()
    End Sub

    ' ── Appearance settings ──

    Private Sub LoadAppearanceSettings()
        _loadingSettings = True
        Try
            Dim localSettings = ApplicationData.Current.LocalSettings

            ' Restore theme selection
            Dim theme As String = AppConstants.ThemeDefault
            If localSettings.Values.ContainsKey(AppConstants.SettingAppTheme) Then
                theme = localSettings.Values(AppConstants.SettingAppTheme).ToString()
            End If
            Select Case theme
                Case AppConstants.ThemeLight : ThemeLightRadio.IsChecked = True
                Case AppConstants.ThemeDark : ThemeDarkRadio.IsChecked = True
                Case Else : ThemeSystemRadio.IsChecked = True
            End Select
            ApplyTheme(theme)

            ' Restore tint color selection
            Dim tintTag As String = AppConstants.ThemeDefault
            If localSettings.Values.ContainsKey(AppConstants.SettingAppTintColor) Then
                tintTag = localSettings.Values(AppConstants.SettingAppTintColor).ToString()
            End If
            ' Reset the custom swatch to its palette glyph first - this runs again after an app
            ' reset, and a stale color chip would imply a custom tint that no longer exists.
            TintCustomButton.ClearValue(Control.BackgroundProperty)
            TintCustomIcon.Visibility = Visibility.Visible

            ApplyTintColor(tintTag)
            UpdateTintSelection(tintTag)

            ' Keep the last custom pick visible on its swatch even while a preset is active.
            ' The glyph still being visible means UpdateTintSelection didn't paint a chip.
            Dim rememberedCustom = TryCast(localSettings.Values(AppConstants.SettingAppCustomTintColor), String)
            If TintCustomIcon.Visibility = Visibility.Visible AndAlso Not String.IsNullOrEmpty(rememberedCustom) Then
                ShowCustomSwatchColor(rememberedCustom)
            End If

            ' Restore settings panel states
            RestoreSettingsPanelStates()
        Finally
            _loadingSettings = False
        End Try
    End Sub

    Private Sub ApplyTheme(theme As String)
        Dim rootFrame = TryCast(Window.Current.Content, Frame)
        If rootFrame Is Nothing Then Return
        Select Case theme
            Case AppConstants.ThemeLight : rootFrame.RequestedTheme = ElementTheme.Light
            Case AppConstants.ThemeDark : rootFrame.RequestedTheme = ElementTheme.Dark
            Case Else : rootFrame.RequestedTheme = ElementTheme.Default
        End Select
        If Not _loadingSettings Then
            ApplicationData.Current.LocalSettings.Values(AppConstants.SettingAppTheme) = theme
        End If
        UpdateTitleBarColors()
        ' Re-apply tint so the correct light/dark shade is used for the new theme
        If Not _loadingSettings Then
            Dim savedTint = ApplicationData.Current.LocalSettings.Values(AppConstants.SettingAppTintColor)?.ToString()
            If Not String.IsNullOrEmpty(savedTint) AndAlso savedTint <> AppConstants.ThemeDefault Then
                ApplyTintColor(savedTint)
            End If
            ' Refresh the selected swatch's highlight border so it matches the new theme
            ' (white outline in dark mode, black outline in light mode).
            UpdateTintSelection(If(savedTint, AppConstants.ThemeDefault))
        End If
    End Sub

    ''' <summary>
    ''' The one custom-tint acrylic brush, reused for the life of the page. A HostBackdrop
    ''' AcrylicBrush is backed by a composition effect that samples the desktop, so building a
    ''' fresh one per call was by far the most expensive thing the appearance code did - the
    ''' colour picker's live preview calls this on every ColorChanged, i.e. continuously while
    ''' the user drags. TintColor is a dependency property, so repainting is just a set.
    ''' </summary>
    Private _tintBrush As AcrylicBrush

    Private Sub ApplyTintColor(colorTag As String)
        If String.IsNullOrEmpty(colorTag) OrElse colorTag = AppConstants.ThemeDefault Then
            Dim original = TryCast(Me.Resources("AppAcrylicBrush"), Brush)
            If original IsNot Nothing Then RootGrid.Background = original
        Else
            Try
                ' Determine effective theme to choose the right tint shade
                Dim isDark = IsEffectiveThemeDark()

                Dim c = HexToColor(colorTag)
                Dim tintOpacity As Double = 0.8
                If Not isDark Then
                    Dim lightHex As String = Nothing
                    c = If(s_lightTintMap.TryGetValue(colorTag, lightHex),
                           HexToColor(lightHex),
                           LightenForLightTheme(c))
                    tintOpacity = 0.6
                End If

                If _tintBrush Is Nothing Then
                    _tintBrush = New AcrylicBrush() With {
                        .BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    }
                End If
                _tintBrush.TintColor = c
                _tintBrush.TintOpacity = tintOpacity
                _tintBrush.FallbackColor = c

                If Not ReferenceEquals(RootGrid.Background, _tintBrush) Then
                    RootGrid.Background = _tintBrush
                End If
            Catch ex As Exception
                Debug.WriteLine($"MainPage: ApplyTintColor failed – {ex.Message}")
            End Try
        End If
        If Not _loadingSettings Then
            ApplicationData.Current.LocalSettings.Values(AppConstants.SettingAppTintColor) = colorTag
        End If
    End Sub

    ''' <summary>
    ''' Whether the app is currently rendering dark - the root Frame's explicit theme if it has
    ''' one, otherwise the system's. Three call sites needed this identically.
    ''' </summary>
    Private Shared Function IsEffectiveThemeDark() As Boolean
        Dim rootFrame = TryCast(Window.Current.Content, Frame)
        Dim effTheme = If(rootFrame IsNot Nothing, rootFrame.RequestedTheme, ElementTheme.Default)
        Return If(effTheme = ElementTheme.Default,
                  Application.Current.RequestedTheme = ApplicationTheme.Dark,
                  effTheme = ElementTheme.Dark)
    End Function

    ' Base accessible names for the tint swatches, keyed by control – selection state is
    ' appended below since the selected swatch is otherwise only shown via border color,
    ' which a screen reader cannot see.
    Private Shared ReadOnly s_tintSwatchNames As New Dictionary(Of String, String) From {
        {"Default", "Default background tint"},
        {"#1E3A5F", "Navy Blue background tint"},
        {"#2D1B69", "Deep Purple background tint"},
        {"#0F3D2E", "Forest Green background tint"},
        {"#3D1515", "Deep Red background tint"},
        {"#1A1A2E", "Dark Slate background tint"},
        {"#0E3A3A", "Deep Teal background tint"},
        {"#3D2A0F", "Bronze background tint"},
        {"#3D1533", "Deep Rose background tint"},
        {"#2E3D0F", "Olive background tint"},
        {"#232323", "Graphite background tint"}
    }

    ''' <summary>
    ''' Every preset swatch, in display order. The custom swatch is deliberately excluded -
    ''' it has no fixed Tag, so it is matched by elimination rather than by lookup.
    ''' Built once: the named fields never change, and this is walked on every tint click and
    ''' every theme change.
    ''' </summary>
    Private _tintPresetSwatches As Button()

    Private ReadOnly Property TintPresetSwatches As Button()
        Get
            If _tintPresetSwatches Is Nothing Then
                _tintPresetSwatches = {TintDefaultButton, TintBlueButton, TintPurpleButton, TintGreenButton,
                                       TintRedButton, TintSlateButton, TintTealButton, TintBronzeButton,
                                       TintRoseButton, TintOliveButton, TintGraphiteButton}
            End If
            Return _tintPresetSwatches
        End Get
    End Property

    ''' <summary>
    ''' One shared brush for the unselected swatch outline, rather than twelve fresh
    ''' SolidColorBrushes every time the selection or theme changes. Brushes are immutable as
    ''' far as this code is concerned, so sharing one instance is safe.
    ''' </summary>
    Private Shared ReadOnly s_transparentBrush As New SolidColorBrush(Colors.Transparent)

    ''' <summary>Selection outline for the active swatch - white on dark, black on light.</summary>
    Private Shared ReadOnly s_selectedBrushDark As New SolidColorBrush(Colors.White)
    Private Shared ReadOnly s_selectedBrushLight As New SolidColorBrush(Colors.Black)

    Private Sub UpdateTintSelection(selectedTag As String)
        selectedTag = If(String.IsNullOrEmpty(selectedTag), AppConstants.ThemeDefault, selectedTag)

        Dim sel As Button = Nothing
        For Each btn In TintPresetSwatches
            btn.BorderBrush = s_transparentBrush
            Dim tag = If(btn.Tag?.ToString(), "")
            Dim baseName As String = Nothing
            If Not s_tintSwatchNames.TryGetValue(tag, baseName) Then
                baseName = tag
            End If
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(btn, baseName)
            If String.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase) Then sel = btn
        Next

        ' Anything that isn't Default and isn't one of the presets is a custom color, so the
        ' custom swatch both takes the selection border and previews the color itself.
        TintCustomButton.BorderBrush = s_transparentBrush
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(TintCustomButton, "Custom background tint")
        If sel Is Nothing Then
            sel = TintCustomButton
            ShowCustomSwatchColor(selectedTag)
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                TintCustomButton, $"Custom background tint {selectedTag}")
        End If

        If sel IsNot Nothing Then
            sel.BorderBrush = If(IsEffectiveThemeDark(), s_selectedBrushDark, s_selectedBrushLight)
            Dim selBaseName = Windows.UI.Xaml.Automation.AutomationProperties.GetName(sel)
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(sel, selBaseName & " (selected)")
        End If
    End Sub

    Private Shared Function HexToColor(hex As String) As Color
        hex = hex.TrimStart("#"c)
        Return Color.FromArgb(255,
                              Convert.ToByte(hex.Substring(0, 2), 16),
                              Convert.ToByte(hex.Substring(2, 2), 16),
                              Convert.ToByte(hex.Substring(4, 2), 16))
    End Function

    Private Shared Function ColorToHex(c As Color) As String
        Return $"#{c.R:X2}{c.G:X2}{c.B:X2}"
    End Function

    ''' <summary>
    ''' Approximates the pastel light-theme shade that <see cref="s_lightTintMap"/> stores by
    ''' hand for the presets, for colors the user picked themselves. Blending most of the way
    ''' to white keeps the hue but stops a saturated pick from turning a light window murky.
    ''' </summary>
    Private Shared Function LightenForLightTheme(c As Color) As Color
        Const keep As Double = 0.22
        Return Color.FromArgb(255,
                              CByte(255 - (255 - CInt(c.R)) * keep),
                              CByte(255 - (255 - CInt(c.G)) * keep),
                              CByte(255 - (255 - CInt(c.B)) * keep))
    End Function

    ''' <summary>
    ''' Paints the custom swatch with the given color and hides its palette glyph, so the
    ''' button reads as a color chip once the user has actually chosen one.
    ''' </summary>
    Private Sub ShowCustomSwatchColor(hex As String)
        Try
            TintCustomButton.Background = New SolidColorBrush(HexToColor(hex))
            TintCustomIcon.Visibility = Visibility.Collapsed
        Catch ex As Exception
            Debug.WriteLine($"MainPage: ShowCustomSwatchColor failed – {ex.Message}")
        End Try
    End Sub

    Private Sub AppearanceHeader_Tapped(sender As Object, e As RoutedEventArgs)
        ToggleSettingsRow(AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded)
    End Sub

    Private Sub ThemeRadio_Checked(sender As Object, e As RoutedEventArgs)
        If _loadingSettings Then Return
        Dim radio = TryCast(sender, RadioButton)
        If radio IsNot Nothing Then
            ApplyTheme(radio.Tag.ToString())
        End If
    End Sub

    Private Sub TintColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn IsNot Nothing Then
            Dim tag = If(btn.Tag?.ToString(), "Default")
            ApplyTintColor(tag)
            UpdateTintSelection(tag)
        End If
    End Sub

    ''' <summary>
    ''' Opens a color picker so the user can choose a tint that isn't one of the presets. The
    ''' pick is applied live while the dialog is open so the choice can be judged against the
    ''' real window, and reverted to whatever was active before if the dialog is cancelled.
    ''' </summary>
    Private Async Sub CustomTintButton_Click(sender As Object, e As RoutedEventArgs)
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

        Dim localSettings = ApplicationData.Current.LocalSettings
        Dim previousTag As String = If(localSettings.Values(AppConstants.SettingAppTintColor)?.ToString(),
                                       AppConstants.ThemeDefault)

        Try
            ' Seed with the active tint if it's already a custom one, otherwise the last color
            ' the user picked here, otherwise a neutral starting point.
            Dim seed As String = previousTag
            If seed = AppConstants.ThemeDefault OrElse s_tintSwatchNames.ContainsKey(seed) Then
                seed = If(localSettings.Values(AppConstants.SettingAppCustomTintColor)?.ToString(), "#1E3A5F")
            End If

            Dim picker As New ColorPicker() With {
                .IsAlphaEnabled = False,
                .IsHexInputVisible = True,
                .IsColorChannelTextInputVisible = True,
                .Color = HexToColor(seed)
            }

            Dim dialog As New ContentDialog() With {
                .Title = "Custom background tint",
                .Content = picker,
                .PrimaryButtonText = "Apply",
                .CloseButtonText = "Cancel",
                .DefaultButton = ContentDialogButton.Primary
            }
            AppConstants.ApplyXamlRoot(dialog, Me)

            ' Live preview: repaint the window as the user drags around the picker.
            Dim previewHandler As TypedEventHandler(Of ColorPicker, ColorChangedEventArgs) =
                Sub(s, args) ApplyTintColorPreview(ColorToHex(args.NewColor))
            AddHandler picker.ColorChanged, previewHandler

            Dim result = Await dialog.ShowAsync()
            RemoveHandler picker.ColorChanged, previewHandler

            If result = ContentDialogResult.Primary Then
                Dim hex = ColorToHex(picker.Color)
                localSettings.Values(AppConstants.SettingAppCustomTintColor) = hex
                ApplyTintColor(hex)
                UpdateTintSelection(hex)
            Else
                ApplyTintColor(previousTag)
                UpdateTintSelection(previousTag)
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Custom tint dialog failed – {ex.Message}")
            ApplyTintColor(previousTag)
            UpdateTintSelection(previousTag)
        Finally
            _dialogSemaphore.Release()
        End Try
    End Sub

    ''' <summary>
    ''' Repaints the window with a tint without persisting it - used while the color picker is
    ''' open so an abandoned dialog leaves no trace in LocalSettings.
    ''' </summary>
    Private Sub ApplyTintColorPreview(hex As String)
        Dim wasLoading = _loadingSettings
        _loadingSettings = True
        Try
            ApplyTintColor(hex)
        Finally
            _loadingSettings = wasLoading
        End Try
    End Sub

    ' ── Settings row expand/collapse ──

    Private Sub StorageHeader_Tapped(sender As Object, e As RoutedEventArgs)
        ToggleSettingsRow(StorageContent, StorageChevronRotation, AppConstants.SettingSettingsStorageExpanded)
    End Sub

    Private Sub TileHeader_Tapped(sender As Object, e As RoutedEventArgs)
        ToggleSettingsRow(TileContent, TileChevronRotation, AppConstants.SettingSettingsTileExpanded)
    End Sub

    Private Sub WelcomeHeader_Tapped(sender As Object, e As RoutedEventArgs)
        ToggleSettingsRow(WelcomeContent, WelcomeChevronRotation, AppConstants.SettingSettingsWelcomeExpanded)
    End Sub

    Private Sub AboutHeader_Tapped(sender As Object, e As RoutedEventArgs)
        ToggleSettingsRow(AboutContent, AboutChevronRotation, AppConstants.SettingSettingsAboutExpanded)
    End Sub

    ''' <summary>
    ''' Toggle settings row with state persistence
    ''' </summary>
    Private Sub ToggleSettingsRow(content As StackPanel, chevronTransform As RotateTransform, Optional settingKey As String = Nothing)
        Dim isExpanded = content.Visibility = Visibility.Collapsed

        If isExpanded Then
            content.Visibility = Visibility.Visible
            chevronTransform.Angle = 90
        Else
            content.Visibility = Visibility.Collapsed
            chevronTransform.Angle = 0
        End If

        ' Save state if key is provided
        If Not String.IsNullOrEmpty(settingKey) Then
            Try
                ApplicationData.Current.LocalSettings.Values(settingKey) = isExpanded
            Catch ex As Exception
                Debug.WriteLine($"MainPage: Failed to save panel state - {ex.Message}")
            End Try
        End If
    End Sub

    ''' <summary>
    ''' Restore settings panel expanded/collapsed states
    ''' </summary>
    Private Sub RestoreSettingsPanelStates()
        Try
            Dim localSettings = ApplicationData.Current.LocalSettings

            ' Restore each panel state
            RestorePanelState(AppConstants.SettingSettingsAppearanceExpanded, AppearanceContent, AppearanceChevronRotation)
            RestorePanelState(AppConstants.SettingSettingsStorageExpanded, StorageContent, StorageChevronRotation)
            RestorePanelState(AppConstants.SettingSettingsTileExpanded, TileContent, TileChevronRotation)
            RestorePanelState(AppConstants.SettingSettingsWelcomeExpanded, WelcomeContent, WelcomeChevronRotation)
            RestorePanelState(AppConstants.SettingSettingsAboutExpanded, AboutContent, AboutChevronRotation)
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Failed to restore panel states - {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Restore individual panel state
    ''' </summary>
    Private Sub RestorePanelState(settingKey As String, content As StackPanel, chevronTransform As RotateTransform)
        Try
            Dim localSettings = ApplicationData.Current.LocalSettings
            If localSettings.Values.ContainsKey(settingKey) Then
                Dim isExpanded = CBool(localSettings.Values(settingKey))
                If isExpanded Then
                    content.Visibility = Visibility.Visible
                    chevronTransform.Angle = 90
                Else
                    content.Visibility = Visibility.Collapsed
                    chevronTransform.Angle = 0
                End If
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Failed to restore {settingKey} - {ex.Message}")
        End Try
    End Sub

    Private Async Function ShowWelcomeDialogAsync() As Task
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

        Try
            Dim dontShowCheckBox As New CheckBox()
            dontShowCheckBox.Content = "dont show me this again >:("
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
            AppConstants.ApplyXamlRoot(welcomeDialog, Me)

            Await welcomeDialog.ShowAsync()

            If dontShowCheckBox.IsChecked.GetValueOrDefault(False) Then
                Dim localSettings = ApplicationData.Current.LocalSettings
                localSettings.Values(AppConstants.SettingHideWelcomeDialog) = True
            End If
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Welcome dialog failed – {ex.Message}")
        Finally
            _dialogSemaphore.Release()
        End Try
    End Function

    Private Async Sub ResetWelcomeButton_Click(sender As Object, e As RoutedEventArgs)
        ' Check (without holding) that no other dialog is open before flipping the setting -
        ' ShowWelcomeDialogAsync takes the semaphore itself, so if we held it here too, its
        ' own WaitAsync(0) would immediately fail and the dialog would never show.
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If
        _dialogSemaphore.Release()

        Try
            Dim localSettings = ApplicationData.Current.LocalSettings
            localSettings.Values(AppConstants.SettingHideWelcomeDialog) = False
            Await ShowWelcomeDialogAsync()
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Reset welcome failed – {ex.Message}")
        End Try
    End Sub

    ' ── Search bar handlers ──

    Private Sub OnCoreKeyDown(sender As Windows.UI.Core.CoreWindow, args As Windows.UI.Core.KeyEventArgs)
        ' Ctrl+F focuses the search box
        Dim ctrl = (Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Control) And
                    Windows.UI.Core.CoreVirtualKeyStates.Down) = Windows.UI.Core.CoreVirtualKeyStates.Down
        If ctrl AndAlso args.VirtualKey = Windows.System.VirtualKey.F Then
            NavSearchBox.Focus(FocusState.Keyboard)
            args.Handled = True
            Return
        End If
        ' Escape clears the search box when it has text
        If args.VirtualKey = Windows.System.VirtualKey.Escape AndAlso Not String.IsNullOrEmpty(NavSearchBox.Text) Then
            CancelPendingSearch()
            NavSearchBox.Text = ""
            NavSearchBox.ItemsSource = Nothing
            args.Handled = True
        End If
    End Sub

    Private Sub CancelPendingSearch()
        ' Clear the field before disposing so no later caller can reach the dead source.
        Dim cts = _searchDebounceCts
        _searchDebounceCts = Nothing
        If cts Is Nothing Then
            Return
        End If

        Try
            cts.Cancel()
        Catch ex As ObjectDisposedException
            ' Already torn down elsewhere – nothing left to cancel.
        End Try
        cts.Dispose()
    End Sub

    Private Sub NavSearchBox_TextChanged(sender As AutoSuggestBox, args As AutoSuggestBoxTextChangedEventArgs)
        If args.Reason = AutoSuggestionBoxTextChangeReason.UserInput Then
            Dim query = sender.Text.Trim()

            CancelPendingSearch()

            If String.IsNullOrEmpty(query) Then
                sender.ItemsSource = Nothing
                Return
            End If

            Dim cts = New Threading.CancellationTokenSource()
            _searchDebounceCts = cts
            ApplySearchSuggestionsAsync(sender, query, cts.Token)
        End If
    End Sub

    Private Async Sub ApplySearchSuggestionsAsync(sender As AutoSuggestBox, query As String, cancellationToken As Threading.CancellationToken)
        Try
            Await Task.Delay(AppConstants.SearchDebounceMilliseconds, cancellationToken)

            If cancellationToken.IsCancellationRequested Then
                Return
            End If

            ' Capture volatile references on the UI thread before going off-thread
            Dim snapshot = _allSearchItems
            Dim currentUser = ProfileService.CurrentUser

            Dim results = Await Task.Run(Function() BuildSearchSuggestions(query, snapshot, currentUser), cancellationToken)

            If Not cancellationToken.IsCancellationRequested Then
                sender.ItemsSource = results
            End If
        Catch ex As OperationCanceledException
            ' Expected while typing quickly.
        Catch ex As Exception
            Debug.WriteLine($"MainPage: Debounced search failed – {ex.Message}")
        End Try
    End Sub

    Private Shared Function BuildSearchSuggestions(query As String, items As IReadOnlyList(Of SearchItem), currentUser As UserProfile) As List(Of SearchItem)
        Dim filtered As New List(Of SearchItem)()
        For Each item In items
            If item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               item.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                filtered.Add(item)
                If filtered.Count >= AppConstants.SearchSuggestionLimit Then
                    Exit For
                End If
            End If
        Next

        ' Add profile-specific item if logged in and matches, respecting the suggestion limit
        If filtered.Count < AppConstants.SearchSuggestionLimit AndAlso currentUser IsNot Nothing Then
            Dim name = If(String.IsNullOrWhiteSpace(currentUser.DisplayName),
                          currentUser.Username,
                          currentUser.DisplayName)
            Dim profileTitle = $"Profile: {name}"
            If profileTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               AppConstants.CategoryProfile.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 Then
                filtered.Add(New SearchItem(profileTitle, AppConstants.CategoryProfile, AppConstants.NavigationProfile))
            End If
        End If

        Return filtered
    End Function

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
        ' Null check for item
        If item Is Nothing Then
            Debug.WriteLine("MainPage: NavigateToSearchItem called with null item")
            Return
        End If

        If Not String.IsNullOrEmpty(item.Url) Then
            ' Only http/https is launched - see AppConstants.TryCreateWebUri.
            Await AppConstants.LaunchWebUriAsync(item.Url)
        ElseIf Not String.IsNullOrEmpty(item.NavigationTag) Then
            ShowContent(item.NavigationTag)
        End If
    End Function

End Class
