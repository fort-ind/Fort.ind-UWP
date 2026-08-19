using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.StartScreen;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Publishes the app's taskbar / Start jump list - the shortcut menu Windows shows when the
    /// user right-clicks the app's icon - and translates a task's argument string back into a
    /// navigation tag when Windows launches the app from one.
    ///
    /// JumpList is UniversalApiContract 2.0 (build 10586), well below this project's 1809 floor,
    /// so unlike XamlRoot it needs no ApiInformation probe. It does need IsSupported(), which is
    /// a device-family check rather than a version one: only desktop persists jump list changes.
    /// </summary>
    public sealed class JumpListService
    {

        private JumpListService()
        {
        }

        /// <summary>
        /// Bumped whenever <see cref="s_tasks"/> changes. SaveAsync is cross-process shell work,
        /// so it is skipped on every launch that would only rewrite the identical list; the
        /// revision last handed to the shell is recorded in LocalSettings.
        ///
        /// This also means a user who removed a task does not get it forced back on next launch -
        /// though as written every task sits in the built-in Tasks group (empty GroupName), and
        /// only items in a *custom* group can be removed by the user, so RemovedByUser never
        /// comes into play. Adding a custom group later would mean honouring it.
        /// </summary>
        private const int TaskRevision = 3;

        /// <summary>
        /// One entry per task, in the order they should appear - the nav pane's own order, with
        /// Settings last where it conventionally sits. Five is still inside Microsoft's taskbar
        /// guidance of surfacing only the genuinely useful destinations, but it is the ceiling:
        /// every extra row makes the rest harder to find.
        /// </summary>
        private static readonly JumpTask[] s_tasks =
        {
            new JumpTask(AppConstants.NavigationLatestNews, "home", "Open the home page", "Home"),
            new JumpTask(AppConstants.NavigationGames, "games", "Browse the games catalogue", "Games"),
            new JumpTask(AppConstants.NavigationSocial, "social", "Open the fort.social feed", "Social"),
            new JumpTask(AppConstants.NavigationProfile, "your profile", "View your profile", "Profile"),
            new JumpTask(AppConstants.NavigationSettings, "settings", "Open app settings", "Settings")
        };

        /// <summary>
        /// Folder holding the task icons, one PNG family per task.
        ///
        /// These are the same Segoe MDL2 glyphs the nav pane draws (via SymbolIcon - note the
        /// Symbol enum uses the legacy E1xx codepoints, not the E7xx ones, so Home is E10F and
        /// not E80F), rasterised onto a solid #2D1B69 plate. Two constraints forced that:
        ///
        /// - Logo takes only an ms-appx:/ms-appdata: image URI. A FontIcon is not an option, so
        ///   the glyphs have to be baked to PNG regardless.
        /// - The jump list is drawn by the shell in the *system* theme, not the app's, so an
        ///   asset the shell might pick has to be legible on both a near-black and a near-white
        ///   menu. A single white or near-black glyph provably is not.
        ///
        /// Hence three variants per icon, which is the same shape the app's own Square44x44Logo
        /// already uses:
        ///
        /// - unqualified: the glyph on a solid #2D1B69 plate. Theme-proof on any background, and
        ///   its square corners agree with ControlCornerRadius=0 in App.xaml. This is the
        ///   fallback, and deliberately so - the shell's documented behaviour for a monochrome
        ///   icon that fails its contrast check is to fall back to the plated asset.
        /// - altform-unplated: transparent, white glyph, for the dark shell.
        /// - altform-lightunplated: transparent, #2D1B69 glyph, for the light shell.
        ///
        /// The altform pair is the part that is not guaranteed: those qualifiers are documented
        /// for the taskbar and window switchers, and the Logo contract only promises resolution
        /// for "languages and DPI plateau". If the shell does not apply them to jump lists, the
        /// unqualified plated asset is what gets drawn and nothing regresses.
        ///
        /// Every variant ships scale-100/200/400 (32/64/128px) and is referenced without any
        /// qualifier - DPI resolution is the one MRT behaviour the Logo contract does guarantee.
        /// </summary>
        private const string LogoFolderUri = "ms-appx:///Assets/JumpList/";

        /// <summary>
        /// Writes the task list to the shell if it isn't already current. Safe to call on every
        /// launch; safe to call on a device that has no jump lists.
        /// </summary>
        public static async Task EnsureTasksAsync()
        {
            try
            {
                if (!JumpList.IsSupported()) return;

                var settings = ApplicationData.Current.LocalSettings;

                // Convert rather than an (int) cast, for the reason the boolean settings reads
                // give: the cast throws on anything that isn't a boxed int, and this whole method
                // swallows exceptions - the jump list would just quietly stop updating.
                int savedRevision = 0;
                object raw;
                if (settings.Values.TryGetValue(AppConstants.SettingJumpListRevision, out raw) && raw != null)
                {
                    try
                    {
                        savedRevision = Convert.ToInt32(raw);
                    }
                    catch
                    {
                        savedRevision = 0;
                    }
                }

                if (savedRevision == TaskRevision) return;

                var jumpList = await JumpList.LoadCurrentAsync();

                // The system group defaults to Recent, which is populated from file activations.
                // This app registers a protocol but no file types, so that section would sit
                // there permanently empty.
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

                jumpList.Items.Clear();
                foreach (var task in s_tasks)
                {
                    var item = JumpListItem.CreateWithArguments(
                        AppConstants.JumpArgumentPrefix + task.NavigationTag,
                        task.DisplayName);
                    item.Description = task.Description;
                    item.Logo = new Uri(LogoFolderUri + task.LogoFileName + ".png");

                    // GroupName left unset on purpose - that is what puts an item in the built-in
                    // Tasks group. See the note on TaskRevision.
                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();

                settings.Values[AppConstants.SettingJumpListRevision] = TaskRevision;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JumpListService: EnsureTasksAsync failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Turns the argument string Windows hands back on activation into a navigation tag, or
        /// null if it isn't one of ours.
        ///
        /// The argument arrives from the shell, so it is untrusted: a stale task left by an older
        /// build, or anything else that can launch the app with arguments, could supply a value
        /// this build no longer has a nav item for. Everything is whitelisted against the tags
        /// that exist now, which also stops RememberLastNavTag persisting a junk value that the
        /// next resume would try to restore.
        /// </summary>
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

        /// <summary>
        /// One row of the task table. A plain class rather than a tuple: ValueTuple is not part
        /// of the framework this project targets without a package reference, and LangVersion is
        /// pinned to 7.3 anyway.
        /// </summary>
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

            /// <summary>
            /// Base name under Assets\JumpList, without the scale qualifier or extension.
            /// </summary>
            public string LogoFileName { get; private set; }
        }

    }
}
