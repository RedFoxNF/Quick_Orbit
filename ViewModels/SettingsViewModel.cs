using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using QuickOrbit.Models;
using QuickOrbit.Services;
using QuickOrbit.Utilities;

namespace QuickOrbit.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _draft;
    private readonly List<INotifyPropertyChanged> _observedObjects = new();
    private readonly List<INotifyCollectionChanged> _observedCollections = new();
    private OrbitConfig? _selectedOrbit;
    private OrbitItem? _selectedItem;
    private Brush _accentPreviewBrush = Brushes.Transparent;

    public SettingsViewModel(AppConfig source)
    {
        _draft = source.Clone();
        _draft.Normalize();
        _selectedOrbit = _draft.Orbits.FirstOrDefault();
        RefreshAccentBrush(raiseEvent: false);
        AttachObservers();
    }

    public AppConfig Draft => _draft;

    public ObservableCollection<OrbitConfig> Orbits => _draft.Orbits;

    public OrbitConfig? SelectedOrbit
    {
        get => _selectedOrbit;
        set
        {
            if (!SetProperty(ref _selectedOrbit, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedOrbit));
            SelectedItem = null;
        }
    }

    public OrbitItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
            }
        }
    }

    public bool HasSelectedOrbit => _selectedOrbit is not null;

    public bool HasSelectedItem => _selectedItem is not null;

    public string Summary
    {
        get
        {
            var itemCount = _draft.Orbits.Sum(orbit => orbit.Items.Count);
            return $"{_draft.Orbits.Count} кольца · {itemCount} элементов";
        }
    }

    public Brush AccentPreviewBrush
    {
        get => _accentPreviewBrush;
        private set => SetProperty(ref _accentPreviewBrush, value);
    }

    public event EventHandler? PreviewChanged;

    public void AddOrbit()
    {
        var number = _draft.Orbits.Count + 1;
        var orbit = new OrbitConfig
        {
            Name = "Орбита " + number,
            Radius = Math.Min(380, 210 + (number * 20)),
            Tilt = 32,
            Speed = number % 2 == 0 ? -2.8 : 3.8,
            IconSize = _draft.IconSize
        };

        orbit.Items.Add(new OrbitItem { Name = "Новый элемент", Type = ItemType.File, Target = "notepad.exe" });
        _draft.Orbits.Add(orbit);
        SelectedOrbit = orbit;
    }

    public bool RemoveSelectedOrbit()
    {
        if (_selectedOrbit is null || _draft.Orbits.Count <= 1)
        {
            return false;
        }

        var index = _draft.Orbits.IndexOf(_selectedOrbit);
        _draft.Orbits.Remove(_selectedOrbit);
        SelectedOrbit = _draft.Orbits[Math.Clamp(index, 0, _draft.Orbits.Count - 1)];
        return true;
    }

    public bool Validate(out string error)
    {
        error = string.Empty;

        if (!HotkeyManager.Validate(_draft.Hotkey, out error))
        {
            return false;
        }

        if (!ColorUtil.IsValid(_draft.AccentColor))
        {
            error = "Акцентный цвет должен быть в формате #RRGGBB или #AARRGGBB.";
            return false;
        }

        if (_draft.Orbits.Count == 0)
        {
            error = "Должна остаться хотя бы одна орбита.";
            return false;
        }

        foreach (var orbit in _draft.Orbits)
        {
            if (orbit.Items.Count == 0)
            {
                error = $"На орбите «{orbit.Name}» нет элементов. Добавьте хотя бы один.";
                return false;
            }

            foreach (var item in orbit.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    error = "У каждого элемента должно быть название.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.Target))
                {
                    error = $"У элемента «{item.Name}» не задана цель.";
                    return false;
                }

                if (item.Type == ItemType.Url && FaviconService.NormalizeUrl(item.Target) is null)
                {
                    error = $"У элемента «{item.Name}» некорректный адрес сайта.";
                    return false;
                }
            }
        }

        return true;
    }

    public AppConfig BuildConfig()
    {
        var config = _draft.Clone();
        config.Normalize();
        return config;
    }

    private void AttachObservers()
    {
        DetachObservers();

        ObserveObject(_draft);
        ObserveCollection(_draft.Orbits);
        foreach (var orbit in _draft.Orbits)
        {
            ObserveObject(orbit);
            ObserveCollection(orbit.Items);
            foreach (var item in orbit.Items)
            {
                ObserveObject(item);
            }
        }
    }

    private void DetachObservers()
    {
        foreach (var item in _observedObjects)
        {
            item.PropertyChanged -= OnDataPropertyChanged;
        }

        foreach (var collection in _observedCollections)
        {
            collection.CollectionChanged -= OnDataCollectionChanged;
        }

        _observedObjects.Clear();
        _observedCollections.Clear();
    }

    private void ObserveObject(INotifyPropertyChanged item)
    {
        if (_observedObjects.Contains(item))
        {
            return;
        }

        item.PropertyChanged += OnDataPropertyChanged;
        _observedObjects.Add(item);
    }

    private void ObserveCollection(INotifyCollectionChanged collection)
    {
        if (_observedCollections.Contains(collection))
        {
            return;
        }

        collection.CollectionChanged += OnDataCollectionChanged;
        _observedCollections.Add(collection);
    }

    private void OnDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _draft && e.PropertyName == nameof(AppConfig.AccentColor))
        {
            RefreshAccentBrush(raiseEvent: true);
        }

        OnPropertyChanged(nameof(Summary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachObservers();

        if (_selectedOrbit is null || !_draft.Orbits.Contains(_selectedOrbit))
        {
            SelectedOrbit = _draft.Orbits.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasSelectedOrbit));
        OnPropertyChanged(nameof(HasSelectedItem));
        OnPropertyChanged(nameof(Summary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshAccentBrush(bool raiseEvent)
    {
        var color = ColorUtil.Parse(_draft.AccentColor, Color.FromRgb(100, 227, 255));
        var brush = ColorUtil.Brush(color);
        if (raiseEvent)
        {
            AccentPreviewBrush = brush;
        }
        else
        {
            _accentPreviewBrush = brush;
        }
    }

    public void Dispose()
    {
        DetachObservers();
    }
}
