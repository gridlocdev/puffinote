using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Model;
using BirdBrain.Components.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Named Range manager (PRD §4 Formulas, §6.2 Insert ▸ Named Range…), rendered as an internal
/// modal. It creates a name for the current selection (the Target defaults to the selected range),
/// lists existing names with their targets, and deletes them — operating directly on the workbook's
/// <see cref="Workbook.AddNamedRange"/>/<see cref="Workbook.RemoveNamedRange"/>/<see cref="Workbook.NamedRanges"/>.
/// Its bool <see cref="ModalDialog{T}.Result"/> is true when anything changed, so the host can refresh
/// the grid and Name Box.
/// </summary>
public sealed class NamedRangeDialog : ModalDialog<bool>
{
    private readonly Workbook _workbook;
    private readonly CellRange _defaultTarget;
    private readonly TextBox _name = new() { Width = 180, PlaceholderText = "TaxRate" };
    private readonly TextBox _target = new() { Width = 180 };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    private readonly StackPanel _list = new() { Spacing = 4 };
    private bool _changed;

    public NamedRangeDialog(Workbook workbook, CellRange selectionWithSheet)
    {
        _workbook = workbook;
        _defaultTarget = selectionWithSheet;
        Title = "Named Ranges";
        InitialFocus = _name;

        _target.Text = selectionWithSheet.ToString();

        var add = new Button { Content = "Add", IsDefault = true };
        add.Click += (_, _) => TryAdd();
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close(_changed);

        DialogContent = new StackPanel
        {
            Width = 420,
            Spacing = 10,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Name", Width = 50, VerticalAlignment = VerticalAlignment.Center }, _name,
                        new TextBlock { Text = "Range", VerticalAlignment = VerticalAlignment.Center }, _target,
                        add,
                    },
                },
                _error,
                new TextBlock { Text = "Existing names:" },
                new ScrollViewer { MaxHeight = 200, Content = _list },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };

        RebuildList();
    }

    /// <summary>
    /// Validate a proposed name (PRD: non-empty, not a cell-address-like token). Returns null when the
    /// name is acceptable, otherwise a plain-language reason. Duplicate detection is left to
    /// <see cref="Workbook.AddNamedRange"/> (it throws), which the caller catches.
    /// </summary>
    internal static string? ValidateName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Enter a name.";
        }

        // A name that parses as a cell reference (A1, $B$2, Sheet1.C3) would be ambiguous with a real
        // address, so it's rejected — the same rule Excel/Calc enforce.
        if (CellAddress.TryParse(trimmed, out _))
        {
            return $"“{trimmed}” looks like a cell reference. Choose a different name.";
        }

        return null;
    }

    private void TryAdd()
    {
        _error.IsVisible = false;

        string name = (_name.Text ?? string.Empty).Trim();
        if (ValidateName(name) is { } reason)
        {
            ShowError(reason);
            return;
        }

        if (!CellRange.TryParse(_target.Text ?? string.Empty, out var range))
        {
            ShowError("Enter a valid range, e.g. Sheet1.A1:B3.");
            return;
        }

        try
        {
            _workbook.AddNamedRange(new NamedRange(name, range));
        }
        catch (InvalidOperationException ex)
        {
            // AddNamedRange throws on a duplicate in the same scope — surface it instead of crashing.
            ShowError(ex.Message);
            return;
        }

        _changed = true;
        _name.Text = string.Empty;
        _target.Text = _defaultTarget.ToString();
        RebuildList();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        if (_workbook.NamedRanges.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "(none)", Opacity = 0.6 });
            return;
        }

        foreach (var named in _workbook.NamedRanges.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList())
        {
            var captured = named;
            var delete = new Button { Content = "Delete" };
            delete.Click += (_, _) =>
            {
                if (_workbook.RemoveNamedRange(captured.Name, captured.Scope))
                {
                    _changed = true;
                    RebuildList();
                }
            };

            _list.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = named.Name, Width = 140, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = named.Target.ToString(), Width = 180, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 },
                    delete,
                },
            });
        }
    }
}
