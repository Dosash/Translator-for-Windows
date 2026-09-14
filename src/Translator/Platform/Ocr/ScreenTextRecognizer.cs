using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Translator.Core;
using Translator.Platform.Capture;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Translator.Platform.Ocr;

public enum OcrOutcomeKind
{
    Text,
    NoText,
    /// <summary>No installed recognizer fits any of the app's languages.</summary>
    NoRecognizers,
    /// <summary>The panel's source language has no recognizer for its script (<see cref="OcrOutcome.MissingLanguage"/>).</summary>
    LanguageMissing,
    Failed,
}

public sealed record OcrOutcome(OcrOutcomeKind Kind, string Text = "", string? MissingLanguage = null);

/// <summary>
/// Recognizes the text of a screen region with the built-in Windows OCR (<c>Windows.Media.Ocr</c>, on this PC).
/// Call it off the UI thread. Logs sizes, scales, timings, recognizer tags and line counts — never the text.
/// </summary>
public static class ScreenTextRecognizer
{
    /// <summary>Border added around the region (after scaling) so glyphs never touch the image edge.</summary>
    private const int Padding = 16;

    private sealed record Engine(string Tag, OcrScript Script, OcrEngine Instance);

    public static async Task<OcrOutcome> RecognizeAsync(BgraImage region, string sourceCode, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var installed = OcrEngine.AvailableRecognizerLanguages
                .Select(language => new OcrRecognizerInfo(language.LanguageTag, OcrRecognizerChooser.ScriptFromCode(language.Script)))
                .ToList();
            var profileTag = OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag;
            var plan = OcrRecognizerChooser.Plan(sourceCode, installed, profileTag);
            var engines = plan.Tags
                .Select(tag => (Tag: tag, Instance: OcrEngine.TryCreateFromLanguage(new Language(tag))))
                .Where(e => e.Instance is not null)
                .Select(e => new Engine(e.Tag, installed.First(r => r.Tag == e.Tag).Script, e.Instance))
                .ToList();
            if (engines.Count == 0)
            {
                DebugLog.Write($"ScreenOcr: no suitable recognizer (installed {installed.Count}, source language set: {plan.MissingLanguage is not null})");
                return plan.MissingLanguage is null
                    ? new OcrOutcome(OcrOutcomeKind.NoRecognizers)
                    : new OcrOutcome(OcrOutcomeKind.LanguageMissing, MissingLanguage: plan.MissingLanguage);
            }

            var maxDimension = (int)OcrEngine.MaxImageDimension;
            var scale = OcrScaling.InitialScale(region.Width, region.Height, maxDimension, Padding);
            var best = OcrRecognizerChooser.PickBest(await RecognizePassAsync(engines, region, scale, cancellationToken).ConfigureAwait(false));
            var passes = $"x{scale:0.##}";

            var retryScale = OcrScaling.RetryScale(region.Width, region.Height, maxDimension, scale, best is not null, best?.MedianLineHeight, Padding);
            if (retryScale is { } retry)
            {
                var retryEngines = best is null ? engines : engines.Where(e => e.Tag == best.Tag).ToList();
                var second = await RecognizePassAsync(retryEngines, region, retry, cancellationToken).ConfigureAwait(false);
                // Upscaled results first: they win ties.
                best = OcrRecognizerChooser.PickBest(best is null ? second : [.. second, best]);
                passes += $", x{retry:0.##}";
            }

            DebugLog.Write($"ScreenOcr: region {region.Width}x{region.Height}, passes {passes}, recognizers {string.Join(",", engines.Select(e => e.Tag))}, " +
                $"chosen {best?.Tag ?? "none"}, lines {best?.LineCount ?? 0}, chars {best?.Text.Length ?? 0}, {watch.ElapsedMilliseconds} ms");
            return best is null ? new OcrOutcome(OcrOutcomeKind.NoText) : new OcrOutcome(OcrOutcomeKind.Text, best.Text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ScreenOcr: recognition failed ({ex.GetType().Name}, 0x{ex.HResult:X8})");
            return new OcrOutcome(OcrOutcomeKind.Failed);
        }
    }

    private static async Task<List<OcrCandidate>> RecognizePassAsync(IReadOnlyList<Engine> engines, BgraImage region, double scale, CancellationToken cancellationToken)
    {
        var scaled = region.Scale(scale);
        var prepared = scaled.Pad(Padding);
        if (!ReferenceEquals(scaled, region))
        {
            scaled.Clear();
        }
        try
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(prepared.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
                prepared.Width, prepared.Height, BitmapAlphaMode.Premultiplied);
            var candidates = new List<OcrCandidate>(engines.Count);
            foreach (var engine in engines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await engine.Instance.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
                var lines = result.Lines
                    .Select(line => new OcrLineBox(line.Words
                        .Select(word => new OcrWordBox(word.Text, word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width, word.BoundingRect.Height))
                        .ToList()))
                    .ToList();
                candidates.Add(new OcrCandidate(engine.Tag, engine.Script, OcrTextAssembler.Assemble(lines), lines.Count,
                    OcrTextAssembler.MedianLineHeight(lines) / scale));
            }
            return candidates;
        }
        finally
        {
            prepared.Clear();
        }
    }
}
