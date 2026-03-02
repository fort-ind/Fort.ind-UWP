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
    ''' Handle Enter key in confirm password box
    ''' </summary>
    Private Sub RegConfirmPasswordBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key = Windows.System.VirtualKey.Enter Then
            RegisterButton_Click(sender, e)
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

                ' Navigate back instead of creating new MainPage instance
                If Frame.CanGoBack Then
                    Frame.GoBack()
                Else
                    Frame.Navigate(GetType(MainPage))
                End If
            Else
                ShowError(result.Message)
            End If
        Catch ex As Exception
            Debug.WriteLine($"LoginButton_Click error: {ex}")
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

        If pwd.Length < 8 Then
            ShowRegError("Password must be at least 8 characters")
            Return
        End If

        If pwd <> confirmPassword Then
            ShowRegError("Passwords do not match")
            Return
        End If

        ' Basic email format validation (only if provided)
        If Not String.IsNullOrWhiteSpace(email) AndAlso
           (Not email.Contains("@") OrElse Not email.Contains(".")) Then
            ShowRegError("Please enter a valid email address")
            Return
        End If

        ShowLoading(True)

        Try
            Dim result = Await ProfileService.RegisterAsync(username, pwd, displayName, email)

            If result.Success Then
                ' Navigate back instead of creating new MainPage instance
                If Frame.CanGoBack Then
                    Frame.GoBack()
                Else
                    Frame.Navigate(GetType(MainPage))
                End If
            Else
                ShowRegError(result.Message)
            End If
        Catch ex As Exception
            Debug.WriteLine($"RegisterButton_Click error: {ex}")
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
        UsernameBox.Focus(FocusState.Programmatic)
    End Sub

    ''' <summary>
    ''' Show register form
    ''' </summary>
    Private Sub ShowRegisterLink_Click(sender As Object, e As RoutedEventArgs)
        LoginForm.Visibility = Visibility.Collapsed
        RegisterForm.Visibility = Visibility.Visible
        ClearForms()
        RegUsernameBox.Focus(FocusState.Programmatic)
    End Sub

    ''' <summary>
    ''' Skip login and continue without account
    ''' </summary>
    Private Sub SkipButton_Click(sender As Object, e As RoutedEventArgs)
        If Frame.CanGoBack Then
            Frame.GoBack()
        Else
            Frame.Navigate(GetType(MainPage))
        End If
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
        SkipButton.IsEnabled = Not show
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
