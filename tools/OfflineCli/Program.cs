using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Translator.Offline;

// Console harness for the offline engine: downloads, translation, benchmarks and tokenizer parity checks.

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var arguments = args.ToList();
var modelsDirectory = TakeOption(arguments, "--models")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translator", "models");
var threadsOption = TakeOption(arguments, "--threads");
var runsOption = TakeOption(arguments, "--runs");
var sourceSpmIds = arguments.Remove("--spm-ids");
var noArena = arguments.Remove("--no-arena");
var noPrepack = arguments.Remove("--no-prepack");
var spin = arguments.Remove("--spin");

if (arguments.Count == 0 || arguments[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return arguments.Count == 0 ? 2 : 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var manager = new OfflineModelManager(modelsDirectory);
manager.Inference = manager.Inference with
{
    IntraOpThreads = threadsOption is null ? manager.Inference.IntraOpThreads : int.Parse(threadsOption, CultureInfo.InvariantCulture),
    UseCpuArena = !noArena,
    Prepacking = !noPrepack,
    AllowSpinning = spin,
};

var cancellationToken = cancellation.Token;
try
{
    return arguments[0] switch
    {
        "status" => await StatusAsync(),
        "download" when arguments.Count == 2 => await DownloadAsync(arguments[1]),
        "remove" when arguments.Count == 2 => await RemoveAsync(arguments[1]),
        "check-updates" => await CheckUpdatesAsync(),
        "translate" when arguments.Count == 4 => await TranslateAsync(arguments[1], arguments[2], arguments[3]),
        "bench" when arguments.Count == 3 => await BenchAsync(arguments[1], arguments[2]),
        "tokenize" when arguments.Count == 3 => Tokenize(arguments[1], arguments[2]),
        "parity" when arguments.Count == 2 => Parity(arguments[1]),
        "raw" when arguments.Count == 6 => Raw(arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]),
        "generate" when arguments.Count == 4 => Generate(arguments[1], arguments[2], arguments[3]),
        _ => PrintUsage(),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex) when (ex is OfflineTranslationException or OfflineDownloadException or ArgumentException
                               or InvalidOperationException or IOException or JsonException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

async Task<int> StatusAsync()
{
    Console.WriteLine($"Models directory: {manager.ModelsDirectory}");
    try
    {
        await manager.RefreshDownloadSizesAsync(cancellationToken);
    }
    catch (OfflineDownloadException ex)
    {
        Console.WriteLine($"(Hub unavailable, sizes are estimates: {ex.Message})");
    }

    Console.WriteLine($"{"lang",-6} {"state",-15} {"quality",-7} {"download",9} {"on disk",9}  to English | from English");
    Console.WriteLine($"{"en",-6} {"Installed",-15} {"",-7} {"",9} {"",9}  base language");
    foreach (var language in OfflineModelCatalog.Languages)
    {
        var toEnglish = OfflineModelCatalog.GetModel(language.ToEnglishModelId);
        var fromEnglish = OfflineModelCatalog.GetModel(language.FromEnglishModelId);
        var token = language.FromEnglishTargetToken is { } t ? " " + t : "";
        Console.WriteLine(
            $"{language.Code,-6} {manager.GetState(language.Code),-15} {(language.IsBasicQuality ? "basic" : "good"),-7} " +
            $"{Mb(manager.GetDownloadSizeBytes(language.Code)),6:0.0} MB {Mb(manager.GetInstalledSizeBytes(language.Code)),6:0.0} MB  " +
            $"{toEnglish.Repo} | {fromEnglish.Repo}{token}");
    }

    return 0;
}

async Task<int> DownloadAsync(string code)
{
    var stopwatch = Stopwatch.StartNew();
    var lastPrint = -1000L;
    var progress = new SynchronousProgress<OfflineDownloadProgress>(p =>
    {
        var now = stopwatch.ElapsedMilliseconds;
        if (now - lastPrint < 500 && p.BytesReceived != p.TotalBytes)
        {
            return;
        }

        lastPrint = now;
        var percent = p.TotalBytes is > 0 ? p.BytesReceived * 100.0 / p.TotalBytes.Value : 100;
        Console.WriteLine($"{p.LanguageCode}: {percent,5:0.0}%  {Mb(p.BytesReceived),6:0.0} / {Mb(p.TotalBytes ?? 0):0.0} MB");
    });

    await manager.DownloadAsync(code, progress, cancellationToken);
    Console.WriteLine($"Done in {stopwatch.Elapsed.TotalSeconds:0.0} s. State: {manager.GetState(code)}, on disk: {Mb(manager.GetInstalledSizeBytes(code)):0.0} MB.");
    return 0;
}

async Task<int> RemoveAsync(string code)
{
    await manager.RemoveAsync(code, cancellationToken);
    Console.WriteLine($"State: {manager.GetState(code)}.");
    foreach (var other in OfflineModelManager.SupportedLanguages.Where(c => manager.GetState(c) != OfflineLanguageState.NotInstalled))
    {
        Console.WriteLine($"  still installed: {other} ({manager.GetState(other)})");
    }

    return 0;
}

async Task<int> CheckUpdatesAsync()
{
    var updates = await manager.CheckForUpdatesAsync(cancellationToken);
    Console.WriteLine(updates.Count == 0 ? "All installed languages are up to date." : "Updates available: " + string.Join(", ", updates));
    return 0;
}

async Task<int> TranslateAsync(string source, string target, string text)
{
    if (text == "-")
    {
        text = await Console.In.ReadToEndAsync(cancellationToken);
    }

    var stopwatch = Stopwatch.StartNew();
    var result = await manager.TranslateAsync(text, source.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : source, target, cancellationToken);
    stopwatch.Stop();
    Console.WriteLine(result.Text);
    Console.Error.WriteLine($"[{result.SourceCode} -> {target}, {stopwatch.ElapsedMilliseconds} ms]");
    return 0;
}

async Task<int> BenchAsync(string source, string target)
{
    var runs = runsOption is null ? 10 : int.Parse(runsOption, CultureInfo.InvariantCulture);
    var (sentence, paragraph) = BenchSamples.For(source);
    Console.WriteLine($"Bench {source} -> {target}: {manager.Inference}, runs {runs}, CPU cores {Environment.ProcessorCount}");

    var cold = Stopwatch.StartNew();
    var first = await manager.TranslateAsync(sentence, source, target, cancellationToken);
    Console.WriteLine($"Cold start (load models + translate): {cold.ElapsedMilliseconds} ms");
    Console.WriteLine($"  {sentence}\n  => {first.Text}");

    var shortTimes = new List<double>();
    for (var i = 0; i < runs; i++)
    {
        shortTimes.Add((await TimeTranslationAsync(sentence, source, target)).Milliseconds);
    }

    Console.WriteLine($"Short sentence ({sentence.Length} chars): median {Median(shortTimes):0} ms, min {shortTimes.Min():0} ms");

    var paragraphTimes = new List<double>();
    var paragraphText = "";
    for (var i = 0; i < Math.Max(3, runs / 3); i++)
    {
        var (milliseconds, text) = await TimeTranslationAsync(paragraph, source, target);
        paragraphTimes.Add(milliseconds);
        paragraphText = text;
    }

    var median = Median(paragraphTimes);
    Console.WriteLine($"Paragraph ({paragraph.Length} chars): median {median:0} ms, min {paragraphTimes.Min():0} ms, {paragraph.Length / (median / 1000):0} chars/s");
    Console.WriteLine($"  => {paragraphText}");

    using var process = Process.GetCurrentProcess();
    process.Refresh();
    Console.WriteLine($"Peak working set: {Mb(process.PeakWorkingSet64):0} MB, private bytes now: {Mb(process.PrivateMemorySize64):0} MB, managed heap: {Mb(GC.GetTotalMemory(false)):0} MB");
    return 0;
}

async Task<(double Milliseconds, string Text)> TimeTranslationAsync(string text, string source, string target)
{
    var stopwatch = Stopwatch.StartNew();
    var result = await manager.TranslateAsync(text, source, target, cancellationToken);
    return (stopwatch.Elapsed.TotalMilliseconds, result.Text);
}

int Tokenize(string modelId, string text)
{
    var tokenizer = MarianTokenizer.Load(Path.Combine(manager.ModelsDirectory, modelId));
    Console.WriteLine(JsonSerializer.Serialize(tokenizer.Encode(text)));
    Console.WriteLine(string.Join(" | ", tokenizer.Tokenize(text)));
    return 0;
}

int Parity(string casesPath)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(casesPath));
    var tokenizers = new Dictionary<string, MarianTokenizer>();
    var total = 0;
    var failed = 0;
    foreach (var item in document.RootElement.EnumerateArray())
    {
        var modelId = item.GetProperty("model").GetString()!;
        var text = item.GetProperty("text").GetString()!;
        var targetToken = item.TryGetProperty("target_token", out var tokenElement) ? tokenElement.GetString() : null;
        var expected = item.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (!tokenizers.TryGetValue(modelId, out var tokenizer))
        {
            tokenizers[modelId] = tokenizer = MarianTokenizer.Load(Path.Combine(manager.ModelsDirectory, modelId));
        }

        var actual = tokenizer.Encode(text, targetToken);
        total++;
        if (!actual.SequenceEqual(expected))
        {
            failed++;
            Console.WriteLine($"MISMATCH [{modelId}] {JsonSerializer.Serialize(text)}");
            Console.WriteLine($"  expected {JsonSerializer.Serialize(expected)}");
            Console.WriteLine($"  actual   {JsonSerializer.Serialize(actual)}");
            Console.WriteLine($"  pieces   {string.Join(" | ", tokenizer.Tokenize(text))}");
        }
    }

    Console.WriteLine($"Token id parity: {total - failed}/{total} identical across {tokenizers.Count} models.");
    return failed == 0 ? 0 : 1;
}

int Raw(string directory, string encoderPath, string decoderPath, string targetToken, string text)
{
    var info = new OfflineModelInfo(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), "local", encoderPath, decoderPath, 0);
    var stopwatch = Stopwatch.StartNew();
    using var model = MarianModel.Load(directory, info, manager.Inference);
    var loadMilliseconds = stopwatch.ElapsedMilliseconds;
    stopwatch.Restart();
    Console.WriteLine(model.Translate(text, targetToken == "-" ? null : targetToken, cancellationToken));
    Console.Error.WriteLine($"[load {loadMilliseconds} ms, translate {stopwatch.ElapsedMilliseconds} ms]");
    return 0;
}

int Generate(string modelId, string targetToken, string text)
{
    var info = OfflineModelCatalog.GetModel(modelId);
    if (sourceSpmIds)
    {
        info = info with { SourceIdsFromSentencePiece = true };
    }

    var directory = Path.Combine(manager.ModelsDirectory, modelId);
    using var model = MarianModel.Load(directory, info, manager.Inference);
    var sourceIds = model.Tokenizer.Encode(text, targetToken == "-" ? null : targetToken);
    var generated = model.Generate(sourceIds, cancellationToken);
    var targetPieces = SentencePieceModel.Load(Path.Combine(directory, OfflineModelInfo.TargetSpm)).Pieces;
    Console.WriteLine($"source ids:    {JsonSerializer.Serialize(sourceIds)}");
    Console.WriteLine($"generated ids: {JsonSerializer.Serialize(generated)}");
    Console.WriteLine($"target.spm:    {string.Join(" | ", generated.Select(id => id < targetPieces.Length ? targetPieces[id] : "?"))}");
    Console.WriteLine($"decoded:       {model.Tokenizer.Decode(generated)}");
    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("""
        Usage: OfflineCli [--models <dir>] [--threads <n>] [--no-arena] [--no-prepack] [--spin] <command>

          status                               languages, states, sizes and models
          download <lang>                      download the models for a language
          remove <lang>                        remove a language (shared models stay while used)
          check-updates                        compare installed files with the Hub
          translate <src|auto> <tgt> "text"    translate ("-" reads text from stdin)
          bench <src> <tgt> [--runs <n>]       latency for a short sentence and a ~500-char paragraph
          tokenize <model-id> "text"           print Marian token ids and pieces
          parity <cases.json>                  compare token ids with scripts/marian_parity.py output
          raw <dir> <encoder> <decoder> <token|-> "text"   run any exported model directory
        """);
    return 2;
}

static string? TakeOption(List<string> arguments, string name)
{
    var index = arguments.IndexOf(name);
    if (index < 0 || index + 1 >= arguments.Count)
    {
        return null;
    }

    var value = arguments[index + 1];
    arguments.RemoveRange(index, 2);
    return value;
}

static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

static double Median(List<double> values)
{
    var sorted = values.Order().ToList();
    return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}

internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

internal static class BenchSamples
{
    public static (string Sentence, string Paragraph) For(string code) => code switch
    {
        "en" => (
            "Where is the nearest train station?",
            "Offline translation runs entirely on this computer, so the text never leaves the device. The first request after " +
            "starting the app takes longer because the models have to be loaded into memory. After that, short phrases are " +
            "translated almost instantly, while long paragraphs need more time and use several processor cores. If you often " +
            "translate large documents, keep the laptop plugged in, because sustained work drains the battery faster than usual."),
        "ru" => (
            "Где находится ближайшая станция метро?",
            "Офлайн-перевод полностью выполняется на этом компьютере, поэтому текст никуда не отправляется. Первый запрос после " +
            "запуска приложения занимает больше времени, потому что модели нужно загрузить в память. После этого короткие фразы " +
            "переводятся почти мгновенно, а длинные абзацы требуют больше времени и нагружают несколько ядер процессора. Если вы " +
            "часто переводите большие документы, держите ноутбук подключённым к сети."),
        "de" => (
            "Wo ist der nächste Bahnhof?",
            "Die Offline-Übersetzung läuft vollständig auf diesem Computer, daher verlässt der Text niemals das Gerät. Die erste " +
            "Anfrage nach dem Start der Anwendung dauert länger, weil die Modelle in den Arbeitsspeicher geladen werden müssen. " +
            "Danach werden kurze Sätze fast sofort übersetzt, während lange Absätze mehr Zeit brauchen und mehrere Prozessorkerne " +
            "auslasten. Wenn Sie oft große Dokumente übersetzen, lassen Sie den Laptop am Netzteil."),
        _ => throw new ArgumentException("Bench samples exist for en, ru and de sources."),
    };
}
