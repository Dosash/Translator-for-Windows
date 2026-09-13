using System.Windows;

namespace Translator.UI.Controls;

/// <summary>Attached properties that templates read (controls like ComboBox have no CornerRadius of their own).</summary>
public static class Assist
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Assist), new FrameworkPropertyMetadata(new CornerRadius(10)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}
