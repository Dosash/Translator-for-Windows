using System.Runtime.InteropServices;
using Translator.Core;

namespace Translator.Platform.Capture;

/// <summary>One monitor's frozen pixels (physical, snapshot origin = the monitor's top-left).</summary>
public sealed class MonitorSnapshot(MonitorMetrics monitor, BgraImage image)
{
    public MonitorMetrics Monitor { get; } = monitor;

    public BgraImage Image { get; } = image;
}

/// <summary>
/// Freeze-frame of every monitor, taken with GDI <c>BitBlt</c> from the screen DC before anything is shown.
/// Memory only; <see cref="Dispose"/> zeroes the pixels.
/// </summary>
public sealed class ScreenSnapshot : IDisposable
{
    private ScreenSnapshot(IReadOnlyList<MonitorSnapshot> monitors) => Monitors = monitors;

    public IReadOnlyList<MonitorSnapshot> Monitors { get; }

    public static ScreenSnapshot CaptureAllMonitors()
    {
        var snapshots = new List<MonitorSnapshot>();
        foreach (var monitor in ScreenInfo.GetAllMonitors())
        {
            if (CaptureRect(monitor.Bounds) is { } image)
            {
                snapshots.Add(new MonitorSnapshot(monitor, image));
            }
        }
        return new ScreenSnapshot(snapshots);
    }

    public void Dispose()
    {
        foreach (var snapshot in Monitors)
        {
            snapshot.Image.Clear();
        }
    }

    private static unsafe BgraImage? CaptureRect(PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            DebugLog.Write("ScreenSnapshot: GetDC failed");
            return null;
        }
        IntPtr memoryDc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero, bits = IntPtr.Zero;
        var byteCount = (long)rect.Width * rect.Height * 4;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = rect.Width,
                biHeight = -rect.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = NativeMethods.CreateDIBSection(screenDc, in header, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                DebugLog.Write($"ScreenSnapshot: couldn't allocate a {rect.Width}x{rect.Height} bitmap");
                return null;
            }
            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            // CAPTUREBLT includes layered (semi-transparent) windows.
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, screenDc, rect.Left, rect.Top,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                DebugLog.Write($"ScreenSnapshot: BitBlt failed ({Marshal.GetLastPInvokeError()})");
                return null;
            }
            var pixels = new byte[byteCount];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves alpha undefined (usually 0); OCR reads the buffer as premultiplied BGRA.
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
            return new BgraImage(rect.Width, rect.Height, pixels);
        }
        finally
        {
            if (bits != IntPtr.Zero)
            {
                NativeMemory.Clear((void*)bits, (nuint)byteCount);
            }
            if (previous != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }
            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
