using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickOrbit.Services;

public sealed class FaviconService
{
    private const int MaxPageBytes = 900_000;
    private const int MaxImageBytes = 3_000_000;
    private static readonly Regex LinkTagRegex = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*[""'](?<value>[^""']*)[""']",
        RegexOptions.Compiled);
    private static readonly HttpClient Client = CreateClient();

    public async Task<string?> DownloadAsync(string rawUrl, CancellationToken cancellationToken = default)
    {
        var url = NormalizeUrl(rawUrl);
        if (url is null)
        {
            return null;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var candidates = new List<Uri>();

            // A missing page must not prevent the conventional endpoints and the
            // public resolvers below from being tried.
            var page = await TryDownloadBytesAsync(url, MaxPageBytes, linked.Token);
            if (page is not null)
            {
                var html = Encoding.UTF8.GetString(page);
                candidates.AddRange(FindIconCandidates(html, url));
            }

            AddCandidate(candidates, new Uri(url, "/favicon.ico"));
            AddCandidate(candidates, new Uri(url, "/apple-touch-icon.png"));
            AddCandidate(candidates, new Uri(url, "/apple-touch-icon-precomposed.png"));

            // Some sites publish only SVG icons, hide the icon behind a CDN or
            // reject non-browser clients. These public resolvers cover that gap.
            var host = url.Host;
            AddCandidate(candidates, new Uri("https://www.google.com/s2/favicons?domain=" +
                                             Uri.EscapeDataString(host) + "&sz=64"));
            AddCandidate(candidates, new Uri("https://icons.duckduckgo.com/ip3/" + host + ".ico"));
            AddCandidate(candidates, new Uri("https://favicon.yandex.net/favicon/" + host));

            foreach (var candidate in candidates.Take(12))
            {
                if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                if (candidate.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = await TryDownloadBytesAsync(candidate, MaxImageBytes, linked.Token);
                if (bytes is null)
                {
                    continue;
                }

                var png = TryRenderPng(bytes);
                if (png is null)
                {
                    continue;
                }

                AppPaths.EnsureDirectories();
                var fileName = MakeFileName(url.Host, url.AbsoluteUri) + ".png";
                var path = Path.Combine(AppPaths.IconsDirectory, fileName);
                await File.WriteAllBytesAsync(path, png, linked.Token);
                return path;
            }

            AppPaths.AppendLog("Favicon not found for " + rawUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            AppPaths.AppendLog("Favicon download failed for " + rawUrl + ": " + ex.Message);
        }

        return null;
    }

    private static void AddCandidate(List<Uri> candidates, Uri candidate)
    {
        if (candidates.Any(uri => uri.AbsoluteUri.Equals(candidate.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(candidate);
    }

    public static Uri? NormalizeUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var text = rawUrl.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    private static IEnumerable<Uri> FindIconCandidates(string html, Uri baseUri)
    {
        var results = new List<(int Priority, Uri Uri)>();

        foreach (Match tag in LinkTagRegex.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex.Matches(tag.Value))
            {
                attributes[attribute.Groups["name"].Value] = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            }

            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!attributes.TryGetValue("rel", out var rel) || rel.IndexOf("icon", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (rel.IndexOf("mask-icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, href, out var resolved) ||
                (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var priority = rel.IndexOf("apple-touch-icon", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
            results.Add((priority, resolved));
        }

        return results
            .OrderBy(entry => entry.Priority)
            .Select(entry => entry.Uri)
            .DistinctBy(uri => uri.AbsoluteUri)
            .ToList();
    }

    private static async Task<byte[]?> TryDownloadBytesAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadBytesAsync(uri, maximumBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or IOException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DownloadBytesAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is > 0 && declaredLength > maximumBytes)
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                return null;
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static byte[]? TryRenderPng(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var frame = decoder.Frames[0];
            if (frame.PixelWidth == 0 || frame.PixelHeight == 0)
            {
                return null;
            }

            const int canvas = 64;
            var scale = Math.Min(canvas / (double)frame.PixelWidth, canvas / (double)frame.PixelHeight);
            var width = frame.PixelWidth * scale;
            var height = frame.PixelHeight * scale;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(frame, new Rect((canvas - width) / 2.0, (canvas - height) / 2.0, width, height));
            }

            var target = new RenderTargetBitmap(canvas, canvas, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FileFormatException
                                       or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string MakeFileName(string host, string url)
    {
        var safeHost = string.Concat(host.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        if (safeHost.Length > 50)
        {
            safeHost = safeHost[..50];
        }

        uint hash = 2166136261;
        foreach (var c in url)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return $"{safeHost}_{hash:X8}";
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
        return client;
    }
}
