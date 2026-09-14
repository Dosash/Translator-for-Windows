using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// Gets the selected text from whatever app has focus: simulate a copy chord, read the clipboard,
/// restore what was there before. No permission is needed (unlike the macOS Accessibility grant).
/// Which chord to send depends on the target window — see <see cref="InputProfile"/>: terminals treat
/// a plain Ctrl+C with nothing selected as an interrupt (SIGINT), which would kill whatever command is
/// running there, so they get Ctrl+Insert instead.
/// </summary>
/// <remarks>
/// Deliberately does not use UI Automation to read text directly — that switches Chromium/Electron
/// apps into accessibility mode, which is slow and has side effects in the target app.
/// </remarks>
public static class SelectionGrabber
{
    /// <summary>Grabs the selection, assuming an ordinary app (Ctrl+C/Ctrl+V) — kept for callers with no window handle.</summary>
    public static Task<string?> GrabSelectedTextAsync(CancellationToken ct = default) =>
        GrabSelectedTextAsync(IntPtr.Zero, ct);

    public static async Task<string?> GrabSelectedTextAsync(IntPtr targetWindow, CancellationToken ct = default)
    {
        var chord = InputProfile.ForWindow(targetWindow);
        if (chord == CopyPasteChord.CopyOnSelect)
        {
            // PuTTY/KiTTY copy the moment the mouse selects text: there's no copy keystroke to send
            // (and Ctrl+C would be forwarded to the remote session as an interrupt), so whatever is
            // already on the clipboard *is* the selection.
            await InputSimulator.WaitForModifiersReleasedAsync(ct).ConfigureAwait(false);
            var copyOnSelectText = ClipboardService.GetText();
            DebugLog.Write($"SelectionGrabber: copy-on-select, clipboard text length={copyOnSelectText?.Length ?? -1}");
            return copyOnSelectText;
        }

        var snapshot = ClipboardService.Capture();
        if (!snapshot.Succeeded)
        {
            // Another app briefly holds the clipboard: try once more, so the user's content can be put back.
            await Task.Delay(100, ct).ConfigureAwait(false);
            snapshot = ClipboardService.Capture();
        }
        var previousSequence = ClipboardService.SequenceNumber;

        await InputSimulator.WaitForModifiersReleasedAsync(ct).ConfigureAwait(false);
        DebugLog.Write($"SelectionGrabber: sending copy chord ({chord})");
        InputSimulator.SendCopyChord(chord);

        var changed = false;
        for (var i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct).ConfigureAwait(false);
            if (ClipboardService.SequenceNumber != previousSequence)
            {
                changed = true;
                break;
            }
        }
        DebugLog.Write($"SelectionGrabber: clipboard changed={changed}");
        if (!changed)
        {
            return null;
        }

        var text = ClipboardService.GetText();
        DebugLog.Write($"SelectionGrabber: text length={text?.Length ?? -1}");
        if (snapshot.Succeeded)
        {
            ClipboardService.Restore(snapshot);
        }
        else
        {
            DebugLog.Write("SelectionGrabber: previous clipboard wasn't captured, leaving the copied selection");
        }
        return text;
    }
}
