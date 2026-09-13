using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Translator.Core;

/// <summary>Text-to-speech, abstracted so <see cref="TranslatorModel"/> can be unit-tested with a fake.</summary>
public interface ISpeechService
{
    /// <summary>Speaks <paramref name="text"/> in the voice for <paramref name="googleCode"/>. Returns a localized error message, or null on success.</summary>
    Task<string?> SpeakAsync(string text, string googleCode);

    void Stop();
}

/// <summary>Windows.Media.SpeechSynthesis + MediaPlayer based implementation.</summary>
public sealed class SpeechService : ISpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly MediaPlayer _player = new();

    public async Task<string?> SpeakAsync(string text, string googleCode)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        Stop();

        var voice = FindVoice(googleCode);
        if (voice is null)
        {
            return L10n.Format("error.speech.voice", L10n.LanguageName(googleCode));
        }

        try
        {
            _synthesizer.Voice = voice;
            try
            {
                _synthesizer.Options.SpeakingRate = 0.95;
            }
            catch
            {
                // Rate control not supported for this voice/OS build; speak at the default rate.
            }

            var stream = await _synthesizer.SynthesizeTextToStreamAsync(trimmed);
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
            return null;
        }
        catch (Exception ex)
        {
            return L10n.Format("error.speech.failed", ex.Message);
        }
    }

    public void Stop()
    {
        try
        {
            _player.Pause();
            _player.Source = null;
        }
        catch
        {
            // Best-effort stop; nothing useful to do if the player is already in a bad state.
        }
    }

    public void Dispose()
    {
        _player.Dispose();
        _synthesizer.Dispose();
    }

    private static VoiceInformation? FindVoice(string googleCode)
    {
        var tag = AppLanguage.ByGoogle(googleCode)?.SpeechTag;
        if (tag is null)
        {
            return null;
        }

        var exact = SpeechSynthesizer.AllVoices.FirstOrDefault(v => string.Equals(v.Language, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var primary = tag.Split('-')[0];
        return SpeechSynthesizer.AllVoices.FirstOrDefault(
            v => v.Language.StartsWith(primary, StringComparison.OrdinalIgnoreCase));
    }
}
