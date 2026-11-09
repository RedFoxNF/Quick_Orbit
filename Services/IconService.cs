using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QuickOrbit.Models;

namespace QuickOrbit.Services;

public sealed class IconService
{
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetIcon(OrbitItem item)
    {
        if (item is null)
        {
            return null;
        }

        var iconPath = ResolveConfiguredIcon(item.Icon);
        var target = (item.Target ?? string.Empty).Trim().Trim('"');
        var iconStamp = 0L;
        try
        {
            if (File.Exists(iconPath))
            {
                iconStamp = File.GetLastWriteTimeUtc(iconPath).Ticks;
            }
        }
        catch (IOException)
        {
            // If the timestamp cannot be read, the path alone is still a valid cache key.
        }

        var key = $"{item.Type}|{iconPath}|{iconStamp}|{target}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            image = LoadImage(iconPath);
        }

        if (image is null && item.Type == ItemType.File && target.Length > 0)
        {
            var shellTarget = ResolveShellTarget(target);
            if (shellTarget is not null)
            {
                image = ExtractShellIcon(shellTarget);
            }
        }

        _cache[key] = image;
        return image;
    }

    public static string ResolveConfiguredIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(icon.Trim().Trim('"'));
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(AppContext.BaseDirectory, expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ResolveShellTarget(string target)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (Directory.Exists(expanded) || File.Exists(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return ResolveExecutable(target);
    }

    private static string? ResolveExecutable(string target)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (File.Exists(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (expanded.Contains(Path.DirectorySeparatorChar) || expanded.Contains(Path.AltDirectorySeparatorChar))
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), expanded);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        foreach (var name in new[] { expanded, expanded + ".exe" })
        {
            var candidate = Path.Combine(system32, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + expanded);
            if (key?.GetValue(null) is string appPath && File.Exists(appPath))
            {
                return appPath;
            }
        }
        catch
        {
            // App Paths is an optional fallback.
        }

        return null;
    }

    private static ImageSource? ExtractShellIcon(string path)
    {
        IntPtr large = IntPtr.Zero;
        IntPtr small = IntPtr.Zero;

        try
        {
            SHDefExtractIcon(path, 0, 0, out large, out small, 64u | (32u << 16));
            if (large != IntPtr.Zero)
            {
                return FromHIcon(large);
            }
        }
        catch (DllNotFoundException)
        {
            // Fall through to the documented ExtractIconEx path.
        }
        catch (EntryPointNotFoundException)
        {
            // Fall through to the documented ExtractIconEx path.
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
            large = IntPtr.Zero;
            small = IntPtr.Zero;
        }

        try
        {
            var count = ExtractIconEx(path, 0, out large, out small, 1);
            if (count > 0 && large != IntPtr.Zero)
            {
                return FromHIcon(large);
            }
        }
        catch (DllNotFoundException)
        {
            // Fall through to SHGetFileInfo.
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
            large = IntPtr.Zero;
            small = IntPtr.Zero;
        }

        var shellIcon = IntPtr.Zero;
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiLargeIcon);
            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                shellIcon = info.hIcon;
                return FromHIcon(shellIcon);
            }
        }
        finally
        {
            if (shellIcon != IntPtr.Zero) DestroyIcon(shellIcon);
        }

        return null;
    }

    private static ImageSource FromHIcon(IntPtr icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(64, 64));
        source.Freeze();
        return source;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "SHDefExtractIconW")]
    private static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIcons);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
