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
        ' Hide all panels
        LatestNewsPanel.Visibility = Visibility.Collapsed
        GamesPanel.Visibility = Visibility.Collapsed
        BetasPanel.Visibility = Visibility.Collapsed
        ContactsPanel.Visibility = Visibility.Collapsed
        SocialPanel.Visibility = Visibility.Collapsed
        AboutPanel.Visibility = Visibility.Collapsed
        SettingsPanel.Visibility = Visibility.Collapsed

        ' Show the selected panel
        Select Case panelName
            Case "LatestNews"
                LatestNewsPanel.Visibility = Visibility.Visible
            Case "Games"
                GamesPanel.Visibility = Visibility.Visible
            Case "Betas"
                BetasPanel.Visibility = Visibility.Visible
            Case "Contacts"
                ContactsPanel.Visibility = Visibility.Visible
            Case "Social"
                SocialPanel.Visibility = Visibility.Visible
            Case "About"
                AboutPanel.Visibility = Visibility.Visible
            Case "Settings"
                SettingsPanel.Visibility = Visibility.Visible
            Case Else
                LatestNewsPanel.Visibility = Visibility.Visible
        End Select
    End Sub

    Private Async Sub AboutButton_Click(sender As Object, e As RoutedEventArgs)
        Dim aboutDialog As New ContentDialog()
        aboutDialog.Title = "About"
        aboutDialog.Content = "Fort.ind desktop for UWP, version 0.4 beta"
        aboutDialog.PrimaryButtonText = "OK"
        aboutDialog.DefaultButton = ContentDialogButton.Primary
        aboutDialog.XamlRoot = Me.XamlRoot

        Await aboutDialog.ShowAsync()
    End Sub

    Private Sub TextBlock_SelectionChanged(sender As Object, e As RoutedEventArgs)

    End Sub
End Class
