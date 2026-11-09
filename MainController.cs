using System.Windows;
using QuickOrbit.Models;
using QuickOrbit.Services;
using QuickOrbit.Views;

namespace QuickOrbit;

public sealed class MainController
{
    private readonly IconService _icons = new();
    private readonly FaviconService _favicons = new();

    private AppConfig _config;
    private MainWindow _overlay = null!;
    private HotkeyManager? _hotkey;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private bool _started;
    private bool _shutdown;

    public MainController()
    {
        _config = ConfigStore.LoadOrCreate(out var warning);
        if (AppPaths.IsFallback)
        {
            warning = CombineWarnings(warning,
                "Каталог %APPDATA% недоступен. Данные сохраняются в " + AppPaths.DataDirectory);
        }

        StartupWarning = warning;
    }

    private string? StartupWarning { get; set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _overlay = new MainWindow();
        _overlay.ItemActivated += OnItemActivated;
        _overlay.ItemsDropped += OnItemsDropped;
        _overlay.Configure(_config, _icons);

        _tray = new TrayIconService(
            ToggleOverlay,
            ShowSettings,
            ExitApplication,
            () => _overlay.IsOverlayVisible,
            _hotkey?.CurrentText ?? _config.Hotkey);

        var hotkeyWarning = TryInitializeHotkey();
        if (!string.IsNullOrWhiteSpace(hotkeyWarning))
        {
            StartupWarning = CombineWarnings(StartupWarning, hotkeyWarning);
            AppPaths.AppendLog(hotkeyWarning);
        }

        if (!string.IsNullOrWhiteSpace(StartupWarning))
        {
            _tray.ShowError("Quick Orbit", StartupWarning);
        }

        _ = RefreshMissingFaviconsAsync();
    }

    public void ToggleOverlay()
    {
        if (_shutdown)
        {
            return;
        }

        _overlay.ToggleOverlay();
    }

    public void ShowOverlay(bool sticky = false)
    {
        if (_shutdown)
        {
            return;
        }

        _overlay.ShowOverlay(sticky);
    }

    public void ShowSettings()
    {
        if (_shutdown)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            _settingsWindow.Focus();
            return;
        }

        _overlay.HideOverlay(immediate: true);

        _settingsWindow = new SettingsWindow(this, _config, _icons, _favicons)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void NotifySettingsClosed(SettingsWindow window)
    {
        if (ReferenceEquals(_settingsWindow, window))
        {
            _settingsWindow = null;
        }
    }

    public string? ApplyConfig(AppConfig config)
    {
        if (_shutdown)
        {
            return "Приложение завершает работу.";
        }

        var normalized = config.Clone();
        normalized.Normalize();

        if (!HotkeyManager.Validate(normalized.Hotkey, out var hotkeyError))
        {
            throw new InvalidOperationException(hotkeyError);
        }

        var oldHotkey = _hotkey?.CurrentText;
        var hotkeyChanged = false;

        // The hotkey is critical: if it cannot be registered, nothing is applied.
        try
        {
            if (_hotkey is null)
            {
                _hotkey = new HotkeyManager(normalized.Hotkey);
                AttachHotkeyEvents();
                hotkeyChanged = true;
            }
            else if (!_hotkey.TryChange(normalized.Hotkey, out hotkeyError))
            {
                throw new InvalidOperationException(hotkeyError);
            }
            else
            {
                hotkeyChanged = true;
            }

            normalized.Hotkey = _hotkey.CurrentText;
        }
        catch
        {
            if (hotkeyChanged && oldHotkey is not null && _hotkey is not null)
            {
                _hotkey.TryChange(oldHotkey, out _);
            }

            throw;
        }

        try
        {
            ConfigStore.Save(normalized);
        }
        catch
        {
            if (hotkeyChanged && oldHotkey is not null && _hotkey is not null)
            {
                _hotkey.TryChange(oldHotkey, out _);
            }

            throw;
        }

        _config = normalized;
        _overlay.Configure(_config, _icons);
        _tray?.UpdateHotkey(_hotkey?.CurrentText ?? _config.Hotkey);

        if (_overlay.IsOverlayVisible)
        {
            _overlay.ShowOverlay();
        }

        return null;
    }

    private string? TryInitializeHotkey()
    {
        try
        {
            _hotkey = new HotkeyManager(_config.Hotkey);
            AttachHotkeyEvents();
            _tray?.UpdateHotkey(_hotkey.CurrentText);
            return null;
        }
        catch (Exception ex)
        {
            AppPaths.AppendLog("Configured hotkey failed: " + ex.Message);

            try
            {
                _hotkey = new HotkeyManager("Ctrl+Alt+Shift+Space");
                AttachHotkeyEvents();
                _tray?.UpdateHotkey(_hotkey.CurrentText);
                return $"Сочетание «{_config.Hotkey}» недоступно. Временно используется Ctrl+Alt+Shift+Space.";
            }
            catch (Exception fallbackError)
            {
                AppPaths.AppendLog("Fallback hotkey failed: " + fallbackError.Message);
                return "Не удалось зарегистрировать глобальный hotkey. Открывайте настройки через значок в трее.";
            }
        }
    }

    private void AttachHotkeyEvents()
    {
        if (_hotkey is not null)
        {
            _hotkey.Pressed += ToggleOverlay;
        }
    }

    private void OnItemActivated(OrbitItem item)
    {
        _overlay.HideOverlay(immediate: true);

        if (LaunchService.TryLaunch(item, out var error))
        {
            return;
        }

        _tray?.ShowError("Quick Orbit", error);
    }

    private async void OnItemsDropped(IReadOnlyList<OrbitItem> items, Point point)
    {
        if (_shutdown || items.Count == 0 || _config.Orbits.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(_overlay.GetOrbitIndexAt(point), 0, _config.Orbits.Count - 1);
        var orbit = _config.Orbits[index];

        foreach (var item in items)
        {
            orbit.Items.Add(item);
        }

        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            foreach (var item in items)
            {
                orbit.Items.Remove(item);
            }

            _tray?.ShowError("Quick Orbit", "Не удалось сохранить элементы: " + ex.Message);
            return;
        }

        _overlay.Configure(_config, _icons);
        _overlay.ShowOverlay(sticky: true);
        _tray?.ShowInfo("Quick Orbit",
            $"Добавлено в «{orbit.Name}»: {items.Count}. Favicon загружается в фоне.");

        await EnrichIconsAsync(items);
    }

    private async Task EnrichIconsAsync(IReadOnlyList<OrbitItem> items)
    {
        var pending = items
            .Where(item => item.Type == ItemType.Url && string.IsNullOrWhiteSpace(item.Icon))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var item in pending)
        {
            if (_shutdown)
            {
                return;
            }

            var path = await _favicons.DownloadAsync(item.Target);
            if (path is not null)
            {
                item.Icon = path;
                changed = true;
            }
        }

        if (!changed || _shutdown)
        {
            return;
        }

        try
        {
            ConfigStore.Save(_config);
            _overlay.Configure(_config, _icons);
        }
        catch (Exception ex)
        {
            AppPaths.AppendLog("Dropped icon save failed: " + ex.Message);
        }
    }

    private async Task RefreshMissingFaviconsAsync()
    {
        var pending = _config.Orbits
            .SelectMany(orbit => orbit.Items)
            .Where(item => item.Type == ItemType.Url &&
                           (string.IsNullOrWhiteSpace(item.Icon) ||
                            !File.Exists(IconService.ResolveConfiguredIcon(item.Icon))))
            .Take(24)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var item in pending)
        {
            if (_shutdown)
            {
                return;
            }

            var path = await _favicons.DownloadAsync(item.Target);
            if (path is not null)
            {
                item.Icon = path;
                changed = true;
            }
        }

        if (!changed || _shutdown)
        {
            return;
        }

        try
        {
            ConfigStore.Save(_config);
            _overlay.Configure(_config, _icons);
        }
        catch (Exception ex)
        {
            AppPaths.AppendLog("Startup icon save failed: " + ex.Message);
        }
    }

    private void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;

        if (_settingsWindow is not null)
        {
            var settings = _settingsWindow;
            _settingsWindow = null;
            settings.Close();
        }

        if (_overlay is not null)
        {
            _overlay.ItemActivated -= OnItemActivated;
            _overlay.ItemsDropped -= OnItemsDropped;
            _overlay.ShutdownOverlay();
        }
    }

    private static string CombineWarnings(string? first, string second)
    {
        return string.IsNullOrWhiteSpace(first) ? second : first + " " + second;
    }
}
