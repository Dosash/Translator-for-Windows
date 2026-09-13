using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Translator.Core;
using static Translator.UI.Interop.UiNativeMethods;

namespace Translator.UI;

public enum BackdropKind
{
    /// <summary>Transient surfaces: panel and bubble.</summary>
    Acrylic,
    /// <summary>Normal windows: settings, history, first run, offline languages.</summary>
    Mica,
}

/// <summary>
/// DWM integration: Windows 11 system backdrop (Acrylic/Mica) behind a transparent WPF surface,
/// rounded corners and a dark/light frame. Callers draw an opaque theme gradient when this returns false.
/// </summary>
public static class WindowEffects
{
    private static readonly int OsBuild = Environment.OSVersion.Version.Build;

    public static bool IsWindows11 => OsBuild >= 22000;

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE is available from Windows 11 22H2.</summary>
    public static bool SupportsSystemBackdrop => OsBuild >= 22621;

    /// <summary>Returns true when a system backdrop is active and the window should draw translucent content.</summary>
    public static bool Apply(Window window, BackdropKind backdrop, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
            if (!IsWindows11)
            {
                return false;
            }
            SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

            var source = HwndSource.FromHwnd(hwnd);
            if (!SupportsSystemBackdrop || SystemParameters.HighContrast || !IsTransparencyEnabled() || source?.CompositionTarget is null)
            {
                DisableBackdrop(hwnd, source);
                return false;
            }

            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0)
            {
                DisableBackdrop(hwnd, source);
                return false;
            }
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var type = backdrop == BackdropKind.Acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
            if (SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, type) != 0)
            {
                DisableBackdrop(hwnd, source);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"WindowEffects.Apply failed ({ex.GetType().Name})");
            return false;
        }
    }

    private static void DisableBackdrop(IntPtr hwnd, HwndSource? source)
    {
        if (SupportsSystemBackdrop)
        {
            SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
        }
        if (source?.CompositionTarget is { } target)
        {
            target.BackgroundColor = SystemColors.WindowColor;
        }
    }

    private static int SetAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    /// <summary>"Transparency effects" in Personalization; when off DWM draws backdrops as flat color.</summary>
    private static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }
}
