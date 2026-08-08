using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Service for managing Live Tile updates with news and notifications
    /// </summary>
    public class LiveTileService
    {

        /// <summary>
        /// Updates the Live Tile with the latest news
        /// </summary>
        public static void UpdateTileWithNews(string title, string message, string branding = "name", TileAnimation animationType = TileAnimation.FadeIn)
        {
            try
            {
                // Create the tile notification content
                var tileXml = CreateTileXml(title, message, branding, animationType);

                // Create and send the notification
                TileNotification tileNotification = new TileNotification(tileXml);
                TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateTileWithNews failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the Live Tile with multiple news items that cycle
        /// </summary>
        public static void UpdateTileWithMultipleNews(List<NewsItem> newsItems)
        {
            if (newsItems == null || newsItems.Count == 0) return;

            try
            {
                // Enable notification queue to show multiple tiles
                var tileUpdater = TileUpdateManager.CreateTileUpdaterForApplication();
                tileUpdater.EnableNotificationQueue(true);

                // Clear existing notifications
                tileUpdater.Clear();

                // Animation types to cycle through
                TileAnimation[] animations = {
                    TileAnimation.FadeIn,
                    TileAnimation.SlideUp,
                    TileAnimation.SlideDown,
                    TileAnimation.SlideLeft,
                    TileAnimation.SlideRight
                };

                // Add each news item (max 5 in queue)
                for (int i = 0; i <= Math.Min(newsItems.Count - 1, 4); i++)
                {
                    var item = newsItems[i];
                    if (item == null) continue;

                    var animation = animations[i % animations.Length];
                    var tileXml = CreateTileXml(item.Title, item.Message, "name", animation);
                    TileNotification tileNotification = new TileNotification(tileXml);
                    tileNotification.Tag = string.IsNullOrWhiteSpace(item.Tag) ? $"news{i}" : item.Tag;
                    tileUpdater.Update(tileNotification);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateTileWithMultipleNews failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the adaptive tile content for all four tile sizes with the requested animation
        /// style, using the Notifications library's typed object model instead of hand-written XML
        /// strings - removes the need to hand-escape every field into a raw XML string.
        /// </summary>
        private static XmlDocument CreateTileXml(string title, string message, string branding, TileAnimation animation = TileAnimation.FadeIn)
        {
            var titleStyle = GetTitleTextStyle(animation);
            var safeBranding = ParseBranding(branding);
            var safeTitle = SanitizeText(title);
            var safeMessage = SanitizeText(message);
            var smallTileText = SanitizeText(GetTileMonogram(title, branding));

            TileContent content = new TileContent();
            content.Visual = new TileVisual();
            content.Visual.DisplayName = "Fort.ind";
            content.Visual.Branding = safeBranding;

            // Small tile: centered monogram
            AdaptiveText smallText = new AdaptiveText();
            smallText.Text = smallTileText;
            smallText.HintStyle = AdaptiveTextStyle.Caption;
            smallText.HintAlign = AdaptiveTextAlign.Center;

            TileBindingContentAdaptive smallContent = new TileBindingContentAdaptive();
            smallContent.TextStacking = TileTextStacking.Center;
            smallContent.Children.Add(smallText);

            content.Visual.TileSmall = new TileBinding();
            content.Visual.TileSmall.Content = smallContent;

            // Medium and wide tiles: title + wrapped message in a group/subgroup, differing only in
            // whether the title itself wraps and how many message lines are allowed
            content.Visual.TileMedium = new TileBinding();
            content.Visual.TileMedium.Content = BuildTitleMessageGroup(safeTitle, titleStyle, true, safeMessage, AdaptiveTextStyle.CaptionSubtle, 3);

            content.Visual.TileWide = new TileBinding();
            content.Visual.TileWide.Content = BuildTitleMessageGroup(safeTitle, titleStyle, false, safeMessage, AdaptiveTextStyle.Body, 2);

            // Large tile: centered title in a group, message and static branding line below it
            TileBindingContentAdaptive largeContent = new TileBindingContentAdaptive();
            largeContent.TextStacking = TileTextStacking.Center;

            AdaptiveText largeTitleText = new AdaptiveText();
            largeTitleText.Text = safeTitle;
            largeTitleText.HintStyle = titleStyle;
            largeTitleText.HintAlign = AdaptiveTextAlign.Center;

            AdaptiveSubgroup largeSubgroup = new AdaptiveSubgroup();
            largeSubgroup.Children.Add(largeTitleText);

            AdaptiveGroup largeGroup = new AdaptiveGroup();
            largeGroup.Children.Add(largeSubgroup);

            AdaptiveText largeMessageText = new AdaptiveText();
            largeMessageText.Text = safeMessage;
            largeMessageText.HintStyle = AdaptiveTextStyle.Body;
            largeMessageText.HintWrap = true;
            largeMessageText.HintMaxLines = 6;
            largeMessageText.HintAlign = AdaptiveTextAlign.Center;

            AdaptiveText largeBrandingText = new AdaptiveText();
            largeBrandingText.Text = "Fort.ind Desktop";
            largeBrandingText.HintStyle = AdaptiveTextStyle.CaptionSubtle;
            largeBrandingText.HintAlign = AdaptiveTextAlign.Center;

            largeContent.Children.Add(largeGroup);
            largeContent.Children.Add(largeMessageText);
            largeContent.Children.Add(largeBrandingText);

            content.Visual.TileLarge = new TileBinding();
            content.Visual.TileLarge.Content = largeContent;

            return content.GetXml();
        }

        /// <summary>
        /// Builds a group/subgroup containing a styled title line and a wrapped message line -
        /// shared by the medium and wide tile bindings, which only differ in title wrap and
        /// message max-line settings.
        /// </summary>
        private static TileBindingContentAdaptive BuildTitleMessageGroup(string title, AdaptiveTextStyle titleStyle, bool wrapTitle, string message, AdaptiveTextStyle messageStyle, int messageMaxLines)
        {
            AdaptiveText titleText = new AdaptiveText();
            titleText.Text = title;
            titleText.HintStyle = titleStyle;
            titleText.HintWrap = wrapTitle;

            AdaptiveText messageText = new AdaptiveText();
            messageText.Text = message;
            messageText.HintStyle = messageStyle;
            messageText.HintWrap = true;
            messageText.HintMaxLines = messageMaxLines;

            AdaptiveSubgroup subgroup = new AdaptiveSubgroup();
            subgroup.Children.Add(titleText);
            subgroup.Children.Add(messageText);

            AdaptiveGroup group = new AdaptiveGroup();
            group.Children.Add(subgroup);

            TileBindingContentAdaptive result = new TileBindingContentAdaptive();
            result.Children.Add(group);
            return result;
        }

        /// <summary>
        /// Maps the tile's requested animation to the AdaptiveText style used for the title line.
        /// </summary>
        private static AdaptiveTextStyle GetTitleTextStyle(TileAnimation animation)
        {
            switch (animation)
            {
                case TileAnimation.FadeIn:
                    return AdaptiveTextStyle.CaptionSubtle;
                case TileAnimation.SlideUp:
                    return AdaptiveTextStyle.Base;
                case TileAnimation.SlideDown:
                    return AdaptiveTextStyle.Body;
                case TileAnimation.SlideLeft:
                    return AdaptiveTextStyle.BodySubtle;
                case TileAnimation.SlideRight:
                    return AdaptiveTextStyle.Subtitle;
                default:
                    return AdaptiveTextStyle.Default;
            }
        }

        /// <summary>
        /// Maps the branding string (always "name" from current call sites, but kept as a
        /// parameter for compatibility) to the TileBranding enum the Notifications library expects.
        /// </summary>
        private static TileBranding ParseBranding(string branding)
        {
            switch ((branding ?? "").Trim().ToLowerInvariant())
            {
                case "none":
                    return TileBranding.None;
                case "logo":
                    return TileBranding.Logo;
                case "nameandlogo":
                    return TileBranding.NameAndLogo;
                default:
                    return TileBranding.Name;
            }
        }

        /// <summary>
        /// Strips characters that aren't legal in XML 1.0 (the Notifications library still
        /// serializes to XML under the hood) while passing valid surrogate pairs (e.g. emoji)
        /// through unchanged. Entity escaping (&amp;, &lt;, etc.) is handled by the library itself,
        /// so this only needs to guard against characters that would make the XML invalid outright.
        /// </summary>
        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            StringBuilder sanitized = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];

                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sanitized.Append(ch);
                    sanitized.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if ((ch == '\t') || (ch == '\n') || (ch == '\r') ||
                    (ch >= '\u0020' && ch <= '\uD7FF') ||
                    (ch >= '\uE000' && ch <= '\uFFFD'))
                {
                    sanitized.Append(ch);
                }

                i += 1;
            }

            return sanitized.ToString();
        }

        /// <summary>
        /// Whether the app may put a badge on its tile and taskbar icon. Backed by LocalSettings so
        /// the choice survives a restart, and enforced here rather than at each call site so no
        /// caller can bypass it. Absent (or unreadable) means enabled, which is how the app behaved
        /// before this setting existed. ClearBadge deliberately ignores this - clearing is always
        /// allowed.
        /// </summary>
        public static bool BadgeEnabled
        {
            get
            {
                try
                {
                    var stored = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingShowTileBadge];
                    if (stored == null) return true;
                    return Convert.ToBoolean(stored);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: BadgeEnabled read failed – {ex.GetType().Name}: {ex.Message}");
                    return true;
                }
            }
            set
            {
                try
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingShowTileBadge] = value;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LiveTileService: BadgeEnabled write failed – {ex.GetType().Name}: {ex.Message}");
                }

                // Switching it off takes effect on the tile now, rather than leaving the badge
                // already on screen sitting there until something else happens to clear it.
                if (!value) ClearBadge();
            }
        }

        /// <summary>
        /// Updates the badge on the tile (shows a number or glyph)
        /// </summary>
        public static void UpdateBadge(int count)
        {
            try
            {
                if (!BadgeEnabled) return;

                if (count <= 0)
                {
                    ClearBadge();
                    return;
                }

                var clampedCount = Math.Min(count, 99);
                var badgeXml = $"<badge value=\"{clampedCount}\"/>";
                XmlDocument badgeDoc = new XmlDocument();
                badgeDoc.LoadXml(badgeXml);

                BadgeNotification badgeNotification = new BadgeNotification(badgeDoc);
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateBadge failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the badge with a glyph (icon)
        /// </summary>
        public static void UpdateBadgeGlyph(string glyph)
        {
            if (string.IsNullOrWhiteSpace(glyph)) return;

            try
            {
                if (!BadgeEnabled) return;

                var normalizedGlyph = glyph.Trim();
                if (!IsSupportedBadgeGlyph(normalizedGlyph))
                {
                    Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph skipped unsupported glyph '{normalizedGlyph}'.");
                    return;
                }

                // Available glyphs: none, activity, alarm, alert, attention, available, away, busy,
                // error, newMessage, paused, playing, unavailable
                var badgeXml = $"<badge value=\"{normalizedGlyph}\"/>";
                XmlDocument badgeDoc = new XmlDocument();
                badgeDoc.LoadXml(badgeXml);

                BadgeNotification badgeNotification = new BadgeNotification(badgeDoc);
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badgeNotification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: UpdateBadgeGlyph failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a Windows toast notification with a title and message.
        /// Returns False (and writes to Debug output) if notifications are blocked or an error occurs.
        /// </summary>
        public static bool SendToast(string title, string message)
        {
            try
            {
                var notifier = ToastNotificationManager.CreateToastNotifier();
                if (notifier.Setting != NotificationSetting.Enabled)
                {
                    Debug.WriteLine($"LiveTileService: Toast suppressed – NotificationSetting is {notifier.Setting}. " +
                                    "Enable notifications for this app in Windows Settings > System > Notifications.");
                    return false;
                }

                var toastContent = new ToastContentBuilder()
                    .AddText(SanitizeText(title))
                    .AddText(SanitizeText(message))
                    .GetToastContent();

                ToastNotification toast = new ToastNotification(toastContent.GetXml());
                notifier.Show(toast);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: SendToast failed – {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clears the Live Tile back to default
        /// </summary>
        public static void ClearTile()
        {
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ClearTile failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the badge
        /// </summary>
        public static void ClearBadge()
        {
            try
            {
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiveTileService: ClearBadge failed – {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string GetTileMonogram(string primaryText, string fallbackText)
        {
            var source = string.IsNullOrWhiteSpace(primaryText) ? fallbackText : primaryText;
            if (string.IsNullOrWhiteSpace(source)) return "FI";

            var trimmed = source.Trim();
            if (trimmed.Length <= 2) return trimmed.ToUpperInvariant();
            return trimmed.Substring(0, 2).ToUpperInvariant();
        }

        private static bool IsSupportedBadgeGlyph(string glyph)
        {
            switch (glyph.ToLowerInvariant())
            {
                case "none":
                case "activity":
                case "alarm":
                case "alert":
                case "attention":
                case "available":
                case "away":
                case "busy":
                case "error":
                case "newmessage":
                case "paused":
                case "playing":
                case "unavailable":
                    return true;
                default:
                    return false;
            }
        }

    }

    /// <summary>
    /// Tile animation types
    /// </summary>
    public enum TileAnimation
    {
        FadeIn,
        SlideUp,
        SlideDown,
        SlideLeft,
        SlideRight
    }

    /// <summary>
    /// Represents a news item for the Live Tile
    /// </summary>
    public class NewsItem
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string Tag { get; set; }
        public DateTime Timestamp { get; set; }

        public NewsItem(string title, string message, string tag = null)
        {
            this.Title = title;
            this.Message = message;
            this.Tag = tag;
            this.Timestamp = DateTime.Now;
        }
    }
}
