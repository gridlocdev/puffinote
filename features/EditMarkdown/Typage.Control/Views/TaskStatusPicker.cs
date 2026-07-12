using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Typage.Control.Rendering;
using Typage.Engine.Markdown;

namespace Typage.Control.Views;

/// <summary>
/// One task checkbox, drawn at a fixed size: the picker's row glyph. It draws through the same
/// <see cref="TaskGlyphs"/> the editing surface draws its boxes with, so the box you pick is exactly the box
/// you get.
/// </summary>
public sealed class TaskCheckbox : Avalonia.Controls.Control
{
    private readonly MarkdownTheme _theme;
    private readonly TaskState _state;

    public TaskCheckbox(MarkdownTheme theme, TaskState state, double size)
    {
        _theme = theme;
        _state = state;
        Width = size;
        Height = size;
    }

    public override void Render(DrawingContext context) =>
        TaskGlyphs.Draw(context, _theme, new Rect(0, 0, Bounds.Width, Bounds.Height), _state);
}

/// <summary>
/// The status picker: the three task states, each with its box, its name, what it means, and how it is spelled in
/// the source. It drops from the checkbox a secondary click lands on (or from the caret's box, for ⌥⌘L) — the
/// palette opens where the eye already is, the way Cellestra's search pill drops its own.
///
/// <para>Completion stays a plain click on the box; this is how <i>in progress</i> is reached, which is a state
/// you choose rather than toggle into. Rows are ordered the way the work moves
/// (<see cref="TaskStates.Order"/>).</para>
/// </summary>
public sealed class TaskStatusPicker : StackPanel
{
    /// <summary>Raised with the chosen state (the flyout's owner writes it into the document and closes).</summary>
    public event Action<TaskState>? Chosen;

    /// <summary>Raised when the picker is dismissed with Esc.</summary>
    public event Action? Cancelled;

    /// <summary>The alpha of the current row's wash — present enough to find, quiet enough not to compete with
    /// the box it is behind.</summary>
    private const byte CurrentRowAlpha = 0x26;

    /// <param name="theme">The editor's palette — the rows are lit by the very brushes the document is.</param>
    /// <param name="current">The state of the task being edited, pre-selected and ticked.</param>
    public TaskStatusPicker(MarkdownTheme theme, TaskState current)
    {
        Spacing = 1;
        MinWidth = 232;

        foreach (var state in TaskStates.Order)
        {
            Children.Add(Row(theme, state, state == current));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc dismisses; Up/Down walk the rows. Enter/Space activate the focused row (Button's own behaviour),
        // so the picker is fully operable from the keyboard (PRD §9.6).
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke();
                e.Handled = true;
                break;

            case Key.Down:
                Step(1);
                e.Handled = true;
                break;

            case Key.Up:
                Step(-1);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Focus the row <paramref name="delta"/> away from the focused one, wrapping at the ends.</summary>
    private void Step(int delta)
    {
        var focused = Children.ToList().FindIndex(c => c.IsFocused);
        var next = focused < 0
            ? (delta > 0 ? 0 : Children.Count - 1)
            : ((focused + delta + Children.Count) % Children.Count);
        Children[next].Focus(NavigationMethod.Directional);
    }

    /// <summary>Focus the row of the current state, so the picker opens with the answer under the keyboard.</summary>
    public void FocusCurrent()
    {
        var row = Children.FirstOrDefault(c => c is Button { Tag: TaskState } b && b.Classes.Contains("current")) ?? Children.FirstOrDefault();
        row?.Focus(NavigationMethod.Directional);
    }

    private Button Row(MarkdownTheme theme, TaskState state, bool current)
    {
        var label = new TextBlock
        {
            Text = TaskStates.Label(state),
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var description = new TextBlock
        {
            Text = TaskStates.Description(state),
            FontSize = 11,
            Opacity = 0.62,
            Margin = new Thickness(0, 1, 0, 0),
        };

        // The source spelling, so the picker teaches the syntax it writes: choose "In progress" once and you know
        // what "[/]" means the next time you read it in a raw file. Tinted like its own box — accent where the box
        // spends the accent, quiet where it doesn't.
        var code = new TextBlock
        {
            Text = TaskStates.Code(state),
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            FontSize = 11,
            Foreground = state == TaskState.NotStarted ? theme.TaskBorder : theme.TaskAccent,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = current ? 1 : 0.45,
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(label);
        text.Children.Add(description);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 11,
        };

        var box = new TaskCheckbox(theme, state, 20) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(code, 2);
        row.Children.Add(box);
        row.Children.Add(text);
        row.Children.Add(code);

        var button = new Button
        {
            Content = row,
            Tag = state,
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = current ? CurrentRowWash(theme) : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
        };

        if (current)
        {
            button.Classes.Add("current");
        }

        AutomationProperties.SetName(button, $"{TaskStates.Label(state)} — {TaskStates.Description(state)}");
        button.Click += (_, _) => Chosen?.Invoke(state);
        return button;
    }

    /// <summary>The wash behind the row the task is already in: the accent at a low alpha — the picker's one hue,
    /// same as the boxes'.</summary>
    private static IBrush CurrentRowWash(MarkdownTheme theme) =>
        theme.TaskAccent is ISolidColorBrush solid
            ? new SolidColorBrush(Color.FromArgb(CurrentRowAlpha, solid.Color.R, solid.Color.G, solid.Color.B))
            : Brushes.Transparent;
}
