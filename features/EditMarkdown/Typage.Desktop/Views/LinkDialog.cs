using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using BirdBrain.Components.Overlays;

namespace Typage.Desktop.Views;

/// <summary>
/// The ⌘K link prompt rendered as an internal modal: a "Display text" field (prefilled from the
/// selection / word under the caret) and a "Location" field, with Cancel / Add. Its
/// <see cref="ModalDialog{T}.Result"/> is the entered values on Add/Enter, or null on Cancel/Esc.
/// Spaces in the location are encoded as <c>%20</c> so the URL survives as one Markdown destination.
/// </summary>
public sealed class LinkDialog : ModalDialog<LinkDialog.Values?>
{
    /// <summary>The trimmed display text and encoded location the user entered.</summary>
    public readonly record struct Values(string Text, string Url);

    private readonly TextBox _display;
    private readonly TextBox _location;

    public LinkDialog(string suggestedText)
    {
        Title = "Insert Link";

        _display = new TextBox { PlaceholderText = "Display text", Text = suggestedText };
        _location = new TextBox { PlaceholderText = "https://example.com or path/to/file" };
        // Land in the field that still needs typing: location when a label is already filled, else display.
        InitialFocus = string.IsNullOrEmpty(suggestedText) ? _display : _location;

        _display.KeyDown += OnFieldKeyDown;
        _location.KeyDown += OnFieldKeyDown;

        var add = new Button { Content = "Add", IsDefault = true };
        add.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        AddRow(grid, 0, "Display text", _display);
        AddRow(grid, 1, "Location", _location);

        DialogContent = new StackPanel
        {
            Width = 340,
            Spacing = 16,
            Children =
            {
                grid,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, add },
                },
            },
        };
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
        }
    }

    private void Accept()
    {
        var url = (_location.Text ?? string.Empty).Trim().Replace(" ", "%20");
        Close(new Values((_display.Text ?? string.Empty).Trim(), url));
    }

    private static void AddRow(Grid grid, int row, string label, Avalonia.Controls.Control field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 0), MinWidth = 90 };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        field.Margin = new Thickness(0, 6, 0, 0);
        grid.Children.Add(text);
        grid.Children.Add(field);
    }
}
