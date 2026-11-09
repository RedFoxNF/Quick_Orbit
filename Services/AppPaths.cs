namespace QuickOrbit.Services;

public static class AppPaths
{
    private static readonly string? OverrideRoot = Environment.GetEnvironmentVariable("QUICKORBIT_DATA");

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static bool IsFallback { get; private set; }

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public static string IconsDirectory => Path.Combine(DataDirectory, "icons");

    public static string LogPath => Path.Combine(DataDirectory, "quickorbit.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconsDirectory);
    }

    public static void AppendLog(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the launcher.
        }
    }

    private static string ResolveDataDirectory()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(OverrideRoot))
        {
            try
            {
                candidates.Add(Path.GetFullPath(OverrideRoot));
            }
            catch
            {
                // An invalid override must not prevent the normal fallback chain.
            }
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roaming))
        {
            candidates.Add(Path.Combine(roaming, "QuickOrbit"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            candidates.Add(Path.Combine(local, "QuickOrbit"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "QuickOrbitData"));

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            try
            {
                Directory.CreateDirectory(candidate);
                Directory.CreateDirectory(Path.Combine(candidate, "icons"));
                IsFallback = index > 0;
                return candidate;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or ArgumentException)
            {
                // Try the next writable location: LocalAppData, then next to the executable.
            }
        }

        IsFallback = true;
        return Path.Combine(AppContext.BaseDirectory, "QuickOrbitData");
    }
}
