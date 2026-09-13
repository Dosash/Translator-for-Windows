namespace Translator.Platform;

/// <summary>
/// Clipboard access that tolerates the clipboard being briefly locked by other apps.
/// </summary>
/// <remarks>Stub: the real implementation replaces the bodies, keeping this public surface.</remarks>
public static class ClipboardService
{
    /// <summary>Unicode text on the clipboard, or null.</summary>
    public static string? GetText() => null;

    /// <summary>Puts text on the clipboard. Returns false if the clipboard stayed locked.</summary>
    public static bool SetText(string text) => false;

    /// <summary>GetClipboardSequenceNumber — changes on every clipboard write.</summary>
    public static uint SequenceNumber => 0;
}
