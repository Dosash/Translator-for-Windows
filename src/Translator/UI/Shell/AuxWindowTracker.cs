namespace Translator.UI.Shell;

public readonly record struct AuxClosedDecision(bool RestorePanelLevel, bool ShowPanel);

/// <summary>
/// Port of macOS <c>rememberPanelBeforeAux</c>/<c>windowWillClose</c>: while settings, history, offline
/// languages or first run are open the panel stays visible behind them; when the last one closes the
/// panel returns to its floating level and reappears if it was open before. Pure, unit-tested.
/// </summary>
public sealed class AuxWindowTracker
{
    public bool PanelWasVisibleBeforeAux { get; private set; }

    /// <summary>Call before an aux window is shown; the caller then sets the panel to "keep visible behind".</summary>
    public void BeforeAuxShown(bool panelVisible) =>
        PanelWasVisibleBeforeAux = panelVisible || PanelWasVisibleBeforeAux;

    public AuxClosedDecision AuxClosed(bool otherAuxStillOpen, bool panelVisible)
    {
        if (otherAuxStillOpen)
        {
            return new AuxClosedDecision(false, false);
        }
        var show = PanelWasVisibleBeforeAux && !panelVisible;
        PanelWasVisibleBeforeAux = false;
        return new AuxClosedDecision(true, show);
    }
}
