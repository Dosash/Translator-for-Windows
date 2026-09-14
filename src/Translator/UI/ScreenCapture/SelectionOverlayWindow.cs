using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Translator.Core;
using Translator.Platform;
using Translator.Platform.Capture;
using Translator.UI.Interop;

namespace Translator.UI.ScreenCapture;

/// <summary>
/// One monitor's frozen screenshot, dimmed, covering that monitor pixel for pixel. Dragging draws the selection
/// (undimmed, accent border, size label); Esc, a right click, Alt+F4 or a click without a drag asks to cancel.
/// </summary>
internal sealed class SelectionOverlayWindow : Window
{
    private const double BorderDips = 2;

    private static readonly Brush DimBrush = CreateDimBrush();

    private readonly WriteableBitmap _bitmap;
    private readonly RectangleGeometry _fullArea = new(new Rect(0, 0, 0, 0));
    private readonly RectangleGeometry _selectionArea = new(new Rect(0, 0, 0, 0));
    private readonly Rectangle _border;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeText;
    private readonly Border _hint;
    private readonly Grid _root;
    private PixelPoint? _dragStart;
    private bool _closingByOwner;

    public SelectionOverlayWindow(MonitorSnapshot snapshot)
    {
        Snapshot = snapshot;
        var monitor = snapshot.Monitor;
        Title = L10n.T("app.title");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = true;
        UseLayoutRounding = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        // A first guess in DIPs; the exact pixel bounds are enforced in WM_WINDOWPOSCHANGING.
        Left = monitor.Bounds.Left / monitor.Scale;
        Top = monitor.Bounds.Top / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(FontFamilyProperty, "UiFontFamily");

        var image = snapshot.Image;
        _bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null);
        _bitmap.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);
        var picture = new Image { Source = _bitmap, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);

        var dim = new System.Windows.Shapes.Path
        {
            Fill = DimBrush,
            IsHitTestVisible = false,
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, _fullArea, _selectionArea),
        };

        _border = new Rectangle { StrokeThickness = BorderDips, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _border.SetResourceReference(Shape.StrokeProperty, "SageBrush");

        _sizeText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        _sizeText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _sizeLabel = CreateCapsule(_sizeText, 6, new Thickness(7, 3, 7, 3));
        _sizeLabel.Visibility = Visibility.Collapsed;

        var hintText = new TextBlock
        {
            Text = L10n.T("ocr.overlay.hint"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _hint = CreateCapsule(hintText, 16, new Thickness(16, 8, 16, 8));
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.VerticalAlignment = VerticalAlignment.Top;
        _hint.Margin = new Thickness(16, 32, 16, 0);

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(_border);
        canvas.Children.Add(_sizeLabel);

        _root = new Grid();
        _root.Children.Add(picture);
        _root.Children.Add(dim);
        _root.Children.Add(canvas);
        _root.Children.Add(_hint);
        _root.SizeChanged += (_, e) => _fullArea.Rect = new Rect(e.NewSize);
        Content = _root;
    }

    public MonitorSnapshot Snapshot { get; }

    public bool IsDragging => _dragStart is not null;

    /// <summary>False once a drag started on another monitor: a selection stays on one monitor.</summary>
    public bool CanStartDrag { get; set; } = true;

    public event EventHandler? DragStarted;

    /// <summary>The finished selection in screen pixels.</summary>
    public event EventHandler<PixelRect>? SelectionCompleted;

    public event EventHandler? CancelRequested;

    private PixelRect Bounds => Snapshot.Monitor.Bounds;

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    public void ShowOverlay()
    {
        WindowPlacement.SetBounds(Handle, Bounds);
        Show();
        WindowPlacement.SetBounds(Handle, Bounds);
    }

    public void HideHint() => _hint.Visibility = Visibility.Collapsed;

    public void CloseOverlay()
    {
        _closingByOwner = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.SetToolWindow(Handle);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        if (!CanStartDrag || _dragStart is not null)
        {
            return;
        }
        if (!IsActive)
        {
            Activate();
        }
        var start = ToScreen(e.GetPosition(this));
        if (!CaptureMouse())
        {
            return;
        }
        _dragStart = start;
        DragStarted?.Invoke(this, EventArgs.Empty);
        UpdateSelection(start);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not null)
        {
            UpdateSelection(ToScreen(e.GetPosition(this)));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is not { } start)
        {
            return;
        }
        e.Handled = true;
        var selection = SelectionGeometry.Normalize(start, ToScreen(e.GetPosition(this)), Bounds);
        _dragStart = null;
        ReleaseMouseCapture();
        if (SelectionGeometry.IsTooSmall(selection, Scale))
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            SelectionCompleted?.Invoke(this, selection);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        e.Handled = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragStart is not null)
        {
            // Capture taken away mid-drag (Alt+Tab, a system dialog): the selection can't be finished.
            _dragStart = null;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_closingByOwner)
        {
            e.Cancel = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ClearBitmap();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == UiNativeMethods.WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
        {
            // WPF sizes windows from DIPs (rounding gaps) and rescales them on WM_DPICHANGED; this one must stay
            // exactly on its monitor's pixels.
            var pos = Marshal.PtrToStructure<UiNativeMethods.WINDOWPOS>(lParam);
            var bounds = Bounds;
            pos.x = bounds.Left;
            pos.y = bounds.Top;
            pos.cx = bounds.Width;
            pos.cy = bounds.Height;
            pos.flags &= ~(UiNativeMethods.SWP_NOMOVE | UiNativeMethods.SWP_NOSIZE);
            Marshal.StructureToPtr(pos, lParam, false);
        }
        else if (msg == UiNativeMethods.WM_KEYDOWN && wParam.ToInt64() == UiNativeMethods.VK_ESCAPE)
        {
            // Also works before anything in the window has keyboard focus.
            handled = true;
            Dispatcher.BeginInvoke(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        }
        return IntPtr.Zero;
    }

    private PixelPoint ToScreen(Point overlayDips) => SelectionGeometry.ToScreenPixels(overlayDips, Bounds, Scale);

    private void UpdateSelection(PixelPoint current)
    {
        if (_dragStart is not { } start)
        {
            return;
        }
        var selection = SelectionGeometry.Normalize(start, current, Bounds);
        var dips = SelectionGeometry.ToOverlayDips(selection, Bounds, Scale);
        _selectionArea.Rect = dips;

        // The stroke sits outside the selection so it never covers the pixels being selected.
        Canvas.SetLeft(_border, dips.X - BorderDips);
        Canvas.SetTop(_border, dips.Y - BorderDips);
        _border.Width = dips.Width + BorderDips * 2;
        _border.Height = dips.Height + BorderDips * 2;
        _border.Visibility = Visibility.Visible;

        _sizeText.Text = $"{selection.Width} × {selection.Height}";
        _sizeLabel.Visibility = Visibility.Visible;
        _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var position = SelectionGeometry.PlaceSizeLabel(dips, _sizeLabel.DesiredSize, _root.RenderSize);
        Canvas.SetLeft(_sizeLabel, position.X);
        Canvas.SetTop(_sizeLabel, position.Y);
    }

    /// <summary>The frozen screen lives no longer than the overlay.</summary>
    private unsafe void ClearBitmap()
    {
        _bitmap.Lock();
        try
        {
            NativeMemory.Clear((void*)_bitmap.BackBuffer, (nuint)((long)_bitmap.BackBufferStride * _bitmap.PixelHeight));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }

    private static Border CreateCapsule(UIElement child, double cornerRadius, Thickness padding)
    {
        var border = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(cornerRadius),
            Padding = padding,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        border.SetResourceReference(Border.BackgroundProperty, "MenuBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "MenuStrokeBrush");
        return border;
    }

    private static Brush CreateDimBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0));
        brush.Freeze();
        return brush;
    }
}
