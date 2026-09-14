using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Translator.Core;

namespace Translator.Platform;

public sealed class TrayIconClickEventArgs(PixelPoint position) : EventArgs
{
    public PixelPoint Position { get; } = position;
}

internal enum TrayMouseAction
{
    None,
    Primary,
    Context,
}

/// <summary>
/// The notification-area icon: <c>Shell_NotifyIconW</c> on a hidden top-level window (not
/// message-only, so it receives the broadcast "TaskbarCreated" message and re-adds the icon after
/// Explorer restarts). The icon bitmap is rendered at runtime from <see cref="TrayGlyph"/>.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int WM_TRAYICON = NativeMethods.WM_APP + 1;
    private const int MaxAddAttempts = 30;
    private static readonly TimeSpan AddRetryInterval = TimeSpan.FromSeconds(2);

    private readonly uint _taskbarCreatedMessage;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _addRetryTimer;
    private IntPtr _hIcon;
    private string _tooltip = string.Empty;
    private int _addAttempts;
    private bool _added;
    private bool _version4;
    private bool _disposed;

    public event EventHandler<TrayIconClickEventArgs>? LeftClick;
    public event EventHandler<TrayIconClickEventArgs>? RightClick;

    public TrayIcon()
    {
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        var parameters = new HwndSourceParameters("TranslatorTrayIcon")
        {
            WindowStyle = 0,
            ParentWindow = IntPtr.Zero,
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    public void Show()
    {
        if (_disposed)
        {
            return;
        }
        RefreshIcon();
        var data = BuildData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        // NIM_ADD can report failure after a timeout even though the icon was added; then NIM_MODIFY succeeds.
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data)
            && !NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data))
        {
            ScheduleAddRetry();
            return;
        }
        _addRetryTimer?.Stop();
        _added = true;
        DebugLog.Write(_addAttempts == 0 ? "TrayIcon: added" : $"TrayIcon: added after {_addAttempts + 1} attempts");
        _addAttempts = 0;
        var version = BuildData(0);
        version.uVersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
        _version4 = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref version);
    }

    /// <summary>
    /// At sign-in (--autostart) the notification area may not be ready yet, and some sessions have no shell
    /// at all; retry for a while, after that only Explorer's "TaskbarCreated" broadcast adds the icon.
    /// </summary>
    private void ScheduleAddRetry()
    {
        _addAttempts++;
        if (_addAttempts == 1)
        {
            DebugLog.Write("TrayIcon.Show: Shell_NotifyIcon NIM_ADD failed, retrying");
        }
        if (_addAttempts >= MaxAddAttempts)
        {
            _addRetryTimer?.Stop();
            DebugLog.Write("TrayIcon: no notification area, waiting for TaskbarCreated");
            return;
        }
        if (_addRetryTimer is null)
        {
            _addRetryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AddRetryInterval };
            _addRetryTimer.Tick += (_, _) =>
            {
                _addRetryTimer.Stop();
                Show();
            };
        }
        _addRetryTimer.Start();
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (!_added)
        {
            return;
        }
        var data = BuildData(NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
    }

    public PixelRect? GetIconRect()
    {
        if (_hwndSource is null)
        {
            return null;
        }
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwndSource.Handle,
            uID = IconId,
        };
        if (NativeMethods.Shell_NotifyIconGetRect(in identifier, out var rect) != 0)
        {
            return null;
        }
        return rect.ToPixelRect();
    }

    /// <summary>Re-renders the glyph for the current theme/DPI and pushes it to the shell.</summary>
    public void RefreshIcon()
    {
        if (_hwndSource is null)
        {
            return;
        }
        var newIcon = RenderIcon(_hwndSource.Handle);
        var previous = _hIcon;
        _hIcon = newIcon;
        if (_added)
        {
            var data = BuildData(NativeMethods.NIF_ICON);
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
        }
        if (previous != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(previous);
        }
    }

    private NativeMethods.NOTIFYICONDATAW BuildData(uint flags)
    {
        var data = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
            hWnd = _hwndSource!.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
        };
        unsafe
        {
            var tip = _tooltip.AsSpan();
            var max = Math.Min(tip.Length, 127);
            for (var i = 0; i < max; i++)
            {
                data.szTip[i] = tip[i];
            }
            data.szTip[max] = '\0';
        }
        return data;
    }

    /// <summary>
    /// With NOTIFYICON_VERSION_4 a right click arrives as WM_RBUTTONUP and then WM_CONTEXTMENU (the menu key and
    /// Shift+F10 send WM_CONTEXTMENU too). Reacting to both opened the menu twice, and the second open dismissed the first.
    /// </summary>
    internal static TrayMouseAction ClassifyMouseMessage(int mouseMsg, bool version4) => mouseMsg switch
    {
        NativeMethods.WM_LBUTTONUP => TrayMouseAction.Primary,
        NativeMethods.WM_CONTEXTMENU => TrayMouseAction.Context,
        NativeMethods.WM_RBUTTONUP when !version4 => TrayMouseAction.Context,
        _ => TrayMouseAction.None,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = unchecked((int)(lParam.ToInt64() & 0xFFFF));
            var x = unchecked((short)(wParam.ToInt64() & 0xFFFF));
            var y = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            var position = new PixelPoint(x, y);
            switch (ClassifyMouseMessage(mouseMsg, _version4))
            {
                case TrayMouseAction.Primary:
                    LeftClick?.Invoke(this, new TrayIconClickEventArgs(position));
                    handled = true;
                    break;
                case TrayMouseAction.Context:
                    RightClick?.Invoke(this, new TrayIconClickEventArgs(position));
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == NativeMethods.WM_SETTINGCHANGE)
        {
            var name = lParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(lParam);
            if (name == "ImmersiveColorSet")
            {
                RefreshIcon();
            }
        }
        else if (msg == NativeMethods.WM_DPICHANGED)
        {
            RefreshIcon();
        }
        else if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            // Explorer (re)started — the shell forgot about us, add the icon again.
            _added = false;
            _addAttempts = 0;
            _addRetryTimer?.Stop();
            Show();
        }
        return IntPtr.Zero;
    }

    private static IntPtr RenderIcon(IntPtr hwndForDpi)
    {
        var dpi = NativeMethods.GetDpiForWindow(hwndForDpi);
        if (dpi == 0)
        {
            dpi = 96;
        }
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
        if (size <= 0)
        {
            size = 16;
        }

        var geometry = TrayGlyph.CreateGeometry();
        var color = IsLightTaskbar() ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Colors.White;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 18.0, size / 18.0));
            dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return CreateHIcon(bitmap, size);
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"TrayIcon.IsLightTaskbar failed: {ex.GetType().Name}");
            return false;
        }
    }

    private static IntPtr CreateHIcon(RenderTargetBitmap bitmap, int size)
    {
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down, matches our pixel buffer order
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var hbmColor = NativeMethods.CreateDIBSection(screenDc, in header, 0, out var bits, IntPtr.Zero, 0);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        if (hbmColor == IntPtr.Zero)
        {
            DebugLog.Write("TrayIcon.CreateHIcon: CreateDIBSection failed");
            return IntPtr.Zero;
        }
        Marshal.Copy(pixels, 0, bits, pixels.Length);

        // AND mask: all zero bits — with a 32-bpp alpha color bitmap, Windows uses the alpha
        // channel and this mask just needs to say "opaque everywhere".
        var maskStride = ((size + 15) / 16) * 2;
        var maskBits = new byte[maskStride * size];
        var hbmMask = NativeMethods.CreateBitmap(size, size, 1, 1, maskBits);

        var iconInfo = new NativeMethods.ICONINFO
        {
            fIcon = 1,
            hbmColor = hbmColor,
            hbmMask = hbmMask,
        };
        var hIcon = NativeMethods.CreateIconIndirect(ref iconInfo);
        NativeMethods.DeleteObject(hbmColor);
        NativeMethods.DeleteObject(hbmMask);
        return hIcon;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _addRetryTimer?.Stop();
        if (_added && _hwndSource is not null)
        {
            var data = BuildData(0);
            // Smoke tests look for these lines to tell a clean exit from a killed process.
            DebugLog.Write(NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data)
                ? "TrayIcon: removed"
                : "TrayIcon: NIM_DELETE failed");
        }
        if (_hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
