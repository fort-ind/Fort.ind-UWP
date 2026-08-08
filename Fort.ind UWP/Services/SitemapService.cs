using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Parses the bundled sitemap.xml and produces SearchItem entries for every URL
    /// </summary>
    public class SitemapService
    {

        // The sitemap ships inside the app package and the URL cache is already revalidated on a
        // 24h TTL, so re-reading and re-parsing it for every caller is pure waste. Reference swap
        // only, exactly like MainPage._allSearchItems: readers never observe a half-built list.
        private static IReadOnlyList<SearchItem> s_allItems;
        private static IReadOnlyList<SearchItem> s_gameItems;

        // Serialises the first parse so two callers racing at startup - MainPage's constructor
        // calls LoadSitemapItems, and GamesPage loads as soon as it is navigated to - cannot both
        // hit the file system and both parse the XML.
        private static readonly SemaphoreSlim s_loadGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Reads sitemap.xml from the app package and returns SearchItem objects allowing for the
        /// latest URLs to be searchable. Parsed at most once per process; an empty result (missing
        /// file, malformed XML) is deliberately NOT memoized so a later caller retries.
        /// </summary>
        public static async Task<IReadOnlyList<SearchItem>> LoadSearchItemsAsync()
        {
            var cached = s_allItems;
            if (cached != null) return cached;

            await s_loadGate.WaitAsync();
            try
            {
                // Re-check: a racing caller may have finished while we waited on the gate.
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

        /// <summary>
        /// The game subset of the sitemap, in sitemap order. Memoized separately so the Games page
        /// does not re-filter every item each time it is shown.
        /// </summary>
        public static async Task<IReadOnlyList<SearchItem>> LoadGameItemsAsync()
        {
            var cachedGames = s_gameItems;
            if (cachedGames != null) return cachedGames;

            // Deliberately NOT inside s_loadGate: LoadSearchItemsAsync takes that gate and
            // SemaphoreSlim is not reentrant, so taking it here would deadlock silently. Two
            // callers racing through here just each build an equivalent list from the same
            // memoized source and one of the two identical results wins - harmless.
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

        /// <summary>
        /// Parses the packaged sitemap (or the URL cache written from it) into SearchItems.
        /// Callers go through LoadSearchItemsAsync, which memoizes this.
        /// </summary>
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

                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///sitemap.xml"));
                var text = await FileIO.ReadTextAsync(file);

                // Streamed rather than XDocument.Parse: the only thing wanted out of the whole
                // document is each <loc> value, so building a DOM for it just to throw it away is
                // a peak-memory spike on the startup path for no benefit.
                var urlsToCache = ReadLocValues(text);
                if (urlsToCache == null)
                {
                    return items; // Malformed XML - return empty so a later caller retries.
                }

                foreach (var urlValue in urlsToCache)
                {
                    var item = CreateSearchItemFromUrl(urlValue);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

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

        /// <summary>
        /// Pulls every &lt;loc&gt; value out of a sitemap document without materialising a DOM.
        /// Returns null (not an empty list) if the XML is malformed, so the caller can tell
        /// "broken document" apart from "document with no URLs" and avoid memoizing the failure.
        /// </summary>
        private static List<string> ReadLocValues(string documentText)
        {
            List<string> urls = new List<string>();

            // DTD processing off and no resolver: the sitemap is bundled, but this also parses
            // nothing that could reach out to an external entity if it ever stopped being.
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

        /// <summary>
        /// Creates a SearchItem instance from a URL string, or returns null if the URL
        /// is invalid or should be skipped (e.g. utility pages like 404).
        ///
        /// Only http/https URLs are accepted. Items built here are eventually handed to
        /// Launcher.LaunchUriAsync, and this is the choke point every URL passes through -
        /// including ones read back from the plain-text cache file in LocalFolder, which is not
        /// the app package and so is not trusted input.
        /// </summary>
        /// <param name="urlValue">The absolute URL string.</param>
        private static SearchItem CreateSearchItemFromUrl(string urlValue)
        {
            var uri = AppConstants.TryCreateWebUri(urlValue);
            if (uri == null)
            {
                return null;
            }

            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(path))
            {
                return new SearchItem("Home", AppConstants.CategoryFortWebsite, null, urlValue);
            }

            // Skip utility pages
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
            List<SearchItem> items = new List<SearchItem>();

            foreach (var urlValue in urls)
            {
                var item = CreateSearchItemFromUrl(urlValue);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
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

                // Ignore a cache written by a different app version - a bundled sitemap.xml can
                // change between releases (new pages/games), and honoring a still-fresh TTL from
                // before the update would hide anything new until the cache naturally expires.
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

                var cacheFile = await ApplicationData.Current.LocalFolder.GetFileAsync(AppConstants.SitemapCacheFileName);
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

        /// <summary>
        /// Slug tokens that are acronyms rather than words - plain title-casing turns them into
        /// "Cs" and "Fnaf", which reads wrong. Deliberately conservative: only tokens that are
        /// never an ordinary English word, so a regenerated sitemap cannot trip it. Note "us"
        /// (as in "amoung-us") is intentionally absent.
        /// </summary>
        private static readonly HashSet<string> s_upperCaseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "css", "dbz", "fnaf", "gba", "gbc", "gta", "hd", "html", "mlb", "mlg",
            "n64", "nba", "nds", "nes", "nfl", "nhl", "psp", "snes", "tmnt", "tv", "ufc",
            "ufo", "wwe"
        };

        /// <summary>
        /// Tokens that are the tail of a domain-style name - "diep-io" is diep.io, not "Diep Io".
        /// Glued onto the preceding token with a dot and left lowercase.
        /// </summary>
        private static readonly HashSet<string> s_domainSuffixTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com", "gg", "io", "lol", "net", "org"
        };

        /// <summary>
        /// Turns the last path segment into a display name: "games/html/rynis-game" -> "Rynis Game".
        /// Beyond plain title-casing, two generic rules clean up the shapes that show up in game
        /// slugs - domain suffixes are re-glued ("diep-io" -> "Diep.io") and runs of numeric tokens
        /// are treated as a version rather than separate words ("minecraft-1-8-8-fixed" ->
        /// "Minecraft 1.8.8 Fixed"). Both are rules rather than a lookup table, so a regenerated or
        /// extended sitemap gets the same treatment with nothing to maintain.
        /// </summary>
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

                // A domain suffix never starts a name, so index 0 is always an ordinary word.
                if (i > 0 && s_domainSuffixTokens.Contains(token))
                {
                    sb.Append('.');
                    sb.Append(token.ToLowerInvariant());
                    continue;
                }

                // Two numbers in a row are a version ("1", "6" -> "1.6"), not two words. A number
                // after a word still gets a space, so "2048 Cupcakes" and "FNAF 2" are unaffected.
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

        /// <summary>
        /// Upper-cases a known acronym, otherwise title-cases the token. Invariant casing
        /// throughout - these are URL slugs, not user text.
        /// </summary>
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
