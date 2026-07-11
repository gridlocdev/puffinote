using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Puffinote.Components.Controls.Icons;

/// <summary>
/// Renders a vendored Lucide glyph (see <see cref="LucideIcons"/>) as a stroked path that scales
/// to <see cref="Size"/> and inherits the ambient text <see cref="Foreground"/>, so icons follow
/// the light/dark theme automatically. A missing or empty key simply draws nothing.
/// </summary>
public sealed class LucideIcon : Control
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<LucideIcon, string?>(nameof(IconKey));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(Size), 16);

    /// <summary>
    /// Draw the glyph solid (filled with <see cref="Foreground"/>) as well as stroked, rather than as an
    /// outline. Only meaningful for glyphs whose outline is a closed path — <c>star</c> is the one this
    /// exists for: an outline star means "pin this", a solid one means "pinned", so the state survives a
    /// reader who cannot see the accent colour (WCAG 2.2 §1.4.1).
    /// </summary>
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<LucideIcon, bool>(nameof(IsFilled));

    /// <summary>Inherited so the icon picks up the foreground of whatever themed control hosts it.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LucideIcon>();

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(IconKeyProperty, SizeProperty, ForegroundProperty, IsFilledProperty);
        AffectsMeasure<LucideIcon>(SizeProperty);
    }

    /// <summary>
    /// The icon factory every command palette hands to its <see cref="PaletteListView"/>: a command's icon key
    /// as a row-sized glyph, or none for a key-less command. Shared so a row reads the same on every palette
    /// surface — the four of them had drifted apart back when each host wrote this expression itself.
    /// </summary>
    public static Control? PaletteGlyph(string? key) =>
        string.IsNullOrEmpty(key) ? null : new LucideIcon { IconKey = key, Size = PaletteListView.IconSize };

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var geometry = LucideIcons.Get(IconKey);
        if (geometry is null)
        {
            return;
        }

        double scale = Size / LucideIcons.ViewBox;
        var brush = Foreground ?? Brushes.Black;
        var pen = new Pen(brush, 2)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(IsFilled ? brush : null, pen, geometry);
        }
    }
}
