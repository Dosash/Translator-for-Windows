using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// Gets the selected text from whatever app has focus: simulate Ctrl+C, read the clipboard,
/// restore what was there before. No permission is needed (unlike the macOS Accessibility grant).
/// </summary>
/// <remarks>
/// Deliberately does not use UI Automation to read text directly — that switches Chromium/Electron
/// apps into accessibility mode, which is slow and has side effects in the target app.
/// </remarks>
public static class SelectionGrabber
{
    public static async Task<string?> GrabSelectedTextAsync(CancellationToken ct = default)
    {
        var snapshot = ClipboardService.Capture();
        if (!snapshot.Succeeded)
        {
            // Another app briefly holds the clipboard: try once more, so the user's content can be put back.
            await Task.Delay(100, ct).ConfigureAwait(false);
            snapshot = ClipboardService.Capture();
        }
        var previousSequence = ClipboardService.SequenceNumber;

        await InputSimulator.WaitForModifiersReleasedAsync(ct).ConfigureAwait(false);
        DebugLog.Write("SelectionGrabber: sending Ctrl+C");
        InputSimulator.SendCtrlC();

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
