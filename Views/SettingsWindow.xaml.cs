using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QuickOrbit.Models;
using QuickOrbit.Services;
using QuickOrbit.ViewModels;

namespace QuickOrbit.Views;

public partial class SettingsWindow : Window
{
    private readonly MainController _controller;
    private readonly IconService _icons;
    private readonly FaviconService _favicons;
    private readonly SettingsViewModel _viewModel;
    private readonly DispatcherTimer _previewTimer;
    private bool _closed;

    public SettingsWindow(MainController controller, AppConfig config, IconService icons, FaviconService favicons)
    {
        InitializeComponent();
        _controller = controller;
        _icons = icons;
        _favicons = favicons;
        _viewModel = new SettingsViewModel(config);
        DataContext = _viewModel;

        PreviewSurface.SetConfig(_viewModel.Draft, icons);

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(70)
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };

        _viewModel.PreviewChanged += OnPreviewChanged;
        Loaded += (_, _) =>
        {
            RefreshPreview();
            PreviewSurface.StartAnimation();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                PreviewSurface.StopAnimation();
            }
            else if (IsLoaded)
            {
                PreviewSurface.StartAnimation();
            }
        };
        Closing += (_, _) => Cleanup();
        Closed += (_, _) =>
        {
            _controller.NotifySettingsClosed(this);
        };
    }

    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        if (_closed || !IsLoaded)
        {
            return;
        }

        PreviewSurface.Refresh(_viewModel.Draft);
        PreviewSurface.StartAnimation();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate(out var error))
        {
            SetStatus(error, isError: true);
            return;
        }

        try
        {
            var config = _viewModel.BuildConfig();
            var warning = _controller.ApplyConfig(config);
            SetStatus(
                warning is null
                    ? "Применено. Конфигурация сохранена, hotkey обновлён."
                    : "Применено. Конфигурация сохранена. " + warning,
                isError: warning is not null);
            RefreshPreview();
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось применить настройки: " + ex.Message, isError: true);
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedOrbit is null)
        {
            SetStatus("Сначала выберите орбиту.", isError: true);
            return;
        }

        var draft = new OrbitItem { Name = string.Empty, Type = ItemType.File, Target = string.Empty };
        var editor = new ItemEditorWindow(draft, _icons, _favicons, isNew: true) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            _viewModel.SelectedOrbit.Items.Add(editor.Result);
            _viewModel.SelectedItem = editor.Result;
            SetStatus("Элемент добавлен в выбранное кольцо.", isError: false);
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedItem();
    }

    private void EditSelectedItem()
    {
        if (_viewModel.SelectedOrbit is null || _viewModel.SelectedItem is null)
        {
            SetStatus("Выберите элемент в таблице.", isError: true);
            return;
        }

        var index = _viewModel.SelectedOrbit.Items.IndexOf(_viewModel.SelectedItem);
        var editor = new ItemEditorWindow(_viewModel.SelectedItem, _icons, _favicons, isNew: false) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null && index >= 0)
        {
            _viewModel.SelectedOrbit.Items[index] = editor.Result;
            _viewModel.SelectedItem = editor.Result;
            SetStatus("Элемент изменён.", isError: false);
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedOrbit is null || _viewModel.SelectedItem is null)
        {
            SetStatus("Выберите элемент в таблице.", isError: true);
            return;
        }

        var result = MessageBox.Show(this,
            $"Удалить элемент «{_viewModel.SelectedItem.Name}»?",
            "Quick Orbit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.SelectedOrbit.Items.Remove(_viewModel.SelectedItem);
        _viewModel.SelectedItem = _viewModel.SelectedOrbit.Items.FirstOrDefault();
        SetStatus("Элемент удалён.", isError: false);
    }

    private void AddOrbit_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddOrbit();
        SetStatus("Добавлена новая орбита.", isError: false);
    }

    private void RemoveOrbit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedOrbit is null)
        {
            return;
        }

        if (_viewModel.Orbits.Count <= 1)
        {
            SetStatus("Нельзя удалить единственную орбиту.", isError: true);
            return;
        }

        var result = MessageBox.Show(this,
            $"Удалить орбиту «{_viewModel.SelectedOrbit.Name}» и все её элементы?",
            "Quick Orbit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_viewModel.RemoveSelectedOrbit())
        {
            SetStatus("Орбита удалена.", isError: false);
        }
    }

    private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditSelectedItem();
    }

    private void OrbitIconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SyncIconSizeCheckBox?.IsChecked != true || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var size = Math.Clamp(e.NewValue, 32.0, 96.0);
        foreach (var orbit in viewModel.Orbits)
        {
            orbit.IconSize = size;
        }
    }

    private void SyncIconSize_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || viewModel.SelectedOrbit is not { } selected)
        {
            return;
        }

        foreach (var orbit in viewModel.Orbits)
        {
            orbit.IconSize = selected.IconSize;
        }
    }

    private void ItemsGrid_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = _viewModel.SelectedOrbit is not null && DropItemFactory.CanAccept(e.Data);
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ItemsGrid_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_viewModel.SelectedOrbit is null)
        {
            SetStatus("Сначала выберите орбиту.", isError: true);
            return;
        }

        var items = DropItemFactory.FromDataObject(e.Data);
        if (items.Count == 0)
        {
            SetStatus("Не удалось распознать перетаскиваемые файлы или ссылки.", isError: true);
            return;
        }

        foreach (var item in items)
        {
            _viewModel.SelectedOrbit.Items.Add(item);
        }

        _viewModel.SelectedItem = items[0];
        SetStatus($"Добавлено: {items.Count}. Загружаю favicon…", isError: false);

        var urls = items.Where(item => item.Type == ItemType.Url && string.IsNullOrWhiteSpace(item.Icon)).ToList();
        var loaded = 0;
        foreach (var item in urls)
        {
            var path = await _favicons.DownloadAsync(item.Target);
            if (path is not null)
            {
                item.Icon = path;
                loaded++;
            }
        }

        SetStatus(
            urls.Count == 0
                ? $"Добавлено: {items.Count}."
                : loaded == urls.Count
                    ? $"Добавлено: {items.Count}. Favicon загружены."
                    : $"Добавлено: {items.Count}. Favicon не найдены для {urls.Count - loaded}.",
            isError: false);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusLabel.Text = message;
        StatusLabel.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "SuccessBrush");
    }

    private void Cleanup()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _previewTimer.Stop();
        _viewModel.PreviewChanged -= OnPreviewChanged;
        _viewModel.Dispose();
        PreviewSurface.StopAnimation();
    }
}
