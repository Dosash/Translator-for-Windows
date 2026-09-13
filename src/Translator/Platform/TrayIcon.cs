using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Translator.Core;

namespace Translator.Platform;

public sealed class TrayIconClickEventArgs(PixelPoint position) : EventArgs
{
    public PixelPoint Position { get; } = position;
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

    private readonly uint _taskbarCreatedMessage;
    private HwndSource? _hwndSource;
    private IntPtr _hIcon;
    private string _tooltip = string.Empty;
    private bool _added;
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
        RefreshIcon();
        var data = BuildData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data))
        {
            DebugLog.Write("TrayIcon.Show: Shell_NotifyIcon NIM_ADD failed");
            return;
        }
        _added = true;
        var version = BuildData(0);
        version.uVersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref version);
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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = unchecked((int)(lParam.ToInt64() & 0xFFFF));
            var x = unchecked((short)(wParam.ToInt64() & 0xFFFF));
            var y = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            var position = new PixelPoint(x, y);
            if (mouseMsg == NativeMethods.WM_LBUTTONUP)
            {
                LeftClick?.Invoke(this, new TrayIconClickEventArgs(position));
                handled = true;
            }
            else if (mouseMsg is NativeMethods.WM_RBUTTONUP or NativeMethods.WM_CONTEXTMENU)
            {
                RightClick?.Invoke(this, new TrayIconClickEventArgs(position));
                handled = true;
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
            // Explorer restarted — the shell forgot about us, add the icon again.
            _added = false;
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
        if (_added && _hwndSource is not null)
        {
            var data = BuildData(0);
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
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
