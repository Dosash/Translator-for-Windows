namespace Translator.Offline;

/// <summary>
/// Cuts a sentence that exceeds the per-call token budget at clause punctuation, then at spaces,
/// then at character boundaries (for scripts without spaces), and packs the pieces back up to the budget.
/// </summary>
internal static class LongSegmentSplitter
{
    public static IReadOnlyList<TextSegment> Split(string text, Func<string, int> countTokens, int maxTokens)
    {
        if (countTokens(text) <= maxTokens)
        {
            return [new TextSegment(text, "")];
        }

        var units = new List<TextSegment>();
        foreach (var clause in SplitAfter(text, IsClauseBreak))
        {
            AddUnits(units, clause, countTokens, maxTokens, splitWords: true);
        }

        units[^1] = units[^1] with { Separator = "" };
        return Pack(units, countTokens, maxTokens);
    }

    private static void AddUnits(List<TextSegment> units, TextSegment segment, Func<string, int> countTokens, int maxTokens, bool splitWords)
    {
        if (countTokens(segment.Text) <= maxTokens)
        {
            units.Add(segment);
            return;
        }

        var first = units.Count;
        if (splitWords)
        {
            foreach (var word in SplitAfter(segment.Text, IsWordEnd))
            {
                AddUnits(units, word, countTokens, maxTokens, splitWords: false);
            }
        }
        else
        {
            units.AddRange(SplitByCharacters(segment.Text, countTokens, maxTokens));
        }

        if (units.Count > first)
        {
            units[^1] = units[^1] with { Separator = segment.Separator };
        }
    }

    private static List<TextSegment> Pack(List<TextSegment> units, Func<string, int> countTokens, int maxTokens)
    {
        var packed = new List<TextSegment>();
        var current = "";
        var currentSeparator = "";
        var currentTokens = 0;
        foreach (var unit in units)
        {
            var tokens = countTokens(unit.Text);
            if (current.Length > 0 && currentTokens + tokens > maxTokens)
            {
                packed.Add(new TextSegment(current, currentSeparator));
                current = "";
                currentTokens = 0;
            }

            current = current.Length == 0 ? unit.Text : current + currentSeparator + unit.Text;
            currentSeparator = unit.Separator;
            currentTokens += tokens;
        }

        if (current.Length > 0)
        {
            packed.Add(new TextSegment(current, currentSeparator));
        }

        return packed;
    }

    private static IEnumerable<TextSegment> SplitByCharacters(string text, Func<string, int> countTokens, int maxTokens)
    {
        var tokens = Math.Max(1, countTokens(text));
        var chunkLength = Math.Max(1, (int)((long)text.Length * maxTokens * 9 / (tokens * 10L)));
        var index = 0;
        while (index < text.Length)
        {
            var length = Math.Min(chunkLength, text.Length - index);
            if (index + length < text.Length && char.IsHighSurrogate(text[index + length - 1]))
            {
                length++;
            }

            yield return new TextSegment(text.Substring(index, length), "");
            index += length;
        }
    }

    /// <summary>Splits after positions where <paramref name="isBreak"/> holds; the following whitespace becomes the separator.</summary>
    private static IEnumerable<TextSegment> SplitAfter(string text, Func<string, int, bool> isBreak)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!isBreak(text, i))
            {
                continue;
            }

            var end = i + 1;
            var next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }

            if (next >= text.Length)
            {
                break;
            }

            yield return new TextSegment(text[start..end], text[end..next]);
            start = next;
            i = next - 1;
        }

        yield return new TextSegment(text[start..].TrimEnd(), "");
    }

    private static bool IsWordEnd(string text, int index) =>
        !char.IsWhiteSpace(text[index]) && index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]);

    private static bool IsClauseBreak(string text, int index) => text[index] switch
    {
        ',' or ';' or ':' => index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]),
        '，' or '；' or '：' or '、' => true,
        _ => false,
    };
}
