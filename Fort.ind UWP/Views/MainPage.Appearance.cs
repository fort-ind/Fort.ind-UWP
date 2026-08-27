using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Appearance settings: theme, the acrylic tint and its swatches. The colour arithmetic
    /// itself lives in <see cref="ColorHelper"/>.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        // ── Appearance settings ──

        private void LoadAppearanceSettings()
        {
            _loadingSettings = true;
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                // Restore theme selection
                string theme = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTheme))
                {
                    theme = localSettings.Values[AppConstants.SettingAppTheme].ToString();
                }
                switch (theme)
                {
                    case AppConstants.ThemeLight: ThemeLightRadio.IsChecked = true; break;
                    case AppConstants.ThemeDark: ThemeDarkRadio.IsChecked = true; break;
                    default: ThemeSystemRadio.IsChecked = true; break;
                }
                ApplyTheme(theme);

                // Restore tint color selection
                string tintTag = AppConstants.ThemeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintColor))
                {
                    tintTag = localSettings.Values[AppConstants.SettingAppTintColor].ToString();
                }
                // Reset the custom swatch to its palette glyph first - this runs again after an app
                // reset, and a stale color chip would imply a custom tint that no longer exists.
                TintCustomButton.ClearValue(Control.BackgroundProperty);
                TintCustomIcon.Visibility = Visibility.Visible;

                ApplyTintColor(tintTag);
                UpdateTintSelection(tintTag);

                // Keep the last custom pick visible on its swatch even while a preset is active.
                // The glyph still being visible means UpdateTintSelection didn't paint a chip.
                var rememberedCustom = localSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (TintCustomIcon.Visibility == Visibility.Visible && !string.IsNullOrEmpty(rememberedCustom))
                {
                    ShowCustomSwatchColor(rememberedCustom);
                }

                // Restore the tile badge toggle. Inside the _loadingSettings guard so assigning IsOn
                // here doesn't bounce straight back through Toggled and rewrite the setting.
                TileBadgeToggle.IsOn = LiveTileService.BadgeEnabled;

                // Restore settings panel states
                RestoreSettingsPanelStates();
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ApplyTheme(string theme)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null) return;
            switch (theme)
            {
                case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                default: rootFrame.RequestedTheme = ElementTheme.Default; break;
            }
            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme] = theme;
            }
            UpdateTitleBarColors();
            // Repaint the window for the new theme. This runs for the untinted case too, not just
            // for a saved tint: the background is a brush this page builds, so nothing else will
            // swap it from the dark recipe to the light one. Skipped while settings are loading
            // only because LoadAppearanceSettings applies the saved tint immediately after.
            if (!_loadingSettings)
            {
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                // Refresh the swatch chips and the selected swatch's highlight border so they
                // match the new theme (white outline in dark mode, black outline in light mode).
                UpdateTintSelection(savedTint);
            }
        }

        /// <summary>
        /// Repaints the parts of the window this page owns when the *system* theme changes under an
        /// app left on "System default". The window acrylic and the title bar buttons are painted
        /// from code rather than by a theme resource, so without this they keep the colours of the
        /// theme the app started in - light chrome with a dark window and white caption buttons.
        /// </summary>
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            try
            {
                UpdateTitleBarColors();
                var savedTint = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor]?.ToString();
                if (string.IsNullOrEmpty(savedTint)) savedTint = AppConstants.ThemeDefault;
                ApplyTintColor(savedTint);
                UpdateTintSelection(savedTint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: OnActualThemeChanged failed – {ex.Message}");
            }
        }

        /// <summary>
        /// The one window-background acrylic brush, reused for the life of the page. A HostBackdrop
        /// AcrylicBrush is backed by a composition effect that samples the desktop, so building a
        /// fresh one per call was by far the most expensive thing the appearance code did - the
        /// colour picker's live preview calls this on every ColorChanged, i.e. continuously while
        /// the user drags. TintColor is a dependency property, so repainting is just a set.
        /// </summary>
        private AcrylicBrush _surfaceBrush;

        // The untinted window acrylic, per theme. These mirror AppSurfaceAcrylicBrush in App.xaml's
        // ThemeDictionaries; RootGrid's background has to be painted from code because a custom
        // tint cannot be expressed as a theme resource, and the untinted case then has to go the
        // same way rather than through a lookup - a ResourceDictionary indexer does not search
        // ThemeDictionaries, so it would fetch the wrong theme's brush (or nothing at all).
        // Values match the OS's own window acrylic: white at 0.8 in light, SystemChromeMediumLow
        // in dark. Keep the two in step.
        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Colors.White;
        private static readonly Color s_surfaceFallbackLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private void ApplyTintColor(string colorTag)
        {
            try
            {
                // Determine effective theme to choose the right tint shade
                var isDark = IsEffectiveThemeDark();

                Color tint;
                Color fallback;
                double tintOpacity;

                if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault)
                {
                    tint = isDark ? s_surfaceTintDark : s_surfaceTintLight;
                    fallback = isDark ? s_surfaceTintDark : s_surfaceFallbackLight;
                    tintOpacity = 0.8;
                }
                else
                {
                    tint = isDark ? ColorHelper.HexToColor(colorTag) : ColorHelper.ForLightTheme(colorTag);
                    fallback = tint;
                    // Light tints used to sit at 0.6, which let 40% of the desktop through a pale
                    // pastel - enough to drag the whole window grey-brown over a dark wallpaper.
                    // A dark tint absorbs that bleed; a pastel has nothing to absorb it with, so
                    // light holds more of its own colour.
                    tintOpacity = isDark ? 0.8 : 0.85;
                }

                if (_surfaceBrush == null)
                {
                    _surfaceBrush = new AcrylicBrush()
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    };
                }
                _surfaceBrush.TintColor = tint;
                _surfaceBrush.TintOpacity = tintOpacity;
                _surfaceBrush.FallbackColor = fallback;

                if (!ReferenceEquals(RootGrid.Background, _surfaceBrush))
                {
                    RootGrid.Background = _surfaceBrush;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ApplyTintColor failed – {ex.Message}");
            }

            if (!_loadingSettings)
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor] = colorTag;
            }
        }

        /// <summary>
        /// Whether the app is currently rendering dark - the root Frame's explicit theme if it has
        /// one, otherwise the system's. Three call sites needed this identically.
        /// </summary>
        private static bool IsEffectiveThemeDark()
        {
            var rootFrame = Window.Current.Content as Frame;
            var effTheme = rootFrame != null ? rootFrame.RequestedTheme : ElementTheme.Default;
            return effTheme == ElementTheme.Default
                   ? Application.Current.RequestedTheme == ApplicationTheme.Dark
                   : effTheme == ElementTheme.Dark;
        }

        // Base accessible names for the tint swatches, keyed by control – selection state is
        // appended below since the selected swatch is otherwise only shown via border color,
        // which a screen reader cannot see.
        /// <summary>
        /// Each swatch's accessible name exactly as authored - which is to say, whatever its
        /// x:Uid pulled out of Resources.resw. Captured on first use rather than restated in a
        /// table here, so the names have one source; the selected swatch's name is rewritten with
        /// a "(selected)" suffix, and re-reading it later would compound the suffix.
        /// </summary>
        private Dictionary<Button, string> _swatchBaseNames;

        private string BaseSwatchName(Button btn)
        {
            if (_swatchBaseNames == null)
            {
                _swatchBaseNames = new Dictionary<Button, string>();
            }

            string name;
            if (!_swatchBaseNames.TryGetValue(btn, out name))
            {
                name = Windows.UI.Xaml.Automation.AutomationProperties.GetName(btn) ?? "";
                _swatchBaseNames[btn] = name;
            }

            return name;
        }

        /// <summary>
        /// Every preset swatch, in display order. The custom swatch is deliberately excluded -
        /// it has no fixed Tag, so it is matched by elimination rather than by lookup.
        /// Built once: the named fields never change, and this is walked on every tint click and
        /// every theme change.
        /// </summary>
        private Button[] _tintPresetSwatches;

        private Button[] TintPresetSwatches
        {
            get
            {
                if (_tintPresetSwatches == null)
                {
                    _tintPresetSwatches = new Button[] { TintDefaultButton, TintBlueButton, TintPurpleButton, TintGreenButton,
                                                         TintRedButton, TintSlateButton, TintTealButton, TintBronzeButton,
                                                         TintRoseButton, TintOliveButton, TintGraphiteButton };
                }
                return _tintPresetSwatches;
            }
        }

        /// <summary>
        /// Shared brushes for the unselected swatch outline, rather than twelve fresh
        /// SolidColorBrushes every time the selection or theme changes. Brushes are immutable as
        /// far as this code is concerned, so sharing one instance is safe.
        /// Transparent in dark - the chips are vivid enough on their own - but light theme's chips
        /// are pastels on a near-white window and dissolve into it without an edge.
        /// </summary>
        private static readonly SolidColorBrush s_restBrushDark = new SolidColorBrush(Colors.Transparent);
        private static readonly SolidColorBrush s_restBrushLight = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));

        /// <summary>Selection outline for the active swatch - white on dark, black on light.</summary>
        private static readonly SolidColorBrush s_selectedBrushDark = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush s_selectedBrushLight = new SolidColorBrush(Colors.Black);

        // Chip colours per theme, keyed by button. The dark ones are the Background values set in
        // XAML and are captured on first use rather than restated here as a second table; the light
        // ones are the pastels the window actually takes in light theme (ColorHelper's preset
        // table), because a
        // chip painted navy that turns the window pale blue is just a wrong label.
        private Dictionary<Button, Brush> _swatchChipsDark;
        private Dictionary<Button, Brush> _swatchChipsLight;

        private void UpdateSwatchChipColors(bool isDark)
        {
            if (_swatchChipsDark == null)
            {
                _swatchChipsDark = new Dictionary<Button, Brush>();
                _swatchChipsLight = new Dictionary<Button, Brush>();
                foreach (var btn in TintPresetSwatches)
                {
                    var tag = btn.Tag?.ToString() ?? "";
                    // Skips the Default swatch, which has no chip - it carries a glyph on the
                    // ordinary button chrome, and that is already theme-aware.
                    var lightHex = ColorHelper.TryGetLightPreset(tag);
                    if (lightHex == null) continue;
                    _swatchChipsDark[btn] = btn.Background;
                    _swatchChipsLight[btn] = new SolidColorBrush(ColorHelper.HexToColor(lightHex));
                }
            }

            var chips = isDark ? _swatchChipsDark : _swatchChipsLight;
            foreach (var pair in chips)
            {
                pair.Key.Background = pair.Value;
            }
        }

        private void UpdateTintSelection(string selectedTag)
        {
            selectedTag = string.IsNullOrEmpty(selectedTag) ? AppConstants.ThemeDefault : selectedTag;

            var isDark = IsEffectiveThemeDark();
            var restBrush = isDark ? s_restBrushDark : s_restBrushLight;
            UpdateSwatchChipColors(isDark);

            Button sel = null;
            foreach (var btn in TintPresetSwatches)
            {
                btn.BorderBrush = restBrush;
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(btn, BaseSwatchName(btn));
                var tag = btn.Tag?.ToString() ?? "";
                if (string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)) sel = btn;
            }

            // Anything that isn't Default and isn't one of the presets is a custom color, so the
            // custom swatch both takes the selection border and previews the color itself.
            TintCustomButton.BorderBrush = restBrush;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(TintCustomButton, BaseSwatchName(TintCustomButton));
            if (sel == null)
            {
                sel = TintCustomButton;
                ShowCustomSwatchColor(selectedTag);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    TintCustomButton,
                    LocalizedStrings.Format("TintCustomSwatchWithColorFormat",
                                            BaseSwatchName(TintCustomButton), selectedTag));
            }
            else if (TintCustomIcon.Visibility == Visibility.Collapsed)
            {
                // A preset is active, but the custom swatch is still previewing the last custom
                // pick (its palette glyph is hidden). Repaint that from the stored value so it
                // follows the theme too, instead of keeping the shade it was painted in.
                var remembered = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppCustomTintColor] as string;
                if (!string.IsNullOrEmpty(remembered))
                {
                    ShowCustomSwatchColor(remembered);
                }
            }

            if (sel != null)
            {
                sel.BorderBrush = isDark ? s_selectedBrushDark : s_selectedBrushLight;
                var selBaseName = Windows.UI.Xaml.Automation.AutomationProperties.GetName(sel);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(
                    sel, LocalizedStrings.Format("TintSwatchSelectedSuffixFormat", selBaseName));
            }
        }

        /// <summary>
        /// Paints the custom swatch with the given color and hides its palette glyph, so the
        /// button reads as a color chip once the user has actually chosen one. In light theme it
        /// shows the lightened shade the window will actually take, the same way the preset chips
        /// do - <paramref name="hex"/> is always the stored (dark) value.
        /// </summary>
        private void ShowCustomSwatchColor(string hex)
        {
            try
            {
                var c = IsEffectiveThemeDark()
                        ? ColorHelper.HexToColor(hex)
                        : ColorHelper.LightenForLightTheme(ColorHelper.HexToColor(hex));
                TintCustomButton.Background = new SolidColorBrush(c);
                TintCustomIcon.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: ShowCustomSwatchColor failed – {ex.Message}");
            }
        }

        private void AppearanceHeader_Tapped(object sender, RoutedEventArgs e)
        {
            ToggleSettingsRow(AppearanceContent, AppearanceChevronRotation, AppConstants.SettingSettingsAppearanceExpanded);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var radio = sender as RadioButton;
            if (radio != null)
            {
                ApplyTheme(radio.Tag.ToString());
            }
        }

        private void TintColorButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var tag = btn.Tag?.ToString() ?? "Default";
                ApplyTintColor(tag);
                UpdateTintSelection(tag);
            }
        }

        /// <summary>
        /// Opens a color picker so the user can choose a tint that isn't one of the presets. The
        /// pick is applied live while the dialog is open so the choice can be judged against the
        /// real window, and reverted to whatever was active before if the dialog is cancelled.
        /// </summary>
        private async void CustomTintButton_Click(object sender, RoutedEventArgs e)
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            string previousTag = localSettings.Values[AppConstants.SettingAppTintColor]?.ToString()
                                 ?? AppConstants.ThemeDefault;

            // The catch stays inside the gated body rather than relying on DialogService's own
            // logging: this handler has repair work to do on failure - the live preview may
            // already have repainted the window, and it has to go back.
            await DialogService.RunExclusiveAsync(async () =>
            {
                try
                {
                    // Seed with the active tint if it's already a custom one, otherwise the last
                    // color the user picked here, otherwise a neutral starting point.
                    string seed = previousTag;
                    // A preset is exactly a tag ColorHelper has a light-theme shade for; Default
                    // is the one tag that is neither a preset nor a custom colour.
                    if (seed == AppConstants.ThemeDefault || ColorHelper.TryGetLightPreset(seed) != null)
                    {
                        seed = localSettings.Values[AppConstants.SettingAppCustomTintColor]?.ToString() ?? "#1E3A5F";
                    }

                    ColorPicker picker = new ColorPicker()
                    {
                        IsAlphaEnabled = false,
                        IsHexInputVisible = true,
                        IsColorChannelTextInputVisible = true,
                        Color = ColorHelper.HexToColor(seed)
                    };

                    ContentDialog dialog = new ContentDialog()
                    {
                        Title = LocalizedStrings.Get("CustomTintDialogTitle"),
                        Content = picker,
                        PrimaryButtonText = LocalizedStrings.Get("CustomTintDialogApply"),
                        CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                        DefaultButton = ContentDialogButton.Primary
                    };
                    DialogService.ApplyXamlRoot(dialog, this);

                    // Live preview: repaint the window as the user drags around the picker.
                    TypedEventHandler<ColorPicker, ColorChangedEventArgs> previewHandler =
                        (s, args) => ApplyTintColorPreview(ColorHelper.ColorToHex(args.NewColor));
                    picker.ColorChanged += previewHandler;

                    var result = await dialog.ShowAsync();
                    picker.ColorChanged -= previewHandler;

                    if (result == ContentDialogResult.Primary)
                    {
                        var hex = ColorHelper.ColorToHex(picker.Color);
                        localSettings.Values[AppConstants.SettingAppCustomTintColor] = hex;
                        ApplyTintColor(hex);
                        UpdateTintSelection(hex);
                    }
                    else
                    {
                        ApplyTintColor(previousTag);
                        UpdateTintSelection(previousTag);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MainPage: Custom tint dialog failed – {ex.Message}");
                    ApplyTintColor(previousTag);
                    UpdateTintSelection(previousTag);
                }
            });
        }

        /// <summary>
        /// Repaints the window with a tint without persisting it - used while the color picker is
        /// open so an abandoned dialog leaves no trace in LocalSettings.
        /// </summary>
        private void ApplyTintColorPreview(string hex)
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                ApplyTintColor(hex);
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
        }

    }
}
