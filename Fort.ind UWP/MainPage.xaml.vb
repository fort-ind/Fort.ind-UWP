' The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

Imports muxc = Microsoft.UI.Xaml.Controls

''' <summary>
''' An empty page that can be used on its own or navigated to within a Frame.
''' </summary>
Public NotInheritable Class MainPage
    Inherits Page

    Private Sub NavView_Loaded(sender As Object, e As RoutedEventArgs)
        ' Select the first item (Latest News) by default
        NavView.SelectedItem = NavView.MenuItems(0)
    End Sub

    Private Sub NavView_ItemInvoked(sender As muxc.NavigationView, args As muxc.NavigationViewItemInvokedEventArgs)
        If args.IsSettingsInvoked Then
            ShowPanel("Settings")
        Else
            Dim invokedItem = TryCast(args.InvokedItemContainer, muxc.NavigationViewItem)
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

    Private Sub TextBlock_SelectionChanged(sender As Object, e As RoutedEventArgs)

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
End Class
