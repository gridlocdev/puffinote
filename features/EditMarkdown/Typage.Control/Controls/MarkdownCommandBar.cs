using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.Icons;

namespace Typage.Control.Controls;

/// <summary>
/// A formatting toolbar over the Markdown editor's own <see cref="AppCommand"/> catalog — the "command bar"
/// chrome a host (e.g. Puffinote) shows around the editor, grouped Text / Heading / List / Insert like the
/// standalone app's toolbar. Each button runs a command and shows its title + shortcut on hover. Chrome
/// colours come from the host's <c>ChromeBackground</c> / <c>ChromeBorder</c> resources (the same ones the
/// embedded spreadsheet uses), so it follows the shell's theme.
/// </summary>
internal sealed class MarkdownCommandBar : Border
{
    // Commands that have no Lucide icon in the shared set get a short text glyph instead.
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["format.code"] = "</>",
        ["format.quote"] = "❝",
        ["insert.codeBlock"] = "{ }",
        ["insert.hr"] = "—",
    };

    public MarkdownCommandBar(IReadOnlyList<AppCommand> commands)
    {
        var byId = commands.ToDictionary(c => c.Id);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Margin = new Thickness(6, 4) };

        AddGroup(row, byId, "format.bold", "format.italic", "format.code", "format.strike", "format.highlight");
        AddSeparator(row);
        AddGroup(row, byId, "format.h1", "format.h2", "format.h3", "format.paragraph");
        AddSeparator(row);
        AddGroup(row, byId, "format.bulletList", "format.numberedList", "format.task", "format.quote");
        AddSeparator(row);
        AddGroup(row, byId, "insert.codeBlock", "insert.hr");

        Child = row;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Bind(BackgroundProperty, this.GetResourceObservable("ChromeBackground"));
        Bind(BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));
    }

    private static void AddGroup(Panel row, IReadOnlyDictionary<string, AppCommand> byId, params string[] ids)
    {
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var command))
            {
                row.Children.Add(ToolButton(command));
            }
        }
    }

    private static Button ToolButton(AppCommand command)
    {
        Avalonia.Controls.Control content = command.Icon is { Length: > 0 } icon
            ? new LucideIcon { IconKey = icon, Size = 16 }
            : new TextBlock
            {
                Text = Glyphs.TryGetValue(command.Id, out var g) ? g : command.Title,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(7, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => command.Execute();

        var hint = command.ShortcutText is { Length: > 0 } s ? $"{command.Title}    {s}" : command.Title;
        ToolTip.SetTip(button, hint);
        return button;
    }

    private static void AddSeparator(Panel row)
    {
        var separator = new Border { Width = 1, Margin = new Thickness(4, 3) };
        separator.Bind(BackgroundProperty, separator.GetResourceObservable("ChromeBorder"));
        row.Children.Add(separator);
    }
}
