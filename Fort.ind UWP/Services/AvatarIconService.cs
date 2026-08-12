using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Produces the small circular avatar that sits in the navigation pane's profile item.
    ///
    /// A NavigationViewItem's Icon must be an IconElement, and IconElement can't be derived from,
    /// so the avatar can only get in there as a BitmapIcon - which takes a URI, not a stream, and
    /// draws the bitmap exactly as it is. Both constraints land here: the remote avatar is
    /// downloaded, centre-cropped to a square, masked to a circle, and written to LocalFolder as a
    /// PNG whose ms-appdata URI the BitmapIcon can point at. The circle has to be baked into the
    /// pixels because there is nowhere in that pipeline to clip one.
    ///
    /// Generated files are keyed by a hash of the source URL, so a repeat launch (or a second
    /// call in the same process) reuses the file instead of re-downloading. Changing avatar
    /// produces a new file name, which also sidesteps XAML's image cache holding onto the old
    /// bitmap for a reused path. LocalStorageService.ResetAllAppDataAsync clears these along with
    /// everything else in LocalFolder.
    /// </summary>
    public sealed class AvatarIconService
    {

        private AvatarIconService()
        {
        }

        /// <summary>
        /// Side of the generated PNG, in pixels. The nav item draws it at 16 view pixels, so this
        /// leaves headroom for 200%/300% displays while still being a trivial decode.
        /// </summary>
        private const int IconPixelSize = 48;

        private const string FilePrefix = "navavatar-";
        private const string FileExtension = ".png";

        /// <summary>
        /// Anything bigger than this isn't an avatar worth turning into a 48px icon. The whole
        /// response is buffered in memory before decoding, so this is the ceiling on that.
        /// </summary>
        private const uint MaxAvatarBytes = 8 * 1024 * 1024;

        /// <summary>
        /// How long to wait before the single retry below.
        /// </summary>
        private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// One client for the process, lazily created for the same reason as the one in
        /// MisskeyAuthService: a signed-out launch never asks for an avatar, so nothing should
        /// stand up an HttpClient (and its filter, cache and cookie manager) on that path.
        /// </summary>
        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

        /// <summary>
        /// The instance serves avatars through its media proxy
        /// (social.fort1nd.com/proxy/avatar.webp?url=...), and that proxy answers 400 to a request
        /// carrying no User-Agent at all - which is what Windows.Web.Http sends by default outside
        /// a packaged app. Any product token satisfies it. TryParseAdd rather than Add so a version
        /// string that isn't a valid token can't throw here; falling back to the bare product name
        /// still beats sending nothing.
        /// </summary>
        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            var version = AppConstants.AppVersionDisplay;
            int space = version.IndexOf(' ');
            if (space > 0)
            {
                version = version.Substring(0, space);
            }

            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind/" + version))
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind");
            }

            return client;
        }

        /// <summary>
        /// Serializes generation. UpdateProfileNavItem runs from the constructor, from Loaded and
        /// from AuthStateChanged, so two calls for the same avatar can easily overlap; without
        /// this they would both download and both write the same file, and the second write could
        /// land on the file the first one is still flushing.
        /// </summary>
        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        // Last (url -> generated icon) pair, so repeat calls in the same process skip even the
        // existence check. Only one account is ever signed in, so a single pair is enough.
        private static string s_cachedUrl;
        private static Uri s_cachedUri;

        /// <summary>
        /// Returns an ms-appdata URI for a circular PNG of the given avatar, generating it if it
        /// isn't cached yet. Returns null if the URL isn't a usable http/https image or anything
        /// in the download/decode/encode path fails - callers fall back to a symbol icon.
        /// </summary>
        public static async Task<Uri> GetCircularAvatarUriAsync(string avatarUrl)
        {
            var sourceUri = AppConstants.TryCreateWebUri(avatarUrl);
            if (sourceUri == null)
            {
                return null;
            }

            await s_gate.WaitAsync();
            try
            {
                if (string.Equals(avatarUrl, s_cachedUrl, StringComparison.Ordinal) && s_cachedUri != null)
                {
                    return s_cachedUri;
                }

                var fileName = FilePrefix + StableHash(avatarUrl) + "-" + IconPixelSize + FileExtension;
                var folder = ApplicationData.Current.LocalFolder;
                var localUri = new Uri("ms-appdata:///local/" + fileName);

                var existing = await folder.TryGetItemAsync(fileName);
                if (existing == null)
                {
                    var pixels = await TryRenderCircularPixelsAsync(sourceUri);
                    if (pixels == null)
                    {
                        // One retry, because the cost of not retrying is high: a single failed
                        // attempt leaves the pane showing the generic glyph until the next
                        // sign-in or profile refresh raises AuthStateChanged, which on a quiet
                        // session may be never. The gate is deliberately still held - a stampede
                        // of overlapping refreshes would be worse than a 2 second stall on a
                        // path nothing is waiting on.
                        await Task.Delay(TransientRetryDelay);
                        pixels = await TryRenderCircularPixelsAsync(sourceUri);
                    }

                    if (pixels == null)
                    {
                        return null;
                    }

                    if (!await WritePngAsync(folder, fileName, pixels))
                    {
                        return null;
                    }
                }

                s_cachedUrl = avatarUrl;
                s_cachedUri = localUri;
                return localUri;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: could not build nav avatar - {ex.Message}");
                return null;
            }
            finally
            {
                s_gate.Release();
            }
        }

        /// <summary>
        /// Runs one download/decode attempt, turning any failure into null so the caller can
        /// decide whether to retry rather than losing the attempt to the outer catch.
        /// </summary>
        private static async Task<byte[]> TryRenderCircularPixelsAsync(Uri sourceUri)
        {
            try
            {
                return await RenderCircularPixelsAsync(sourceUri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AvatarIconService: avatar fetch/decode failed - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads the avatar and returns IconPixelSize-square BGRA8 pixels, centre-cropped and
        /// masked to a circle. Straight (not premultiplied) alpha, so masking is a write to the
        /// alpha byte alone and the colour channels are left as the decoder produced them.
        /// </summary>
        private static async Task<byte[]> RenderCircularPixelsAsync(Uri sourceUri)
        {
            var buffer = await s_client.Value.GetBufferAsync(sourceUri);
            if (buffer == null || buffer.Length == 0 || buffer.Length > MaxAvatarBytes)
            {
                Debug.WriteLine("AvatarIconService: avatar response empty or too large");
                return null;
            }

            using (var stream = new InMemoryRandomAccessStream())
            {
                await stream.WriteAsync(buffer);
                await stream.FlushAsync();
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
                {
                    return null;
                }

                // Scale so the shorter side lands on IconPixelSize, then take the middle square of
                // that - a plain scale to a square would stretch a non-square avatar.
                double scale = (double)IconPixelSize / Math.Min(decoder.PixelWidth, decoder.PixelHeight);
                uint scaledWidth = (uint)Math.Max(IconPixelSize, Math.Round(decoder.PixelWidth * scale));
                uint scaledHeight = (uint)Math.Max(IconPixelSize, Math.Round(decoder.PixelHeight * scale));

                var transform = new BitmapTransform();
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
                transform.ScaledWidth = scaledWidth;
                transform.ScaledHeight = scaledHeight;
                transform.Bounds = new BitmapBounds
                {
                    X = (scaledWidth - IconPixelSize) / 2,
                    Y = (scaledHeight - IconPixelSize) / 2,
                    Width = IconPixelSize,
                    Height = IconPixelSize
                };

                // IgnoreExifOrientation, not Respect: a quarter-turn orientation flag makes the
                // decoder swap width and height *after* the transform is applied, which would put
                // the crop bounds outside the image. Avatars aren't camera output.
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = pixelData.DetachPixelData();

                // The crop bounds are what makes this exactly one square of pixels; check rather
                // than trust it, because the masking loop below indexes on that assumption.
                if (pixels.Length < IconPixelSize * IconPixelSize * 4)
                {
                    Debug.WriteLine($"AvatarIconService: unexpected pixel buffer size {pixels.Length}");
                    return null;
                }

                ApplyCircularMask(pixels);
                return pixels;
            }
        }

        /// <summary>
        /// Zeroes the alpha outside an inscribed circle, feathering the last pixel of the edge so
        /// the circle doesn't read as a staircase at 16px. Coverage multiplies whatever alpha the
        /// source already had rather than replacing it, so avatars with their own transparency
        /// keep it.
        /// </summary>
        private static void ApplyCircularMask(byte[] pixels)
        {
            const double center = IconPixelSize / 2.0;

            for (int y = 0; y < IconPixelSize; y++)
            {
                double dy = (y + 0.5) - center;
                int rowStart = y * IconPixelSize * 4;

                for (int x = 0; x < IconPixelSize; x++)
                {
                    double dx = (x + 0.5) - center;
                    double coverage = center - Math.Sqrt((dx * dx) + (dy * dy)) + 0.5;

                    if (coverage >= 1.0)
                    {
                        continue;
                    }

                    int alphaIndex = rowStart + (x * 4) + 3;
                    if (coverage <= 0.0)
                    {
                        pixels[alphaIndex] = 0;
                    }
                    else
                    {
                        // Math.Round, not a cast - a cast truncates, which darkens every feathered
                        // edge pixel by a level.
                        pixels[alphaIndex] = (byte)Math.Round(pixels[alphaIndex] * coverage, MidpointRounding.ToEven);
                    }
                }
            }
        }

        /// <summary>
        /// Encodes the pixels to a PNG under a temporary name and renames it into place, so a
        /// crash or suspend mid-write can't leave a truncated file that later launches would
        /// happily hand to BitmapIcon as a cache hit.
        /// </summary>
        private static async Task<bool> WritePngAsync(StorageFolder folder, string fileName, byte[] pixels)
        {
            var tempFile = await folder.CreateFileAsync(fileName + ".tmp", CreationCollisionOption.ReplaceExisting);

            using (var fileStream = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fileStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    IconPixelSize,
                    IconPixelSize,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
            }

            await tempFile.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
            return true;
        }

        /// <summary>
        /// FNV-1a over the URL, as hex. Only needs to be stable across launches and collision-free
        /// between one user's successive avatars, which is why it isn't a real digest.
        /// </summary>
        private static string StableHash(string value)
        {
            uint hash = 2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash ^ value[i]) * 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
