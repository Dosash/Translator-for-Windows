using System.Globalization;
using System.Text;

namespace Translator.Offline;

/// <summary>
/// Lightweight detection for offline mode: script ranges first, then stop words and diacritics
/// for Latin-script languages. Returns null when the evidence is too weak.
/// </summary>
internal static class LanguageDetector
{
    private static readonly (string Code, string[] Words)[] StopWords =
    [
        ("en", ["the", "be", "to", "of", "and", "a", "in", "that", "have", "i", "it", "for", "not", "on", "with", "he",
            "as", "you", "do", "at", "this", "but", "his", "by", "from", "they", "we", "she", "or", "an", "will", "my",
            "one", "all", "would", "there", "their", "what", "so", "if", "about", "who", "which", "me", "is", "are",
            "was", "were", "has", "had", "hello", "thanks", "thank", "please", "yes", "how", "can", "your", "been",
            "does", "did", "where", "when", "why", "just", "it's", "i'm", "don't", "good", "morning", "world"]),
        ("es", ["el", "la", "los", "las", "de", "que", "y", "en", "un", "una", "es", "por", "con", "para", "no", "se",
            "del", "al", "lo", "como", "más", "pero", "sus", "le", "ya", "este", "sí", "porque", "esta", "cuando",
            "muy", "sin", "sobre", "también", "me", "hasta", "hay", "donde", "quien", "desde", "todo", "nos", "uno",
            "les", "ni", "otros", "ese", "eso", "esto", "yo", "él", "ella", "hola", "gracias", "está", "estoy", "soy",
            "eres", "tengo", "bueno", "buenos", "días", "qué", "cómo", "usted", "mucho", "pues", "son", "tiene"]),
        ("pt", ["o", "a", "os", "as", "de", "que", "e", "do", "da", "em", "um", "uma", "para", "é", "com", "não", "no",
            "se", "na", "por", "mais", "dos", "como", "mas", "foi", "ao", "ele", "das", "tem", "à", "seu", "sua", "ou",
            "ser", "quando", "muito", "há", "nos", "já", "está", "eu", "também", "só", "pelo", "pela", "até", "isso",
            "ela", "entre", "era", "depois", "sem", "mesmo", "aos", "você", "obrigado", "obrigada", "olá", "bom", "dia",
            "tudo", "bem", "então", "são", "estou", "agora", "aqui", "meu", "minha", "nós", "vocês"]),
        ("fr", ["le", "la", "les", "de", "des", "du", "et", "en", "un", "une", "est", "que", "qui", "dans", "pour",
            "pas", "sur", "au", "avec", "ce", "il", "elle", "ne", "se", "plus", "par", "je", "vous", "nous", "on",
            "mais", "ou", "son", "sa", "ses", "sont", "été", "être", "avoir", "cette", "aux", "leur", "comme", "tout",
            "bien", "très", "fait", "bonjour", "merci", "oui", "non", "suis", "avez", "c'est", "j'ai", "moi", "toi",
            "ça", "aussi", "où", "quoi", "comment", "salut", "mon", "ma", "mes"]),
        ("de", ["der", "die", "das", "und", "ist", "nicht", "ich", "du", "sie", "es", "ein", "eine", "einen", "zu",
            "den", "dem", "mit", "von", "auf", "für", "im", "sich", "auch", "als", "an", "wie", "wir", "ihr", "aber",
            "noch", "nach", "bei", "aus", "so", "wenn", "oder", "nur", "hat", "haben", "sind", "war", "werden", "wird",
            "kann", "mein", "dein", "sehr", "guten", "danke", "bitte", "ja", "nein", "heute", "hallo", "schon", "mehr",
            "über", "morgen", "tag", "gut", "wo", "was", "warum", "können", "möchte", "habe", "bin"]),
        ("it", ["il", "lo", "la", "i", "gli", "le", "di", "che", "e", "è", "un", "una", "per", "non", "in", "con",
            "del", "della", "dei", "delle", "al", "alla", "si", "sono", "ma", "come", "questo", "questa", "anche",
            "più", "mi", "ti", "ci", "io", "tu", "lui", "lei", "noi", "voi", "loro", "ho", "ha", "hanno", "essere",
            "avere", "molto", "grazie", "ciao", "buongiorno", "perché", "cosa", "sei", "sto", "bene", "dove",
            "quando", "nel", "nella", "sul", "gli", "tutto", "anche", "ancora", "oggi"]),
        ("tr", ["ve", "bir", "bu", "da", "de", "için", "ile", "çok", "ne", "mi", "mı", "mu", "mü", "ben", "sen", "o",
            "biz", "siz", "onlar", "var", "yok", "gibi", "daha", "ama", "en", "kadar", "olan", "olarak", "değil",
            "merhaba", "teşekkür", "ederim", "nasıl", "evet", "hayır", "şey", "her", "sonra", "şimdi", "iyi",
            "çünkü", "ise", "diye", "benim", "senin", "bana", "sana", "nasılsın", "günaydın", "lütfen", "tamam"]),
    ];

    private static readonly Dictionary<string, List<string>> WordLanguages = BuildWordIndex();

    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int latin = 0, cyrillic = 0, hangul = 0, kana = 0, han = 0, letters = 0;
        var ukrainianMarks = 0;
        var russianMarks = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            letters++;
            switch (value)
            {
                case >= 0x0400 and <= 0x052F:
                    cyrillic++;
                    if (value is 'і' or 'ї' or 'є' or 'ґ' or 'І' or 'Ї' or 'Є' or 'Ґ')
                    {
                        ukrainianMarks++;
                    }
                    else if (value is 'ы' or 'э' or 'ъ' or 'ё' or 'Ы' or 'Э' or 'Ъ' or 'Ё')
                    {
                        russianMarks++;
                    }

                    break;
                case >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F:
                    hangul++;
                    break;
                case >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF9F:
                    kana++;
                    break;
                case >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x2FA1F:
                    han++;
                    break;
                case < 0x0250 or >= 0x1E00 and <= 0x1EFF:
                    latin++;
                    break;
            }
        }

        if (letters == 0)
        {
            return null;
        }

        // Japanese mixes kana with kanji; any meaningful amount of kana decides it.
        if (kana > 0 && kana * 10 >= kana + han)
        {
            return "ja";
        }

        var best = Math.Max(Math.Max(cyrillic, hangul), Math.Max(han, latin));
        if (best == 0 || best * 2 < letters)
        {
            return null;
        }

        if (best == hangul)
        {
            return "ko";
        }

        if (best == han)
        {
            return "zh-CN";
        }

        if (best == cyrillic)
        {
            return ukrainianMarks > russianMarks ? "uk" : "ru";
        }

        return DetectLatin(text);
    }

    private static string? DetectLatin(string text)
    {
        var scores = new Dictionary<string, double>();
        void Add(string code, double value) => scores[code] = scores.GetValueOrDefault(code) + value;

        foreach (var word in Words(text))
        {
            if (WordLanguages.TryGetValue(word, out var languages))
            {
                // Words shared by several languages ("de", "la") carry less evidence.
                foreach (var code in languages)
                {
                    Add(code, 1.0 / languages.Count);
                }
            }
        }

        foreach (var c in text)
        {
            switch (c)
            {
                case 'ñ' or 'Ñ' or '¿' or '¡':
                    Add("es", 2);
                    break;
                case 'ã' or 'õ' or 'Ã' or 'Õ':
                    Add("pt", 2);
                    break;
                case 'ß':
                    Add("de", 3);
                    break;
                case 'ä' or 'Ä':
                    Add("de", 1);
                    break;
                case 'ğ' or 'Ğ' or 'ı' or 'İ' or 'ş' or 'Ş':
                    Add("tr", 3);
                    break;
                case 'ö' or 'ü' or 'Ö' or 'Ü':
                    Add("de", 0.5);
                    Add("tr", 0.5);
                    break;
                case 'ç' or 'Ç':
                    Add("fr", 0.5);
                    Add("pt", 0.5);
                    Add("tr", 0.5);
                    break;
                case 'œ' or 'Œ' or 'ê' or 'è' or 'ù' or 'û' or 'î' or 'ï' or 'ë' or 'â' or 'ô':
                    Add("fr", 1);
                    break;
                case 'ì' or 'ò':
                    Add("it", 1);
                    break;
            }
        }

        if (scores.Count == 0)
        {
            return null;
        }

        var ranked = scores.OrderByDescending(p => p.Value).ToList();
        var top = ranked[0];
        var runnerUp = ranked.Count > 1 ? ranked[1].Value : 0;
        return top.Value >= 1 && top.Value > runnerUp * 1.2 ? top.Key : null;
    }

    private static IEnumerable<string> Words(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetter(c) || (c is '\'' or '’' && builder.Length > 0))
            {
                builder.Append(c == '’' ? '\'' : char.ToLower(c, CultureInfo.InvariantCulture));
            }
            else if (builder.Length > 0)
            {
                yield return builder.ToString().TrimEnd('\'');
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().TrimEnd('\'');
        }
    }

    private static Dictionary<string, List<string>> BuildWordIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (code, words) in StopWords)
        {
            foreach (var word in words.Distinct())
            {
                if (!index.TryGetValue(word, out var list))
                {
                    index[word] = list = [];
                }

                list.Add(code);
            }
        }

        return index;
    }
}
