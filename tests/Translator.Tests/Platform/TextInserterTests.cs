using Translator.Platform;

namespace Translator.Tests.Platform;

public class TextInserterTests
{
    [Fact]
    public async Task PasteAsync_returns_false_without_pasting_when_the_clipboard_stays_busy()
    {
        using var hold = new ClipboardHold();
        // Guard: only call PasteAsync once the clipboard is really unavailable, so it can never send Ctrl+V.
        Assert.False(ClipboardService.Capture().Succeeded);

        var pasted = await TextInserter.PasteAsync("translation", IntPtr.Zero);

        Assert.False(pasted);
    }
}
