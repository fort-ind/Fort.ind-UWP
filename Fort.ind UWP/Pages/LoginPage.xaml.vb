Imports Windows.UI.Core

''' <summary>
''' Login and registration page
''' </summary>
Public NotInheritable Class LoginPage
    Inherits Page

    Public Sub New()
        Me.InitializeComponent()
    End Sub

    ''' <summary>
    ''' Handle Enter key in password box
    ''' </summary>
    Private Sub PasswordBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key = Windows.System.VirtualKey.Enter Then
            LoginButton_Click(sender, e)
        End If
    End Sub

    ''' <summary>
    ''' Handle login button click
    ''' </summary>
    Private Async Sub LoginButton_Click(sender As Object, e As RoutedEventArgs)
        ErrorText.Visibility = Visibility.Collapsed
        
        Dim username = UsernameBox.Text.Trim()
        Dim pwd = PasswordBox.Password

        If String.IsNullOrWhiteSpace(username) Then
            ShowError("Please enter your username")
            Return
        End If

        If String.IsNullOrWhiteSpace(pwd) Then
            ShowError("Please enter your password")
            Return
        End If

        ShowLoading(True)

        Try
            Dim result = Await ProfileService.LoginAsync(username, pwd)

            If result.Success Then
                ' Update remember preference
                If ProfileService.CurrentUser IsNot Nothing Then
                    ProfileService.CurrentUser.Preferences.RememberLogin = RememberMeCheck.IsChecked.GetValueOrDefault(True)
                    Await LocalStorageService.SaveProfileAsync(ProfileService.CurrentUser)
                End If

                ' Navigate to main page
                Frame.Navigate(GetType(MainPage))
            Else
                ShowError(result.Message)
            End If
        Catch ex As Exception
            ShowError("An error occurred. Please try again.")
        Finally
            ShowLoading(False)
        End Try
    End Sub

    ''' <summary>
    ''' Handle register button click
    ''' </summary>
    Private Async Sub RegisterButton_Click(sender As Object, e As RoutedEventArgs)
        RegErrorText.Visibility = Visibility.Collapsed

        Dim username = RegUsernameBox.Text.Trim()
        Dim displayName = RegDisplayNameBox.Text.Trim()
        Dim email = RegEmailBox.Text.Trim()
        Dim pwd = RegPasswordBox.Password
        Dim confirmPassword = RegConfirmPasswordBox.Password

        If String.IsNullOrWhiteSpace(username) Then
            ShowRegError("Please enter a username")
            Return
        End If

        If String.IsNullOrWhiteSpace(pwd) Then
            ShowRegError("Please enter a password")
            Return
        End If

        If pwd <> confirmPassword Then
            ShowRegError("Passwords do not match")
            Return
        End If

        ShowLoading(True)

        Try
            Dim result = Await ProfileService.RegisterAsync(username, pwd, displayName, email)

            If result.Success Then
                ' Navigate to main page
                Frame.Navigate(GetType(MainPage))
            Else
                ShowRegError(result.Message)
            End If
        Catch ex As Exception
            ShowRegError("An error occurred. Please try again.")
        Finally
            ShowLoading(False)
        End Try
    End Sub

    ''' <summary>
    ''' Show login form
    ''' </summary>
    Private Sub ShowLoginLink_Click(sender As Object, e As RoutedEventArgs)
        LoginForm.Visibility = Visibility.Visible
        RegisterForm.Visibility = Visibility.Collapsed
        ClearForms()
    End Sub

    ''' <summary>
    ''' Show register form
    ''' </summary>
    Private Sub ShowRegisterLink_Click(sender As Object, e As RoutedEventArgs)
        LoginForm.Visibility = Visibility.Collapsed
        RegisterForm.Visibility = Visibility.Visible
        ClearForms()
    End Sub

    ''' <summary>
    ''' Skip login and continue without account
    ''' </summary>
    Private Sub SkipButton_Click(sender As Object, e As RoutedEventArgs)
        Frame.Navigate(GetType(MainPage))
    End Sub

    ''' <summary>
    ''' Show error message on login form
    ''' </summary>
    Private Sub ShowError(message As String)
        ErrorText.Text = message
        ErrorText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>
    ''' Show error message on register form
    ''' </summary>
    Private Sub ShowRegError(message As String)
        RegErrorText.Text = message
        RegErrorText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>
    ''' Show/hide loading overlay
    ''' </summary>
    Private Sub ShowLoading(show As Boolean)
        LoadingOverlay.Visibility = If(show, Visibility.Visible, Visibility.Collapsed)
        LoginButton.IsEnabled = Not show
        RegisterButton.IsEnabled = Not show
    End Sub

    ''' <summary>
    ''' Clear all form fields
    ''' </summary>
    Private Sub ClearForms()
        ErrorText.Visibility = Visibility.Collapsed
        RegErrorText.Visibility = Visibility.Collapsed
        UsernameBox.Text = ""
        PasswordBox.Password = ""
        RegUsernameBox.Text = ""
        RegDisplayNameBox.Text = ""
        RegEmailBox.Text = ""
        RegPasswordBox.Password = ""
        RegConfirmPasswordBox.Password = ""
    End Sub

End Class
