Imports Windows.UI.Xaml.Media.Animation
Imports Windows.UI.Xaml.Media.Imaging

''' <summary>
''' Profile viewing page. Read-only: the fort.social account is the source of truth,
''' so editing happens on the instance, not here.
''' </summary>
Public NotInheritable Class ProfilePage
    Inherits Page

    ' Guard to prevent multiple ContentDialogs from opening simultaneously
    Private _dialogSemaphore As New Threading.SemaphoreSlim(1, 1)

    ' Tracks whether the AuthStateChanged handler is attached, so repeated Loaded/Unloaded
    ' cycles (which UWP can fire more than once) don't attach it more than once, and so
    ' the handler is reliably reattached after an Unloaded/Loaded pair instead of leaving
    ' this page permanently deaf to sign-in/out.
    Private _authHandlerAttached As Boolean = False

    ' The avatar URL currently shown, so a second RefreshUI for the same profile (e.g. the
    ' background refresh in ProfileService.TryRestoreSessionAsync firing shortly after the
    ' cached profile is first shown) doesn't re-download/re-decode an unchanged avatar image
    ' or replay its fade-in animation.
    Private _lastAvatarUrl As String = Nothing

    Public Sub New()
        Me.InitializeComponent()
        AddHandler Loaded, AddressOf ProfilePage_Loaded
        AddHandler Unloaded, AddressOf ProfilePage_Unloaded
    End Sub

    Private Sub ProfilePage_Unloaded(sender As Object, e As RoutedEventArgs)
        If _authHandlerAttached Then
            RemoveHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
            _authHandlerAttached = False
        End If
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
        If Not _authHandlerAttached Then
            AddHandler ProfileService.AuthStateChanged, AddressOf OnAuthStateChanged
            _authHandlerAttached = True
        End If
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
        Dim host = If(String.IsNullOrWhiteSpace(user.Host), MisskeyAuthService.InstanceHost, user.Host)

        ' Update profile header
        DisplayNameText.Text = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        UsernameText.Text = $"@{user.Username}@{host}"

        If user.CreatedDate > DateTime.MinValue Then
            MemberSinceText.Text = $"Member since {user.CreatedDate:MMMM yyyy}"
            MemberSinceText.Visibility = Visibility.Visible
        Else
            MemberSinceText.Visibility = Visibility.Collapsed
        End If

        If user.LastLoginDate > DateTime.MinValue Then
            LastLoginText.Text = $"Last signed in: {user.LastLoginDate:MMM d, yyyy h:mm tt}"
            LastLoginText.Visibility = Visibility.Visible
        Else
            LastLoginText.Visibility = Visibility.Collapsed
        End If

        ' Set initials (up to two letters: first letter of each word)
        Dim name = If(String.IsNullOrWhiteSpace(user.DisplayName), user.Username, user.DisplayName)
        ProfileInitials.Text = GetInitials(name)

        ' Update bio
        BioText.Text = If(String.IsNullOrWhiteSpace(user.Bio), "No bio set", user.Bio)

        ' Fade in the avatar - only when it's actually new/changed, not on every RefreshUI.
        If UpdateAvatarUI(user.AvatarUrl) Then
            Dim sb = TryCast(Me.Resources("AvatarFadeIn"), Storyboard)
            sb?.Begin()
        End If
    End Sub

    Private Sub ShowNotLoggedInState()
        NotLoggedInPanel.Visibility = Visibility.Visible
        LoggedInPanel.Visibility = Visibility.Collapsed
        ' So the avatar reloads and fades in again on the next sign-in, even if it's the same
        ' account/URL as before this sign-out.
        _lastAvatarUrl = Nothing
    End Sub

    Private Sub SignInButton_Click(sender As Object, e As RoutedEventArgs)
        Frame.Navigate(GetType(LoginPage))
    End Sub

    Private Async Sub ManageOnFortSocialButton_Click(sender As Object, e As RoutedEventArgs)
        Dim user = ProfileService.CurrentUser
        If user Is Nothing Then Return

        Try
            ' Host and username come from the instance's JSON and are cached to disk, so they
            ' are treated as untrusted when they are spliced into a URL. An unchecked host
            ' ("evil.com/x") or username ("a/../..") would silently point this link somewhere
            ' other than the user's profile.
            Dim host = If(String.IsNullOrWhiteSpace(user.Host), MisskeyAuthService.InstanceHost, user.Host)
            If Uri.CheckHostName(host) = UriHostNameType.Unknown Then
                host = MisskeyAuthService.InstanceHost
            End If

            Await AppConstants.LaunchWebUriAsync($"https://{host}/@{Uri.EscapeDataString(user.Username)}")
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Failed to open fort.social profile - {ex.Message}")
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
            AppConstants.ApplyXamlRoot(dialog, Me)

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

    Private Function GetInitials(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then
            Return "?"
        End If

        Dim parts = name.Trim().Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        If parts.Length >= 2 AndAlso parts(1).Length > 0 Then
            Return (parts(0).Substring(0, 1) & parts(1).Substring(0, 1)).ToUpper()
        End If

        Return parts(0).Substring(0, 1).ToUpper()
    End Function

    ''' <summary>
    ''' Applies the avatar image (or falls back to initials). Returns False without doing any
    ''' work if avatarUrl is the same one already applied - RefreshUI can run more than once
    ''' for the same profile (e.g. the background refresh in
    ''' ProfileService.TryRestoreSessionAsync), and without this check each call would
    ''' re-download/re-decode an unchanged image and the caller would replay its fade-in.
    ''' </summary>
    Private Function UpdateAvatarUI(avatarUrl As String) As Boolean
        If String.Equals(avatarUrl, _lastAvatarUrl, StringComparison.Ordinal) Then
            Return False
        End If
        _lastAvatarUrl = avatarUrl

        Try
            If String.IsNullOrWhiteSpace(avatarUrl) Then
                ProfileImage.Source = Nothing
                ProfileImage.Visibility = Visibility.Collapsed
                ProfileInitials.Visibility = Visibility.Visible
                Return True
            End If

            ' Only http/https avatars are fetched - the URL comes from the instance's JSON via
            ' the on-disk profile cache, and BitmapImage will happily resolve other schemes.
            Dim avatarUri = AppConstants.TryCreateWebUri(avatarUrl)
            If avatarUri Is Nothing Then
                ProfileImage.Source = Nothing
                ProfileImage.Visibility = Visibility.Collapsed
                ProfileInitials.Visibility = Visibility.Visible
                Return True
            End If

            ' Decode at the displayed 80x80 size, not the source resolution - avatars served
            ' at 512px+ would otherwise sit in memory fully decoded (several MB each) despite
            ' being drawn into an 80px circle. DecodePixelType.Logical means these are view
            ' pixels, which XAML already scales by the display's rasterization scale, so the
            ' high-DPI case is handled without asking for 2x here on top of it.
            ' Must be set before UriSource or the decode already happened at full size.
            Dim bitmap As New BitmapImage()
            bitmap.DecodePixelType = DecodePixelType.Logical
            bitmap.DecodePixelWidth = 80
            bitmap.DecodePixelHeight = 80
            bitmap.UriSource = avatarUri
            ProfileImage.Source = bitmap
            ProfileImage.Visibility = Visibility.Visible
            ProfileInitials.Visibility = Visibility.Collapsed
            Return True
        Catch ex As Exception
            Debug.WriteLine($"ProfilePage: Avatar load failed - {ex.Message}")
            ProfileImage.Source = Nothing
            ProfileImage.Visibility = Visibility.Collapsed
            ProfileInitials.Visibility = Visibility.Visible
            Return True
        End Try
    End Function

End Class
