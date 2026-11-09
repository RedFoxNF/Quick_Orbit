namespace QuickOrbit.Models;

public enum ItemType
{
    File,
    Url
}

public static class ItemTypeExtensions
{
    public static string ToRussian(this ItemType type) => type == ItemType.Url ? "Сайт" : "Файл";
}
