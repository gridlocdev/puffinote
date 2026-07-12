using Avalonia;
using Avalonia.Media;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The drawn task checkbox: a rounded square, and what is in it (PRD §8.3.2). One place draws it — the editing
/// surface's concealed <c>[x]</c> decoration and the status picker's rows both call <see cref="Draw"/> — so the
/// box a reader clicks and the box they pick from can never be two different pictures.
///
/// <para>Three states, three pictures, <b>one hue</b>: an empty outline (not started), the same outline with an
/// accent <i>dash</i> across it (in progress), and the box filled with that same accent carrying a check (done).
/// The dash and the check share the accent because "under way" and "done" are two points on one path — a second
/// hue would make them two unrelated alarms. Shape carries the state on its own, before colour, which is what
/// keeps the three legible to a colour-blind reader and in a monochrome print (PRD §9.6).</para>
///
/// <para>The symbols are authored in a 24×24 box that <i>is</i> the checkbox — so every number below is also a
/// fraction of the box's side, and the HTML export can draw the very same two paths at the very same stroke
/// widths (<c>HtmlRenderer.TaskGlyph</c>). Two surfaces, one vocabulary: keep them in step.</para>
/// </summary>
public static class TaskGlyphs
{
    /// <summary>The box the symbols are authored in — it maps onto the checkbox itself, so 24 units = one side.</summary>
    private const double ViewBox = 24;

    /// <summary>The check, in the design box. Spans a little under half the box — a tick sitting in a square, not
    /// filling it.</summary>
    private const string CheckPath = "M6.5 12.6 L10.2 16.3 L17.6 7.9";

    /// <summary>The dash: a bar across the middle. Half-drawn work, said with half a check.</summary>
    private const string DashPath = "M6.7 12 L17.3 12";

    /// <summary>Stroke of the check, in design-box units.</summary>
    private const double CheckStroke = 2.2;

    /// <summary>Stroke of the dash, in design-box units — heavier than the outline it sits in, so it reads as a
    /// mark rather than as part of the box.</summary>
    private const double DashStroke = 2.8;

    /// <summary>The thinnest the outline may get: below this a hairline border fades into the page at small font
    /// sizes, and the resting box stops reading as a box at all.</summary>
    private const double MinBorder = 1.25;

    // Immutable and shared, so a document of a thousand tasks parses two paths, not two thousand.
    private static readonly Geometry Check = Geometry.Parse(CheckPath);
    private static readonly Geometry Dash = Geometry.Parse(DashPath);

    /// <summary>
    /// Draw the box for <paramref name="state"/> filling <paramref name="box"/> (a square).
    /// </summary>
    public static void Draw(DrawingContext context, MarkdownTheme theme, Rect box, TaskState state)
    {
        var side = box.Width;
        var radius = side * theme.CheckboxCornerRatio;

        if (state == TaskState.Done)
        {
            // Done is the one state that fills: the accent square *is* the signal, and the check rides on it. It
            // takes the whole allotted square (there is no outline to inset for), which is why a done box reads
            // heavier than the two open ones without being any larger.
            context.DrawRectangle(theme.TaskAccent, null, new RoundedRect(box, radius));
            Symbol(context, box, Check, theme.TaskCheck, CheckStroke);
            return;
        }

        // The two open states share one outline. Stroking straddles the edge, so the rect is deflated by half the
        // pen: the border lands *inside* the square the gutter reserved, and the three boxes line up down the page.
        var border = Math.Max(MinBorder, side * theme.CheckboxBorderRatio);
        context.DrawRectangle(
            null,
            new Pen(theme.TaskBorder, border),
            new RoundedRect(box.Deflate(border / 2), Math.Max(0, radius - (border / 2))));

        if (state == TaskState.InProgress)
        {
            Symbol(context, box, Dash, theme.TaskAccent, DashStroke);
        }
    }

    /// <summary>Stroke <paramref name="path"/> over <paramref name="box"/>, mapping the 24×24 design box onto it.
    /// The pen rides the same transform, so the stroke thickens with the box instead of thinning to a hair.</summary>
    private static void Symbol(DrawingContext context, Rect box, Geometry path, IBrush brush, double stroke)
    {
        var scale = box.Width / ViewBox;
        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X, box.Y));

        context.DrawGeometry(
            null,
            new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            path);
    }
}
