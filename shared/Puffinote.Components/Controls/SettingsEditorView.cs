using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Controls;

/// <summary>
/// One editable option in a <see cref="SettingsEditorView"/>: a label, an optional hint, and the pair of
/// delegates that read the setting's current value and write the chosen one back. A field owns no state of
/// its own beyond the control it builds, so the settings object stays the single source of truth and nothing
/// is written until the host calls <see cref="SettingsEditorView.Commit"/> — which is what lets a dialog's
/// Cancel genuinely leave the settings untouched.
/// </summary>
public abstract class SettingsField
{
    protected SettingsField(string label, string? hint)
    {
        Label = label;
        Hint = hint;
    }

    /// <summary>The option's name, as shown beside (or on) its editor.</summary>
    public string Label { get; }

    /// <summary>A one-line explanation shown under the row, or null for a self-explanatory option.</summary>
    public string? Hint { get; }

    /// <summary>Build the editor control, seeded from the setting's current value.</summary>
    internal abstract Control CreateEditor();

    /// <summary>Write the editor's value back through the setter. A field that was never built is a no-op.</summary>
    internal abstract void Commit();

    /// <summary>
    /// Whether the row needs a separate label column. False for an editor that carries its own text (a
    /// checkbox), which reads better spanning the full width than as a bare box in the value column.
    /// </summary>
    internal virtual bool WantsLabelColumn => true;
}

/// <summary>A boolean option, rendered as a checkbox that carries its own label.</summary>
public sealed class ToggleSetting : SettingsField
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private CheckBox? _box;

    public ToggleSetting(string label, Func<bool> get, Action<bool> set, string? hint = null)
        : base(label, hint)
    {
        _get = get;
        _set = set;
    }

    internal override bool WantsLabelColumn => false;

    internal override Control CreateEditor() =>
        _box = new CheckBox { Content = Label, IsChecked = _get() };

    internal override void Commit()
    {
        if (_box is { } box)
        {
            _set(box.IsChecked == true);
        }
    }
}

/// <summary>
/// A numeric option, rendered as a spinner clamped to <paramref name="minimum"/>…<paramref name="maximum"/>.
/// Values travel as <see cref="double"/> so one field type serves both the integer settings (tab width,
/// padding) and the fractional ones (zoom, font size); <paramref name="decimals"/> picks the display format.
/// </summary>
public sealed class NumberSetting : SettingsField
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly double _step;
    private readonly int _decimals;
    private NumericUpDown? _spinner;

    public NumberSetting(
        string label,
        Func<double> get,
        Action<double> set,
        double minimum,
        double maximum,
        double step = 1,
        int decimals = 0,
        string? hint = null)
        : base(label, hint)
    {
        _get = get;
        _set = set;
        _minimum = minimum;
        _maximum = maximum;
        _step = step;
        _decimals = decimals;
    }

    internal override Control CreateEditor() => _spinner = new NumericUpDown
    {
        Minimum = (decimal)_minimum,
        Maximum = (decimal)_maximum,
        Increment = (decimal)_step,
        Value = (decimal)Math.Clamp(_get(), _minimum, _maximum),
        FormatString = _decimals <= 0 ? "0" : "0." + new string('0', _decimals),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    internal override void Commit()
    {
        // A cleared box (Value == null) means "no number entered" — leave the setting as it was rather
        // than inventing a default the user never chose.
        if (_spinner?.Value is { } value)
        {
            _set(Math.Clamp((double)value, _minimum, _maximum));
        }
    }
}

/// <summary>
/// A one-of-N option, rendered as a drop-down over <paramref name="options"/>. The getter and setter work in
/// <em>indexes</em> into that list; use <see cref="ForEnum{TEnum}"/> when the underlying setting is an enum.
/// </summary>
public sealed class ChoiceSetting : SettingsField
{
    private readonly IReadOnlyList<string> _options;
    private readonly Func<int> _get;
    private readonly Action<int> _set;
    private ComboBox? _combo;

    public ChoiceSetting(
        string label,
        IReadOnlyList<string> options,
        Func<int> get,
        Action<int> set,
        string? hint = null)
        : base(label, hint)
    {
        _options = options;
        _get = get;
        _set = set;
    }

    /// <summary>
    /// A choice over the values of <typeparamref name="TEnum"/>, displayed as <paramref name="labels"/> in
    /// declaration order. Saves every caller the clamp-and-cast dance between a selected index and its enum.
    /// </summary>
    public static ChoiceSetting ForEnum<TEnum>(
        string label,
        Func<TEnum> get,
        Action<TEnum> set,
        IReadOnlyList<string> labels,
        string? hint = null)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        return new ChoiceSetting(
            label,
            labels,
            () => Array.IndexOf(values, get()),
            index => set(values[Math.Clamp(index, 0, values.Length - 1)]),
            hint);
    }

    internal override Control CreateEditor() => _combo = new ComboBox
    {
        ItemsSource = _options,
        SelectedIndex = Math.Clamp(_get(), 0, Math.Max(_options.Count - 1, 0)),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    internal override void Commit()
    {
        if (_combo is { SelectedIndex: >= 0 } combo)
        {
            _set(combo.SelectedIndex);
        }
    }
}

/// <summary>A titled group of related options. An empty title renders the fields with no header.</summary>
public sealed record SettingsSection(string Title, IReadOnlyList<SettingsField> Fields);

/// <summary>
/// The shared settings form (each app's Preferences body): a declared list of
/// <see cref="SettingsSection"/>s rendered as labelled rows, so every app in the family configures its
/// config file through the same surface instead of hand-rolling a grid per app. The host wraps it in a
/// <c>FormDialog</c> and calls <see cref="Commit"/> only when the user accepts — until then the settings
/// object is untouched, so Cancel and Esc are genuinely free. Persisting the result, and applying it to the
/// live UI, stay the host's job: only it knows which of its settings need more than a value written.
/// </summary>
public sealed class SettingsEditorView : UserControl
{
    private readonly List<SettingsField> _fields = [];

    public SettingsEditorView(IEnumerable<SettingsSection> sections, double width = 360)
    {
        var stack = new StackPanel { Spacing = 2, Width = width };
        var first = true;

        foreach (var section in sections)
        {
            if (!string.IsNullOrEmpty(section.Title))
            {
                stack.Children.Add(SectionHeader(section.Title, first));
            }

            foreach (var field in section.Fields)
            {
                _fields.Add(field);
                stack.Children.Add(Row(field));
            }

            first = false;
        }

        Content = new ScrollViewer
        {
            MaxHeight = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
        };
    }

    /// <summary>The fields on this form, in declaration order (the seam the UI tests drive).</summary>
    public IReadOnlyList<SettingsField> Fields => _fields;

    /// <summary>Write every field's edited value back through its setter.</summary>
    public void Commit()
    {
        foreach (var field in _fields)
        {
            field.Commit();
        }
    }

    private static Control SectionHeader(string text, bool first) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, first ? 0 : 14, 0, 2),
        FontWeight = FontWeight.SemiBold,
        Foreground = Tokens.Active.MutedText,
    };

    private static Control Row(SettingsField field)
    {
        var editor = field.CreateEditor();
        editor.Margin = new Thickness(0, 6, 0, 0);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (field.WantsLabelColumn)
        {
            // No TextWrapping here: the label sits in an Auto column, which measures against infinite
            // width, so a wrapping TextBlock inside it never settles on a size — it re-measures forever
            // and hangs layout. Labels are short by design; the Hint below is where prose goes, and it
            // wraps safely because it spans a fixed-width row.
            var label = new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 12, 0),
                MinWidth = 96,
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            Grid.SetColumn(editor, 1);
        }
        else
        {
            Grid.SetColumn(editor, 0);
            Grid.SetColumnSpan(editor, 2);
        }

        grid.Children.Add(editor);

        if (field.Hint is not { Length: > 0 } hint)
        {
            return grid;
        }

        return new StackPanel
        {
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    Foreground = Tokens.Active.MutedText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                },
            },
        };
    }
}
