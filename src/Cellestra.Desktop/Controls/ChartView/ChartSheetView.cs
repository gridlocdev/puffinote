using System;
using System.Collections.Generic;
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
/// labelled controls on the right defines the data and presentation. Every field carries a title and
/// a hover description (tooltip), and editing any field updates the chart immediately. The view reads
/// and writes the sheet's <see cref="ChartDefinition"/> in place and resolves its ranges against the
/// workbook through <see cref="ChartDataResolver"/>, so it also re-renders when the source data changes.
/// </summary>
public sealed class ChartSheetView : UserControl
{
    private readonly Workbook _workbook;
    private readonly ChartDefinition _chart;
    private readonly ChartControl _control = new();
    private readonly StackPanel _seriesPanel = new() { Spacing = 8 };
    private bool _loading;

    /// <summary>Raised when the user edits a field (so the host can mark the document modified).</summary>
    public event EventHandler? ChartChanged;

    private readonly (ChartType Type, string Label)[] _types =
    {
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
    };

    private ComboBox _stacking = null!;

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
            Child = _control,
        };
        chartBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ChromeBorder"));
        Grid.SetColumn(chartBorder, 0);
        grid.Children.Add(chartBorder);

        var panel = BuildControlsPanel();
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);

        Content = grid;

        LoadFromModel();
        RenderChart();
    }

    /// <summary>Re-resolve the chart against the workbook and repaint (e.g. after the source data changed).</summary>
    public void RefreshData() => RenderChart();

    /// <summary>The chart control, exposed for headless tests.</summary>
    internal ChartControl ChartControl => _control;

    /// <summary>The most recently resolved chart data, exposed for headless tests.</summary>
    internal ResolvedChart? ResolvedForTest { get; private set; }

    private Control BuildControlsPanel()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(12), Width = 320 };

        stack.Children.Add(new TextBlock
        {
            Text = "Chart data",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        });

        var title = new TextBox();
        title.TextChanged += (_, _) => Edit(() => _chart.Title = title.Text ?? string.Empty);
        stack.Children.Add(Field("Title", "The heading shown above the chart. Leave blank for no title.", title));
        _title = title;

        var type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, label) in _types)
        {
            type.Items.Add(label);
        }

        type.SelectionChanged += (_, _) => Edit(() =>
        {
            if (type.SelectedIndex >= 0)
            {
                _chart.Type = _types[type.SelectedIndex].Type;
                UpdateStackingEnabled();
            }
        });
        stack.Children.Add(Field("Chart type", "How the data is drawn. Column/Bar are the same data shown vertically or horizontally; Pie/Doughnut show one series as proportions; Scatter/Bubble plot X vs Y; Radar plots each category on its own spoke.", type));
        _type = type;

        _stacking = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _stacking.Items.Add("None");
        _stacking.Items.Add("Stacked");
        _stacking.Items.Add("100% stacked");
        _stacking.SelectionChanged += (_, _) => Edit(() =>
        {
            _chart.Stacking = _stacking.SelectedIndex switch
            {
                1 => ChartStacking.Stacked,
                2 => ChartStacking.Percent,
                _ => ChartStacking.None,
            };
        });
        stack.Children.Add(Field("Stacking", "For column, bar, and area charts: stack series on top of one another, or scale each category to 100%. Ignored by other chart types.", _stacking));

        var categories = new TextBox { PlaceholderText = "e.g. Sheet1.A2:A7" };
        categories.TextChanged += (_, _) => Edit(() => _chart.CategoriesReference = Blank(categories.Text));
        stack.Children.Add(Field("Category labels (X)", "The cells holding the label for each data point — one per row or column, e.g. Sheet1.A2:A7. These name the columns/bars/points and the radar spokes.", categories));
        _categories = categories;

        var xTitle = new TextBox();
        xTitle.TextChanged += (_, _) => Edit(() => _chart.XAxisTitle = Blank(xTitle.Text));
        stack.Children.Add(Field("X-axis title", "Optional label drawn beneath the horizontal axis.", xTitle));
        _xTitle = xTitle;

        var yTitle = new TextBox();
        yTitle.TextChanged += (_, _) => Edit(() => _chart.YAxisTitle = Blank(yTitle.Text));
        stack.Children.Add(Field("Y-axis title", "Optional label for the value axis.", yTitle));
        _yTitle = yTitle;

        var legend = new CheckBox { Content = "Show legend" };
        legend.IsCheckedChanged += (_, _) => Edit(() => _chart.ShowLegend = legend.IsChecked == true);
        ToolTip.SetTip(legend, "Show a key naming each series. Hidden when there is only one series you don't need to distinguish.");
        stack.Children.Add(legend);
        _legend = legend;

        stack.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        stack.Children.Add(new TextBlock { Text = "Series", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = "Each series is one set of values plotted across the categories.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 11,
        });
        stack.Children.Add(_seriesPanel);

        var addSeries = new Button { Content = "＋ Add series", HorizontalAlignment = HorizontalAlignment.Stretch };
        addSeries.Click += (_, _) =>
        {
            _chart.Series.Add(new ChartSeries());
            RebuildSeriesRows();
            OnEdited();
        };
        ToolTip.SetTip(addSeries, "Add another set of values to plot (a second column, line, etc.). Give each its own values range.");
        stack.Children.Add(addSeries);

        return new ScrollViewer
        {
            Width = 344,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = stack,
        };
    }

    private TextBox _title = null!;
    private ComboBox _type = null!;
    private TextBox _categories = null!;
    private TextBox _xTitle = null!;
    private TextBox _yTitle = null!;
    private CheckBox _legend = null!;

    private void LoadFromModel()
    {
        _loading = true;
        try
        {
            _title.Text = _chart.Title;
            _type.SelectedIndex = Array.FindIndex(_types, t => t.Type == _chart.Type);
            _stacking.SelectedIndex = _chart.Stacking switch
            {
                ChartStacking.Stacked => 1,
                ChartStacking.Percent => 2,
                _ => 0,
            };
            _categories.Text = _chart.CategoriesReference ?? string.Empty;
            _xTitle.Text = _chart.XAxisTitle ?? string.Empty;
            _yTitle.Text = _chart.YAxisTitle ?? string.Empty;
            _legend.IsChecked = _chart.ShowLegend;
            UpdateStackingEnabled();
            RebuildSeriesRows();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateStackingEnabled() => _stacking.IsEnabled = ChartTypes.SupportsStacking(_chart.Type);

    private void RebuildSeriesRows()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _seriesPanel.Children.Clear();
            for (int i = 0; i < _chart.Series.Count; i++)
            {
                _seriesPanel.Children.Add(BuildSeriesRow(_chart.Series[i], i + 1));
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private Control BuildSeriesRow(ChartSeries series, int ordinal)
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
            RebuildSeriesRows();
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

        var values = new TextBox { Text = series.ValuesReference, PlaceholderText = "e.g. Sheet1.B2:B7" };
        values.TextChanged += (_, _) => Edit(() => series.ValuesReference = values.Text ?? string.Empty);
        box.Children.Add(Field("Values", "The cells holding this series' numbers — one per category, e.g. Sheet1.B2:B7. This is the data that gets plotted.", values));

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

        var xValues = new TextBox { Text = series.XValuesReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.A2:A7" };
        xValues.TextChanged += (_, _) => Edit(() => series.XValuesReference = Blank(xValues.Text));
        box.Children.Add(Field("X values", "Only for Scatter and Bubble charts: the horizontal position of each point. Defaults to 1, 2, 3… when blank.", xValues));

        var bubble = new TextBox { Text = series.BubbleSizeReference ?? string.Empty, PlaceholderText = "e.g. Sheet1.C2:C7" };
        bubble.TextChanged += (_, _) => Edit(() => series.BubbleSizeReference = Blank(bubble.Text));
        box.Children.Add(Field("Bubble size", "Only for Bubble charts: the value that sizes each bubble.", bubble));

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
        _control.Show(resolved);
    }
}
