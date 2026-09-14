using Translator.Core;

namespace Translator.Platform.Ocr;

/// <summary>An installed Windows OCR recognizer: BCP-47 tag and script.</summary>
public sealed record OcrRecognizerInfo(string Tag, OcrScript Script);

/// <summary>
/// Recognizers to run, in preference order (ties go to the earlier one). Empty with
/// <see cref="MissingLanguage"/> set: the chosen source language has no usable recognizer; empty without it:
/// no recognizer fits any app language.
/// </summary>
public sealed record OcrPlan(IReadOnlyList<string> Tags, string? MissingLanguage = null);

/// <summary>One recognizer's assembled result.</summary>
public sealed record OcrCandidate(string Tag, OcrScript Script, string Text, int LineCount, double? MedianLineHeight = null);

/// <summary>
/// Which recognizers to run for the panel's source language and which result to keep (pure, unit-tested).
/// A recognizer applied to another script doesn't fail — it returns look-alike gibberish ("nepeBoA" for
/// "перевод"), so results are compared by how much plausible text of the recognizer's own script they hold.
/// </summary>
public static class OcrRecognizerChooser
{
    private static readonly string[] LatinAppLanguages = ["en", "es", "de", "fr", "it", "pt", "tr"];

    /// <summary>ISO 15924 script code (<c>Windows.Globalization.Language.Script</c>) to <see cref="OcrScript"/>.</summary>
    public static OcrScript ScriptFromCode(string? iso15924) => iso15924?.ToUpperInvariant() switch
    {
        "LATN" => OcrScript.Latin,
        "CYRL" => OcrScript.Cyrillic,
        "JPAN" or "HRKT" or "HIRA" or "KANA" => OcrScript.Japanese,
        "HANS" or "HANT" or "HANI" => OcrScript.Chinese,
        "KORE" or "HANG" => OcrScript.Korean,
        _ => OcrScript.Other,
    };

    /// <summary>Script of one of the app's language codes (<see cref="AppLanguage.All"/>).</summary>
    public static OcrScript ScriptOfLanguage(string code) => Primary(code) switch
    {
        "ru" or "uk" => OcrScript.Cyrillic,
        "ja" => OcrScript.Japanese,
        "zh" => OcrScript.Chinese,
        "ko" => OcrScript.Korean,
        _ => OcrScript.Latin,
    };

    public static OcrPlan Plan(string sourceCode, IReadOnlyList<OcrRecognizerInfo> installed, string? userProfileTag)
    {
        var profile = userProfileTag is null
            ? null
            : installed.FirstOrDefault(r => string.Equals(r.Tag, userProfileTag, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(sourceCode) && !string.Equals(sourceCode, AppLanguage.AutoCode, StringComparison.OrdinalIgnoreCase))
        {
            var chosen = FindForLanguage(installed, sourceCode) ?? BestForScript(ScriptOfLanguage(sourceCode), installed, profile);
            return chosen is null ? new OcrPlan([], sourceCode) : new OcrPlan([chosen.Tag]);
        }

        var tags = new List<string>();
        void Add(OcrRecognizerInfo? recognizer)
        {
            if (recognizer is not null && !tags.Contains(recognizer.Tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(recognizer.Tag);
            }
        }
        if (profile is { Script: not OcrScript.Other })
        {
            Add(profile);
        }
        foreach (var script in new[] { OcrScript.Latin, OcrScript.Cyrillic, OcrScript.Japanese, OcrScript.Chinese, OcrScript.Korean })
        {
            Add(BestForScript(script, installed, profile));
        }
        return new OcrPlan(tags);
    }

    /// <summary>The highest-scoring non-empty candidate; the earlier one wins a tie. Null when all are empty.</summary>
    public static OcrCandidate? PickBest(IEnumerable<OcrCandidate> candidates)
    {
        OcrCandidate? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Text))
            {
                continue;
            }
            var score = Score(candidate.Text, candidate.Script);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Letters of <paramref name="script"/> count fully (Chinese/Japanese/Korean characters double, they carry more),
    /// other letters half. Words that look like misrecognition — digits between letters, a capital after a lowercase
    /// letter, Latin mixed with Cyrillic, stray symbols — count against the result, replacement characters more so.
    /// </summary>
    public static double Score(string text, OcrScript script)
    {
        double score = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            double letters = 0;
            var letterCount = 0;
            var garbage = 0;
            var anomalous = false;
            bool hasLatin = false, hasCyrillic = false;
            for (var i = 0; i < word.Length; i++)
            {
                var c = word[i];
                if (c == '�')
                {
                    garbage += 2;
                    anomalous = true;
                }
                else if (char.IsLetter(c))
                {
                    var charScript = CharScripts.Of(c);
                    var weight = charScript is OcrScript.Japanese or OcrScript.Chinese or OcrScript.Korean ? 2.0 : 1.0;
                    letters += IsOwn(charScript, script) ? weight : weight * 0.5;
                    letterCount++;
                    hasLatin |= charScript == OcrScript.Latin;
                    hasCyrillic |= charScript == OcrScript.Cyrillic;
                    if (char.IsUpper(c) && i > 0 && char.IsLower(word[i - 1]))
                    {
                        anomalous = true;
                    }
                }
                else if (char.IsDigit(c))
                {
                    if (i > 0 && char.IsLetter(word[i - 1]) && NextNonDigitIsLetter(word, i))
                    {
                        anomalous = true;
                    }
                }
                else if (!IsOrdinaryPunctuation(c))
                {
                    garbage++;
                    anomalous = true;
                }
            }
            anomalous |= hasLatin && hasCyrillic;
            score += anomalous ? -0.5 * letterCount : letters;
            score -= garbage * 2;
        }
        return score;
    }

    private static bool IsOwn(OcrScript charScript, OcrScript engine) => engine switch
    {
        OcrScript.Japanese => charScript is OcrScript.Japanese or OcrScript.Chinese,
        _ => charScript == engine,
    };

    private static bool NextNonDigitIsLetter(string word, int index)
    {
        for (var j = index + 1; j < word.Length; j++)
        {
            if (!char.IsDigit(word[j]))
            {
                return char.IsLetter(word[j]);
            }
        }
        return false;
    }

    private static bool IsOrdinaryPunctuation(char c) =>
        c is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"' or '(' or ')' or '[' or ']' or '{' or '}'
            or '-' or '–' or '—' or '…' or '/' or '\\' or '%' or '&' or '@' or '#' or '*' or '+' or '=' or '<' or '>'
            or '«' or '»' or '„' or '“' or '”' or '‘' or '’' or '$' or '€' or '£' or '₽' or '¥' or '_' or '|' or '№' or '°'
        || c is >= '　' and <= '〿' or >= '！' and <= '･';

    private static OcrRecognizerInfo? BestForScript(OcrScript script, IReadOnlyList<OcrRecognizerInfo> installed, OcrRecognizerInfo? profile)
    {
        if (profile is not null && profile.Script == script)
        {
            return profile;
        }
        OcrRecognizerInfo? ByPrimary(string primary) => installed.FirstOrDefault(r => Primary(r.Tag) == primary);
        return script switch
        {
            OcrScript.Latin => ByPrimary("en")
                ?? installed.FirstOrDefault(r => r.Script == OcrScript.Latin && LatinAppLanguages.Contains(Primary(r.Tag)))
                ?? installed.FirstOrDefault(r => r.Script == OcrScript.Latin),
            OcrScript.Cyrillic => ByPrimary("ru") ?? ByPrimary("uk") ?? installed.FirstOrDefault(r => r.Script == OcrScript.Cyrillic),
            OcrScript.Japanese => ByPrimary("ja"),
            OcrScript.Chinese => FindForLanguage(installed, "zh-CN"),
            OcrScript.Korean => ByPrimary("ko"),
            _ => null,
        };
    }

    /// <summary>A recognizer for exactly this language; Simplified Chinese preferred for "zh-CN".</summary>
    private static OcrRecognizerInfo? FindForLanguage(IReadOnlyList<OcrRecognizerInfo> installed, string code)
    {
        var primary = Primary(code);
        var matches = installed.Where(r => Primary(r.Tag) == primary).ToList();
        if (primary == "zh")
        {
            return matches.FirstOrDefault(r => r.Tag.Contains("Hans", StringComparison.OrdinalIgnoreCase)
                    || r.Tag.EndsWith("-CN", StringComparison.OrdinalIgnoreCase) || r.Tag.EndsWith("-SG", StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault();
        }
        return matches.FirstOrDefault();
    }

    private static string Primary(string tag)
    {
        var dash = tag.IndexOf('-');
        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}
