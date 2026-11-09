using System.IO;
using System.Windows;
using QuickOrbit.Models;

namespace QuickOrbit.Services;

/// <summary>Turns an Explorer/browser drop into orbit items without touching the UI.</summary>
public static class DropItemFactory
{
    private const string UriListFormat = "text/uri-list";

    public static bool CanAccept(System.Windows.IDataObject data)
    {
        try
        {
            return data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
                   data.GetDataPresent(System.Windows.DataFormats.UnicodeText) ||
                   data.GetDataPresent(System.Windows.DataFormats.Text) ||
                   data.GetDataPresent(UriListFormat);
        }
        catch
        {
            return false;
        }
    }

    public static List<OrbitItem> FromDataObject(System.Windows.IDataObject data)
    {
        var results = new List<OrbitItem>();

        try
        {
            if (data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
                data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            {
                foreach (var path in paths)
                {
                    var item = FromPath(path);
                    if (item is not null)
                    {
                        results.Add(item);
                    }
                }
            }

            if (results.Count == 0 && ReadText(data) is { Length: > 0 } text)
            {
                foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var value = line.Trim();
                    if (value.Length == 0 || value.StartsWith('#'))
                    {
                        continue;
                    }

                    var item = FromPath(value) ?? FromUrl(value);
                    if (item is not null)
                    {
                        results.Add(item);
                    }
                }
            }
        }
        catch
        {
            // A broken IDataObject must not break the drop target.
        }

        return results
            .GroupBy(item => item.Type + "|" + item.Target, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static OrbitItem? FromPath(string rawPath)
    {
        var path = Environment.ExpandEnvironmentVariables((rawPath ?? string.Empty).Trim().Trim('"'));
        if (path.Length == 0)
        {
            return null;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return new OrbitItem
            {
                Name = SafeName(Path.GetFileName(path), "Папка"),
                Type = ItemType.File,
                Target = path
            };
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var url = ReadInternetShortcut(path);
            var uri = FaviconService.NormalizeUrl(url ?? string.Empty);
            if (uri is not null)
            {
                return new OrbitItem
                {
                    Name = SafeName(Path.GetFileNameWithoutExtension(path), uri.Host),
                    Type = ItemType.Url,
                    Target = uri.AbsoluteUri
                };
            }
        }

        return new OrbitItem
        {
            Name = SafeName(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path)),
            Type = ItemType.File,
            Target = path
        };
    }

    public static OrbitItem? FromUrl(string rawText)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
        {
            return FromPath(fileUri.LocalPath);
        }

        var uri = FaviconService.NormalizeUrl(text);
        if (uri is null || string.IsNullOrWhiteSpace(uri.Host) || !uri.Host.Contains('.'))
        {
            return null;
        }

        var name = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;

        return new OrbitItem
        {
            Name = name,
            Type = ItemType.Url,
            Target = uri.AbsoluteUri
        };
    }

    private static string? ReadText(System.Windows.IDataObject data)
    {
        foreach (var format in new[]
                 {
                     System.Windows.DataFormats.UnicodeText,
                     System.Windows.DataFormats.Text,
                     UriListFormat
                 })
        {
            try
            {
                if (data.GetDataPresent(format) && data.GetData(format) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
                // Try the next clipboard format.
            }
        }

        return null;
    }

    private static string? ReadInternetShortcut(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[4..].Trim();
                }
            }
        }
        catch (IOException)
        {
            // Fall back to treating the .url file as a normal file.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall back to treating the .url file as a normal file.
        }

        return null;
    }

    private static string SafeName(string? value, string fallback)
    {
        var name = (value ?? string.Empty).Trim();
        return name.Length == 0 ? fallback : name;
    }
}
