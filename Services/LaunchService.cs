using System.ComponentModel;
using System.Diagnostics;
using QuickOrbit.Models;

namespace QuickOrbit.Services;

public static class LaunchService
{
    public static bool TryLaunch(OrbitItem item, out string error)
    {
        error = string.Empty;

        try
        {
            var target = (item.Target ?? string.Empty).Trim().Trim('"');
            if (target.Length == 0)
            {
                error = "У элемента не задана цель запуска.";
                return false;
            }

            if (item.Type == ItemType.Url)
            {
                if (!target.Contains("://", StringComparison.Ordinal))
                {
                    target = "https://" + target;
                }

                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    error = "Некорректный адрес сайта.";
                    return false;
                }

                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }

            target = Environment.ExpandEnvironmentVariables(target);
            if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return true;
            }

            if (File.Exists(target) || Directory.Exists(target))
            {
                var info = new ProcessStartInfo(target) { UseShellExecute = true };
                if (File.Exists(target))
                {
                    info.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? AppContext.BaseDirectory;
                }

                Process.Start(info);
                return true;
            }

            // Bare executable names such as notepad.exe and calc.exe are resolved by the shell.
            if (!target.Contains(Path.DirectorySeparatorChar) && !target.Contains(Path.AltDirectorySeparatorChar))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return true;
            }

            error = "Файл не найден: " + target;
            return false;
        }
        catch (Win32Exception ex)
        {
            error = "Windows не смогла запустить элемент: " + ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = "Ошибка запуска: " + ex.Message;
            return false;
        }
    }
}
