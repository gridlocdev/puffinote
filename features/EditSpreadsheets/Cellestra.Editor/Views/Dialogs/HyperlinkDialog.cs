using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Model;
using BirdBrain.Components.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// Insert/edit Hyperlink dialog (PRD §4 Editing, §6.2 Insert ▸ Hyperlink…), rendered as an internal
/// modal. Sets, edits, or removes the active cell's <see cref="Hyperlink"/>: fields for the link
/// target, an optional tooltip, and a display-text field that the caller writes into the cell only
/// when the cell is empty (so an existing value isn't clobbered). Its <see cref="ModalDialog{T}.Result"/>
/// is an <see cref="Outcome"/> — or null on cancel (no change). An empty target with
/// <see cref="Outcome.Remove"/> true clears the link.
/// </summary>
public sealed class HyperlinkDialog : ModalDialog<HyperlinkDialog.Outcome?>
{
    /// <summary>
    /// The dialog's decision. <see cref="Link"/> is null when the user removed the hyperlink; otherwise
    /// it's the new/edited link. <see cref="DisplayText"/> is non-null only when the cell was empty and
    /// the user supplied text to show — the caller writes it into the cell.
    /// </summary>
    public sealed record Outcome(Hyperlink? Link, string? DisplayText);

    private readonly bool _cellIsEmpty;
    private readonly TextBox _target = new() { Width = 320, PlaceholderText = "https://example.com or /path/to/file" };
    private readonly TextBox _tooltip = new() { Width = 320 };
    private readonly TextBox _displayText = new() { Width = 320 };

    public HyperlinkDialog(Hyperlink? existing, string displayValue, bool cellIsEmpty)
    {
        _cellIsEmpty = cellIsEmpty;
        Title = existing is null ? "Insert Hyperlink" : "Edit Hyperlink";
        InitialFocus = _target;

        _target.Text = existing?.Target ?? string.Empty;
        _tooltip.Text = existing?.Tooltip ?? string.Empty;
        _displayText.Text = displayValue;

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Close(Build());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        // "Remove" only matters when there's a link to remove; offer it when editing.
        var remove = new Button { Content = "Remove Link", IsVisible = existing is not null };
        remove.Click += (_, _) => Close(new Outcome(null, null));

        var children = new System.Collections.Generic.List<Control>
        {
            Row("Link to", _target),
            Row("Tooltip", _tooltip),
        };

        // The display-text field is only meaningful when the cell is empty — otherwise the cell's own
        // value is the visible text and we leave it untouched (per the brief). Disable it so it's clear.
        if (cellIsEmpty)
        {
            children.Add(Row("Display text", _displayText));
        }

        children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { remove, cancel, ok },
        });

        var panel = new StackPanel { Width = 420, Spacing = 10 };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        DialogContent = panel;
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center }, control },
    };

    /// <summary>Test seam: build the outcome the way clicking OK would, given typed field values.</summary>
    internal Outcome? BuildForTest(string target, string? tooltip = null, string? displayText = null)
    {
        _target.Text = target;
        _tooltip.Text = tooltip ?? string.Empty;
        _displayText.Text = displayText ?? string.Empty;
        return Build();
    }

    /// <summary>
    /// Turn the fields into an outcome. An empty target means "remove" (a hyperlink needs a target). A
    /// non-empty target produces the link plus, when the cell was empty and text was entered, the
    /// display text the caller should write into the cell.
    /// </summary>
    private Outcome Build()
    {
        string target = (_target.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
        {
            return new Outcome(null, null);
        }

        string tooltip = (_tooltip.Text ?? string.Empty).Trim();
        var link = new Hyperlink(target) { Tooltip = string.IsNullOrEmpty(tooltip) ? null : tooltip };

        string display = (_displayText.Text ?? string.Empty).Trim();
        string? displayText = _cellIsEmpty && !string.IsNullOrEmpty(display) ? display : null;
        return new Outcome(link, displayText);
    }
}
