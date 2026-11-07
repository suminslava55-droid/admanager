using System.Globalization;
using System.Windows.Data;

namespace AdManager.Converters;

/// <summary>Логическое значение в русское «да/нет» для карточек и таблиц.</summary>
public sealed class BoolToRussianConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? (flag ? "да" : "нет") : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
