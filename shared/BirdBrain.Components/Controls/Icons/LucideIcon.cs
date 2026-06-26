using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BirdBrain.Components.Controls.Icons;

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

    /// <summary>Inherited so the icon picks up the foreground of whatever themed control hosts it.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LucideIcon>();

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(IconKeyProperty, SizeProperty, ForegroundProperty);
        AffectsMeasure<LucideIcon>(SizeProperty);
    }

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
        var pen = new Pen(Foreground ?? Brushes.Black, 2)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
