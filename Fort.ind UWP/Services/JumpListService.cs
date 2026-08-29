using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.StartScreen;

namespace Fort.ind_UWP
{
    public sealed class JumpListService
    {
        private JumpListService()
        {
        }

        private const int TaskRevision = 4;

        private static JumpTask[] BuildTasks()
        {
            return new JumpTask[]
            {
                new JumpTask(AppConstants.NavigationLatestNews,
                             LocalizedStrings.Get("JumpTaskHomeName"),
                             LocalizedStrings.Get("JumpTaskHomeDescription"), "Home"),
                new JumpTask(AppConstants.NavigationGames,
                             LocalizedStrings.Get("JumpTaskGamesName"),
                             LocalizedStrings.Get("JumpTaskGamesDescription"), "Games"),
                new JumpTask(AppConstants.NavigationSocial,
                             LocalizedStrings.Get("JumpTaskSocialName"),
                             LocalizedStrings.Get("JumpTaskSocialDescription"), "Social"),
                new JumpTask(AppConstants.NavigationProfile,
                             LocalizedStrings.Get("JumpTaskProfileName"),
                             LocalizedStrings.Get("JumpTaskProfileDescription"), "Profile"),
                new JumpTask(AppConstants.NavigationSettings,
                             LocalizedStrings.Get("JumpTaskSettingsName"),
                             LocalizedStrings.Get("JumpTaskSettingsDescription"), "Settings")
            };
        }

        private const string LogoFolderUri = "ms-appx:///Assets/JumpList/";

        private static string CurrentRevisionStamp()
        {
            string language;
            try
            {
                var languages = Windows.Globalization.ApplicationLanguages.Languages;
                language = languages.Count > 0 ? languages[0] : "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JumpListService: could not read the current language - {ex.Message}");
                language = "";
            }

            return $"{TaskRevision}|{language}";
        }

        public static async Task EnsureTasksAsync()
        {
            try
            {
                if (!JumpList.IsSupported()) return;

                var settings = ApplicationData.Current.LocalSettings;

                string savedRevision = null;
                object raw;
                if (settings.Values.TryGetValue(AppConstants.SettingJumpListRevision, out raw) && raw != null)
                {
                    try
                    {
                        savedRevision = Convert.ToString(raw);
                    }
                    catch
                    {
                        savedRevision = null;
                    }
                }

                var currentRevision = CurrentRevisionStamp();
                if (string.Equals(savedRevision, currentRevision, StringComparison.Ordinal)) return;

                var jumpList = await JumpList.LoadCurrentAsync();

                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

                jumpList.Items.Clear();
                foreach (var task in BuildTasks())
                {
                    var item = JumpListItem.CreateWithArguments(
                        AppConstants.JumpArgumentPrefix + task.NavigationTag,
                        task.DisplayName);
                    item.Description = task.Description;
                    item.Logo = new Uri(LogoFolderUri + task.LogoFileName + ".png");

                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();

                settings.Values[AppConstants.SettingJumpListRevision] = currentRevision;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JumpListService: EnsureTasksAsync failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static string ResolveNavTag(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return null;
            if (!arguments.StartsWith(AppConstants.JumpArgumentPrefix, StringComparison.Ordinal)) return null;

            var tag = arguments.Substring(AppConstants.JumpArgumentPrefix.Length);
            switch (tag)
            {
                case AppConstants.NavigationLatestNews:
                case AppConstants.NavigationGames:
                case AppConstants.NavigationBetas:
                case AppConstants.NavigationProfile:
                case AppConstants.NavigationSocial:
                case AppConstants.NavigationSettings:
                    return tag;
                default:
                    Debug.WriteLine($"JumpListService: ignoring unrecognised jump list tag '{tag}'");
                    return null;
            }
        }

        private sealed class JumpTask
        {
            public JumpTask(string navigationTag, string displayName, string description, string logoFileName)
            {
                NavigationTag = navigationTag;
                DisplayName = displayName;
                Description = description;
                LogoFileName = logoFileName;
            }

            public string NavigationTag { get; private set; }
            public string DisplayName { get; private set; }
            public string Description { get; private set; }

            public string LogoFileName { get; private set; }
        }
    }
}
