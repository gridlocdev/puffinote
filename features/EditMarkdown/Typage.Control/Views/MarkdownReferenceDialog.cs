using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Overlays;

namespace Typage.Control.Views;

/// <summary>
/// The offline Markdown reference (Help ▸ Markdown Reference): the syntax cheat-sheet, plus every editing
/// shortcut the surface currently binds. Shown as an internal modal in the editor's own overlay layer rather
/// than a separate OS window, so it works the same in an embedded pane as it does in the standalone app.
/// <para>
/// The shortcut half is <em>generated</em> from the live <see cref="AppCommand"/> catalog and formatted per
/// platform (⌘ vs Ctrl), so it can't drift from the keymap the way a hand-written list does — the old help
/// text still advertised chords the editor had long since moved.
/// </para>
/// </summary>
public sealed class MarkdownReferenceDialog : ModalDialog<bool>
{
    private const string Syntax =
        "# H1 … ###### H6      headings\n" +
        "**bold**  *italic*  ~~strike~~  ==highlight==\n" +
        "`inline code`         ```fenced code```\n" +
        "- bullet   1. number  - [ ] task / - [x] done\n" +
        "> blockquote\n" +
        "[text](url)   ![alt](image)   <https://url>\n" +
        "| table | header |   with a |---|---| delimiter row\n" +
        "---                   horizontal rule";

    public MarkdownReferenceDialog(IReadOnlyList<AppCommand> commands)
    {
        Title = "Markdown Reference";

        var body = new StackPanel { Width = 420, Spacing = 12 };
        body.Children.Add(Heading("Syntax"));
        body.Children.Add(new TextBlock
        {
            Text = Syntax,
            FontFamily = new FontFamily("monospace"),
            TextWrapping = TextWrapping.NoWrap,
        });

        body.Children.Add(Heading("Shortcuts"));
        var shortcuts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var bound = commands.Where(c => c.ShortcutText.Length > 0).ToList();
        for (var i = 0; i < bound.Count; i++)
        {
            shortcuts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            shortcuts.Children.Add(Cell(bound[i].Title, i, 0, null));
            shortcuts.Children.Add(Cell(bound[i].ShortcutText, i, 1, CommandBarChrome.MutedBrush));
        }

        // The list runs past a screen on a small display; cap it and scroll rather than overflow the card.
        body.Children.Add(new ScrollViewer { MaxHeight = 280, Content = shortcuts });

        var ok = new Button { Content = "Close", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        body.Children.Add(ok);

        DialogContent = body;
        InitialFocus = ok;
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold };

    private static TextBlock Cell(string text, int row, int column, IBrush? foreground)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 2, column == 0 ? 24 : 0, 2) };
        if (foreground is not null)
        {
            block.Foreground = foreground;
        }

        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        return block;
    }
}
