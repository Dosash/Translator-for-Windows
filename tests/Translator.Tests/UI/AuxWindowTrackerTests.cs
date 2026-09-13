using Translator.UI.Shell;

namespace Translator.Tests.UI;

public class AuxWindowTrackerTests
{
    [Fact]
    public void Panel_that_stayed_visible_is_only_restored_to_floating()
    {
        var tracker = new AuxWindowTracker();
        tracker.BeforeAuxShown(panelVisible: true);

        var decision = tracker.AuxClosed(otherAuxStillOpen: false, panelVisible: true);

        Assert.True(decision.RestorePanelLevel);
        Assert.False(decision.ShowPanel);
    }

    [Fact]
    public void Panel_hidden_meanwhile_comes_back_when_the_last_aux_window_closes()
    {
        var tracker = new AuxWindowTracker();
        tracker.BeforeAuxShown(panelVisible: true);

        var decision = tracker.AuxClosed(otherAuxStillOpen: false, panelVisible: false);

        Assert.True(decision.ShowPanel);
        Assert.False(tracker.PanelWasVisibleBeforeAux);
    }

    [Fact]
    public void Panel_that_was_never_open_is_not_shown()
    {
        var tracker = new AuxWindowTracker();
        tracker.BeforeAuxShown(panelVisible: false);

        Assert.False(tracker.AuxClosed(otherAuxStillOpen: false, panelVisible: false).ShowPanel);
    }

    [Fact]
    public void Nothing_happens_while_another_aux_window_is_open()
    {
        var tracker = new AuxWindowTracker();
        tracker.BeforeAuxShown(panelVisible: true);
        tracker.BeforeAuxShown(panelVisible: true);

        var first = tracker.AuxClosed(otherAuxStillOpen: true, panelVisible: false);
        var last = tracker.AuxClosed(otherAuxStillOpen: false, panelVisible: false);

        Assert.Equal(new AuxClosedDecision(false, false), first);
        Assert.Equal(new AuxClosedDecision(true, true), last);
    }

    [Fact]
    public void Visibility_is_remembered_across_several_aux_windows()
    {
        var tracker = new AuxWindowTracker();
        tracker.BeforeAuxShown(panelVisible: true);
        tracker.BeforeAuxShown(panelVisible: false); // second window opened after the panel was hidden

        Assert.True(tracker.PanelWasVisibleBeforeAux);
    }
}
