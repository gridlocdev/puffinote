using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The star, in the one place it is defined.
///
/// <para>It appears on every card and again in the task dialog's header, and those were two different
/// stars: a "★" typed into a button on the card — whatever size and weight the font decided, which is
/// how it came out visibly smaller than everything beside it — and a drawn glyph in the dialog. One
/// control means one answer to what starred looks like, in both places, forever.</para>
///
/// <para>Outline for unstarred and solid for starred, rather than grey against gold: the state has to
/// survive a reader who cannot tell the two colours apart (WCAG 2.2 §1.4.1), which is exactly what
/// <see cref="LucideIcon.IsFilled"/> exists for.</para>
/// </summary>
internal static class StarButton
{
    /// <summary>The drawn size of the star, in both places it appears — the prototype's 19px, near enough.</summary>
    public const double Glyph = 18;

    /// <summary>The prototype's <c>--star</c>, brightened for the dark chrome these controls sit on.</summary>
    private static readonly IBrush Gold = new SolidColorBrush(Color.FromRgb(0xE0, 0xA9, 0x2A));

    public static Button Build(bool important, Action toggle)
    {
        string label = important ? "Remove star" : "Add star";

        var button = new Button
        {
            Content = new LucideIcon { IconKey = "star", Size = Glyph, IsFilled = important },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(3, 0),
            Foreground = important ? Gold : Tokens.Active.MutedText,
        };

        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => toggle();
        return button;
    }
}
