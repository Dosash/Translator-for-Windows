using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Translator.Core;

namespace Translator.UI;

/// <summary>Shared "is there something" test: true, non-empty text/collection, non-zero number, any other non-null object.</summary>
internal static class Truthiness
{
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        int i => i != 0,
        double d => d != 0,
        ICollection c => c.Count > 0,
        _ => true,
    };
}

/// <summary>Visible when the value is truthy (see <see cref="Truthiness"/>); <see cref="Invert"/> flips it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Truthiness.IsTruthy(value) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TruthyConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Truthiness.IsTruthy(value) ^ Invert;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EnumToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Enum.TryParse(targetType, text, out var result) ? result! : Binding.DoNothing;
}

/// <summary>Post-processes text from <see cref="LocExtension"/>: upper-cased section titles, window titles without "…".</summary>
public sealed class LocTextConverter : IValueConverter
{
    public bool Upper { get; init; }

    public bool TrimEllipsis { get; init; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
        {
            return value;
        }
        if (TrimEllipsis)
        {
            text = TextFormat.TrimTrailingEllipsis(text);
        }
        return Upper ? text.ToUpper(L10n.CultureFor(L10n.CurrentCode)) : text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
