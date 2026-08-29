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
    public sealed class AvatarIconService
    {
        private AvatarIconService()
        {
        }

        private const int IconPixelSize = 48;

        private const string FilePrefix = "navavatar-";
        private const string FileExtension = ".png";

        private const uint MaxAvatarBytes = 8 * 1024 * 1024;

        private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

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

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private static string s_cachedUrl;
        private static Uri s_cachedUri;

        public static async Task<Uri> GetCircularAvatarUriAsync(string avatarUrl)
        {
            var sourceUri = WebLauncher.TryCreateWebUri(avatarUrl);
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

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = pixelData.DetachPixelData();

                if (pixels.Length < IconPixelSize * IconPixelSize * 4)
                {
                    Debug.WriteLine($"AvatarIconService: unexpected pixel buffer size {pixels.Length}");
                    return null;
                }

                ApplyCircularMask(pixels);
                return pixels;
            }
        }

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

                    pixels[alphaIndex] = coverage <= 0.0
                        ? (byte)0
                        : (byte)Math.Round(pixels[alphaIndex] * coverage, MidpointRounding.ToEven);
                }
            }
        }

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
