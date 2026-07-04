using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Charts;
using Cellestra.Core.Charts;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.ChartView;

/// <summary>
/// The chart sheet's two-pane editor: the chart fills the left (most of the window), and a panel of
/// labelled controls on the right defines the data and presentation. Title and chart type are pinned
/// at the top; everything below adapts to the chosen type's <see cref="ChartFamily"/> so each chart
/// only asks for the data it actually plots (e.g. a pie asks for slice labels and one set of values;
/// a scatter asks for X and Y values, not categories). Editing any field updates the chart immediately.
/// The view reads and writes the sheet's <see cref="ChartDefinition"/> in place and resolves its ranges
/// against the workbook through <see cref="ChartDataResolver"/>, so it also re-renders when source data
/// changes.
/// </summary>
public sealed class ChartSheetView : UserControl
{
    private readonly Workbook _workbook;
    private readonly ChartDefinition _chart;

    /// <summary>The pinned controls below Title/Type, rebuilt whenever the chart type changes.</summary>
    private readonly StackPanel _typeControls = new() { Spacing = 10 };

    /// <summary>The per-series rows; reused across rebuilds (not used by the single-series pie/doughnut).</summary>
    private readonly StackPanel _seriesPanel = new() { Spacing = 8 };

    private bool _loading;

    /// <summary>Raised when the user edits a field (so the host can mark the document modified).</summary>
    public event EventHandler? ChartChanged;

    /// <summary>The data shape a chart type needs; drives which fields the panel shows.</summary>
    private enum ChartFamily
    {
        /// <summary>Column/Bar/Line/Area: categories down one axis, one or more value series, optional combo.</summary>
        Cartesian,

        /// <summary>Radar/FilledRadar: one value per spoke (category), one or more series.</summary>
        Radar,

        /// <summary>Pie/Doughnut: a single series shown as proportions of a whole.</summary>
        PartToWhole,

        /// <summary>Scatter/Bubble: each series carries its own X and Y values (and bubble sizes).</summary>
        Xy,
    }

    private readonly (ChartType Type, string Label)[] _types =
    [
        (ChartType.Column, "Column"),
        (ChartType.Bar, "Bar"),
        (ChartType.Line, "Line"),
        (ChartType.Area, "Area"),
        (ChartType.Pie, "Pie"),
        (ChartType.Doughnut, "Doughnut"),
        (ChartType.Scatter, "Scatter"),
        (ChartType.Bubble, "Bubble"),
        (ChartType.Radar, "Radar"),
        (ChartType.FilledRadar, "Filled Radar"),
    ];

    private static ChartFamily FamilyOf(ChartType type) => type switch
    {
        ChartType.Pie or ChartType.Doughnut => ChartFamily.PartToWhole,
        ChartType.Scatter or ChartType.Bubble => ChartFamily.Xy,
        ChartType.Radar or ChartType.FilledRadar => ChartFamily.Radar,
        _ => ChartFamily.Cartesian,
    };

    public ChartSheetView(Workbook workbook, ChartDefinition chart)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var chartBorder = new Border
        {
            Margin = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = ChartControl,
        };
        chartBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));
        Grid.SetColumn(chartBorder, 0);
        grid.Children.Add(chartBorder);

        _loading = true;
        var panel = BuildControlsPanel();
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);

        Content = grid;

        RebuildTypeControls();
        _loading = false;
        RenderChart();
    }

    /// <summary>Re-resolve the chart against the workbook and repaint (e.g. after the source data changed).</summary>
    public void RefreshData() => RenderChart();

    /// <summary>The chart control, exposed for headless tests.</summary>
    internal ChartControl ChartControl { get; } = new();

    /// <summary>The most recently resolved chart data, exposed for headless tests.</summary>
    internal ResolvedChart? ResolvedForTest { get; private set; }

    /// <summary>The field and section labels currently shown in the adaptive panel, for headless tests.</summary>
    internal System.Collections.Generic.IReadOnlyList<string> VisibleFieldsForTest()
    {
        var labels = new System.Collections.Generic.List<string>();
        Collect(_typeControls);
        return labels;

        void Collect(Control control)
        {
            switch (control)
            {
                case TextBlock text when !string.IsNullOrEmpty(text.Text):
                    labels.Add(text.Text!);
                    break;
                case CheckBox check when check.Content is string content:
                    labels.Add(content);
                    break;
                case Panel panel:
                    foreach (var child in panel.Children)
                    {
                        Collect(child);
                    }

                    break;
                case Decorator decorator when decorator.Child is { } child:
                    Collect(child);
                    break;
                case ContentControl host when host.Content is Control hosted:
                    Collect(hosted);
                    break;
            }
        }
    }

    /// <summary>Switch the chart type as if the user picked it, for headless tests.</summary>
    internal void SetTypeForTest(ChartType type)
    {
        _chart.Type = type;
        RebuildTypeControls();
        OnEdited();
    }

    /// <summary>Builds the pinned header (Title + Chart type) and hosts the adaptive controls below it.</summary>
    private Control BuildControlsPanel()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(12), Width = 320 };

        stack.Children.Add(new TextBlock
        {
            Text = "Chart data",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        });

        var title = new TextBox { Text = _chart.Title };
        title.TextChanged += (_, _) => Edit(() => _chart.Title = title.Text ?? string.Empty);
        stack.Children.Add(Field("Title", "The heading shown above the chart. Leave blank for no title.", title));

        var type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, label) in _types)
        {
            type.Items.Add(label);
        }

        type.SelectedIndex = Array.FindIndex(_types, t => t.Type == _chart.Type);
        type.SelectionChanged += (_, _) => Edit(() =>
        {
            if (type.SelectedIndex >= 0)
            {
                _chart.Type = _types[type.SelectedIndex].Type;
                RebuildTypeControls();
            }
        });
        stack.Children.Add(Field(
            "Chart type",
            "How the data is drawn. Column/Bar are the same data shown vertically or horizontally; " +
            "Line/Area trace it across categories; Pie/Doughnut show one series as proportions; " +
            "Scatter/Bubble plot X vs Y; Radar plots each category on its own spoke. " +
            "The fields below change to match the type you pick.",
            type));

        stack.Children.Add(_typeControls);

        return new ScrollViewer
        {
            Width = 344,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = stack,
        };
    }

    /// <summary>Rebuild the adaptive controls so only the fields the current chart type needs are shown.</summary>
    private void RebuildTypeControls()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _typeControls.Children.Clear();

            var type = _chart.Type;
            var family = FamilyOf(type);

            // Stacking — only column/bar/area honour it, so only they offer it.
            if (ChartTypes.SupportsStacking(type))
            {
                _typeControls.Children.Add(Field(
                    "Stacking",
                    "Stack each category's series on top of one another, or scale every category to 100%.",
                    BuildStacking()));
            }

            // Category / spoke / slice labels — every family except X/Y, with family-correct wording.
            if (family != ChartFamily.Xy)
            {
                var (label, tip) = family switch
                {
                    ChartFamily.PartToWhole => (
                        "Slice labels",
                        "The cells naming each slice — one per value, e.g. Sheet1.A2:A7. Shown in the legend."),
                    ChartFamily.Radar => (
                        "Spoke labels",
                        "The cells naming each spoke of the radar — one per value, e.g. Sheet1.A2:A7."),
                    _ => (
                        "Category labels",
                        "The cells holding the label for each data point — one per row, e.g. Sheet1.A2:A7. " +
                        "These name the columns/bars/points along the category axis."),
                };

                var categories = new TextBox { Text = _chart.CategoriesReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.A2:A7" };
                categories.TextChanged += (_, _) => Edit(() => _chart.CategoriesReference = Blank(categories.Text));
                _typeControls.Children.Add(Field(label, tip, categories));
            }

            // Axis titles — only the families whose renderer draws axes (cartesian and X/Y).
            if (family is ChartFamily.Cartesian or ChartFamily.Xy)
            {
                var xTitle = new TextBox { Text = _chart.XAxisTitle ?? string.Empty };
                xTitle.TextChanged += (_, _) => Edit(() => _chart.XAxisTitle = Blank(xTitle.Text));
                _typeControls.Children.Add(Field("X-axis title", "Optional label drawn beneath the horizontal axis.", xTitle));

                var yTitle = new TextBox { Text = _chart.YAxisTitle ?? string.Empty };
                yTitle.TextChanged += (_, _) => Edit(() => _chart.YAxisTitle = Blank(yTitle.Text));
                _typeControls.Children.Add(Field("Y-axis title", "Optional label for the value axis.", yTitle));
            }

            // Legend — every family can show one.
            var legend = new CheckBox { Content = "Show legend", IsChecked = _chart.ShowLegend };
            legend.IsCheckedChanged += (_, _) => Edit(() => _chart.ShowLegend = legend.IsChecked == true);
            ToolTip.SetTip(legend, family == ChartFamily.PartToWhole
                ? "Show a key naming each slice."
                : "Show a key naming each series. Hide it when a single series needs no label.");
            _typeControls.Children.Add(legend);

            _typeControls.Children.Add(new Separator { Margin = new Thickness(0, 4) });

            if (family == ChartFamily.PartToWhole)
            {
                BuildSingleSeriesControls();
            }
            else
            {
                BuildSeriesListControls(family);
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private ComboBox BuildStacking()
    {
        var stacking = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        stacking.Items.Add("None");
        stacking.Items.Add("Stacked");
        stacking.Items.Add("100% stacked");
        stacking.SelectedIndex = _chart.Stacking switch
        {
            ChartStacking.Stacked => 1,
            ChartStacking.Percent => 2,
            _ => 0,
        };
        stacking.SelectionChanged += (_, _) => Edit(() =>
        {
            _chart.Stacking = stacking.SelectedIndex switch
            {
                1 => ChartStacking.Stacked,
                2 => ChartStacking.Percent,
                _ => ChartStacking.None,
            };
        });
        return stacking;
    }

    /// <summary>Pie/Doughnut: a single series shown as one "Slice values" range — no series list.</summary>
    private void BuildSingleSeriesControls()
    {
        // A part-to-whole chart only ever draws its first series, so present exactly one values field.
        if (_chart.Series.Count == 0)
        {
            _chart.Series.Add(new ChartSeries());
        }

        var series = _chart.Series[0];

        _typeControls.Children.Add(new TextBlock { Text = "Values", FontWeight = FontWeight.SemiBold });
        _typeControls.Children.Add(new TextBlock
        {
            Text = "A pie or doughnut shows one set of values as proportions of a whole.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 11,
        });

        var values = new TextBox { Text = series.ValuesReference, PlaceholderText = "e.g. Sheet1.B2:B7" };
        values.TextChanged += (_, _) => Edit(() => series.ValuesReference = values.Text ?? string.Empty);
        _typeControls.Children.Add(Field(
            "Slice values",
            "The cells holding the value for each slice — one per slice label, e.g. Sheet1.B2:B7.",
            values));
    }

    /// <summary>Cartesian/Radar/X-Y: a list of series with an "Add series" button.</summary>
    private void BuildSeriesListControls(ChartFamily family)
    {
        _typeControls.Children.Add(new TextBlock { Text = "Series", FontWeight = FontWeight.SemiBold });
        _typeControls.Children.Add(new TextBlock
        {
            Text = family switch
            {
                ChartFamily.Xy => "Each series is a set of points. Give each its own X and Y values.",
                ChartFamily.Radar => "Each series is one outline plotted across the spokes.",
                _ => "Each series is one set of values plotted across the categories.",
            },
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 11,
        });

        RebuildSeriesRows(family);
        _typeControls.Children.Add(_seriesPanel);

        var addSeries = new Button { Content = "＋ Add series", HorizontalAlignment = HorizontalAlignment.Stretch };
        addSeries.Click += (_, _) =>
        {
            _chart.Series.Add(new ChartSeries());
            RebuildSeriesRows(family);
            OnEdited();
        };
        ToolTip.SetTip(addSeries, family == ChartFamily.Xy
            ? "Add another set of points to plot. Give each its own X and Y values."
            : "Add another set of values to plot (a second column, line, etc.). Give each its own values range.");
        _typeControls.Children.Add(addSeries);
    }

    private void RebuildSeriesRows(ChartFamily family)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _seriesPanel.Children.Clear();
            for (int i = 0; i < _chart.Series.Count; i++)
            {
                _seriesPanel.Children.Add(BuildSeriesRow(_chart.Series[i], i + 1, family));
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private Control BuildSeriesRow(ChartSeries series, int ordinal, ChartFamily family)
    {
        var box = new StackPanel { Spacing = 4 };
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = box,
        };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));

        var header = new DockPanel();
        header.Children.Add(new TextBlock { Text = $"Series {ordinal}", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), [DockPanel.DockProperty] = Dock.Right };
        remove.Click += (_, _) =>
        {
            _chart.Series.Remove(series);
            RebuildSeriesRows(family);
            OnEdited();
        };
        ToolTip.SetTip(remove, "Remove this series from the chart.");
        header.Children.Add(remove);
        box.Children.Add(header);

        var name = new TextBox { Text = series.Name ?? string.Empty, PlaceholderText = "Series name (optional)" };
        name.TextChanged += (_, _) => Edit(() => series.Name = Blank(name.Text));
        box.Children.Add(Field("Name", "A label for this series. Leave blank to use the name cell below, or an automatic \"Series N\".", name));

        var nameRef = new TextBox { Text = series.NameReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.B1" };
        nameRef.TextChanged += (_, _) => Edit(() => series.NameReference = Blank(nameRef.Text));
        box.Children.Add(Field("Name cell", "A single cell whose text names the series (e.g. the column header Sheet1.B1). Overrides the typed name.", nameRef));

        // X/Y charts need an X range per series; categories don't apply to them.
        if (family == ChartFamily.Xy)
        {
            var xValues = new TextBox { Text = series.XValuesReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.A2:A7" };
            xValues.TextChanged += (_, _) => Edit(() => series.XValuesReference = Blank(xValues.Text));
            box.Children.Add(Field("X values", "The horizontal position of each point, e.g. Sheet1.A2:A7. Defaults to 1, 2, 3… when blank.", xValues));
        }

        string valuesTitle = family == ChartFamily.Xy ? "Y values" : "Values";
        string valuesTip = family == ChartFamily.Xy
            ? "The vertical position of each point — one per X value, e.g. Sheet1.B2:B7."
            : "The cells holding this series' numbers — one per category, e.g. Sheet1.B2:B7. This is the data that gets plotted.";
        var values = new TextBox { Text = series.ValuesReference, PlaceholderText = "e.g. Sheet1.B2:B7" };
        values.TextChanged += (_, _) => Edit(() => series.ValuesReference = values.Text ?? string.Empty);
        box.Children.Add(Field(valuesTitle, valuesTip, values));

        // Combo charts: only the cartesian family lets a series override the chart type.
        if (family == ChartFamily.Cartesian)
        {
            var seriesType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            seriesType.Items.Add("Same as chart");
            foreach (var (_, label) in _types)
            {
                seriesType.Items.Add(label);
            }

            seriesType.SelectedIndex = series.Type is { } t ? Array.FindIndex(_types, x => x.Type == t) + 1 : 0;
            seriesType.SelectionChanged += (_, _) => Edit(() =>
                series.Type = seriesType.SelectedIndex <= 0 ? null : _types[seriesType.SelectedIndex - 1].Type);
            box.Children.Add(Field("Draw as", "Override the chart type for just this series — e.g. draw one series as a Line over Column bars to make a combo chart.", seriesType));
        }

        // Bubble charts size each point by a third value.
        if (_chart.Type == ChartType.Bubble)
        {
            var bubble = new TextBox { Text = series.BubbleSizeReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.C2:C7" };
            bubble.TextChanged += (_, _) => Edit(() => series.BubbleSizeReference = Blank(bubble.Text));
            box.Children.Add(Field("Bubble size", "The value that sizes each bubble — one per point, e.g. Sheet1.C2:C7.", bubble));
        }

        return border;
    }

    /// <summary>A titled field row with a hover description on both the title and the control.</summary>
    private static Control Field(string title, string description, Control control)
    {
        var label = new TextBlock { Text = title, FontSize = 11, Foreground = Brushes.Gray };
        ToolTip.SetTip(label, description);
        ToolTip.SetTip(control, description);
        return new StackPanel { Spacing = 2, Children = { label, control } };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

    /// <summary>Apply a model mutation from a control handler, unless we're loading values programmatically.</summary>
    private void Edit(Action mutate)
    {
        if (_loading)
        {
            return;
        }

        mutate();
        OnEdited();
    }

    private void OnEdited()
    {
        RenderChart();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderChart()
    {
        var resolved = ChartDataResolver.Resolve(_workbook, _chart);
        ResolvedForTest = resolved;
        ChartControl.Show(resolved);
    }
}
