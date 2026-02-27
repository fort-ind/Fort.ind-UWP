Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' Service for managing user accounts and authentication
''' Handles login, registration, and profile management
''' Ready to be extended with server authentication later
''' </summary>
Public Class ProfileService

    ''' <summary>
    ''' The currently logged-in user profile
    ''' </summary>
    Public Shared Property CurrentUser As UserProfile

    ''' <summary>
    ''' Event raised when user logs in or out
    ''' </summary>
    Public Shared Event AuthStateChanged As EventHandler(Of Boolean)

    ''' <summary>
    ''' Attempts to register a new user
    ''' </summary>
    Public Shared Async Function RegisterAsync(username As String, password As String, displayName As String, email As String) As Task(Of RegistrationResult)
        ' Validate inputs
        If String.IsNullOrWhiteSpace(username) OrElse username.Length < 3 Then
            Return New RegistrationResult(False, "Username must be at least 3 characters")
        End If

        If String.IsNullOrWhiteSpace(password) OrElse password.Length < 4 Then
            Return New RegistrationResult(False, "Password must be at least 4 characters")
        End If

        If String.IsNullOrWhiteSpace(displayName) Then
            displayName = username
        End If

        ' Check if username is taken
        If Await LocalStorageService.IsUsernameTakenAsync(username) Then
            Return New RegistrationResult(False, "Username is already taken")
        End If

        ' Create new profile
        Dim profile As New UserProfile(username, displayName, email)
        profile.PasswordHash = HashPassword(password)
        profile.LastLoginDate = DateTime.Now

        ' Save profile
        Dim saved = Await LocalStorageService.SaveProfileAsync(profile)
        If Not saved Then
            Return New RegistrationResult(False, "Failed to save profile")
        End If

        ' Auto-login after registration
        CurrentUser = profile
        Await LocalStorageService.SaveCurrentUserIdAsync(profile.UserId)
        RaiseEvent AuthStateChanged(Nothing, True)

        Return New RegistrationResult(True, "Account created successfully!", profile)
    End Function

    ''' <summary>
    ''' Attempts to log in with username and password
    ''' </summary>
    Public Shared Async Function LoginAsync(username As String, password As String) As Task(Of LoginResult)
        If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrWhiteSpace(password) Then
            Return New LoginResult(False, "Username and password are required")
        End If

        ' Find user by username
        Dim profile = Await LocalStorageService.LoadProfileByUsernameAsync(username)
        If profile Is Nothing Then
            Return New LoginResult(False, "Username not found")
        End If

        ' Verify password
        If Not VerifyPassword(password, profile.PasswordHash) Then
            Return New LoginResult(False, "Incorrect password")
        End If

        ' Update last login
        profile.LastLoginDate = DateTime.Now
        Await LocalStorageService.SaveProfileAsync(profile)

        ' Set current user
        CurrentUser = profile
        Await LocalStorageService.SaveCurrentUserIdAsync(profile.UserId)
        RaiseEvent AuthStateChanged(Nothing, True)

        Return New LoginResult(True, "Login successful!", profile)
    End Function

    ''' <summary>
    ''' Logs out the current user
    ''' </summary>
    Public Shared Async Function LogoutAsync() As Task
        CurrentUser = Nothing
        Await LocalStorageService.ClearCurrentUserAsync()
        RaiseEvent AuthStateChanged(Nothing, False)
    End Function

    ''' <summary>
    ''' Tries to restore session from stored user ID
    ''' Call this at app startup
    ''' </summary>
    Public Shared Async Function TryRestoreSessionAsync() As Task(Of Boolean)
        Try
            Dim userId = Await LocalStorageService.GetCurrentUserIdAsync()
            If String.IsNullOrEmpty(userId) Then
                Return False
            End If

            Dim profile = Await LocalStorageService.LoadProfileAsync(userId)
            If profile Is Nothing Then
                Return False
            End If

            ' Check if remember login is enabled
            If Not profile.Preferences.RememberLogin Then
                Await LocalStorageService.ClearCurrentUserAsync()
                Return False
            End If

            CurrentUser = profile
            RaiseEvent AuthStateChanged(Nothing, True)
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Updates the current user's profile
    ''' </summary>
    Public Shared Async Function UpdateProfileAsync(displayName As String, email As String, bio As String) As Task(Of Boolean)
        If CurrentUser Is Nothing Then
            Return False
        End If

        CurrentUser.DisplayName = displayName
        CurrentUser.Email = email
        CurrentUser.Bio = bio

        Return Await LocalStorageService.SaveProfileAsync(CurrentUser)
    End Function

    ''' <summary>
    ''' Updates user preferences
    ''' </summary>
    Public Shared Async Function UpdatePreferencesAsync(preferences As UserPreferences) As Task(Of Boolean)
        If CurrentUser Is Nothing Then
            Return False
        End If

        CurrentUser.Preferences = preferences
        Return Await LocalStorageService.SaveProfileAsync(CurrentUser)
    End Function

    ''' <summary>
    ''' Changes the user's password
    ''' </summary>
    Public Shared Async Function ChangePasswordAsync(currentPassword As String, newPassword As String) As Task(Of Boolean)
        If CurrentUser Is Nothing Then
            Return False
        End If

        ' Verify current password
        If Not VerifyPassword(currentPassword, CurrentUser.PasswordHash) Then
            Return False
        End If

        ' Validate new password
        If String.IsNullOrWhiteSpace(newPassword) OrElse newPassword.Length < 4 Then
            Return False
        End If

        CurrentUser.PasswordHash = HashPassword(newPassword)
        Return Await LocalStorageService.SaveProfileAsync(CurrentUser)
    End Function

    ''' <summary>
    ''' Deletes the current user's account
    ''' </summary>
    Public Shared Async Function DeleteAccountAsync() As Task(Of Boolean)
        If CurrentUser Is Nothing Then
            Return False
        End If

        Dim userId = CurrentUser.UserId
        Await LogoutAsync()
        Return Await LocalStorageService.DeleteProfileAsync(userId)
    End Function

#Region "Password Hashing"

    ''' <summary>
    ''' Creates a simple hash of the password
    ''' Note: For production, use a proper password hashing library
    ''' </summary>
    Private Shared Function HashPassword(password As String) As String
        Using sha256 As SHA256 = SHA256.Create()
            ' Add a simple salt based on password length (basic security for local storage)
            Dim saltedPassword = $"fort.ind_{password}_uwp"
            Dim bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword))
            Return Convert.ToBase64String(bytes)
        End Using
    End Function

    ''' <summary>
    ''' Verifies a password against a stored hash
    ''' </summary>
    Private Shared Function VerifyPassword(password As String, storedHash As String) As Boolean
        Dim hash = HashPassword(password)
        Return hash = storedHash
    End Function

#End Region

End Class

''' <summary>
''' Result of a registration attempt
''' </summary>
Public Class RegistrationResult
    Public Property Success As Boolean
    Public Property Message As String
    Public Property Profile As UserProfile

    Public Sub New(success As Boolean, message As String, Optional profile As UserProfile = Nothing)
        Me.Success = success
        Me.Message = message
        Me.Profile = profile
    End Sub
End Class

''' <summary>
''' Result of a login attempt
''' </summary>
Public Class LoginResult
    Public Property Success As Boolean
    Public Property Message As String
    Public Property Profile As UserProfile

    Public Sub New(success As Boolean, message As String, Optional profile As UserProfile = Nothing)
        Me.Success = success
        Me.Message = message
        Me.Profile = profile
    End Sub
End Class
