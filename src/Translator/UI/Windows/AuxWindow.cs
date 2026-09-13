using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Translator.Core;
using Translator.Platform;

namespace Translator.UI.Windows;

/// <summary>
/// Base for the normal captioned windows (settings, history, first run, offline languages): Mica backdrop
/// with the theme gradient over it, a light/dark title bar that follows the theme, centered, Esc closes.
/// </summary>
public class AuxWindow : Window
{
    public AuxWindow()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        Background = Brushes.Transparent;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(FontFamilyProperty, "UiFontFamily");
        SetResourceReference(ForegroundProperty, "InkBrush");
        if (AppInfo.Icon is { } icon)
        {
            Icon = icon;
        }

        ThemeManager.ThemeChanged += OnThemeChanged;
        L10n.LanguageChanged += OnLanguageChangedHandler;
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            L10n.LanguageChanged -= OnLanguageChangedHandler;
        };
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        if (!IsVisible)
        {
            Show();
        }
        if (!Activate() || !IsActive)
        {
            ForegroundWindow.Activate(new WindowInteropHelper(this).EnsureHandle());
        }
    }

    /// <summary>Called on the UI thread after the UI language changed (for texts not bound via Loc).</summary>
    protected virtual void OnLanguageChanged()
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(EscapeHook);
        ApplyEffects();
    }

    /// <summary>Esc that no element handled, including when nothing in the window has keyboard focus yet.</summary>
    private IntPtr EscapeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Translator.UI.Interop.UiNativeMethods.WM_KEYDOWN && wParam.ToInt64() == Translator.UI.Interop.UiNativeMethods.VK_ESCAPE)
        {
            handled = true;
            Dispatcher.BeginInvoke(Close);
        }
        return IntPtr.Zero;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>Wheel over nested text boxes would otherwise be swallowed instead of scrolling the page.</summary>
    protected static void ForwardMouseWheel(ScrollViewer scroller, MouseWheelEventArgs e)
    {
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void ApplyEffects()
    {
        var backdrop = WindowEffects.Apply(this, BackdropKind.Mica, ThemeManager.Current.IsDark());
        SetResourceReference(BackgroundProperty, backdrop ? "BackgroundMicaBrush" : "BackgroundOpaqueBrush");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
        {
            ApplyEffects();
        }
    }

    private void OnLanguageChangedHandler(object? sender, EventArgs e) =>
        // Deferred: the change may originate inside a picker's selection handler in this very window.
        Dispatcher.BeginInvoke(OnLanguageChanged);
}
