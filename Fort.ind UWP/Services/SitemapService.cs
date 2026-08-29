using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public class SitemapService
    {
        private static IReadOnlyList<SearchItem> s_allItems;
        private static IReadOnlyList<SearchItem> s_gameItems;

        private static readonly SemaphoreSlim s_loadGate = new SemaphoreSlim(1, 1);

        public static async Task<IReadOnlyList<SearchItem>> LoadSearchItemsAsync()
        {
            var cached = s_allItems;
            if (cached != null) return cached;

            await s_loadGate.WaitAsync();
            try
            {
                if (s_allItems != null) return s_allItems;

                var parsed = await ParseSitemapAsync();
                var result = parsed.AsReadOnly();
                if (parsed.Count > 0)
                {
                    s_allItems = result;
                }
                return result;
            }
            finally
            {
                s_loadGate.Release();
            }
        }

        public static async Task<IReadOnlyList<SearchItem>> LoadGameItemsAsync()
        {
            var cachedGames = s_gameItems;
            if (cachedGames != null) return cachedGames;

            var all = await LoadSearchItemsAsync();

            List<SearchItem> games = new List<SearchItem>();
            foreach (var item in all)
            {
                if (item.Category != null &&
                    item.Category.StartsWith(AppConstants.CategoryGames, StringComparison.Ordinal))
                {
                    games.Add(item);
                }
            }

            var result = games.AsReadOnly();
            if (all.Count > 0)
            {
                s_gameItems = result;
            }
            return result;
        }

        private static async Task<List<SearchItem>> ParseSitemapAsync()
        {
            List<SearchItem> items = new List<SearchItem>();

            try
            {
                var cachedUrls = await TryLoadCachedUrlsAsync();
                if (cachedUrls != null && cachedUrls.Count > 0)
                {
                    return BuildSearchItemsFromUrls(cachedUrls);
                }

                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/sitemap.xml"));
                var text = await FileIO.ReadTextAsync(file);

                var urlsToCache = ReadLocValues(text);
                if (urlsToCache == null)
                {
                    return items;
                }

                items = BuildSearchItemsFromUrls(urlsToCache);

                if (urlsToCache.Count > 0)
                {
                    await SaveCachedUrlsAsync(urlsToCache);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: failed to load sitemap – {ex.Message}");
            }

            return items;
        }

        private static List<string> ReadLocValues(string documentText)
        {
            List<string> urls = new List<string>();

            System.Xml.XmlReaderSettings settings = new System.Xml.XmlReaderSettings()
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            try
            {
                using (System.IO.StringReader stringReader = new System.IO.StringReader(documentText))
                {
                    using (var reader = System.Xml.XmlReader.Create(stringReader, settings))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == System.Xml.XmlNodeType.Element &&
                                string.Equals(reader.LocalName, "loc", StringComparison.Ordinal))
                            {
                                var value = reader.ReadElementContentAsString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    urls.Add(value.Trim());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: XML parsing failed – {ex.Message}");
                return null;
            }

            return urls;
        }

        private static SearchItem CreateSearchItemFromUrl(string urlValue)
        {
            var uri = WebLauncher.TryCreateWebUri(urlValue);
            if (uri == null)
            {
                return null;
            }

            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(path))
            {
                return new SearchItem("Home", AppConstants.CategoryFortWebsite, null, urlValue);
            }

            if (path == "404")
            {
                return null;
            }

            var category = GetCategory(path);
            var title = GetTitle(path);
            return new SearchItem(title, category, null, urlValue);
        }

        private static List<SearchItem> BuildSearchItemsFromUrls(IEnumerable<string> urls)
        {
            return urls
                .Select(CreateSearchItemFromUrl)
                .Where(item => item != null)
                .ToList();
        }

        private static async Task<List<string>> TryLoadCachedUrlsAsync()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (!settings.Values.ContainsKey(AppConstants.SitemapCacheTimestampKey))
                {
                    return null;
                }

                var cachedVersion = settings.Values[AppConstants.SitemapCacheAppVersionKey]?.ToString();
                if (cachedVersion != AppConstants.AppVersionDisplay)
                {
                    return null;
                }

                var rawTimestamp = settings.Values[AppConstants.SitemapCacheTimestampKey];
                long cacheUnixSeconds;
                try
                {
                    cacheUnixSeconds = Convert.ToInt64(rawTimestamp);
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (InvalidCastException)
                {
                    return null;
                }
                catch (OverflowException)
                {
                    return null;
                }

                var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var maxAgeSeconds = (long)AppConstants.SitemapCacheTtlHours * 60L * 60L;
                if ((nowUnixSeconds - cacheUnixSeconds) > maxAgeSeconds)
                {
                    return null;
                }

                var cacheFile = await ApplicationData.Current.LocalFolder.TryGetItemAsync(AppConstants.SitemapCacheFileName) as StorageFile;
                if (cacheFile == null)
                {
                    return null;
                }

                var content = await FileIO.ReadTextAsync(cacheFile);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                List<string> urls = new List<string>();
                var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var value = line.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        urls.Add(value);
                    }
                }

                if (urls.Count == 0)
                {
                    return null;
                }

                return urls;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: failed to load sitemap cache – {ex.Message}");
                return null;
            }
        }

        private static async Task SaveCachedUrlsAsync(IEnumerable<string> urls)
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (var url in urls)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        lines.Add(url);
                    }
                }

                if (lines.Count == 0)
                {
                    return;
                }

                var cacheFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    AppConstants.SitemapCacheFileName,
                    CreationCollisionOption.ReplaceExisting);

                await FileIO.WriteTextAsync(cacheFile, string.Join(Environment.NewLine, lines));
                ApplicationData.Current.LocalSettings.Values[AppConstants.SitemapCacheTimestampKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                ApplicationData.Current.LocalSettings.Values[AppConstants.SitemapCacheAppVersionKey] = AppConstants.AppVersionDisplay;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SitemapService: failed to save sitemap cache – {ex.Message}");
            }
        }

        private static string GetCategory(string path)
        {
            if (path.StartsWith("games/html/")) return "Games — HTML";
            if (path.StartsWith("games/flash/")) return "Games — Flash";
            if (path.StartsWith("games/codepen/")) return "Games — CodePen";
            if (path.StartsWith("games/retroclassic-mostly-emulated/")) return "Games — Retro";
            if (path.StartsWith("games/minecraft/")) return "Games — Minecraft";
            if (path.StartsWith("games/")) return "Games";
            if (path.StartsWith("social/")) return "Social";
            if (path.StartsWith("emulators/")) return "Emulators";
            if (path.StartsWith("apps/appstone/")) return "Apps — AppStone";
            if (path.StartsWith("apps/")) return "Apps";
            if (path.StartsWith("extras/")) return "Extras";
            if (path.StartsWith("labs-betas/")) return "Labs & Betas";
            return AppConstants.CategoryFortWebsite;
        }

        private static readonly HashSet<string> s_upperCaseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "css", "dbz", "fnaf", "gba", "gbc", "gta", "hd", "html", "mlb", "mlg",
            "n64", "nba", "nds", "nes", "nfl", "nhl", "psp", "snes", "tmnt", "tv", "ufc",
            "ufo", "wwe"
        };

        private static readonly HashSet<string> s_domainSuffixTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com", "gg", "io", "lol", "net", "org"
        };

        private static string GetTitle(string path)
        {
            var trimmed = path.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            var slug = lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;

            if (string.IsNullOrEmpty(slug)) return path;

            var tokens = slug.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return path;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(slug.Length);
            for (int i = 0; i <= tokens.Length - 1; i++)
            {
                var token = tokens[i];

                if (i > 0 && s_domainSuffixTokens.Contains(token))
                {
                    sb.Append('.');
                    sb.Append(token.ToLowerInvariant());
                    continue;
                }

                if (i > 0 && IsAllDigits(token) && IsAllDigits(tokens[i - 1]))
                {
                    sb.Append('.');
                    sb.Append(token);
                    continue;
                }

                if (i > 0) sb.Append(' ');
                sb.Append(FormatToken(token));
            }

            return sb.ToString();
        }

        private static string FormatToken(string token)
        {
            if (s_upperCaseTokens.Contains(token)) return token.ToUpperInvariant();

            System.Text.StringBuilder sb = new System.Text.StringBuilder(token.Length);
            sb.Append(char.ToUpperInvariant(token[0]));
            for (int i = 1; i <= token.Length - 1; i++)
            {
                sb.Append(char.ToLowerInvariant(token[i]));
            }
            return sb.ToString();
        }

        private static bool IsAllDigits(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i <= token.Length - 1; i++)
            {
                if (!char.IsDigit(token[i])) return false;
            }
            return true;
        }
    }
}
