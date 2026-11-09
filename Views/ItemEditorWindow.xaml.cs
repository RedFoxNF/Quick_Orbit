using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QuickOrbit.Models;
using QuickOrbit.Services;
using QuickOrbit.Utilities;

namespace QuickOrbit.Views;

public partial class ItemEditorWindow : Window
{
    private readonly OrbitItem _draft;
    private readonly IconService _icons;
    private readonly FaviconService _favicons;
    private readonly bool _isNew;
    private bool _busy;

    public ItemEditorWindow(OrbitItem item, IconService icons, FaviconService favicons, bool isNew)
    {
        InitializeComponent();
        _draft = item.Clone();
        _icons = icons;
        _favicons = favicons;
        _isNew = isNew;
        DataContext = _draft;

        HeaderText.Text = isNew ? "Новый элемент" : "Изменить элемент";
        TypeCombo.SelectedValue = _draft.Type;
        _draft.PropertyChanged += OnDraftPropertyChanged;
        Loaded += (_, _) =>
        {
            RefreshIconPreview();
            UpdateTypeUi();
            NameBox.Focus();
            NameBox.SelectAll();
        };
        Closed += (_, _) => _draft.PropertyChanged -= OnDraftPropertyChanged;
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshIconPreview();

    public OrbitItem? Result { get; private set; }

    private void UpdateTypeUi()
    {
        var isUrl = _draft.Type == ItemType.Url;
        TargetLabel.Text = isUrl ? "Адрес сайта" : "Путь к программе или файлу";
        TargetHint.Text = isUrl
            ? "Например, https://github.com. Favicon будет скачан при сохранении и сохранён локально."
            : "Можно указать полный путь или имя команды Windows, например notepad.exe.";
        BrowseButton.Visibility = isUrl ? Visibility.Collapsed : Visibility.Visible;
        FaviconButton.Visibility = isUrl ? Visibility.Visible : Visibility.Collapsed;
        IconHint.Text = isUrl
            ? "Пусто — будет загружен favicon сайта. Своя картинка имеет приоритет."
            : "Пусто — иконка извлекается из EXE через Win32. Своя картинка имеет приоритет.";
    }

    private void RefreshIconPreview()
    {
        var source = _icons.GetIcon(_draft);
        IconPreview.Source = source;
        IconPreview.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackText.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        FallbackText.Text = _draft.FallbackLetter;

        var color = ColorUtil.StableColor(_draft.Name);
        if (source is null)
        {
            IconPreviewBorderBrush(color);
        }
        else
        {
            IconPreviewBorderBrush(Color.FromRgb(48, 66, 89));
        }
    }

    private void IconPreviewBorderBrush(Color color)
    {
        if (IconPreview.Parent is Grid grid && grid.Parent is Border border)
        {
            border.BorderBrush = ColorUtil.Brush(color, 160);
            border.Background = ColorUtil.Brush(color, sourceIsImage() ? (byte)22 : (byte)46);
        }

        bool sourceIsImage() => IconPreview.Source is not null;
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeCombo.SelectedValue is ItemType type)
        {
            _draft.Type = type;
            UpdateTypeUi();
            RefreshIconPreview();
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите программу или файл",
            Filter = "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd;*.com;*.msc;*.url|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _draft.Target = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_draft.Name) ||
                string.Equals(_draft.Name, "Новый элемент", StringComparison.Ordinal))
            {
                _draft.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            }

            RefreshIconPreview();
        }
    }

    private void IconBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите иконку",
            Filter = "Изображения и иконки|*.png;*.ico;*.jpg;*.jpeg;*.bmp|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _draft.Icon = dialog.FileName;
            RefreshIconPreview();
        }
    }

    private async void Favicon_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var uri = FaviconService.NormalizeUrl(_draft.Target);
        if (uri is null)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = "Сначала укажите корректный адрес сайта.";
            return;
        }

        await DownloadFaviconAsync(uri);
    }

    private async Task DownloadFaviconAsync(Uri uri)
    {
        _busy = true;
        SaveButton.IsEnabled = false;
        FaviconButton.IsEnabled = false;
        StatusText.Foreground = (Brush)FindResource("MutedBrush");
        StatusText.Text = "Загружаю favicon…";

        try
        {
            var path = await _favicons.DownloadAsync(uri.AbsoluteUri);
            if (path is null)
            {
                StatusText.Foreground = (Brush)FindResource("DangerBrush");
                StatusText.Text = "Favicon не найден. Будет использован цветной круг с первой буквой.";
                return;
            }

            _draft.Icon = path;
            RefreshIconPreview();
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
            StatusText.Text = "Favicon сохранён: " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (Brush)FindResource("DangerBrush");
            StatusText.Text = "Не удалось загрузить favicon: " + ex.Message;
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
            FaviconButton.IsEnabled = true;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            ShowError("Введите название элемента.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft.Target))
        {
            ShowError(_draft.Type == ItemType.Url ? "Введите адрес сайта." : "Укажите путь к программе или файлу.");
            return;
        }

        if (_draft.Type == ItemType.Url)
        {
            var uri = FaviconService.NormalizeUrl(_draft.Target);
            if (uri is null)
            {
                ShowError("Адрес сайта некорректен. Пример: https://github.com");
                return;
            }

            _draft.Target = uri.AbsoluteUri;

            if (string.IsNullOrWhiteSpace(_draft.Icon))
            {
                await DownloadFaviconAsync(uri);

                // A failed favicon download is not fatal: the colored fallback is intentional.
                if (_draft.Icon is null)
                {
                    _draft.Icon = string.Empty;
                }
            }
        }
        else
        {
            var target = _draft.Target.Trim().Trim('"');
            if (!target.Contains("://", StringComparison.Ordinal) &&
                !target.Contains(Path.DirectorySeparatorChar) &&
                !target.Contains(Path.AltDirectorySeparatorChar) &&
                !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                // A bare command such as notepad.exe is valid.
            }
            else
            {
                _draft.Target = target;
            }
        }

        _draft.Name = _draft.Name.Trim();
        _draft.Target = _draft.Target.Trim();
        Result = _draft;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = (Brush)FindResource("DangerBrush");
        StatusText.Text = message;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            DialogResult = false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
