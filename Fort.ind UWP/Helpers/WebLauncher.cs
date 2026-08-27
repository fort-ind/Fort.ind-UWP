using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    /// <summary>
    /// The single gate every externally-sourced URL passes through before the app hands it to
    /// the shell. Lives here rather than on AppConstants so that file stays what its name says
    /// it is - constants - and so this check is findable as the security control it is.
    /// </summary>
    public static class WebLauncher
    {

        /// <summary>
        /// Parses a string into a web URI, accepting only http/https.
        ///
        /// Every URL the app launches originates outside the code: the bundled sitemap, the
        /// plain-text URL cache in LocalFolder, or profile JSON returned by the instance. Handing
        /// any of those straight to Launcher.LaunchUriAsync means one click can invoke *any*
        /// registered protocol on the machine - "ms-settings:", "file:", "shell:", or another
        /// installed app's custom scheme - because Uri.TryCreate(..., Absolute) happily accepts
        /// all of them. A browsable link is the only thing any of these call sites ever intends,
        /// so anything else is rejected here rather than at each call site.
        /// Returns null if the value is not a well-formed http/https URL.
        /// </summary>
        public static Uri TryCreateWebUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            Uri uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return null;

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrEmpty(uri.Host)) return null;

            return uri;
        }

        /// <summary>
        /// Opens a URL in the user's browser, but only if it is a well-formed http/https URL.
        /// See <see cref="TryCreateWebUri"/> for why the scheme is checked. Returns False (and
        /// launches nothing) for anything else.
        /// </summary>
        public static async Task<bool> LaunchAsync(string value)
        {
            var uri = TryCreateWebUri(value);
            if (uri == null)
            {
                Debug.WriteLine($"WebLauncher: refused to launch non-web URI - {value}");
                return false;
            }

            return await Windows.System.Launcher.LaunchUriAsync(uri);
        }

    }
}
