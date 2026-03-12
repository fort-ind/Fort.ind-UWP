Imports Windows.UI.Xaml.Media.Animation
Imports Windows.UI.Xaml.Media.Imaging
Imports Windows.Storage
Imports Windows.Storage.Pickers

''' <summary>
''' Profile viewing and editing page
''' </summary>
Public NotInheritable Class ProfilePage
    Inherits Page

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _dialogSemaphore As New Threading.SemaphoreSlim(1, 1)
    Private _pendingProfilePicturePath As String = Nothing
    Private _removeProfilePictureRequested As Boolean = False

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
        Try
            Await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                Sub() 
                    Try
                        RefreshUI()
                    Catch ex As Exception
                        Debug.WriteLine($"ProfilePage: RefreshUI failed - {ex.Message}")
                    End Try
                End Sub)
        Catch ex As Exception
            ' Critical: Catch exceptions in async void to prevent app crash
            Debug.WriteLine($"ProfilePage: Auth state change handler failed - {ex.Message}")
        End Try
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

        If user.LastLoginDate > DateTime.MinValue Then
            LastLoginText.Text = $"Last login: {user.LastLoginDate:MMM d, yyyy h:mm tt}"
            LastLoginText.Visibility = Visibility.Visible
        Else
            LastLoginText.Visibility = Visibility.Collapsed
        End If

        ' Set initials (up to two letters: first letter of each word)
        Dim name = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        ProfileInitials.Text = GetInitials(name)
        UpdateAvatarUI(user.ProfilePicturePath)

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
            _pendingProfilePicturePath = ProfileService.CurrentUser.ProfilePicturePath
            _removeProfilePictureRequested = False
            PhotoStatusText.Visibility = Visibility.Collapsed
        End If

        ' Clear password fields (with null checks)
        If CurrentPasswordBox IsNot Nothing Then CurrentPasswordBox.Password = ""
        If NewPasswordBox IsNot Nothing Then NewPasswordBox.Password = ""
        If ConfirmNewPasswordBox IsNot Nothing Then ConfirmNewPasswordBox.Password = ""
        If PasswordErrorText IsNot Nothing Then PasswordErrorText.Visibility = Visibility.Collapsed
        If EditErrorText IsNot Nothing Then EditErrorText.Visibility = Visibility.Collapsed

        ' Set initial focus for better keyboard accessibility
        If EditDisplayNameBox IsNot Nothing Then
            EditDisplayNameBox.Focus(FocusState.Programmatic)
        End If
    End Sub

    Private Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)
        Frame.Navigate(GetType(LoginPage))
    End Sub

    Private Sub EditProfileButton_Click(sender As Object, e As RoutedEventArgs)
        ShowEditMode()
    End Sub

    Private Sub CancelEditButton_Click(sender As Object, e As RoutedEventArgs)
        _pendingProfilePicturePath = Nothing
        _removeProfilePictureRequested = False
        ShowViewMode()
        RefreshUI()
    End Sub

    Private Async Sub SaveProfileButton_Click(sender As Object, e As RoutedEventArgs)
        EditErrorText.Visibility = Visibility.Collapsed
        EditSuccessText.Visibility = Visibility.Collapsed

        Dim displayName = EditDisplayNameBox.Text.Trim()
        Dim email = EditEmailBox.Text.Trim()
        Dim bio = EditBioBox.Text.Trim()

        If String.IsNullOrWhiteSpace(displayName) Then
            displayName = ProfileService.CurrentUser.Username
        End If
        
        ' Basic email validation
        If Not String.IsNullOrWhiteSpace(email) AndAlso
           (Not email.Contains("@") OrElse Not email.Contains(".")) Then
            EditErrorText.Text = "Please enter a valid email address"
            EditErrorText.Visibility = Visibility.Visible
            Return
        End If

        ' Show loading state
        SaveProfileButton.IsEnabled = False
        CancelEditButton.IsEnabled = False
        SaveProfileProgress.IsActive = True
        SaveProfileProgress.Visibility = Visibility.Visible

        Try
            Dim updateProfilePicture = _removeProfilePictureRequested OrElse _pendingProfilePicturePath IsNot Nothing
            Dim profilePicturePathToSave As String = If(_removeProfilePictureRequested, Nothing, _pendingProfilePicturePath)

            Dim success = Await ProfileService.UpdateProfileAsync(displayName, email, bio, profilePicturePathToSave, updateProfilePicture)

            If success Then
                _pendingProfilePicturePath = Nothing
                _removeProfilePictureRequested = False
                PhotoStatusText.Visibility = Visibility.Collapsed
                EditSuccessText.Text = "Profile updated successfully!"
                EditSuccessText.Visibility = Visibility.Visible
                RefreshUI()
                ' Auto-hide success message and return to view mode after delay
                Await Task.Delay(1500)
                ShowViewMode()
            Else
                EditErrorText.Text = "Failed to save profile"
                EditErrorText.Visibility = Visibility.Visible
            End If
        Catch ex As Exception
            EditErrorText.Text = "An error occurred while saving"
            EditErrorText.Visibility = Visibility.Visible
            Debug.WriteLine($"SaveProfileButton_Click error: {ex.Message}")
        Finally
            ' Hide loading state
            SaveProfileButton.IsEnabled = True
            CancelEditButton.IsEnabled = True
            SaveProfileProgress.IsActive = False
            SaveProfileProgress.Visibility = Visibility.Collapsed
        End Try
    End Sub

    Private Async Sub ChangePasswordButton_Click(sender As Object, e As RoutedEventArgs)
        PasswordErrorText.Visibility = Visibility.Collapsed
        PasswordSuccessText.Visibility = Visibility.Collapsed

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

        ' Show loading state
        ChangePasswordButton.IsEnabled = False
        ChangePasswordProgress.IsActive = True
        ChangePasswordProgress.Visibility = Visibility.Visible

        Try
            Dim success = Await ProfileService.ChangePasswordAsync(currentPwd, newPwd)

            If success Then
                ' Clear fields and show success
                CurrentPasswordBox.Password = ""
                NewPasswordBox.Password = ""
                ConfirmNewPasswordBox.Password = ""
                NewPasswordStrengthText.Visibility = Visibility.Collapsed

                PasswordSuccessText.Text = "Password changed successfully!"
                PasswordSuccessText.Visibility = Visibility.Visible
                
                ' Auto-hide success message after delay
                Await Task.Delay(3000)
                PasswordSuccessText.Visibility = Visibility.Collapsed
            Else
                ShowPasswordError("Current password is incorrect")
            End If
        Catch ex As Exception
            ShowPasswordError("An error occurred while changing password")
            Debug.WriteLine($"ChangePasswordButton_Click error: {ex.Message}")
        Finally
            ' Hide loading state
            ChangePasswordButton.IsEnabled = True
            ChangePasswordProgress.IsActive = False
            ChangePasswordProgress.Visibility = Visibility.Collapsed
        End Try
    End Sub

    Private Async Sub LogoutButton_Click(sender As Object, e As RoutedEventArgs)
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

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
            _dialogSemaphore.Release()
        End Try
    End Sub

    Private Async Sub DeleteAccountButton_Click(sender As Object, e As RoutedEventArgs)
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

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
            _dialogSemaphore.Release()
        End Try
    End Sub

    Private Sub ShowPasswordError(message As String)
        PasswordErrorText.Text = message
        PasswordErrorText.Visibility = Visibility.Visible
    End Sub

    Private Sub NewPasswordBox_PasswordChanged(sender As Object, e As RoutedEventArgs)
        LoginPage.UpdateStrengthLabel(NewPasswordBox.Password, NewPasswordStrengthText)
    End Sub

    Private Async Sub ChoosePhotoButton_Click(sender As Object, e As RoutedEventArgs)
        EditErrorText.Visibility = Visibility.Collapsed

        If ProfileService.CurrentUser Is Nothing Then
            Return
        End If

        Try
            Dim picker As New FileOpenPicker()
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary
            picker.ViewMode = PickerViewMode.Thumbnail
            picker.FileTypeFilter.Add(".png")
            picker.FileTypeFilter.Add(".jpg")
            picker.FileTypeFilter.Add(".jpeg")
            picker.FileTypeFilter.Add(".bmp")

            Dim pickedFile = Await picker.PickSingleFileAsync()
            If pickedFile Is Nothing Then
                Return
            End If

            Dim relativePath = Await SaveAvatarToLocalStorageAsync(pickedFile)
            If String.IsNullOrWhiteSpace(relativePath) Then
                EditErrorText.Text = "Could not save selected photo"
                EditErrorText.Visibility = Visibility.Visible
                Return
            End If

            _pendingProfilePicturePath = relativePath
            _removeProfilePictureRequested = False
            PhotoStatusText.Text = "New profile photo selected. Save changes to apply."
            PhotoStatusText.Visibility = Visibility.Visible
            UpdateAvatarUI(relativePath)
        Catch ex As Exception
            EditErrorText.Text = "Could not open photo picker"
            EditErrorText.Visibility = Visibility.Visible
            Debug.WriteLine($"ProfilePage: Choose photo failed - {ex.Message}")
        End Try
    End Sub

    Private Sub RemovePhotoButton_Click(sender As Object, e As RoutedEventArgs)
        If ProfileService.CurrentUser Is Nothing Then
            Return
        End If

        _pendingProfilePicturePath = Nothing
        _removeProfilePictureRequested = True
        PhotoStatusText.Text = "Profile photo will be removed when you save changes."
        PhotoStatusText.Visibility = Visibility.Visible
        UpdateAvatarUI(Nothing)
    End Sub

    Private Function GetInitials(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then
            Return "?"
        End If

        Dim parts = name.Trim().Split(" "c)
        If parts.Length >= 2 AndAlso parts(1).Length > 0 Then
            Return (parts(0).Substring(0, 1) & parts(1).Substring(0, 1)).ToUpper()
        End If

        Return parts(0).Substring(0, 1).ToUpper()
    End Function

    Private Sub UpdateAvatarUI(profilePicturePath As String)
        Try
            If String.IsNullOrWhiteSpace(profilePicturePath) Then
                ProfileImage.Source = Nothing
                ProfileImage.Visibility = Visibility.Collapsed
                ProfileInitials.Visibility = Visibility.Visible
                Return
            End If

            Dim imageUri As Uri = Nothing
            If profilePicturePath.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase) Then
                imageUri = New Uri(profilePicturePath)
            Else
                imageUri = New Uri($"ms-appdata:///local/{profilePicturePath.TrimStart("/"c)}")
            End If

            ProfileImage.Source = New BitmapImage(imageUri)
            ProfileImage.Visibility = Visibility.Visible
            ProfileInitials.Visibility = Visibility.Collapsed
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Avatar load failed - {ex.Message}")
            ProfileImage.Source = Nothing
            ProfileImage.Visibility = Visibility.Collapsed
            ProfileInitials.Visibility = Visibility.Visible
        End Try
    End Sub

    Private Shared Async Function SaveAvatarToLocalStorageAsync(sourceFile As StorageFile) As Task(Of String)
        Try
            Dim extension = sourceFile.FileType
            If String.IsNullOrWhiteSpace(extension) Then
                extension = ".png"
            End If

            Dim localFolder = ApplicationData.Current.LocalFolder
            Dim avatarFolder = Await localFolder.CreateFolderAsync(AppConstants.AvatarFolderName, CreationCollisionOption.OpenIfExists)
            Dim fileName = $"{ProfileService.CurrentUser.UserId}{extension.ToLowerInvariant()}"

            Await sourceFile.CopyAsync(avatarFolder, fileName, NameCollisionOption.ReplaceExisting)
            Return $"{AppConstants.AvatarFolderName}/{fileName}"
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Failed to persist avatar - {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Async Function ShowMessageAsync(title As String, message As String) As Task
        ' Use semaphore to prevent concurrent dialog opening
        If Not Await _dialogSemaphore.WaitAsync(0) Then
            Return ' Another dialog is already open
        End If

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
            _dialogSemaphore.Release()
        End Try
    End Function

End Class
