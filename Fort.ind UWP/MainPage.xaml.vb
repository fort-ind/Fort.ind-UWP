' The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

''' <summary>
''' An empty page that can be used on its own or navigated to within a Frame.
''' </summary>
Public NotInheritable Class MainPage
    Inherits Page

    Private Sub LatestNews_Click(sender As Object, e As RoutedEventArgs)
        ' Show the Latest News panel
        LatestNewsPanel.Visibility = Visibility.Visible
    End Sub

End Class
