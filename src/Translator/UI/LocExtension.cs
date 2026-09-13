using System.Windows.Data;
using System.Windows.Markup;
using Translator.Core;

namespace Translator.UI;

/// <summary>
/// <c>{ui:Loc app.title}</c> — a live binding to <see cref="LocalizationSource"/>, so text updates when the
/// UI language changes. <c>Upper=True</c> upper-cases it in the current UI culture (section titles);
/// <c>TrimEllipsis=True</c> drops a trailing "…" (window titles reached through "… " links).
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public bool Upper { get; set; }

    public bool TrimEllipsis { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
        };
        if (Upper || TrimEllipsis)
        {
            binding.Converter = new LocTextConverter { Upper = Upper, TrimEllipsis = TrimEllipsis };
        }
        return binding.ProvideValue(serviceProvider);
    }
}
