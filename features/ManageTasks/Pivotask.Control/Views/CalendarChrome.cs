using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The one thing Pivotask asks of the stock calendar: that <b>today</b> be unmistakable.
///
/// <para>The Fluent theme marks it, but marks it faintly — a hairline in a colour a half-step off the
/// other thirty squares, which on a grid of near-identical numbers is not a difference you can find
/// without hunting for it. "Is this week's Thursday the 12th or the 19th?" is the question a due-date
/// picker exists to answer, and it is answered by finding today first.</para>
///
/// <para>Three signals rather than one, because a colour alone is not a difference every reader can see
/// (WCAG 2.2 §1.4.1): an accent ring, an accent wash inside it, and the numeral in bold. Held off a day
/// that is also <em>selected</em> — there, the selection's own fill is the more urgent fact, and two
/// competing treatments on one square say less than either.</para>
///
/// <para>Applied to a host's <see cref="StyledElement.Styles"/> rather than to the buttons themselves,
/// which is the only way to reach them: the day grid lives inside the picker's popup and is built and
/// thrown away as the months are paged through.</para>
/// </summary>
internal static class CalendarChrome
{
    /// <summary>The ring's weight. Heavier than a hairline on purpose — it is the thing being found.</summary>
    private const double Ring = 1.5;

    /// <summary>Mark today in whatever calendar <paramref name="host"/> contains.</summary>
    public static void MarkToday(StyledElement host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var palette = Tokens.Active;

        host.Styles.Add(new Style(x => x.OfType<CalendarDayButton>()
            .Class(":today")
            .Not(y => y.Class(":selected")))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.AccentSubtle),
                new Setter(TemplatedControl.BorderBrushProperty, palette.Accent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(Ring)),
                new Setter(TemplatedControl.ForegroundProperty, palette.Accent),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Bold),
            },
        });
    }
}
