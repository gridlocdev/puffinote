using Avalonia;
using Avalonia.Media;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The drawn task checkbox: a circular lamp, ringed and tinted in its state's colour, with a symbol inside
/// (PRD §8.3.2). One place draws it — the editing surface's concealed <c>[x]</c> decoration and the status
/// picker's rows both call <see cref="Draw"/> — so the box a reader clicks and the box they pick from can
/// never be two different pictures.
///
/// <para>The symbol carries the state on its own, before colour: a check, a cross, a half-lit disc, a
/// double chevron, an exclamation. Colour is a second, redundant channel, which is what keeps the six
/// states legible to a colour-blind reader and in a monochrome print (PRD §9.6).</para>
///
/// <para>The shapes are authored in a 24×24 box, the same six symbols the HTML export draws as inline SVG
/// (<c>HtmlRenderer.TaskGlyph</c>) — two surfaces, one vocabulary. Keep them in step.</para>
/// </summary>
public static class TaskGlyphs
{
    /// <summary>The box the path data is authored in — everything below is expressed in these units and scaled.</summary>
    private const double ViewBox = 24;

    /// <summary>The symbol's size as a fraction of the lamp's diameter: it sits <i>inside</i> the ring, not on it.</summary>
    private const double GlyphRatio = 0.72;

    /// <summary>Stroke width of a stroked symbol, in view-box units (scaled with the glyph).</summary>
    private const double GlyphStroke = 2.5;

    /// <summary>The ring's thickness as a fraction of the lamp's diameter.</summary>
    private const double RingRatio = 0.10;

    /// <summary>
    /// Draw the lamp for <paramref name="state"/> filling <paramref name="box"/> (a square).
    /// </summary>
    public static void Draw(DrawingContext context, MarkdownTheme theme, Rect box, TaskState state)
    {
        var color = theme.TaskColor(state);
        var r = box.Width / 2;
        var centre = box.Center;

        // The ring, over a wash of its own colour. Not-started gets no wash: an un-lit lamp — the zero point —
        // is an empty outline, so the five *lit* states read as changes from a resting state rather than as five
        // equal options.
        var pen = new Pen(color, Math.Max(1, box.Width * RingRatio));
        context.DrawEllipse(theme.TaskFill(state), pen, centre, r, r);

        var (stroke, fill) = Paths(state);
        if (stroke is null && fill is null)
        {
            return;
        }

        // Place the symbol's 24×24 design box centred in the lamp, at GlyphRatio of its size. The pen's width
        // rides the same transform, so a stroked symbol thickens with the lamp instead of thinning to a hair.
        var glyph = box.Width * GlyphRatio;
        var scale = glyph / ViewBox;
        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation(centre.X - (glyph / 2), centre.Y - (glyph / 2)));

        if (fill is not null)
        {
            context.DrawGeometry(color, null, fill);
        }

        if (stroke is not null)
        {
            context.DrawGeometry(null, new Pen(color, GlyphStroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke);
        }
    }

    /// <summary>The symbol for <paramref name="state"/>, as a stroked path and/or a filled one, in the 24×24 design box.</summary>
    private static (Geometry? Stroke, Geometry? Fill) Paths(TaskState state) => Cache.TryGetValue(state, out var g) ? g : default;

    /// <summary>
    /// Built once: the geometries are immutable and shared by every lamp on screen, so a document of a thousand
    /// tasks parses six paths, not a thousand.
    /// </summary>
    private static readonly Dictionary<TaskState, (Geometry? Stroke, Geometry? Fill)> Cache = new()
    {
        // Not started: nothing inside the ring.
        [TaskState.NotStarted] = (null, null),

        // In progress: a disc filled to its meridian — half-lit, the way a progress dial reads.
        [TaskState.InProgress] = (null, Geometry.Parse("M12 3 A9 9 0 0 1 12 21 Z")),

        // Blocked: an exclamation — something needs attention before this can move.
        [TaskState.Blocked] = (
            Geometry.Parse("M12 5.5 L12 13.5"),
            new EllipseGeometry(new Rect(10.5, 16.5, 3, 3))),

        // Deferred: a double chevron — pushed forward, in time rather than out of the list.
        [TaskState.Deferred] = (Geometry.Parse("M6 6 L11 12 L6 18 M12 6 L17 12 L12 18"), null),

        // Done: the check.
        [TaskState.Done] = (Geometry.Parse("M5.5 12.5 L10 17 L18.5 7"), null),

        // Cancelled: the cross — struck out, not ticked off.
        [TaskState.Cancelled] = (Geometry.Parse("M7 7 L17 17 M17 7 L7 17"), null),
    };
}
