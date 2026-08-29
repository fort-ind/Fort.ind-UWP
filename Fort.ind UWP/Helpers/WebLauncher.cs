using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    public static class WebLauncher
    {
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
