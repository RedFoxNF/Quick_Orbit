using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickOrbit.Models;

namespace QuickOrbit.Services;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static AppConfig LoadOrCreate(out string? warning)
    {
        warning = null;
        AppPaths.EnsureDirectories();

        if (!File.Exists(AppPaths.ConfigPath))
        {
            var defaults = AppConfig.CreateDefault();
            defaults.Normalize();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.ConfigPath, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null)
            {
                throw new JsonException("Корневой объект конфигурации отсутствует.");
            }

            config.Normalize();
            return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            warning = "Файл config.json повреждён. Создан новый конфиг, старый сохранён как резервная копия.";
            TryBackupBrokenConfig();
            var defaults = AppConfig.CreateDefault();
            defaults.Normalize();
            try
            {
                Save(defaults);
            }
            catch (Exception saveError)
            {
                AppPaths.AppendLog("Failed to save default config: " + saveError);
            }

            return defaults;
        }
    }

    public static void Save(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempPath = AppPaths.ConfigPath + ".tmp";

        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, AppPaths.ConfigPath, overwrite: true);
    }

    public static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var clone = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Не удалось скопировать конфигурацию.");
        clone.Normalize();
        return clone;
    }

    private static void TryBackupBrokenConfig()
    {
        try
        {
            var backup = Path.Combine(
                AppPaths.DataDirectory,
                $"config.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(AppPaths.ConfigPath, backup, overwrite: true);
        }
        catch (Exception ex)
        {
            AppPaths.AppendLog("Failed to back up broken config: " + ex.Message);
        }
    }
}
