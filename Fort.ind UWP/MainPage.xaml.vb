' The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

Imports Windows.UI
Imports Windows.UI.ViewManagement
Imports Windows.ApplicationModel.Core

''' <summary>
''' An empty page that can be used on its own or navigated to within a Frame.
''' </summary>
Public NotInheritable Class MainPage
    Inherits Page

    Public Sub New()
        Me.InitializeComponent()
        SetupTitleBar()
        UpdateLiveTile()
        UpdateProfileNavItem()

        ' Listen for auth state changes
        AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        UpdateProfileNavItem()
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

    Private Sub NavView_Loaded(sender As Object, e As RoutedEventArgs)
        ' Select the first item (Latest News) by default
        NavView.SelectedItem = NavView.MenuItems(0)
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
                ' Navigate to ProfilePage in the frame
                ContentScrollViewer.Visibility = Visibility.Collapsed
                ContentFrame.Visibility = Visibility.Visible
                ContentFrame.Navigate(GetType(ProfilePage))
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

End Class
