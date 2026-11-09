using System.Text.Json.Serialization;
using QuickOrbit.Utilities;

namespace QuickOrbit.Models;

public sealed class OrbitItem : ObservableObject
{
    private string _name = "Новый элемент";
    private ItemType _type = ItemType.File;
    private string _target = string.Empty;
    private string _icon = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(FallbackLetter));
            }
        }
    }

    public ItemType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(TypeName));
            }
        }
    }

    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    /// <summary>Optional icon override or a cached favicon path.</summary>
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    [JsonIgnore]
    public string TypeName => Type.ToRussian();

    [JsonIgnore]
    public string FallbackLetter
    {
        get
        {
            var trimmed = (Name ?? string.Empty).Trim();
            return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
        }
    }

    public OrbitItem Clone() => new()
    {
        Name = Name,
        Type = Type,
        Target = Target,
        Icon = Icon
    };
}
