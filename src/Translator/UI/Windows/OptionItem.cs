using System.Windows.Media;
using Translator.Core;

namespace Translator.UI.Windows;

/// <summary>One selectable card/capsule (theme or panel size) in settings and first run.</summary>
public sealed class OptionItem : ObservableObject
{
    private bool _isSelected;
    private Brush? _swatch;
    private Brush? _accent;

    public OptionItem(object value, string captionKey)
    {
        Value = value;
        CaptionKey = captionKey;
    }

    public object Value { get; }

    public string CaptionKey { get; }

    /// <summary>Untranslated product name (theme names), used instead of <see cref="NameKey"/>.</summary>
    public string? FixedName { get; init; }

    public string? NameKey { get; init; }

    public string Name => FixedName ?? (NameKey is null ? string.Empty : L10n.T(NameKey));

    public string Caption => L10n.T(CaptionKey);

    public Brush? Swatch
    {
        get => _swatch;
        set => SetField(ref _swatch, value);
    }

    public Brush? Accent
    {
        get => _accent;
        set => SetField(ref _accent, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Caption));
    }
}
