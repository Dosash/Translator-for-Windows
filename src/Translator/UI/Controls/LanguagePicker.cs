using System.Collections;
using System.Windows;
using System.Windows.Controls;
using Translator.Core;

namespace Translator.UI.Controls;

/// <summary>
/// Dropdown of <see cref="LanguageOption"/>s with a checkmark on the selected one, bound by code.
/// Selection is synced by hand: a plain SelectedValue binding would push null into the model whenever
/// the options list is rebuilt (e.g. on a UI-language change).
/// </summary>
public sealed class LanguagePicker : ComboBox
{
    public static readonly DependencyProperty SelectedCodeProperty = DependencyProperty.Register(
        nameof(SelectedCode), typeof(string), typeof(LanguagePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((LanguagePicker)d).SyncSelectionFromCode()));

    private bool _syncing;

    public string? SelectedCode
    {
        get => (string?)GetValue(SelectedCodeProperty);
        set => SetValue(SelectedCodeProperty, value);
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        _syncing = true;
        try
        {
            base.OnItemsSourceChanged(oldValue, newValue);
        }
        finally
        {
            _syncing = false;
        }
        SyncSelectionFromCode();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_syncing || SelectedItem is not LanguageOption option || option.Code == SelectedCode)
        {
            return;
        }
        SetCurrentValue(SelectedCodeProperty, option.Code);
    }

    private void SyncSelectionFromCode()
    {
        _syncing = true;
        try
        {
            SelectedItem = Items.OfType<LanguageOption>().FirstOrDefault(o => o.Code == SelectedCode);
        }
        finally
        {
            _syncing = false;
        }
    }
}
