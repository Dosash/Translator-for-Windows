using Translator.Core;

namespace Translator.UI;

public readonly record struct PrivacyCapsuleContent(string Glyph, string? Label);

/// <summary>
/// What the engine indicator under the panel's result shows. The full sentence (<see cref="TranslatorModel.EnginePrivacyText"/>)
/// lives in the tooltip; the indicator itself only gets an icon and the engine name so it fits the compact panel.
/// </summary>
public static class PrivacyCapsule
{
    public static PrivacyCapsuleContent Describe(EngineKind engine, bool offlineOnly) => engine switch
    {
        EngineKind.Google => new(Icons.Globe, EngineName("engine.google.label")),
        EngineKind.Offline => new(Icons.Lock, EngineName("engine.offline.label")),
        _ when offlineOnly => new(Icons.Lock, EngineName("engine.offline.label")),
        _ => new(Icons.Shield, null),
    };

    /// <summary>Engine labels read "name · where" in every UI language (Japanese uses "・"); keep the name.</summary>
    internal static string EngineName(string labelKey)
    {
        var label = L10n.T(labelKey);
        var separator = label.IndexOfAny(['·', '・']);
        return (separator > 0 ? label[..separator] : label).Trim();
    }
}
