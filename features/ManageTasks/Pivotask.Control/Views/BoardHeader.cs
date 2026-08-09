using Avalonia;
using Avalonia.Controls;

namespace Pivotask.Control.Views;

/// <summary>
/// The strip above the board: the search box, the four filters, and the view switch.
///
/// <para>It has two arrangements, and picks between them from the room it is actually given rather than
/// from a breakpoint someone guessed. <b>Wide</b> is the prototype's one row — search on the left, the
/// filters pushed right, the switch at the far end. <b>Narrow</b> is what the prototype's own media query
/// does: the switch drops its words down to its icons, and the search moves to a full-width row of its
/// own underneath. Before this the three simply fought over one row and the losers were clipped.</para>
///
/// <para>Search is the one that moves, and it is the right choice: it is the only control here whose
/// usefulness scales with its width, and the only one whose meaning survives losing its label — a
/// magnifier and a placeholder say "search" without help, where four filter pills reduced to icons would
/// be four guesses.</para>
/// </summary>
internal sealed class BoardHeader : Panel
{
    /// <summary>What the search box gets on one row when there is room for everything.</summary>
    private const double SearchWide = 260;

    /// <summary>Below this the search box has stopped being a search box, and the strip wraps instead.</summary>
    private const double SearchMin = 170;

    private const double Gap = 12;
    private const double RowGap = 8;

    private readonly Avalonia.Controls.Control _search;
    private readonly Avalonia.Controls.Control _filters;
    private readonly Avalonia.Controls.Control _switcher;
    private readonly Action<bool> _setCompact;

    /// <summary>
    /// What the switch costs with its words on. Remembered from the last wide pass and used as the
    /// threshold even while narrow, so the strip only unwraps when the full-size switch would fit — one
    /// number for both directions is what stops a resize oscillating on the boundary.
    /// </summary>
    private double _fullSwitchWidth = double.NaN;

    public BoardHeader(
        Avalonia.Controls.Control search,
        Avalonia.Controls.Control filters,
        Avalonia.Controls.Control switcher,
        Action<bool> setCompact)
    {
        _search = search;
        _filters = filters;
        _switcher = switcher;
        _setCompact = setCompact;

        Children.Add(search);
        Children.Add(filters);
        Children.Add(switcher);
    }

    /// <summary>Whether the strip is in its wrapped form. Exposed so a test can assert the arrangement.</summary>
    public bool IsCompact { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        _filters.Measure(Size.Infinity);
        _switcher.Measure(Size.Infinity);

        if (!IsCompact)
        {
            _fullSwitchWidth = _switcher.DesiredSize.Width;
        }

        double switchWide = double.IsNaN(_fullSwitchWidth) ? _switcher.DesiredSize.Width : _fullSwitchWidth;
        double needed = SearchMin + Gap + _filters.DesiredSize.Width + Gap + switchWide;
        bool narrow = double.IsFinite(availableSize.Width) && availableSize.Width < needed;

        if (narrow != IsCompact)
        {
            // Only on a change, so a settled layout never touches the switch and never re-invalidates.
            IsCompact = narrow;
            _setCompact(narrow);
            _filters.Measure(Size.Infinity);
            _switcher.Measure(Size.Infinity);
        }

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : needed;

        if (!IsCompact)
        {
            _search.Measure(new Size(SearchWide, availableSize.Height));
            double row = Max(_search.DesiredSize.Height, _filters.DesiredSize.Height, _switcher.DesiredSize.Height);
            return new Size(width, row);
        }

        _search.Measure(new Size(width, availableSize.Height));
        double top = Math.Max(_filters.DesiredSize.Height, _switcher.DesiredSize.Height);
        return new Size(width, top + RowGap + _search.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double switchWidth = _switcher.DesiredSize.Width;
        double filtersWidth = _filters.DesiredSize.Width;

        if (!IsCompact)
        {
            double row = finalSize.Height;
            double searchWidth = Math.Min(SearchWide, Math.Max(0, finalSize.Width - switchWidth - filtersWidth - (Gap * 2)));

            _search.Arrange(Centred(0, searchWidth, _search.DesiredSize.Height, row));
            _switcher.Arrange(Centred(finalSize.Width - switchWidth, switchWidth, _switcher.DesiredSize.Height, row));
            _filters.Arrange(Centred(
                finalSize.Width - switchWidth - Gap - filtersWidth, filtersWidth, _filters.DesiredSize.Height, row));
            return finalSize;
        }

        // Narrow: the filters and the switch share the top row, the search takes the whole of the next.
        double top = Math.Max(_filters.DesiredSize.Height, _switcher.DesiredSize.Height);
        _filters.Arrange(Centred(0, filtersWidth, _filters.DesiredSize.Height, top));
        _switcher.Arrange(Centred(finalSize.Width - switchWidth, switchWidth, _switcher.DesiredSize.Height, top));
        _search.Arrange(new Rect(0, top + RowGap, finalSize.Width, _search.DesiredSize.Height));
        return finalSize;
    }

    /// <summary>A slot of <paramref name="height"/>, with a <paramref name="own"/>-tall child centred in it.</summary>
    private static Rect Centred(double x, double width, double own, double height) =>
        new(x, (height - own) / 2, width, own);

    private static double Max(double a, double b, double c) => Math.Max(a, Math.Max(b, c));
}
