''' <summary>
''' Centralized app constants to avoid repeated literals and drift.
''' </summary>
Public NotInheritable Class AppConstants

    Private Sub New()
    End Sub

    ' Search categories
    Public Const CategoryMenu As String = "Menu"
    Public Const CategorySettings As String = "Settings"
    Public Const CategoryProfile As String = "Profile"
    Public Const CategoryGames As String = "Games"
    Public Const CategorySocial As String = "Social"
    Public Const CategoryEmulators As String = "Emulators"
    Public Const CategoryApps As String = "Apps"
    Public Const CategoryExtras As String = "Extras"
    Public Const CategoryLabsAndBetas As String = "Labs & Betas"
    Public Const CategoryFortWebsite As String = "fort1nd.com"

    ' Navigation tags
    Public Const NavigationLatestNews As String = "LatestNews"
    Public Const NavigationGames As String = "Games"
    Public Const NavigationBetas As String = "Betas"
    Public Const NavigationProfile As String = "Profile"
    Public Const NavigationSocial As String = "Social"
    Public Const NavigationSettings As String = "Settings"

    ' Theme values
    Public Const ThemeDefault As String = "Default"
    Public Const ThemeLight As String = "Light"
    Public Const ThemeDark As String = "Dark"

    ' LocalSettings keys
    Public Const SettingHideWelcomeDialog As String = "HideWelcomeDialog"
    Public Const SettingAppTheme As String = "AppTheme"
    Public Const SettingAppTintColor As String = "AppTintColor"
    ' Remembers the last color picked in the custom-tint dialog so the custom swatch keeps
    ' showing it even while a preset is the active tint.
    Public Const SettingAppCustomTintColor As String = "AppCustomTintColor"
    Public Const SettingSettingsAppearanceExpanded As String = "SettingsAppearanceExpanded"
    Public Const SettingSettingsStorageExpanded As String = "SettingsStorageExpanded"
    Public Const SettingSettingsTileExpanded As String = "SettingsTileExpanded"
    Public Const SettingSettingsWelcomeExpanded As String = "SettingsWelcomeExpanded"
    Public Const SettingSettingsAboutExpanded As String = "SettingsAboutExpanded"

    ' Search behavior
    Public Const SearchDebounceMilliseconds As Integer = 300
    Public Const SearchSuggestionLimit As Integer = 15

    ' Sitemap cache
    Public Const SitemapCacheFileName As String = "sitemap_urls.cache"
    Public Const SitemapCacheTimestampKey As String = "SitemapCacheUnixSeconds"
    Public Const SitemapCacheTtlHours As Integer = 24

    ' Release channel suffix appended after the numeric version (e.g. "0.5.0 Beta")
    Public Const VersionChannel As String = " "

    ''' <summary>
    ''' The app version pulled from the package manifest, formatted as "Major.Minor.Build".
    ''' Falls back to a static string if the package identity is unavailable (e.g. unpackaged).
    ''' Single source of truth so the About screen never drifts from the manifest.
    ''' </summary>
    Public Shared ReadOnly Property AppVersionDisplay As String
        Get
            Try
                Dim v = Windows.ApplicationModel.Package.Current.Id.Version
                Return $"{v.Major}.{v.Minor}.{v.Build} {VersionChannel}"
            Catch
                Return $"2.0.10 {VersionChannel}"
            End Try
        End Get
    End Property

End Class