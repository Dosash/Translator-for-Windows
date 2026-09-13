using Translator.Platform;

namespace Translator.Tests.Platform;

/// <summary>
/// Makes <see cref="ClipboardService"/> see the clipboard as held by another app until disposed. A test can't
/// simply open the real clipboard: an open without a window handle doesn't block other openers.
/// </summary>
internal sealed class ClipboardHold : IDisposable
{
    private readonly Func<IntPtr, bool> _previous;

    public ClipboardHold()
    {
        _previous = ClipboardService.OpenClipboardFunc;
        ClipboardService.OpenClipboardFunc = _ => false;
    }

    public void Dispose() => ClipboardService.OpenClipboardFunc = _previous;
}
