using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Shell;

/// <summary>
/// The family's unified title bar (Cellestra PRD §6.1, and the same bar in every other app): a single
/// strip at the top of the window showing the <b>document's name, centred</b>, with an "edited" dot when
/// there are unsaved changes and an optional transient badge ("Saved").
/// </summary>
/// <remarks>
/// <para>
/// A native title bar centres its text on macOS and left-aligns it on Windows and Linux, and its text is
/// whatever <see cref="Window.Title"/> says — app-name suffixes and all. This bar is the app's own, so the
/// document's name is centred identically everywhere and nothing else is allowed into it.
/// </para>
/// <para>
/// <see cref="Extend"/> is what merges it with the window (macOS only — see that method). The window's
/// <see cref="Window.Title"/> is still the app's to set and still matters: it is what the Window menu, the
/// app switcher, and Mission Control show. This bar does not touch it.
/// </para>
/// </remarks>
public sealed class DocumentTitleBar : UserControl
{
    /// <summary>
    /// Room kept clear at both ends for macOS's traffic lights, so a long document name is ellipsised
    /// rather than run under the close/minimise/zoom buttons. Reserved at <em>both</em> ends because the
    /// name stays centred in the window (as macOS centres a native title), not in the space left over.
    /// </summary>
    public const double MacTrafficLightInset = 78;

    /// <summary>The inset on platforms with no traffic lights to dodge — just breathing room.</summary>
    public const double PlainInset = 12;

    /// <summary>
    /// The bar's height. Comfortably clears macOS's ~28pt title bar band, so the traffic lights sit
    /// inside the bar rather than over the content below it.
    /// </summary>
    public const double BarHeight = 32;

    /// <summary>The gap between the dot, the name, and the badge.</summary>
    private const double RowSpacing = 6;

    /// <summary>The document's name — the file name alone (e.g. <c>Budget.ods</c>), never a path.</summary>
    public static readonly StyledProperty<string> DocumentNameProperty =
        AvaloniaProperty.Register<DocumentTitleBar, string>(nameof(DocumentName), "Untitled");

    /// <summary>Whether the document has unsaved changes — shows the "edited" dot before the name.</summary>
    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<DocumentTitleBar, bool>(nameof(IsDirty));

    /// <summary>
    /// A transient note shown after the name (Cellestra flashes "Saved" here). Null hides it. A badge and
    /// the dot say opposite things, so the badge is suppressed while <see cref="IsDirty"/> is set.
    /// </summary>
    public static readonly StyledProperty<string?> BadgeProperty =
        AvaloniaProperty.Register<DocumentTitleBar, string?>(nameof(Badge));

    private readonly TextBlock _dot = new()
    {
        Text = "●",
        FontSize = 9,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly TextBlock _name = new()
    {
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _badge = new()
    {
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly Border _border;
    private readonly StackPanel _row;

    public DocumentTitleBar()
    {
        Inset = OperatingSystem.IsMacOS() ? MacTrafficLightInset : PlainInset;

        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = RowSpacing,
            // Equal margins at both ends keep the name centred *in the window* while still reserving the
            // traffic-light zone: the panel is centred in a box inset symmetrically, which has the same
            // centre as the bar itself.
            Margin = new Thickness(Inset, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dot, _name, _badge },
        };

        _border = new Border
        {
            Height = BarHeight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _row,
        };

        Content = _border;
        // The bar is chrome, not content: it must never take focus away from the document.
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => ApplyChrome();
        UpdateText();
        ApplyChrome();
    }

    /// <inheritdoc cref="DocumentNameProperty"/>
    public string DocumentName
    {
        get => GetValue(DocumentNameProperty);
        set => SetValue(DocumentNameProperty, value);
    }

    /// <inheritdoc cref="IsDirtyProperty"/>
    public bool IsDirty
    {
        get => GetValue(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }

    /// <inheritdoc cref="BadgeProperty"/>
    public string? Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    /// <summary>The name currently drawn, and whether the dot and badge show (test seam).</summary>
    internal (string Name, bool Dot, string? Badge) ContentForTest =>
        (_name.Text ?? string.Empty, _dot.IsVisible, _badge.IsVisible ? _badge.Text : null);

    /// <summary>
    /// The space this bar keeps clear at <em>each</em> end — <see cref="MacTrafficLightInset"/> on macOS,
    /// <see cref="PlainInset"/> elsewhere. Both ends, so the name stays centred in the window.
    /// </summary>
    public double Inset { get; }

    /// <summary>The centred row, so a test can assert its placement and reserved insets (test seam).</summary>
    internal Control RowForTest => _row;

    /// <summary>The text block the name is drawn in, to assert what it actually occupies (test seam).</summary>
    internal Control NameBlockForTest => _name;

    /// <summary>
    /// Merge the bar with the window's own title bar, so the two read as one surface. <b>macOS only</b>:
    /// there, extending the client area keeps the system's traffic lights and drops only the title text,
    /// which is exactly the trade we want. On Windows and Linux the same hint hands us the caption buttons
    /// too — a different, larger piece of work — so those platforms keep their native title bar and this
    /// bar simply sits below it, still centring the name.
    /// </summary>
    public static void Extend(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = -1; // -1 = keep the system's own title-bar height
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentNameProperty
            || change.Property == IsDirtyProperty
            || change.Property == BadgeProperty)
        {
            UpdateText();
        }
    }

    /// <summary>
    /// Cap the name's width to what is left after both insets and the dot/badge, so a long file name is
    /// ellipsised at the edge of the traffic-light zone. Without the cap it would simply keep growing: a
    /// horizontal <see cref="StackPanel"/> arranges each child at its desired width and lets the row
    /// overflow, so the trimming set on the text block would never have anything to trim against.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width))
        {
            _dot.Measure(availableSize);
            _badge.Measure(availableSize);
            double extras = (_dot.IsVisible ? _dot.DesiredSize.Width + RowSpacing : 0)
                + (_badge.IsVisible ? _badge.DesiredSize.Width + RowSpacing : 0);
            _name.MaxWidth = Math.Max(0, availableSize.Width - (2 * Inset) - extras);
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyChrome(); // the host's Chrome* resources are only resolvable once we're in its tree
    }

    /// <summary>
    /// Dragging the bar moves the window and double-clicking it zooms, because with the client area
    /// extended this strip <em>is</em> the title bar and has to behave like one.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || VisualRoot is not Window window)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            window.BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    private void UpdateText()
    {
        _name.Text = string.IsNullOrWhiteSpace(DocumentName) ? "Untitled" : DocumentName;
        _dot.IsVisible = IsDirty;

        // The dot wins over the badge: by the time there are unsaved changes again, the save the badge
        // refers to is out of date (the rule Cellestra's title already followed).
        bool badge = !IsDirty && !string.IsNullOrEmpty(Badge);
        _badge.IsVisible = badge;
        _badge.Text = badge ? $"— {Badge}" : null;
    }

    /// <summary>
    /// Paint from the host's <c>ChromeBackground</c>/<c>ChromeBorder</c> when it publishes them, so this bar
    /// and the command bar directly beneath it are one continuous surface rather than two near-but-unequal
    /// greys. Apps without those resources (Plaintain, Pivotask) fall back to the family's chrome tokens,
    /// which is what their status bars already use.
    /// </summary>
    private void ApplyChrome()
    {
        var palette = Tokens.For(ActualThemeVariant);
        _border.Background = FindChrome("ChromeBackground") ?? palette.Surface;
        _border.BorderBrush = FindChrome("ChromeBorder") ?? palette.SurfaceBorder;
        // The name keeps the theme's inherited foreground — it is primary text, and the family has no
        // token for that; only the secondary marks (dot, badge) step back to MutedText.
        _dot.Foreground = palette.MutedText;
        _badge.Foreground = palette.MutedText;
    }

    private IBrush? FindChrome(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
