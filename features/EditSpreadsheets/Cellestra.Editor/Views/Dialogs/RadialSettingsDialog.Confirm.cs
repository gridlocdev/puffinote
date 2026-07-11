using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Cellestra.Desktop.Commands;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The inline confirmation overlays (reset-to-defaults and the Escape-with-unsaved-edits save/cancel
/// prompt), the dirty-tracking that decides when the save prompt is warranted, and the keyboard handling
/// (Escape gating and the ⌥⇧↑/↓ reorder shortcuts).
/// </summary>
public sealed partial class RadialSettingsDialog
{
    /// <summary>The "reset to defaults?" confirmation layered over the body.</summary>
    private Border BuildResetConfirm() => BuildConfirmOverlay(
        "Reset radial menu?",
        "This restores the default segment count and command bindings. Your current changes will be discarded.",
        ("Cancel", false, HideResetConfirm),
        ("Reset", true, () =>
        {
            ApplyDefaults();
            HideResetConfirm();
        }
    ));

    /// <summary>
    /// The "save or cancel?" confirmation shown when Escape is pressed with unsaved edits. Saving closes
    /// with the edited config; cancelling discards (closes with null, like Escape would have); Esc on the
    /// prompt backs out to keep editing.
    /// </summary>
    private Border BuildSaveConfirm() => BuildConfirmOverlay(
        "Save changes?",
        "You've changed the radial menu settings. Save them, or cancel to discard your changes.",
        ("Cancel", false, () => Close(null)),
        ("Save Settings", true, () => Close(BuildConfig())));

    /// <summary>A semi-transparent confirmation card (heading, message, buttons) layered over the body.</summary>
    private static Border BuildConfirmOverlay(string heading, string message, params (string Label, bool IsDefault, Action OnClick)[] buttons)
    {
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var (label, isDefault, onClick) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault };
            button.Click += (_, _) => onClick();
            buttonRow.Children.Add(button);
        }

        var card = new Border
        {
            MaxWidth = 320,
            Background = PickerCardBrush,
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x60, 0, 0, 0) }),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 14 },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    buttonRow,
                },
            },
        };

        var scrim = new Border
        {
            IsVisible = false,
            Background = ScrimBrush, // a scrim that swallows clicks to the body
            Child = card,
        };
        // Trap Tab inside this prompt so it cycles its own buttons, not the dialog body behind it.
        KeyboardNavigation.SetTabNavigation(scrim, KeyboardNavigationMode.Cycle);
        return scrim;
    }

    private void ShowResetConfirm() => ShowOverlay(_resetConfirm);

    private void HideResetConfirm() => _resetConfirm.IsVisible = false;

    private void ShowSaveConfirm() => ShowOverlay(_saveConfirm);

    private void HideSaveConfirm() => _saveConfirm.IsVisible = false;

    /// <summary>Show a layered overlay and move keyboard focus into it (so its Tab trap takes effect).</summary>
    private static void ShowOverlay(Border overlay)
    {
        overlay.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var target = overlay.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault)
                ?? overlay.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable);
            target?.Focus();
        });
    }

    /// <summary>Whether the edited config differs from how it was when the dialog opened.</summary>
    private bool IsDirty => !ConfigsEqual(BuildConfig(), _originalConfig);

    /// <summary>Content equality for two normalized configs (record equality won't compare the id list).</summary>
    private static bool ConfigsEqual(RadialMenuConfig a, RadialMenuConfig b)
    {
        if (a.SegmentCount != b.SegmentCount)
        {
            return false;
        }

        for (int i = 0; i < a.SegmentCount; i++)
        {
            if (a.CommandIdAt(i) != b.CommandIdAt(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Restore every field — count and all bindings — to <see cref="RadialMenuConfig.Default"/>.</summary>
    private void ApplyDefaults()
    {
        var defaults = RadialMenuConfig.Default;
        var fullBindings = (defaults with { SegmentCount = RadialMenuConfig.MaxSegments }).Normalized().CommandIds;
        for (int i = 0; i < _bindings.Count; i++)
        {
            _bindings[i] = i < fullBindings.Count ? fullBindings[i] : null;
        }

        SelectedSegmentForTest = -1;
        _countField.Value = defaults.SegmentCount;
        RebuildBindingRows(); // covers the case where the count was already at the default (no ValueChanged)
    }

    /// <summary>Whether any layered overlay (a prompt or the command picker) is up over the body.</summary>
    private bool AnyOverlayOpen => _resetConfirm.IsVisible || _saveConfirm.IsVisible || _commandPicker.IsVisible;

    /// <summary>Handle Escape: dismiss an open prompt, or guard a dirty close. Returns true if it acted.</summary>
    private bool HandleEscape()
    {
        if (_commandPicker.IsVisible)
        {
            HideCommandPicker();
            return true;
        }

        if (_resetConfirm.IsVisible)
        {
            HideResetConfirm();
            return true;
        }

        if (_saveConfirm.IsVisible)
        {
            HideSaveConfirm();
            return true;
        }

        if (IsDirty)
        {
            ShowSaveConfirm();
            return true;
        }

        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // A prompt dismisses first; otherwise unsaved edits raise the save prompt instead of closing.
            if (HandleEscape())
            {
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e); // nothing to guard — let the dialog cancel/close as usual
            return;
        }

        // Reordering shortcuts are inert while any scrim (a prompt or the picker) is up over the body.
        if (AnyOverlayOpen)
        {
            base.OnKeyDown(e);
            return;
        }

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (SelectedSegmentForTest >= 0)
        {
            // ⌥⇧↑/↓ reorders the selected command; plain ↑/↓ (while a row label has focus) moves the
            // selection so it can be reordered without the mouse.
            if (alt && shift && e.Key == Key.Up)
            {
                MoveSelected(-1);
                e.Handled = true;
                return;
            }
            if (alt && shift && e.Key == Key.Down)
            {
                MoveSelected(1);
                e.Handled = true;
                return;
            }
            if (!alt && !shift && _labelHasFocus && e.Key == Key.Up && InRange(SelectedSegmentForTest - 1))
            {
                SelectSegment(SelectedSegmentForTest - 1, focus: true);
                e.Handled = true;
                return;
            }

            if (!alt && !shift && _labelHasFocus && e.Key == Key.Down && InRange(SelectedSegmentForTest + 1))
            {
                SelectSegment(SelectedSegmentForTest + 1, focus: true);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private RadialMenuConfig BuildConfig() => new RadialMenuConfig
    {
        SegmentCount = SegmentCount,
        CommandIds = [.. _bindings.Take(SegmentCount)],
    }.Normalized();
}
