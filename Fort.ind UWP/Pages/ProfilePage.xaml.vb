Imports Windows.UI.Xaml.Media.Animation

''' <summary>
''' Profile viewing and editing page
''' </summary>
Public NotInheritable Class ProfilePage
    Inherits Page

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _isDialogOpen As Boolean = False

    Public Sub New()
        Me.InitializeComponent()
        AddHandler Loaded, AddressOf ProfilePage_Loaded
        AddHandler Unloaded, AddressOf ProfilePage_Unloaded
        AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Sub ProfilePage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
    End Sub

    Private Async Sub OnAuthStateChanged(sender As Object, isLoggedIn As Boolean)
        Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            Sub() RefreshUI())
    End Sub

    Private Sub ProfilePage_Loaded(sender As Object, e As RoutedEventArgs)
        RefreshUI()
    End Sub

    ''' <summary>
    ''' Refresh the UI based on login state
    ''' </summary>
    Public Sub RefreshUI()
        If ProfileService.CurrentUser IsNot Nothing Then
            ShowLoggedInState()
        Else
            ShowNotLoggedInState()
        End If
    End Sub

    Private Sub ShowLoggedInState()
        NotLoggedInPanel.Visibility = Visibility.Collapsed
        LoggedInPanel.Visibility = Visibility.Visible

        Dim user = ProfileService.CurrentUser

        ' Update profile header
        DisplayNameText.Text = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        UsernameText.Text = $"@{user.Username}"
        MemberSinceText.Text = $"Member since {user.CreatedDate:MMMM yyyy}"

        ' Set initials (up to two letters: first letter of each word)
        Dim name = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        If name.Length > 0 Then
            Dim parts = name.Trim().Split(" "c)
            If parts.Length >= 2 AndAlso parts(1).Length > 0 Then
                ProfileInitials.Text = (parts(0).Substring(0, 1) & parts(1).Substring(0, 1)).ToUpper()
            Else
                ProfileInitials.Text = parts(0).Substring(0, 1).ToUpper()
            End If
        End If

        ' Update bio
        BioText.Text = If(String.IsNullOrWhiteSpace(user.Bio), "No bio yet. Click Edit Profile to add one!", user.Bio)

        ' Update email (and pray it wont explode when you dont set one)
        EmailText.Text = If(String.IsNullOrWhiteSpace(user.Email), "No email set", user.Email)

        ' Show view mode by default
        ShowViewMode()

        ' Fade in the avatar
        Dim sb = TryCast(Me.Resources("AvatarFadeIn"), Storyboard)
        sb?.Begin()
    End Sub

    Private Sub ShowNotLoggedInState()
        NotLoggedInPanel.Visibility = Visibility.Visible
        LoggedInPanel.Visibility = Visibility.Collapsed
    End Sub

    Private Sub ShowViewMode()
        ViewModePanel.Visibility = Visibility.Visible
        EditModePanel.Visibility = Visibility.Collapsed
    End Sub

    Private Sub ShowEditMode()
        ViewModePanel.Visibility = Visibility.Collapsed
        EditModePanel.Visibility = Visibility.Visible

        ' Populate edit fields (convert null to empty string for WinRT compatibility)
        If ProfileService.CurrentUser IsNot Nothing Then
            If EditDisplayNameBox IsNot Nothing Then EditDisplayNameBox.Text = If(ProfileService.CurrentUser.DisplayName, "")
            If EditEmailBox IsNot Nothing Then EditEmailBox.Text = If(ProfileService.CurrentUser.Email, "")
            If EditBioBox IsNot Nothing Then EditBioBox.Text = If(ProfileService.CurrentUser.Bio, "")
        End If

        ' Clear password fields (with null checks)
        If CurrentPasswordBox IsNot Nothing Then CurrentPasswordBox.Password = ""
        If NewPasswordBox IsNot Nothing Then NewPasswordBox.Password = ""
        If ConfirmNewPasswordBox IsNot Nothing Then ConfirmNewPasswordBox.Password = ""
        If PasswordErrorText IsNot Nothing Then PasswordErrorText.Visibility = Visibility.Collapsed
        If EditErrorText IsNot Nothing Then EditErrorText.Visibility = Visibility.Collapsed
    End Sub

    Private Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)
        Frame.Navigate(GetType(LoginPage))
    End Sub

    Private Sub EditProfileButton_Click(sender As Object, e As RoutedEventArgs)
        ShowEditMode()
    End Sub

    Private Sub CancelEditButton_Click(sender As Object, e As RoutedEventArgs)
        ShowViewMode()
    End Sub

    Private Async Sub SaveProfileButton_Click(sender As Object, e As RoutedEventArgs)
        EditErrorText.Visibility = Visibility.Collapsed

        Dim displayName = EditDisplayNameBox.Text.Trim()
        Dim email = EditEmailBox.Text.Trim()
        Dim bio = EditBioBox.Text.Trim()

        If String.IsNullOrWhiteSpace(displayName) Then
            displayName = ProfileService.CurrentUser.Username
        End If

        Try
            Dim success = Await ProfileService.UpdateProfileAsync(displayName, email, bio)

            If success Then
                RefreshUI()
                ShowViewMode()
            Else
                EditErrorText.Text = "Failed to save profile"
                EditErrorText.Visibility = Visibility.Visible
            End If
        Catch ex As Exception
            EditErrorText.Text = "An error occurred"
            EditErrorText.Visibility = Visibility.Visible
        End Try
    End Sub

    Private Async Sub ChangePasswordButton_Click(sender As Object, e As RoutedEventArgs)
        PasswordErrorText.Visibility = Visibility.Collapsed

        Dim currentPwd = CurrentPasswordBox.Password
        Dim newPwd = NewPasswordBox.Password
        Dim confirmPwd = ConfirmNewPasswordBox.Password

        If String.IsNullOrWhiteSpace(currentPwd) Then
            ShowPasswordError("Please enter your current password")
            Return
        End If

        If String.IsNullOrWhiteSpace(newPwd) Then
            ShowPasswordError("Please enter a new password")
            Return
        End If

        If newPwd.Length < 8 Then
            ShowPasswordError("New password must be at least 8 characters")
            Return
        End If

        If newPwd <> confirmPwd Then
            ShowPasswordError("New passwords do not match")
            Return
        End If

        Try
            Dim success = Await ProfileService.ChangePasswordAsync(currentPwd, newPwd)

            If success Then
                ' Clear fields and show success
                CurrentPasswordBox.Password = ""
                NewPasswordBox.Password = ""
                ConfirmNewPasswordBox.Password = ""

                Await ShowMessageAsync("Success", "Password changed successfully!")
            Else
                ShowPasswordError("Current password is incorrect")
            End If
        Catch ex As Exception
            ShowPasswordError("An error occurred")
        End Try
    End Sub

    Private Async Sub LogoutButton_Click(sender As Object, e As RoutedEventArgs)
        If _isDialogOpen Then Return
        _isDialogOpen = True

        Try
            Dim dialog As New ContentDialog()
            dialog.Title = "Sign Out"
            dialog.Content = "Are you sure you want to sign out?"
            dialog.PrimaryButtonText = "Sign Out"
            dialog.CloseButtonText = "Cancel"
            dialog.DefaultButton = ContentDialogButton.Close
            dialog.XamlRoot = Me.XamlRoot

            Dim result = Await dialog.ShowAsync()

            If result = ContentDialogResult.Primary Then
                Await ProfileService.LogoutAsync()
                RefreshUI()
            End If
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Logout dialog failed – {ex.Message}")
        Finally
            _isDialogOpen = False
        End Try
    End Sub

    Private Async Sub DeleteAccountButton_Click(sender As Object, e As RoutedEventArgs)
        If _isDialogOpen Then Return
        _isDialogOpen = True

        Try
            Dim dialog As New ContentDialog()
            dialog.Title = "Delete Account"
            dialog.Content = "Are you sure you want to delete your account? This action cannot be undone and all your data will be permanently lost."
            dialog.PrimaryButtonText = "Delete Forever"
            dialog.CloseButtonText = "Cancel"
            dialog.DefaultButton = ContentDialogButton.Close
            dialog.XamlRoot = Me.XamlRoot

            Dim result = Await dialog.ShowAsync()

            If result = ContentDialogResult.Primary Then
                ' Ask for confirmation again
                Dim confirmDialog As New ContentDialog()
                confirmDialog.Title = "Final Confirmation"

                Dim confirmBox As New TextBox()
                confirmBox.PlaceholderText = "Type DELETE"
                confirmDialog.Content = confirmBox

                confirmDialog.PrimaryButtonText = "Delete"
                confirmDialog.CloseButtonText = "Cancel"
                confirmDialog.XamlRoot = Me.XamlRoot

                Dim confirmResult = Await confirmDialog.ShowAsync()

                If confirmResult = ContentDialogResult.Primary AndAlso confirmBox.Text.Trim().ToUpper() = "DELETE" Then
                    Await ProfileService.DeleteAccountAsync()
                    RefreshUI()
                End If
            End If
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Delete account dialog failed – {ex.Message}")
        Finally
            _isDialogOpen = False
        End Try
    End Sub

    Private Sub ShowPasswordError(message As String)
        PasswordErrorText.Text = message
        PasswordErrorText.Visibility = Visibility.Visible
    End Sub

    Private Async Function ShowMessageAsync(title As String, message As String) As Task
        If _isDialogOpen Then Return
        _isDialogOpen = True

        Try
            Dim dialog As New ContentDialog()
            dialog.Title = title
            dialog.Content = message
            dialog.CloseButtonText = "OK"
            dialog.XamlRoot = Me.XamlRoot
            Await dialog.ShowAsync()
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: ShowMessage dialog failed – {ex.Message}")
        Finally
            _isDialogOpen = False
        End Try
    End Function

End Class
