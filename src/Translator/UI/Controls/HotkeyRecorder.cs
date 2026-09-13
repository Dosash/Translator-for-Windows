using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Translator.Core;

namespace Translator.UI.Controls;

/// <summary>
/// Click (or Space/Enter), then press a shortcut. Esc cancels, Backspace/Delete turns it off; only
/// shortcuts with Ctrl, Alt or Win are accepted. <see cref="RecordingChanged"/> lets the owner suspend global
/// hotkeys while recording so the pressed chord reaches this control.
/// </summary>
public sealed class HotkeyRecorder : Control
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyRecorder),
        new FrameworkPropertyMetadata(Hotkey.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyRecorder)d).UpdateDisplayText()));

    private static readonly DependencyPropertyKey IsRecordingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsRecording), typeof(bool), typeof(HotkeyRecorder), new PropertyMetadata(false));

    public static readonly DependencyProperty IsRecordingProperty = IsRecordingPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(HotkeyRecorder), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    static HotkeyRecorder()
    {
        FocusableProperty.OverrideMetadata(typeof(HotkeyRecorder), new FrameworkPropertyMetadata(true));
    }

    public HotkeyRecorder()
    {
        Loaded += (_, _) =>
        {
            L10n.LanguageChanged += OnLanguageChanged;
            UpdateDisplayText();
        };
        Unloaded += (_, _) =>
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            StopRecording();
        };
        UpdateDisplayText();
    }

    /// <summary>Raised with true when recording starts and false when it stops (after <see cref="Hotkey"/> was updated).</summary>
    public event EventHandler<bool>? RecordingChanged;

    public Hotkey Hotkey
    {
        get => (Hotkey)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public bool IsRecording => (bool)GetValue(IsRecordingProperty);

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }
        SetValue(IsRecordingPropertyKey, true);
        UpdateDisplayText();
        RecordingChanged?.Invoke(this, true);
    }

    public void StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }
        SetValue(IsRecordingPropertyKey, false);
        UpdateDisplayText();
        RecordingChanged?.Invoke(this, false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsRecording)
        {
            if (e.Key is Key.Space or Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                StartRecording();
                e.Handled = true;
            }
            return;
        }

        var key = HotkeyCapture.ResolveKey(e.Key, e.SystemKey, e.ImeProcessedKey, e.DeadCharProcessedKey);
        var modifiers = Keyboard.Modifiers;
        var result = HotkeyCapture.Interpret(key, modifiers);
        switch (result.Action)
        {
            case HotkeyCaptureAction.Ignore:
                // Plain Tab still moves focus (which ends recording) so the recorder never traps the keyboard.
                if (key == Key.Tab && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
                {
                    return;
                }
                break;
            case HotkeyCaptureAction.Cancel:
                StopRecording();
                break;
            case HotkeyCaptureAction.Clear:
                SetCurrentValue(HotkeyProperty, Hotkey.None);
                StopRecording();
                break;
            case HotkeyCaptureAction.Accept:
                SetCurrentValue(HotkeyProperty, result.Hotkey);
                StopRecording();
                break;
        }
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateDisplayText();

    private void UpdateDisplayText() =>
        SetValue(DisplayTextPropertyKey, IsRecording
            ? L10n.T("hotkey.recording")
            : Hotkey.IsNone ? L10n.T("hotkey.none") : Hotkey.Display);
}
