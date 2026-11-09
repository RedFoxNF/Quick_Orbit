using System.Collections.ObjectModel;
using QuickOrbit.Utilities;

namespace QuickOrbit.Models;

public sealed class AppConfig : ObservableObject
{
    private string _hotkey = "Ctrl+Alt+Space";
    private string _corner = "TopRight";
    private double _edgeMargin = 44;
    private double _overlayOpacity = 1.0;
    private string _theme = "Dark";
    private string _accentColor = "#64E3FF";
    private double _glowIntensity = 0.9;
    private double _parallaxDegrees = 4.0;
    private double _iconSize = 52.0;
    private string _iconShape = "RoundedSquare";

    public string Hotkey
    {
        get => _hotkey;
        set => SetProperty(ref _hotkey, value);
    }

    /// <summary>TopLeft, TopRight, BottomLeft or BottomRight.</summary>
    public string Corner
    {
        get => _corner;
        set => SetProperty(ref _corner, value);
    }

    public double EdgeMargin
    {
        get => _edgeMargin;
        set => SetProperty(ref _edgeMargin, value);
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => SetProperty(ref _overlayOpacity, value);
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public double GlowIntensity
    {
        get => _glowIntensity;
        set => SetProperty(ref _glowIntensity, value);
    }

    public double ParallaxDegrees
    {
        get => _parallaxDegrees;
        set => SetProperty(ref _parallaxDegrees, value);
    }

    /// <summary>Fallback icon size for orbits that have no explicit value.</summary>
    public double IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    /// <summary>RoundedSquare or Circle.</summary>
    public string IconShape
    {
        get => _iconShape;
        set => SetProperty(ref _iconShape, value);
    }

    public ObservableCollection<OrbitConfig> Orbits { get; set; } = new();

    public AppConfig Clone()
    {
        var clone = new AppConfig
        {
            Hotkey = Hotkey,
            Corner = Corner,
            EdgeMargin = EdgeMargin,
            OverlayOpacity = OverlayOpacity,
            Theme = Theme,
            AccentColor = AccentColor,
            GlowIntensity = GlowIntensity,
            ParallaxDegrees = ParallaxDegrees,
            IconSize = IconSize,
            IconShape = IconShape
        };

        foreach (var orbit in Orbits)
        {
            clone.Orbits.Add(orbit.Clone());
        }

        return clone;
    }

    public void Normalize()
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "Ctrl+Alt+Space" : Hotkey.Trim();
        Corner = Corner is "TopLeft" or "TopRight" or "BottomLeft" or "BottomRight" ? Corner : "TopRight";
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        AccentColor = ColorUtil.IsValid(AccentColor) ? AccentColor : "#64E3FF";
        EdgeMargin = Math.Clamp(EdgeMargin, 0, 300);
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0.2, 1.0);
        GlowIntensity = Math.Clamp(GlowIntensity, 0.0, 2.0);
        ParallaxDegrees = Math.Clamp(ParallaxDegrees, 0.0, 8.0);
        IconSize = Math.Clamp(IconSize, 32.0, 96.0);
        IconShape = string.Equals(IconShape, "Circle", StringComparison.OrdinalIgnoreCase)
            ? "Circle"
            : "RoundedSquare";
        Orbits ??= new ObservableCollection<OrbitConfig>();

        if (Orbits.Count == 0)
        {
            Orbits.Add(CreateDefaultOrbits()[0]);
        }

        var index = 1;
        foreach (var orbit in Orbits)
        {
            orbit.Name = string.IsNullOrWhiteSpace(orbit.Name) ? $"Орбита {index}" : orbit.Name.Trim();
            orbit.Radius = Math.Clamp(orbit.Radius, 60, 420);
            orbit.Tilt = Math.Clamp(orbit.Tilt, 12, 75);
            orbit.Speed = Math.Clamp(orbit.Speed, -45, 45);
            orbit.IconSize = orbit.IconSize <= 0
                ? IconSize
                : Math.Clamp(orbit.IconSize, 32.0, 96.0);
            orbit.Items ??= new ObservableCollection<OrbitItem>();
            foreach (var item in orbit.Items)
            {
                item.Name = string.IsNullOrWhiteSpace(item.Name) ? "Без названия" : item.Name.Trim();
                item.Target = item.Target?.Trim() ?? string.Empty;
                item.Icon = item.Icon?.Trim() ?? string.Empty;
            }

            index++;
        }
    }

    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        foreach (var orbit in CreateDefaultOrbits())
        {
            config.Orbits.Add(orbit);
        }

        return config;
    }

    private static List<OrbitConfig> CreateDefaultOrbits()
    {
        var inner = new OrbitConfig
        {
            Name = "Ближняя орбита",
            Radius = 172,
            Tilt = 36,
            Speed = 4.0
        };
        inner.Items.Add(new OrbitItem { Name = "Проводник", Type = ItemType.File, Target = "explorer.exe" });
        inner.Items.Add(new OrbitItem { Name = "Блокнот", Type = ItemType.File, Target = "notepad.exe" });
        inner.Items.Add(new OrbitItem { Name = "Калькулятор", Type = ItemType.File, Target = "calc.exe" });
        inner.Items.Add(new OrbitItem { Name = "Терминал", Type = ItemType.File, Target = "cmd.exe" });

        var outer = new OrbitConfig
        {
            Name = "Дальняя орбита",
            Radius = 282,
            Tilt = 30,
            Speed = -2.6
        };
        outer.Items.Add(new OrbitItem { Name = "GitHub", Type = ItemType.Url, Target = "https://github.com" });
        outer.Items.Add(new OrbitItem { Name = "YouTube", Type = ItemType.Url, Target = "https://www.youtube.com" });
        outer.Items.Add(new OrbitItem { Name = "Google", Type = ItemType.Url, Target = "https://www.google.com" });
        outer.Items.Add(new OrbitItem { Name = "ChatGPT", Type = ItemType.Url, Target = "https://chatgpt.com" });

        return new List<OrbitConfig> { inner, outer };
    }
}
