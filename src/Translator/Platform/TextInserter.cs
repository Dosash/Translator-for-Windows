using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// Replaces the selected text with a translation: put it on the clipboard, refocus the source
/// window, simulate Ctrl+V, restore whatever was on the clipboard before. Mirrors <see cref="SelectionGrabber"/>.
/// </summary>
public static class TextInserter
{
    /// <returns>False when the text couldn't be put on the clipboard; nothing is pasted in that case.</returns>
    public static async Task<bool> PasteAsync(string text, IntPtr targetWindow, CancellationToken ct = default)
    {
        var snapshot = ClipboardService.Capture();
        if (!ClipboardService.SetText(text, excludeFromHistory: true))
        {
            // Ctrl+V now would paste the old clipboard content over the user's selection.
            DebugLog.Write("TextInserter: clipboard busy, paste skipped");
            return false;
        }

        if (targetWindow != IntPtr.Zero)
        {
            ForegroundWindow.Activate(targetWindow);
        }

        // The bubble/panel just closed; give keyboard focus a moment to land back in the target app.
        await Task.Delay(150, ct).ConfigureAwait(false);

        await InputSimulator.WaitForModifiersReleasedAsync(ct).ConfigureAwait(false);
        DebugLog.Write($"TextInserter: pasting, length={text.Length}");
        InputSimulator.SendCtrlV();

        // Give the target app time to read the clipboard before we put the old content back.
        await Task.Delay(400, ct).ConfigureAwait(false);
        if (snapshot.Succeeded)
        {
            ClipboardService.Restore(snapshot);
        }
        else
        {
            DebugLog.Write("TextInserter: previous clipboard wasn't captured, not restoring");
        }
        return true;
    }
}
