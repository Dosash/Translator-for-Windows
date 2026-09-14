using System.Windows.Input;
using System.Windows.Interop;
using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// Global shortcuts via <c>RegisterHotKey</c> on a hidden window created on the UI thread.
/// Callbacks run on that thread (WM_HOTKEY arrives through the WPF dispatcher's message pump).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const int SelectionId = 1;
    public const int ClipboardId = 2;
    public const int PanelId = 3;
    public const int ScreenId = 4;

    private readonly Dictionary<int, Action> _callbacks = new();
    private HwndSource? _hwndSource;
    private bool _disposed;

    public HotkeyManager()
    {
        // A hidden top-level window: no WS_VISIBLE, no parent, just a WndProc for WM_HOTKEY.
        var parameters = new HwndSourceParameters("TranslatorHotkeys")
        {
            WindowStyle = 0,
            ParentWindow = IntPtr.Zero,
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Registers (or replaces) the shortcut for <paramref name="id"/>.
    /// <see cref="Hotkey.None"/> just unregisters and returns true. Returns false when the
    /// shortcut is invalid or already taken by another app.
    /// </summary>
    public bool Register(int id, Hotkey hotkey, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unregister(id);

        if (hotkey.IsNone)
        {
            return true;
        }
        if (!TryConvertToWin32(hotkey, out var modifiers, out var vk))
        {
            DebugLog.Write($"HotkeyManager.Register: invalid hotkey for id={id}");
            return false;
        }
        if (!NativeMethods.RegisterHotKey(_hwndSource!.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
        {
            DebugLog.Write($"HotkeyManager.Register: RegisterHotKey failed for id={id} (likely taken)");
            return false;
        }
        _callbacks[id] = callback;
        return true;
    }

    public void Unregister(int id)
    {
        if (_callbacks.Remove(id) && _hwndSource is not null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in new List<int>(_callbacks.Keys))
        {
            Unregister(id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;
            callback();
        }
        return IntPtr.Zero;
    }

    /// <summary>Converts a <see cref="Hotkey"/> to RegisterHotKey's (MOD flags, virtual-key) form.</summary>
    internal static bool TryConvertToWin32(Hotkey hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (!hotkey.IsValid)
        {
            return false;
        }
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= NativeMethods.MOD_WIN;

        try
        {
            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return virtualKey != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }
}
