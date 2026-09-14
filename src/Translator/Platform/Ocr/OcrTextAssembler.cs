using System.Text;

namespace Translator.Platform.Ocr;

/// <summary>A recognized word and its box in image pixels.</summary>
public readonly record struct OcrWordBox(string Text, double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>A recognized line: its words in reading order.</summary>
public sealed record OcrLineBox(IReadOnlyList<OcrWordBox> Words);

/// <summary>Writing systems the recognizer choice and text assembly care about.</summary>
public enum OcrScript
{
    Latin,
    Cyrillic,
    Japanese,
    Chinese,
    Korean,
    Other,
}

/// <summary>
/// Rebuilds readable text from OCR lines (pure, unit-tested): lines of one paragraph are joined with spaces
/// (Chinese/Japanese without), end-of-line hyphenation is merged, and a new paragraph starts on a large
/// vertical gap, a first-line indent, a bullet, or after a line that ended early.
/// </summary>
public static class OcrTextAssembler
{
    private static readonly HashSet<char> Bullets = ['•', '●', '▪', '◦', '‣', '○', '■', '□', '►', '▶'];

    public static string Assemble(IReadOnlyList<OcrLineBox> lines)
    {
        var rows = lines.Select(Row.From).OfType<Row>().ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var lineHeight = LowerMedian(rows.Select(r => r.Height));
        var gaps = rows.Zip(rows.Skip(1), (above, below) => below.Top - above.Bottom).Where(gap => gap >= 0).ToList();
        // Paragraph gaps are measured against the usual gap of this text, so double-spaced text isn't split per line.
        var breakGap = gaps.Count >= 2
            ? Math.Max(0.9 * lineHeight, LowerMedian(gaps) + 0.5 * lineHeight)
            : 0.9 * lineHeight;
        var maxRight = rows.Max(r => r.Right);
        var minLeft = rows.Min(r => r.Left);

        var text = new StringBuilder(rows[0].Text);
        for (var i = 1; i < rows.Count; i++)
        {
            if (StartsParagraph(rows[i - 1], rows[i], lineHeight, breakGap, maxRight, minLeft))
            {
                text.Append('\n').Append(rows[i].Text);
            }
            else
            {
                AppendLine(text, rows[i].Text);
            }
        }
        return text.ToString();
    }

    /// <summary>Median height of the non-empty lines, in the same pixels as the boxes; null without lines.</summary>
    public static double? MedianLineHeight(IReadOnlyList<OcrLineBox> lines)
    {
        var heights = lines.Select(Row.From).OfType<Row>().Select(r => r.Height).ToList();
        return heights.Count == 0 ? null : LowerMedian(heights);
    }

    private static bool StartsParagraph(Row previous, Row current, double lineHeight, double breakGap, double maxRight, double minLeft)
    {
        var gap = current.Top - previous.Bottom;
        if (gap < -0.5 * lineHeight)
        {
            // Beside or above the previous line: another column or a separate label.
            return true;
        }
        if (gap > breakGap || Bullets.Contains(current.Text[0]))
        {
            return true;
        }
        if (!EndsWithHyphen(previous.Text)
            && previous.Right + 0.3 * lineHeight + current.FirstWordWidth < maxRight - 0.5 * lineHeight)
        {
            // The next line's first word would have fit here, so this line was ended on purpose.
            return true;
        }
        return current.Left - minLeft > 1.2 * lineHeight && previous.Left - minLeft < 0.5 * lineHeight;
    }

    private static void AppendLine(StringBuilder text, string line)
    {
        var last = text[^1];
        var first = line[0];
        if (last == '­')
        {
            text.Length--;
            text.Append(line);
        }
        else if (EndsWithHyphen(text))
        {
            // "recog-" + "nition" → "recognition"; "Jean-" + "Paul" keeps the hyphen.
            if (char.IsLower(first))
            {
                text.Length--;
            }
            text.Append(line);
        }
        else if (CharScripts.IsSpacelessCjk(last) || CharScripts.IsSpacelessCjk(first))
        {
            text.Append(line);
        }
        else
        {
            text.Append(' ').Append(line);
        }
    }

    private static bool EndsWithHyphen(string text) =>
        text.Length >= 2 && text[^1] is '-' or '‐' && char.IsLetter(text[^2]);

    private static bool EndsWithHyphen(StringBuilder text) =>
        text.Length >= 2 && text[^1] is '-' or '‐' && char.IsLetter(text[^2]);

    private static double LowerMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    private sealed record Row(string Text, double Left, double Top, double Right, double Bottom, double FirstWordWidth)
    {
        public double Height => Bottom - Top;

        public static Row? From(OcrLineBox line)
        {
            var words = line.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            if (words.Count == 0)
            {
                return null;
            }
            var text = new StringBuilder(words[0].Text.Trim());
            foreach (var word in words.Skip(1))
            {
                var next = word.Text.Trim();
                // Windows OCR separates every Chinese/Japanese character with a space.
                if (!(CharScripts.IsSpacelessCjk(text[^1]) && CharScripts.IsSpacelessCjk(next[0])))
                {
                    text.Append(' ');
                }
                text.Append(next);
            }
            return new Row(text.ToString(), words.Min(w => w.Left), words.Min(w => w.Top),
                words.Max(w => w.Right), words.Max(w => w.Bottom), words[0].Width);
        }
    }
}

/// <summary>Unicode block tests shared by text assembly and scoring.</summary>
internal static class CharScripts
{
    public static bool IsHan(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '豈' and <= '﫿';

    public static bool IsKana(char c) => c is >= '぀' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ' or >= 'ｦ' and <= 'ﾟ';

    public static bool IsHangul(char c) => c is >= '가' and <= '힯' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏';

    public static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ԯ';

    public static bool IsLatin(char c) => char.IsLetter(c) && (c <= 'ɏ' || c is >= 'Ḁ' and <= 'ỿ');

    /// <summary>Han, kana and CJK/fullwidth punctuation: written without spaces between words and lines.</summary>
    public static bool IsSpacelessCjk(char c) =>
        IsHan(c) || IsKana(c) || c is >= '　' and <= '〿' or >= '！' and <= '･';

    public static OcrScript Of(char c) =>
        IsLatin(c) ? OcrScript.Latin
        : IsCyrillic(c) ? OcrScript.Cyrillic
        : IsKana(c) ? OcrScript.Japanese
        : IsHan(c) ? OcrScript.Chinese
        : IsHangul(c) ? OcrScript.Korean
        : OcrScript.Other;
}
