namespace Translator.Core;

public enum PanelSizeMode
{
    Compact,
    Standard,
    Wide,
}

public static class PanelSizeModeInfo
{
    public static PanelSizeMode Parse(string? value) =>
        Enum.TryParse<PanelSizeMode>(value, ignoreCase: true, out var mode) ? mode : PanelSizeMode.Standard;

    /// <summary>Panel width in device-independent pixels.</summary>
    public static double PanelWidth(this PanelSizeMode mode) => mode switch
    {
        PanelSizeMode.Compact => 360,
        PanelSizeMode.Wide => 500,
        _ => 400,
    };

    public static double TextAreaHeight(this PanelSizeMode mode) => mode switch
    {
        PanelSizeMode.Compact => 72,
        PanelSizeMode.Wide => 116,
        _ => 84,
    };

    public static double ResultHeight(this PanelSizeMode mode) => mode switch
    {
        PanelSizeMode.Compact => 72,
        PanelSizeMode.Wide => 126,
        _ => 84,
    };

    public static string NameKey(this PanelSizeMode mode) => mode switch
    {
        PanelSizeMode.Compact => "panel.compact",
        PanelSizeMode.Wide => "panel.wide",
        _ => "panel.standard",
    };

    public static string CaptionKey(this PanelSizeMode mode) => NameKey(mode) + ".caption";
}
