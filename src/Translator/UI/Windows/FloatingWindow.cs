using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Translator.Core;
using Translator.Platform;
using Translator.UI.Interop;

namespace Translator.UI.Windows;

/// <summary>
/// Borderless, topmost tool window shared by the panel and the bubble (port of macOS <c>FloatingPanel</c>):
/// Acrylic backdrop, hides on deactivation and Esc, draggable by its background, positioned in physical
/// pixels and re-anchored whenever its size changes.
/// </summary>
public class FloatingWindow : Window
{
    private static readonly TimeSpan OutsideClickWindow = TimeSpan.FromMilliseconds(250);

    private DateTime _lastHideUtc = DateTime.MinValue;
    private bool _keepsVisibleBehindOtherWindows;
    private bool _allowClose;
    private bool _userMoved;
    private bool _dragging;
    private Func<PixelSize, PixelPoint>? _placement;

    internal enum PositionSource
    {
        /// <summary>Leave the position to the user/system (a drag is in progress).</summary>
        Keep,
        /// <summary>Re-anchor with the placement function.</summary>
        Anchor,
        /// <summary>Keep the user's position, clamped into the work area.</summary>
        ClampUserPosition,
    }

    public FloatingWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;
        Background = Brushes.Transparent;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(FontFamilyProperty, "UiFontFamily");
        SetResourceReference(ForegroundProperty, "InkBrush");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    /// <summary>
    /// While an auxiliary window is open the panel drops to the normal z-level and doesn't hide on
    /// deactivation, so it stays visible behind that window.
    /// </summary>
    public bool KeepsVisibleBehindOtherWindows
    {
        get => _keepsVisibleBehindOtherWindows;
        set
        {
            _keepsVisibleBehindOtherWindows = value;
            Topmost = !value;
        }
    }

    /// <summary>True right after a hide, so the tray click that caused it doesn't immediately reopen the window.</summary>
    public bool JustHidByOutsideClick => DateTime.UtcNow - _lastHideUtc < OutsideClickWindow;

    protected IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>
    /// Shows and activates the window at the position computed by <paramref name="placement"/> from the
    /// window's pixel size. The same function is re-applied whenever the size changes.
    /// </summary>
    public void ShowPlaced(Func<PixelSize, PixelPoint> placement)
    {
        _placement = placement;
        _userMoved = false;
        var hwnd = Handle;
        UpdateLayout();
        PlaceNow();
        if (!IsVisible)
        {
            Show();
            // SizeToContent settles during Show(); anchor again with the final size.
            PlaceNow();
        }
        ActivateWindow();
        OnShownPlaced();
    }

    public void ActivateWindow()
    {
        if (!Activate() || !IsActive)
        {
            ForegroundWindow.Activate(Handle);
        }
    }

    public void HideWindow()
    {
        if (!IsVisible)
        {
            return;
        }
        _lastHideUtc = DateTime.UtcNow;
        Hide();
    }

    public void CloseForQuit()
    {
        _allowClose = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        Close();
    }

    protected virtual void OnShownPlaced()
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.SetToolWindow(Handle);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        ApplyEffects();
    }

    /// <summary>
    /// Re-anchors inside the resize itself (SizeToContent growth, DPI change), so the window never
    /// shows a frame at the old position with the new size.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == UiNativeMethods.WM_KEYDOWN && wParam.ToInt64() == UiNativeMethods.VK_ESCAPE)
        {
            // Esc that no element handled: WPF only dispatches key messages it didn't consume, which also
            // covers the case where nothing inside the window has keyboard focus yet.
            handled = true;
            Dispatcher.BeginInvoke(HideWindow);
            return IntPtr.Zero;
        }
        if (msg != UiNativeMethods.WM_WINDOWPOSCHANGING || _placement is null || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var pos = Marshal.PtrToStructure<UiNativeMethods.WINDOWPOS>(lParam);
        var source = ChoosePositionSource(_dragging, _userMoved);
        if ((pos.flags & UiNativeMethods.SWP_NOSIZE) != 0 || pos.cx <= 0 || pos.cy <= 0 || source == PositionSource.Keep)
        {
            return IntPtr.Zero;
        }
        var size = new PixelSize(pos.cx, pos.cy);
        PixelPoint target;
        if (source == PositionSource.ClampUserPosition)
        {
            var current = WindowPlacement.GetWindowRect(hwnd);
            var left = (pos.flags & UiNativeMethods.SWP_NOMOVE) != 0 ? current.Left : pos.x;
            var top = (pos.flags & UiNativeMethods.SWP_NOMOVE) != 0 ? current.Top : pos.y;
            var proposed = PixelRect.FromLtwh(left, top, size.Width, size.Height);
            target = ClampInto(proposed, ScreenInfo.GetWorkArea(proposed));
        }
        else
        {
            target = _placement(size);
        }
        pos.x = target.X;
        pos.y = target.Y;
        pos.flags &= ~UiNativeMethods.SWP_NOMOVE;
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_keepsVisibleBehindOtherWindows)
        {
            HideWindow();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            HideWindow();
            e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_allowClose)
        {
            // Alt+F4 hides; these windows live for the whole session.
            e.Cancel = true;
            HideWindow();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }
        var before = WindowPlacement.GetWindowRect(Handle);
        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            _dragging = false;
        }
        if (WindowPlacement.GetWindowRect(Handle) != before)
        {
            _userMoved = true;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsVisible)
        {
            PlaceNow();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(PlaceNow);
        }
    }

    /// <summary>
    /// Dragging onto a monitor with another scale resizes the window mid-drag (WM_DPICHANGED); re-anchoring
    /// then would snap it back to the tray or the selection, so nothing moves it while the drag lasts.
    /// </summary>
    internal static PositionSource ChoosePositionSource(bool dragging, bool userMoved) =>
        dragging ? PositionSource.Keep : userMoved ? PositionSource.ClampUserPosition : PositionSource.Anchor;

    private void PlaceNow()
    {
        var source = ChoosePositionSource(_dragging, _userMoved);
        if (_placement is null || source == PositionSource.Keep)
        {
            return;
        }
        var hwnd = Handle;
        var rect = WindowPlacement.GetWindowRect(hwnd);
        var target = source == PositionSource.ClampUserPosition
            ? ClampInto(rect, ScreenInfo.GetWorkArea(rect))
            : _placement(new PixelSize(rect.Width, rect.Height));
        if (target.X != rect.Left || target.Y != rect.Top)
        {
            WindowPlacement.Move(hwnd, target);
        }
    }

    /// <summary>After a manual drag, keep the dragged position but never let growth push the window off the work area.</summary>
    internal static PixelPoint ClampInto(PixelRect window, PixelRect workArea)
    {
        static int Clamp(int value, int min, int max) => max < min ? min : Math.Max(min, Math.Min(value, max));
        return new PixelPoint(
            Clamp(window.Left, workArea.Left, workArea.Right - window.Width),
            Clamp(window.Top, workArea.Top, workArea.Bottom - window.Height));
    }

    private void ApplyEffects()
    {
        var backdrop = WindowEffects.Apply(this, BackdropKind.Acrylic, ThemeManager.Current.IsDark());
        SetResourceReference(BackgroundProperty, backdrop ? "BackgroundAcrylicBrush" : "BackgroundOpaqueBrush");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
        {
            ApplyEffects();
        }
    }
}
