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
    Public Shared Async Function InitializeAsync() As Task
        Try
            ' Future: Load storage mode from settings
            CurrentStorageMode = StorageMode.JsonFile
        Catch ex As Exception
            Debug.WriteLine($"Error initializing storage: {ex.Message}")
        End Try
    End Function

#Region "Profile Operations"

    ''' <summary>
    ''' Saves a user profile to local storage
    ''' </summary>
    Public Shared Async Function SaveProfileAsync(profile As UserProfile) As Task(Of Boolean)
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
    ''' Loads a user profile by username
    ''' </summary>
    Public Shared Async Function LoadProfileByUsernameAsync(username As String) As Task(Of UserProfile)
        Try
            Dim profiles = Await GetAllProfilesAsync()
            Return profiles.FirstOrDefault(Function(p) p.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
        Catch ex As Exception
            Debug.WriteLine($"Error loading profile by username: {ex.Message}")
            Return Nothing
        End Try
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
                If file.Name.EndsWith(".json") AndAlso file.Name <> CURRENT_USER_FILE Then
                    Try
                        Dim json = Await FileIO.ReadTextAsync(file)
                        Dim profile = DeserializeFromJson(Of UserProfile)(json)
                        If profile IsNot Nothing Then
                            profiles.Add(profile)
                        End If
                    Catch
                        ' Skip invalid files
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
        Try
            Dim profilesFolder = Await LocalFolder.GetFolderAsync(PROFILES_FOLDER)
            Dim fileName = $"{userId}.json"
            Dim file = Await profilesFolder.GetFileAsync(fileName)
            Await file.DeleteAsync()
            Return True
        Catch ex As Exception
            Debug.WriteLine($"Error deleting profile: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Checks if a username is already taken
    ''' </summary>
    Public Shared Async Function IsUsernameTakenAsync(username As String) As Task(Of Boolean)
        Dim profiles = Await GetAllProfilesAsync()
        Return profiles.Any(Function(p) p.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
    End Function

#End Region

#Region "Session Management"

    ''' <summary>
    ''' Saves the current logged-in user ID
    ''' </summary>
    Public Shared Async Function SaveCurrentUserIdAsync(userId As String) As Task
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
            Return Await FileIO.ReadTextAsync(file)
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
    ''' Gets the number of registered users
    ''' </summary>
    Public Shared Async Function GetUserCountAsync() As Task(Of Integer)
        Dim profiles = Await GetAllProfilesAsync()
        Return profiles.Count
    End Function

#End Region

#Region "JSON Serialization"

    Private Shared Function SerializeToJson(Of T)(obj As T) As String
        Using stream As New MemoryStream()
            Dim serializer As New DataContractJsonSerializer(GetType(T))
            serializer.WriteObject(stream, obj)
            stream.Position = 0
            Using reader As New StreamReader(stream)
                Return reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Shared Function DeserializeFromJson(Of T)(json As String) As T
        Using stream As New MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))
            Dim serializer As New DataContractJsonSerializer(GetType(T))
            Return CType(serializer.ReadObject(stream), T)
        End Using
    End Function

#End Region

End Class
