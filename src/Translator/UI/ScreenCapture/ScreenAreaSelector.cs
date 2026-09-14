using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Translator.Platform;
using Translator.Platform.Capture;

namespace Translator.UI.ScreenCapture;

/// <summary>A finished selection: the monitor it was drawn on and the rectangle in screen pixels.</summary>
public sealed record ScreenAreaSelection(MonitorSnapshot Monitor, PixelRect Rect);

/// <summary>
/// Shows a <see cref="SelectionOverlayWindow"/> on every monitor of a snapshot and completes <see cref="Result"/>
/// with the selection, or null when cancelled (Esc, right click, tiny click, or switching to another app).
/// All overlays are closed before the result is delivered.
/// </summary>
internal sealed class ScreenAreaSelector
{
    private readonly List<SelectionOverlayWindow> _overlays = [];
    private readonly TaskCompletionSource<ScreenAreaSelection?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _armed;
    private bool _finished;

    public Task<ScreenAreaSelection?> Result => _result.Task;

    public void Start(ScreenSnapshot snapshot)
    {
        if (snapshot.Monitors.Count == 0)
        {
            Finish(null);
            return;
        }
        var cursor = ScreenInfo.GetCursorPosition();
        SelectionOverlayWindow? underCursor = null;
        foreach (var monitor in snapshot.Monitors)
        {
            var overlay = new SelectionOverlayWindow(monitor);
            overlay.DragStarted += OnDragStarted;
            overlay.SelectionCompleted += OnSelectionCompleted;
            overlay.CancelRequested += (_, _) => Finish(null);
            overlay.Deactivated += OnOverlayDeactivated;
            _overlays.Add(overlay);
            if (monitor.Monitor.Bounds.Contains(cursor))
            {
                underCursor = overlay;
            }
        }
        foreach (var overlay in _overlays)
        {
            overlay.ShowOverlay();
        }

        var active = underCursor ?? _overlays[0];
        if (!active.Activate() || !active.IsActive)
        {
            ForegroundWindow.Activate(new WindowInteropHelper(active).Handle);
        }
        active.Focus();
        // Only watch for deactivation once showing and activating the overlays has settled.
        active.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _armed = true);
    }

    public void Cancel() => Finish(null);

    private void OnDragStarted(object? sender, EventArgs e)
    {
        foreach (var overlay in _overlays)
        {
            overlay.HideHint();
            overlay.CanStartDrag = ReferenceEquals(overlay, sender);
        }
    }

    private void OnSelectionCompleted(object? sender, PixelRect rect)
    {
        if (sender is SelectionOverlayWindow overlay)
        {
            Finish(new ScreenAreaSelection(overlay.Snapshot, rect));
        }
    }

    private void OnOverlayDeactivated(object? sender, EventArgs e)
    {
        if (!_armed || _finished || sender is not Window window)
        {
            return;
        }
        // Moving between our own overlays deactivates one before activating the next; only leaving them all cancels.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_finished && !_overlays.Any(o => o.IsActive || o.IsDragging))
            {
                Finish(null);
            }
        });
    }

    private void Finish(ScreenAreaSelection? selection)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        foreach (var overlay in _overlays)
        {
            overlay.CloseOverlay();
        }
        _overlays.Clear();
        _result.TrySetResult(selection);
    }
}
