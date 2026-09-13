using System.Text;

namespace Translator.Offline;

/// <summary>A sentence (or fragment) plus the raw whitespace that followed it in the source.</summary>
internal readonly record struct TextSegment(string Text, string Separator);

internal sealed record SegmentedText(string Leading, IReadOnlyList<TextSegment> Segments);

/// <summary>
/// Splits text into sentences so each model call stays short: Opus-MT is trained on single sentences
/// and tends to drop content from long multi-sentence inputs.
/// </summary>
internal static class TextSegmenter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e", "inc", "ltd", "co",
        "no", "nr", "vol", "fig", "approx", "dept", "ca", "cf", "al", "p", "pp",
        "т.е", "т.д", "т.п", "т", "д", "г", "гг", "др", "пр", "см", "ул", "стр", "им", "тыс", "млн", "млрд", "руб", "проф",
        "z.b", "bzw", "usw", "ggf", "d.h", "u.a", "sra", "sr", "ud", "uds", "mme", "mlle", "sig", "dott", "ecc",
    };

    public static SegmentedText Split(string text)
    {
        var segments = new List<TextSegment>();
        var i = SkipWhitespace(text, 0);
        var leading = text[..i];
        var start = i;

        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\n' or '\r')
            {
                var next = SkipWhitespace(text, i);
                Add(segments, text, start, i, next);
                start = i = next;
                continue;
            }

            if (IsTerminator(c))
            {
                var j = i + 1;
                var cjk = IsCjkTerminator(c);
                while (j < text.Length && (IsTerminator(text[j]) || IsClosing(text[j])))
                {
                    cjk |= IsCjkTerminator(text[j]);
                    j++;
                }

                if (j < text.Length && (cjk ? true : char.IsWhiteSpace(text[j]) && IsLatinBoundary(text, i, j)))
                {
                    var next = SkipWhitespace(text, j);
                    if (next < text.Length)
                    {
                        Add(segments, text, start, j, next);
                        start = i = next;
                        continue;
                    }
                }

                i = j;
                continue;
            }

            i++;
        }

        if (start < text.Length)
        {
            Add(segments, text, start, text.Length, text.Length);
        }

        return new SegmentedText(leading, segments);
    }

    /// <summary>
    /// Separator to emit after a translated segment: paragraph breaks are kept verbatim; sentence gaps
    /// follow the target script (no spaces between sentences in Chinese and Japanese).
    /// </summary>
    public static string JoinSeparator(string separator, string targetCode, bool isLast)
    {
        if (isLast || separator.AsSpan().IndexOfAny('\n', '\r') >= 0)
        {
            return separator;
        }

        return UsesSpaces(targetCode) ? " " : "";
    }

    public static string Join(SegmentedText segmented, IReadOnlyList<string> translated, string targetCode)
    {
        var builder = new StringBuilder(segmented.Leading);
        for (var k = 0; k < segmented.Segments.Count; k++)
        {
            builder.Append(translated[k]);
            builder.Append(JoinSeparator(segmented.Segments[k].Separator, targetCode, k == segmented.Segments.Count - 1));
        }

        return builder.ToString();
    }

    public static bool UsesSpaces(string code) => code is not ("zh-CN" or "ja");

    private static void Add(List<TextSegment> segments, string text, int start, int end, int next)
    {
        var trimmedEnd = end;
        while (trimmedEnd > start && char.IsWhiteSpace(text[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd > start)
        {
            segments.Add(new TextSegment(text[start..trimmedEnd], text[trimmedEnd..next]));
        }
        else if (segments.Count > 0)
        {
            var last = segments[^1];
            segments[^1] = last with { Separator = last.Separator + text[start..next] };
        }
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsLatinBoundary(string text, int terminatorIndex, int afterRun)
    {
        var next = SkipWhitespace(text, afterRun);
        if (next < text.Length && char.IsLower(text[next]))
        {
            return false;
        }

        if (text[terminatorIndex] != '.')
        {
            return true;
        }

        var wordStart = terminatorIndex;
        while (wordStart > 0 && (char.IsLetter(text[wordStart - 1]) || text[wordStart - 1] == '.'))
        {
            wordStart--;
        }

        var word = text[wordStart..terminatorIndex];
        if (word.Length == 1 && char.IsUpper(word[0]))
        {
            return false; // initials: "J. R. R. Tolkien"
        }

        return !Abbreviations.Contains(word);
    }

    private static bool IsTerminator(char c) => c is '.' or '!' or '?' or '…' || IsCjkTerminator(c);

    private static bool IsCjkTerminator(char c) => c is '。' or '！' or '？' or '｡';

    private static bool IsClosing(char c) => c is '"' or '\'' or '”' or '’' or '»' or ')' or ']' or '」' or '』' or '）';
}
