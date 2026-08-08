using System;
using System.Runtime.Serialization;

namespace Fort.ind_UWP
{
    /// <summary>
    /// A cached snapshot of the signed-in fort.social (Misskey) account.
    /// The instance is the source of truth; this is a local copy for offline display.
    /// </summary>
    [DataContract]
    public class UserProfile
    {

        /// <summary>
        /// The Misskey user ID on the instance.
        /// </summary>
        [DataMember]
        public string UserId { get; set; }

        /// <summary>
        /// Misskey username (without the leading @ or host).
        /// </summary>
        [DataMember]
        public string Username { get; set; }

        /// <summary>
        /// Remote instance host, or null/empty for a local fort.social account.
        /// </summary>
        [DataMember]
        public string Host { get; set; }

        /// <summary>
        /// Display name shown in the app
        /// </summary>
        [DataMember]
        public string DisplayName { get; set; }

        /// <summary>
        /// Bio/description, as set on fort.social
        /// </summary>
        [DataMember]
        public string Bio { get; set; }

        /// <summary>
        /// URL of the user's avatar image on the instance.
        /// </summary>
        [DataMember]
        public string AvatarUrl { get; set; }

        /// <summary>
        /// When the fort.social account was created
        /// </summary>
        [DataMember]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Last time this app signed the user in
        /// </summary>
        [DataMember]
        public DateTime LastLoginDate { get; set; }

        /// <summary>
        /// User preferences and settings.
        /// Getter ensures this is never null even after deserialization
        /// (DataContractJsonSerializer bypasses the constructor).
        /// </summary>
        [DataMember]
        public UserPreferences Preferences
        {
            get
            {
                if (_preferences == null) _preferences = new UserPreferences();
                return _preferences;
            }
            set
            {
                _preferences = value;
            }
        }
        private UserPreferences _preferences;

        public UserProfile()
        {
            Preferences = new UserPreferences();
        }

        /// <summary>
        /// Returns a deep copy of this profile. Used so edits can be saved on a detached copy
        /// and only swapped into the shared CurrentUser after the save succeeds, avoiding
        /// readers observing a half-applied mutation.
        /// </summary>
        public UserProfile Clone()
        {
            UserProfile copy = new UserProfile()
            {
                UserId = this.UserId,
                Username = this.Username,
                Host = this.Host,
                DisplayName = this.DisplayName,
                Bio = this.Bio,
                AvatarUrl = this.AvatarUrl,
                CreatedDate = this.CreatedDate,
                LastLoginDate = this.LastLoginDate
            };
            var prefs = this.Preferences;
            copy.Preferences = new UserPreferences()
            {
                EnableLiveTile = prefs.EnableLiveTile,
                EnableNotifications = prefs.EnableNotifications,
                Theme = prefs.Theme
            };
            return copy;
        }

    }

    /// <summary>
    /// User preferences and settings
    /// </summary>
    [DataContract]
    public class UserPreferences
    {

        /// <summary>
        /// Enable Live Tile updates
        /// </summary>
        [DataMember]
        public bool EnableLiveTile { get; set; } = true;

        /// <summary>
        /// Enable notifications
        /// </summary>
        [DataMember]
        public bool EnableNotifications { get; set; } = true;

        /// <summary>
        /// Theme preference (Dark, Light, System)
        /// </summary>
        [DataMember]
        public string Theme { get; set; } = "Dark";

    }
}
