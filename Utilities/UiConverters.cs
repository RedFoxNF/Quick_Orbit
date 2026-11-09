using System.Globalization;
using System.IO;
using System.Windows.Data;
using QuickOrbit.Models;

namespace QuickOrbit.Utilities;

public sealed class ItemTypeToRussianConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ItemType type ? type.ToRussian() : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class IconDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "авто";
        }

        try
        {
            return Path.GetFileName(text);
        }
        catch
        {
            return text;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
