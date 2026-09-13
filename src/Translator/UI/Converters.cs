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

/// <summary>For radio-style cards: IsChecked = (value == parameter); checking selects the parameter's enum value.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

public sealed class EnumToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Enum.TryParse(targetType, text, out var result) ? result! : Binding.DoNothing;
}

public sealed class UpperCaseConverter : IValueConverter
{
    public static readonly UpperCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? text.ToUpper(L10n.CultureFor(L10n.CurrentCode)) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// MultiBinding: [0] = an L10n key, [1] = any <see cref="LocalizationSource"/> indexer binding (only there so the
/// text re-evaluates when the UI language changes).
/// </summary>
public sealed class LocKeyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is string key ? L10n.T(key) : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Privacy capsule icon: globe after an online translation, lock otherwise.</summary>
public sealed class EngineGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is EngineKind.Google ? Icons.Globe : Icons.Lock;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
