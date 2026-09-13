namespace Translator.UI;

public static class TextFormat
{
    /// <summary>
    /// "Offline languages…" → "Offline languages": the ellipsis marks links and menu items that open a
    /// window, not the window's own title or header.
    /// </summary>
    public static string TrimTrailingEllipsis(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith('…'))
        {
            return trimmed[..^1].TrimEnd();
        }
        return trimmed.EndsWith("...", StringComparison.Ordinal) ? trimmed[..^3].TrimEnd() : trimmed;
    }
}
