using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The custom title bar, and the live tile push that rides the same startup path.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        private void SetupTitleBar()
        {
            // Extend view into title bar for seamless acrylic
            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            // Set the draggable title bar region
            Window.Current.SetTitleBar(AppTitleBar);

            // The system owns the title bar's height and the width of the corner it reserves for
            // the caption buttons, and it changes both at runtime - tablet mode makes the bar
            // taller, and the reserved corner moves from right to left under RTL. Hardcoding 32px
            // and a right margin was right for exactly one configuration; everywhere else the
            // drag region and the caption buttons disagreed about where the title bar ended.
            // The subscription itself is attached in MainPage_Loaded and released in
            // MainPage_Unloaded, alongside the page's other handlers; this call is just the
            // initial value, so the first frame is drawn with the right height instead of the
            // XAML placeholder.
            ApplyTitleBarLayoutMetrics(coreTitleBar);

            // Make title bar buttons transparent to match acrylic
            UpdateTitleBarColors();
        }

        /// <summary>
        /// Mirrors the system's current title bar metrics onto the custom title bar: the row
        /// height, and the two padding columns that keep content clear of the caption buttons.
        ///
        /// The insets are applied as Grid columns rather than a Margin so that AppTitleBar's
        /// background still paints underneath the caption buttons - this app makes those buttons
        /// transparent in <see cref="UpdateTitleBarColors"/>, so anything that stopped short of
        /// the window edge would show as a notch of bare window behind them.
        /// </summary>
        private void ApplyTitleBarLayoutMetrics(CoreApplicationViewTitleBar coreTitleBar)
        {
            if (coreTitleBar == null) return;

            // Height is 0 before the view is fully initialized; keep the XAML value until the
            // system reports a real one rather than collapsing the bar to nothing.
            if (coreTitleBar.Height > 0)
            {
                AppTitleBar.Height = coreTitleBar.Height;
            }

            TitleBarLeftInset.Width = new GridLength(coreTitleBar.SystemOverlayLeftInset);
            TitleBarRightInset.Width = new GridLength(coreTitleBar.SystemOverlayRightInset);
        }

        private void OnTitleBarLayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            try
            {
                ApplyTitleBarLayoutMetrics(sender);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: Failed to apply title bar layout metrics - {ex.Message}");
            }
        }

        private void UpdateTitleBarColors()
        {
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;

            var isDark = IsEffectiveThemeDark();

            var fgColor = isDark ? Colors.White : Colors.Black;
            var inactiveFg = isDark ? Color.FromArgb(128, 255, 255, 255) : Color.FromArgb(128, 0, 0, 0);
            var hoverBg = isDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            var pressedBg = isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0);

            // Button colors - transparent with subtle hover
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedBackgroundColor = pressedBg;

            // Button foreground colors
            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = fgColor;
            titleBar.ButtonPressedForegroundColor = fgColor;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }

        private void UpdateLiveTile()
        {
            try
            {
                // Update Live Tile with latest news. Built here rather than in the service so the
                // tile text sits alongside the rest of the shell's display strings; the service
                // only knows how to render whatever it is handed.
                List<NewsItem> newsItems = new List<NewsItem>()
                {
                    new NewsItem(LocalizedStrings.Get("TileNewsWhatsNewTitle"),
                                 LocalizedStrings.Get("TileNewsWhatsNewBody"),
                                 "welcome"),
                    new NewsItem(LocalizedStrings.Get("TileNewsGetStartedTitle"),
                                 LocalizedStrings.Get("TileNewsGetStartedBody"),
                                 "features")
                };

                // Update tile with cycling news
                LiveTileService.UpdateTileWithMultipleNews(newsItems);

                // Show badge indicating new content
                LiveTileService.UpdateBadgeGlyph("newMessage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: UpdateLiveTile failed – {ex.Message}");
            }
        }

    }
}
