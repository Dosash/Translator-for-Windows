// Icon generator for Translator (Windows port of tools/../make_icon.swift from the macOS project).
//
// Draws the same "chat bubble" concept — a big light bubble with "A" and a small pink reply
// bubble with "a" over a sage-green gradient rounded square — but tuned for Windows:
//   - a smaller outer margin (icons bleed closer to the frame than on macOS),
//   - a simplified single-bubble variant for the tiny 16/20/24/32 px sizes where two bubbles
//     and two letters are not legible.
//
// Output:
//   src/Translator/Assets/AppIcon.ico   (16, 20, 24, 32, 40, 48, 64, 256 px; PNG-compressed frames)
//   docs/images/icon-256.png            (256 px preview used by README.md)

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGen;

internal static class Program
{
    // Palette ported from make_icon.swift (Mindora).
    private static readonly Color SageLight = Color.FromRgb(0x74, 0xA0, 0x92);
    private static readonly Color SageDeep = Color.FromRgb(0x46, 0x70, 0x5F);
    private static readonly Color Mist = Color.FromRgb(0xF1, 0xF5, 0xF4);
    private static readonly Color Rose = Color.FromRgb(0xE8, 0xAE, 0xB7);
    private static readonly Color Ink = Color.FromRgb(0x2F, 0x3E, 0x46);

    private static void Main()
    {
        var repoRoot = FindRepoRoot();
        var assetsDir = Path.Combine(repoRoot, "src", "Translator", "Assets");
        var docsImagesDir = Path.Combine(repoRoot, "docs", "images");
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(docsImagesDir);

        // Sizes embedded in the .ico. 256 is stored as a PNG frame (standard since Vista).
        int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 256];
        // Below this size the small "a" reply bubble stops being legible; use the simplified mark.
        const int simplifiedThreshold = 64;

        var frames = new Dictionary<int, byte[]>();
        foreach (var size in icoSizes)
        {
            var png = size < simplifiedThreshold
                ? RenderPng(size, DrawSimplified)
                : RenderPng(size, DrawFull);
            frames[size] = png;

            var previewPath = Path.Combine(Path.GetTempPath(), $"icongen-preview-{size}.png");
            File.WriteAllBytes(previewPath, png);
            Console.WriteLine($"rendered {size}x{size} -> {previewPath}");
        }

        var icoPath = Path.Combine(assetsDir, "AppIcon.ico");
        WriteIco(icoPath, icoSizes.Select(s => frames[s]).ToArray(), icoSizes);
        Console.WriteLine($"wrote {icoPath}");

        // docs/images/icon-256.png: a dedicated, slightly larger render (512) downsized isn't
        // needed — 256 direct render already matches the .ico frame.
        var docPng = RenderPng(256, DrawFull);
        var docPngPath = Path.Combine(docsImagesDir, "icon-256.png");
        File.WriteAllBytes(docPngPath, docPng);
        Console.WriteLine($"wrote {docPngPath}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Translator.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (Translator.slnx not found above " + AppContext.BaseDirectory + ").");
    }

    // ---- Drawing -----------------------------------------------------------------------------

    /// <summary>Full design: gradient square, big "A" bubble, small "a" reply bubble.</summary>
    private static void DrawFull(DrawingContext dc, double canvas)
    {
        double s = canvas / 1024.0;

        DrawBackground(dc, canvas, s);

        // Big light bubble with tail, "A".
        var bigBubble = RoundedRect(225 * s, 460 * s, 400 * s, 270 * s, 90 * s);
        dc.DrawGeometry(new SolidColorBrush(Mist), null, bigBubble);
        dc.DrawGeometry(new SolidColorBrush(Mist), null, Tail(
            (305 * s, 470 * s), (258 * s, 352 * s), (405 * s, 462 * s)));
        DrawGlyph(dc, "A", 425 * s, 600 * s, 190 * s, SageDeep, FontWeights.Bold);

        // Small pink reply bubble with tail, "a".
        var smallBubble = RoundedRect(565 * s, 295 * s, 250 * s, 190 * s, 64 * s);
        dc.DrawGeometry(new SolidColorBrush(Rose), null, smallBubble);
        dc.DrawGeometry(new SolidColorBrush(Rose), null, Tail(
            (700 * s, 476 * s), (742 * s, 540 * s), (628 * s, 480 * s)));
        DrawGlyph(dc, "a", 690 * s, 385 * s, 145 * s, Ink, FontWeights.Bold);
    }

    /// <summary>
    /// Simplified mark for 16/20/24/32 px: a single bold bubble with "A" — no tail, no second
    /// bubble, thicker strokes and a bigger glyph so it reads at tray-icon size.
    /// </summary>
    private static void DrawSimplified(DrawingContext dc, double canvas)
    {
        double s = canvas / 1024.0;

        DrawBackground(dc, canvas, s);

        var bubble = RoundedRect(178 * s, 178 * s, 668 * s, 668 * s, 180 * s);
        dc.DrawGeometry(new SolidColorBrush(Mist), null, bubble);
        DrawGlyph(dc, "A", 512 * s, 528 * s, 470 * s, SageDeep, FontWeights.ExtraBold);
    }

    private static void DrawBackground(DrawingContext dc, double canvas, double s)
    {
        // Smaller margin than macOS (100/1024 there) — Windows icons bleed closer to the frame.
        double margin = 48 * s;
        var bgRect = new Rect(margin, margin, canvas - 2 * margin, canvas - 2 * margin);
        var bg = new StreamGeometry();
        using (var ctx = bg.Open())
        {
            AddRoundedRect(ctx, bgRect, 168 * s);
        }
        bg.Freeze();

        var gradient = new LinearGradientBrush(SageLight, SageDeep, new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawGeometry(gradient, null, bg);
    }

    private static void DrawGlyph(DrawingContext dc, string text, double centerX, double centerY, double emSize, Color color, FontWeight weight)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            emSize,
            new SolidColorBrush(color),
            1.0);
        var origin = new Point(centerX - formatted.Width / 2, centerY - formatted.Height / 2);
        dc.DrawText(formatted, origin);
    }

    private static Geometry RoundedRect(double x, double y, double w, double h, double r)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            AddRoundedRect(ctx, new Rect(x, y, w, h), r);
        }
        geo.Freeze();
        return geo;
    }

    private static void AddRoundedRect(StreamGeometryContext ctx, Rect rect, double r)
    {
        r = Math.Min(r, Math.Min(rect.Width, rect.Height) / 2);
        ctx.BeginFigure(new Point(rect.Left + r, rect.Top), true, true);
        ctx.LineTo(new Point(rect.Right - r, rect.Top), true, false);
        ctx.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        ctx.LineTo(new Point(rect.Right, rect.Bottom - r), true, false);
        ctx.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        ctx.LineTo(new Point(rect.Left + r, rect.Bottom), true, false);
        ctx.ArcTo(new Point(rect.Left, rect.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        ctx.LineTo(new Point(rect.Left, rect.Top + r), true, false);
        ctx.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
    }

    private static Geometry Tail((double x, double y) a, (double x, double y) b, (double x, double y) c)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(a.x, a.y), true, true);
            ctx.LineTo(new Point(b.x, b.y), true, false);
            ctx.LineTo(new Point(c.x, c.y), true, false);
        }
        geo.Freeze();
        return geo;
    }

    private static byte[] RenderPng(int pixels, Action<DrawingContext, double> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc, pixels);
        }

        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // ---- ICO packaging -------------------------------------------------------------------------

    private static void WriteIco(string path, byte[][] pngFrames, int[] sizes)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR
        bw.Write((short)0);            // reserved
        bw.Write((short)1);            // type: icon
        bw.Write((short)pngFrames.Length);

        int offset = 6 + 16 * pngFrames.Length; // header + directory entries
        for (int i = 0; i < pngFrames.Length; i++)
        {
            var size = sizes[i];
            var data = pngFrames[i];
            byte dim = size >= 256 ? (byte)0 : (byte)size; // 0 means 256 in ICO format
            bw.Write(dim);            // width
            bw.Write(dim);            // height
            bw.Write((byte)0);        // color palette
            bw.Write((byte)0);        // reserved
            bw.Write((short)1);       // color planes
            bw.Write((short)32);      // bits per pixel
            bw.Write(data.Length);    // size of image data
            bw.Write(offset);         // offset of image data
            offset += data.Length;
        }

        foreach (var data in pngFrames)
        {
            bw.Write(data);
        }
    }
}
