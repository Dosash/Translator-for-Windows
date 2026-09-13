using Translator.Platform;

namespace Translator.Tests.Platform;

/// <summary>
/// These touch the real system clipboard, so every test saves it first and restores it in
/// <c>finally</c> — a shared machine resource that must come back exactly as it was.
/// </summary>
public class ClipboardServiceTests
{
    [Fact]
    public void SetText_then_GetText_round_trips()
    {
        var original = ClipboardService.Capture();
        try
        {
            const string text = "Translator clipboard round-trip éè中";
            Assert.True(ClipboardService.SetText(text));
            Assert.Equal(text, ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void SetText_changes_the_sequence_number()
    {
        var original = ClipboardService.Capture();
        try
        {
            ClipboardService.SetText("first");
            var afterFirst = ClipboardService.SequenceNumber;
            ClipboardService.SetText("second");
            var afterSecond = ClipboardService.SequenceNumber;

            Assert.NotEqual(afterFirst, afterSecond);
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void Capture_then_Restore_round_trips_text()
    {
        var original = ClipboardService.Capture();
        try
        {
            ClipboardService.SetText("before snapshot");
            var snapshot = ClipboardService.Capture();

            ClipboardService.SetText("overwritten");
            Assert.Equal("overwritten", ClipboardService.GetText());

            ClipboardService.Restore(snapshot);
            Assert.Equal("before snapshot", ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void Restoring_an_empty_snapshot_empties_the_clipboard()
    {
        var original = ClipboardService.Capture();
        try
        {
            var empty = new ClipboardSnapshot([]);
            ClipboardService.SetText("something");

            ClipboardService.Restore(empty);

            Assert.Null(ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void Capture_reports_failure_while_another_app_holds_the_clipboard()
    {
        using (new ClipboardHold())
        {
            var snapshot = ClipboardService.Capture();

            Assert.False(snapshot.Succeeded);
            Assert.Empty(snapshot.Entries);
        }
        Assert.True(ClipboardService.Capture().Succeeded);
    }

    [Fact]
    public void Restoring_a_failed_capture_leaves_the_clipboard_alone()
    {
        var original = ClipboardService.Capture();
        try
        {
            Assert.True(ClipboardService.SetText("keep me", excludeFromHistory: true));

            ClipboardService.Restore(ClipboardSnapshot.Failed);

            Assert.Equal("keep me", ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void An_empty_clipboard_is_a_successful_capture()
    {
        Assert.True(new ClipboardSnapshot([]).Succeeded);
        Assert.False(ClipboardSnapshot.Failed.Succeeded);
    }

    [Fact]
    public void Restore_marks_the_restored_data_as_excluded_from_clipboard_history()
    {
        var original = ClipboardService.Capture();
        try
        {
            // A snapshot without the history formats, so only Restore itself can add them.
            var userItem = new ClipboardSnapshot([new ClipboardSnapshotEntry(13 /* CF_UNICODETEXT */, System.Text.Encoding.Unicode.GetBytes("user item\0"))]);

            ClipboardService.Restore(userItem);

            var historyFormat = NativeMethods.RegisterClipboardFormatW(ClipboardService.CanIncludeInHistoryFormat);
            var marker = ClipboardService.Capture().Entries.FirstOrDefault(e => e.Format == historyFormat);
            Assert.NotNull(marker.Data);
            Assert.Equal(0, BitConverter.ToInt32(marker.Data, 0));
            Assert.Equal("user item", ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }

    [Fact]
    public void SetText_with_excludeFromHistory_still_round_trips_the_text()
    {
        var original = ClipboardService.Capture();
        try
        {
            Assert.True(ClipboardService.SetText("temporary paste text", excludeFromHistory: true));
            Assert.Equal("temporary paste text", ClipboardService.GetText());
        }
        finally
        {
            ClipboardService.Restore(original);
        }
    }
}
