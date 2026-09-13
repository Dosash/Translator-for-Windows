using System.Runtime.InteropServices;
using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// Clipboard access that tolerates the clipboard being briefly locked by other apps.
/// Usable from any thread (no window handle is required to open the clipboard).
/// </summary>
public static class ClipboardService
{
    private const int MaxRetries = 10;
    private const int RetryDelayMs = 30;
    private const long MaxSnapshotBytes = 64 * 1024 * 1024;

    /// <summary>GDI-handle formats can't be copied as plain bytes; skip them in snapshots.</summary>
    private static readonly uint[] SkippedGdiFormats =
    [
        NativeMethods.CF_BITMAP, NativeMethods.CF_METAFILEPICT, NativeMethods.CF_PALETTE,
        NativeMethods.CF_ENHMETAFILE, NativeMethods.CF_OWNERDISPLAY, NativeMethods.CF_DSPTEXT,
        NativeMethods.CF_DSPBITMAP, NativeMethods.CF_DSPMETAFILEPICT, NativeMethods.CF_DSPENHMETAFILE,
    ];

    /// <summary>Unicode text on the clipboard, or null.</summary>
    public static string? GetText()
    {
        if (!TryOpenClipboard())
        {
            DebugLog.Write("ClipboardService.GetText: clipboard busy");
            return null;
        }
        try
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            var ptr = NativeMethods.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>Puts text on the clipboard. Returns false if the clipboard stayed locked.</summary>
    public static bool SetText(string text) => SetText(text, excludeFromHistory: false);

    /// <summary>
    /// Puts text on the clipboard. When <paramref name="excludeFromHistory"/> is set, also marks the
    /// data so Win+V clipboard history and cloud sync skip it — for temporary writes (paste, restore).
    /// </summary>
    public static bool SetText(string text, bool excludeFromHistory)
    {
        if (!TryOpenClipboard())
        {
            DebugLog.Write("ClipboardService.SetText: clipboard busy");
            return false;
        }
        try
        {
            NativeMethods.EmptyClipboard();
            if (!TrySetGlobalText(NativeMethods.CF_UNICODETEXT, text))
            {
                return false;
            }
            if (excludeFromHistory)
            {
                var excludeFormat = NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
                var historyFormat = NativeMethods.RegisterClipboardFormatW("CanIncludeInClipboardHistory");
                TrySetGlobalDword(excludeFormat, 0);
                TrySetGlobalDword(historyFormat, 0);
            }
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>GetClipboardSequenceNumber — changes on every clipboard write.</summary>
    public static uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>Copies every HGLOBAL-backed format currently on the clipboard.</summary>
    public static ClipboardSnapshot Capture()
    {
        var entries = new List<ClipboardSnapshotEntry>();
        if (!TryOpenClipboard())
        {
            DebugLog.Write("ClipboardService.Capture: clipboard busy");
            return new ClipboardSnapshot(entries);
        }
        try
        {
            uint format = 0;
            while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
            {
                if (Array.IndexOf(SkippedGdiFormats, format) >= 0)
                {
                    continue;
                }
                var handle = NativeMethods.GetClipboardData(format);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }
                var size = NativeMethods.GlobalSize(handle);
                if (size == 0 || size > MaxSnapshotBytes)
                {
                    continue;
                }
                var ptr = NativeMethods.GlobalLock(handle);
                if (ptr == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    var data = new byte[size];
                    Marshal.Copy(ptr, data, 0, (int)size);
                    entries.Add(new ClipboardSnapshotEntry(format, data));
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
        DebugLog.Write($"ClipboardService.Capture: {entries.Count} format(s)");
        return new ClipboardSnapshot(entries);
    }

    /// <summary>Restores a previously captured snapshot. An empty snapshot restores as an empty clipboard.</summary>
    public static void Restore(ClipboardSnapshot snapshot)
    {
        if (!TryOpenClipboard())
        {
            DebugLog.Write("ClipboardService.Restore: clipboard busy");
            return;
        }
        try
        {
            NativeMethods.EmptyClipboard();
            foreach (var entry in snapshot.Entries)
            {
                var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)entry.Data.Length);
                if (hGlobal == IntPtr.Zero)
                {
                    continue;
                }
                var ptr = NativeMethods.GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hGlobal);
                    continue;
                }
                Marshal.Copy(entry.Data, 0, ptr, entry.Data.Length);
                NativeMethods.GlobalUnlock(hGlobal);
                if (NativeMethods.SetClipboardData(entry.Format, hGlobal) == IntPtr.Zero)
                {
                    // Ownership transfers to the system only on success; free it ourselves otherwise.
                    NativeMethods.GlobalFree(hGlobal);
                }
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (var i = 0; i < MaxRetries; i++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }
            Thread.Sleep(RetryDelayMs);
        }
        return false;
    }

    private static bool TrySetGlobalText(uint format, string text)
    {
        var byteCount = (text.Length + 1) * sizeof(char);
        var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)byteCount);
        if (hGlobal == IntPtr.Zero)
        {
            return false;
        }
        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }
        if (text.Length > 0)
        {
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
        }
        Marshal.WriteInt16(ptr, text.Length * sizeof(char), 0); // null terminator
        NativeMethods.GlobalUnlock(hGlobal);

        if (NativeMethods.SetClipboardData(format, hGlobal) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }
        return true;
    }

    private static bool TrySetGlobalDword(uint format, int value)
    {
        var hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, sizeof(int));
        if (hGlobal == IntPtr.Zero)
        {
            return false;
        }
        var ptr = NativeMethods.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }
        Marshal.WriteInt32(ptr, value);
        NativeMethods.GlobalUnlock(hGlobal);

        if (NativeMethods.SetClipboardData(format, hGlobal) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }
        return true;
    }
}

/// <summary>One raw clipboard format captured by <see cref="ClipboardService.Capture"/>.</summary>
public readonly record struct ClipboardSnapshotEntry(uint Format, byte[] Data);

/// <summary>A copy of every HGLOBAL-backed clipboard format, restorable with <see cref="ClipboardService.Restore"/>.</summary>
public sealed record ClipboardSnapshot(IReadOnlyList<ClipboardSnapshotEntry> Entries);
