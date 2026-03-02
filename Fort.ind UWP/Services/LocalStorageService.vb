Imports Windows.Storage
Imports System.Runtime.Serialization.Json
Imports System.IO

''' <summary>
''' Service for storing and retrieving data locally
''' Uses Windows.Storage.ApplicationData for UWP-safe storage
''' Can be extended to sync with server later
''' </summary>
Public Class LocalStorageService

    Private Shared ReadOnly LocalFolder As StorageFolder = ApplicationData.Current.LocalFolder
    Private Const PROFILES_FOLDER As String = "Profiles"
    Private Const CURRENT_USER_FILE As String = "current_user.json"
    Private Const SETTINGS_FILE As String = "app_settings.json"

    ''' <summary>
    ''' Storage mode for user data (future: SQLite, Server)
    ''' </summary>
    Public Enum StorageMode
        JsonFile = 0
        ' SQLite = 1  ' Coming soon - requires server setup
        ' Server = 2  ' Coming soon - requires API backend
    End Enum

    ''' <summary>
    ''' Current storage mode
    ''' </summary>
    Public Shared Property CurrentStorageMode As StorageMode = StorageMode.JsonFile

    ''' <summary>
    ''' Initializes storage service
    ''' </summary>
    Public Shared Function InitializeAsync() As Task
        ' Future: Load storage mode from settings
        CurrentStorageMode = StorageMode.JsonFile
        Return Task.CompletedTask
    End Function

#Region "Profile Operations"

    ''' <summary>
    ''' Saves a user profile to local storage as a JSON file :p (temporarily) until we implement a more secure storage solution like SQLite or server-side storage with encryption.
    ''' </summary>
    Public Shared Async Function SaveProfileAsync(profile As UserProfile) As Task(Of Boolean)
        If profile Is Nothing OrElse String.IsNullOrWhiteSpace(profile.UserId) Then
            Return False
        End If

        Try
            ' Ensure profiles folder exists
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)

            ' Create file for this user
            Dim fileName = $"{profile.UserId}.json"
            Dim file = Await profilesFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting)

            ' Serialize and save
            Dim json = SerializeToJson(profile)
            Await FileIO.WriteTextAsync(file, json)

            Return True
        Catch ex As Exception
            Debug.WriteLine($"Error saving profile: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Loads a user profile by user ID
    ''' </summary>
    Public Shared Async Function LoadProfileAsync(userId As String) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(userId) Then Return Nothing

        Try
            Dim profilesFolder = Await LocalFolder.GetFolderAsync(PROFILES_FOLDER)
            Dim fileName = $"{userId}.json"
            Dim file = Await profilesFolder.GetFileAsync(fileName)

            Dim json = Await FileIO.ReadTextAsync(file)
            Return DeserializeFromJson(Of UserProfile)(json)
        Catch ex As Exception
            Debug.WriteLine($"Error loading profile: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Loads a user profile by username (scans files with early exit to avoid loading all profiles)
    ''' </summary>
    Public Shared Async Function LoadProfileByUsernameAsync(username As String) As Task(Of UserProfile)
        If String.IsNullOrWhiteSpace(username) Then Return Nothing

        Try
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
            Dim files = Await profilesFolder.GetFilesAsync()

            For Each file In files
                If file.Name.EndsWith(".json") Then
                    Try
                        Dim json = Await FileIO.ReadTextAsync(file)
                        Dim profile = DeserializeFromJson(Of UserProfile)(json)
                        If profile IsNot Nothing AndAlso String.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase) Then
                            Return profile
                        End If
                    Catch
                        ' Skip corrupt files
                    End Try
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine($"Error loading profile by username: {ex.Message}")
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' Gets all stored profiles
    ''' </summary>
    Public Shared Async Function GetAllProfilesAsync() As Task(Of List(Of UserProfile))
        Dim profiles As New List(Of UserProfile)

        Try
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
            Dim files = Await profilesFolder.GetFilesAsync()

            For Each file In files
                If file.Name.EndsWith(".json") Then
                    Try
                        Dim json = Await FileIO.ReadTextAsync(file)
                        Dim profile = DeserializeFromJson(Of UserProfile)(json)
                        If profile IsNot Nothing Then
                            profiles.Add(profile)
                        End If
                    Catch ex As Exception
                        Debug.WriteLine($"Skipping corrupt profile '{file.Name}': {ex.Message}")
                    End Try
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine($"Error getting all profiles: {ex.Message}")
        End Try

        Return profiles
    End Function

    ''' <summary>
    ''' Deletes a user profile
    ''' </summary>
    Public Shared Async Function DeleteProfileAsync(userId As String) As Task(Of Boolean)
        If String.IsNullOrWhiteSpace(userId) Then Return False

        Try
            Dim profilesFolder = Await LocalFolder.GetFolderAsync(PROFILES_FOLDER)
            Dim fileName = $"{userId}.json"
            Dim file = Await profilesFolder.GetFileAsync(fileName)
            Await file.DeleteAsync()

            ' Clear the session file if the deleted user was logged in
            Dim currentId = Await GetCurrentUserIdAsync()
            If currentId = userId Then
                Await ClearCurrentUserAsync()
            End If

            Return True
        Catch ex As Exception
            Debug.WriteLine($"Error deleting profile: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Checks if a username is already taken (reuses early-exit scan, avoids loading all profiles)
    ''' </summary>
    Public Shared Async Function IsUsernameTakenAsync(username As String) As Task(Of Boolean)
        Dim existing = Await LoadProfileByUsernameAsync(username)
        Return existing IsNot Nothing
    End Function

#End Region

#Region "Session Management"

    ''' <summary>
    ''' Saves the current logged-in user ID
    ''' </summary>
    Public Shared Async Function SaveCurrentUserIdAsync(userId As String) As Task
        If String.IsNullOrWhiteSpace(userId) Then Return

        Try
            Dim file = Await LocalFolder.CreateFileAsync(CURRENT_USER_FILE, CreationCollisionOption.ReplaceExisting)
            Await FileIO.WriteTextAsync(file, userId)
        Catch ex As Exception
            Debug.WriteLine($"Error saving current user: {ex.Message}")
        End Try
    End Function

    ''' <summary>
    ''' Gets the current logged-in user ID
    ''' </summary>
    Public Shared Async Function GetCurrentUserIdAsync() As Task(Of String)
        Try
            Dim file = Await LocalFolder.GetFileAsync(CURRENT_USER_FILE)
            Dim id = (Await FileIO.ReadTextAsync(file))?.Trim()
            If String.IsNullOrWhiteSpace(id) Then Return Nothing
            Return id
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Clears the current user session
    ''' </summary>
    Public Shared Async Function ClearCurrentUserAsync() As Task
        Try
            Dim file = Await LocalFolder.GetFileAsync(CURRENT_USER_FILE)
            Await file.DeleteAsync()
        Catch
            ' File doesn't exist, that's fine
        End Try
    End Function

#End Region

#Region "Settings"

    ''' <summary>
    ''' Gets the data storage location path
    ''' </summary>
    Public Shared ReadOnly Property DataPath As String
        Get
            Return LocalFolder.Path
        End Get
    End Property

    ''' <summary>
    ''' Gets the number of registered users (counts profile files without deserializing)
    ''' </summary>
    Public Shared Async Function GetUserCountAsync() As Task(Of Integer)
        Try
            Dim profilesFolder = Await LocalFolder.CreateFolderAsync(PROFILES_FOLDER, CreationCollisionOption.OpenIfExists)
            Dim files = Await profilesFolder.GetFilesAsync()
            Dim count As Integer = 0
            For Each f In files
                If f.Name.EndsWith(".json") Then count += 1
            Next
            Return count
        Catch ex As Exception
            Debug.WriteLine($"Error getting user count: {ex.Message}")
            Return 0
        End Try
    End Function

#End Region

#Region "JSON Serialization"

    ' Cache serializers to avoid repeated reflection on each call
    Private Shared ReadOnly s_serializerCache As New Concurrent.ConcurrentDictionary(Of Type, DataContractJsonSerializer)

    Private Shared Function GetSerializer(t As Type) As DataContractJsonSerializer
        Return s_serializerCache.GetOrAdd(t, Function(key) New DataContractJsonSerializer(key))
    End Function

    Private Shared Function SerializeToJson(Of T)(obj As T) As String
        Using stream As New MemoryStream()
            Dim serializer = GetSerializer(GetType(T))
            serializer.WriteObject(stream, obj)
            stream.Position = 0
            Using reader As New StreamReader(stream)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Function DeserializeFromJson(Of T)(json As String) As T
        If String.IsNullOrWhiteSpace(json) Then Return Nothing
        Using stream As New MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))
            Dim serializer = GetSerializer(GetType(T))
            Return CType(serializer.ReadObject(stream), T)
        End Using
    End Function

#End Region

End Class
