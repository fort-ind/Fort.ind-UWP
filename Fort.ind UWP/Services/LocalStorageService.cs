using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class LocalStorageService
    {
        private static readonly StorageFolder LocalFolder = ApplicationData.Current.LocalFolder;
        private const string PROFILE_FILE = "misskey_profile.json";

        public static Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        #region Profile Cache

        public static async Task<bool> SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId))
            {
                return false;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = await LocalFolder.CreateFileAsync(PROFILE_FILE, CreationCollisionOption.ReplaceExisting);
                var json = SerializeToJson(profile);
                await FileIO.WriteTextAsync(file, json).AsTask(cancellationToken);

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Profile save operation was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving profile: {ex.Message}");
                return false;
            }
        }

        public static async Task<UserProfile> LoadProfileAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = await LocalFolder.TryGetItemAsync(PROFILE_FILE) as StorageFile;
                if (file == null) return null;

                var json = await FileIO.ReadTextAsync(file).AsTask(cancellationToken);
                return DeserializeFromJson<UserProfile>(json);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Profile load operation was cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading profile: {ex.Message}");
                return null;
            }
        }

        public static async Task ClearProfileAsync()
        {
            try
            {
                var file = await LocalFolder.TryGetItemAsync(PROFILE_FILE) as StorageFile;
                if (file == null) return;

                await file.DeleteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing profile: {ex.Message}");
            }
        }

        #endregion

        #region Settings

        public static string DataPath
        {
            get
            {
                return LocalFolder.Path;
            }
        }

        public static async Task ResetAllAppDataAsync()
        {
            try
            {
                var items = await LocalFolder.GetItemsAsync();
                foreach (var item in items)
                {
                    try
                    {
                        await item.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting {item.Name} during app reset: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enumerating local folder during app reset: {ex.Message}");
            }

            try
            {
                ApplicationData.Current.LocalSettings.Values.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing local settings during app reset: {ex.Message}");
            }
        }

        #endregion

        #region JSON Serialization

        private static readonly ConcurrentDictionary<Type, DataContractJsonSerializer> s_serializerCache =
            new ConcurrentDictionary<Type, DataContractJsonSerializer>();

        private static DataContractJsonSerializer GetSerializer(Type t)
        {
            return s_serializerCache.GetOrAdd(t, key => new DataContractJsonSerializer(key));
        }

        private static string SerializeToJson<T>(T obj)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = GetSerializer(typeof(T));
                serializer.WriteObject(stream, obj);
                stream.Position = 0;
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static T DeserializeFromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            {
                var serializer = GetSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        #endregion
    }
}
