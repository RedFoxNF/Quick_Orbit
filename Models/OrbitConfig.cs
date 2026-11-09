using System.Collections.ObjectModel;
using QuickOrbit.Utilities;

namespace QuickOrbit.Models;

public sealed class OrbitConfig : ObservableObject
{
    private string _name = "Орбита";
    private double _radius = 180;
    private double _tilt = 34;
    private double _speed = 4.0;
    private double _iconSize;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Horizontal radius in device-independent pixels.</summary>
    public double Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, value);
    }

    /// <summary>Tilt in degrees: 0 is a line, 90 is a circle.</summary>
    public double Tilt
    {
        get => _tilt;
        set => SetProperty(ref _tilt, value);
    }

    /// <summary>Angular speed in degrees per second.</summary>
    public double Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    /// <summary>Icon plate size in pixels; 0 means inherit AppConfig.IconSize.</summary>
    public double IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public ObservableCollection<OrbitItem> Items { get; set; } = new();

    public OrbitConfig Clone()
    {
        var clone = new OrbitConfig
        {
            Name = Name,
            Radius = Radius,
            Tilt = Tilt,
            Speed = Speed,
            IconSize = IconSize
        };

        foreach (var item in Items)
        {
            clone.Items.Add(item.Clone());
        }

        return clone;
    }
}
