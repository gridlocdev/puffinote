using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Puffinote.Components.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// Data Validation dialog (PRD §8.13), rendered as an internal modal: a list rule, or a
/// number/date/text-length constraint with an operator and operands, plus the error response. Its
/// <see cref="ModalDialog{T}.Result"/> is a <see cref="DataValidation"/> or null on cancel.
/// </summary>
public sealed class DataValidationDialog : ModalDialog<DataValidation?>
{
    private readonly CellRange _range;
    private readonly ComboBox _kind;
    private readonly ComboBox _operator;
    private readonly TextBox _operand1 = new() { Width = 120 };
    private readonly TextBox _operand2 = new() { Width = 120 };
    private readonly TextBox _listValues = new() { Width = 250, PlaceholderText = "Red, Green, Blue" };
    private readonly TextBox _listSource = new() { Width = 250, PlaceholderText = "e.g. $E$1:$E$8 or Lists.A1:A20" };
    private readonly ComboBox _response;
    private readonly TextBox _errorMessage = new() { Width = 250 };
    private readonly StackPanel _numberPanel;
    private readonly StackPanel _listPanel;

    public DataValidationDialog(CellRange range)
    {
        _range = range;
        Title = "Data Validation";

        _kind = new ComboBox { Width = 200, ItemsSource = new[] { "Any value", "List", "Whole number", "Decimal", "Date", "Text length" }, SelectedIndex = 1 };
        _operator = new ComboBox { Width = 160, ItemsSource = new[] { "between", "not between", "equal to", "not equal to", "greater than", "less than", "greater or equal", "less or equal" }, SelectedIndex = 0 };
        _response = new ComboBox { Width = 160, ItemsSource = new[] { "Stop", "Warning", "Information" }, SelectedIndex = 0 };

        _numberPanel = new StackPanel { Spacing = 6, Children =
        {
            Row("Condition", _operator),
            Row("Value", _operand1),
            Row("And", _operand2),
        } };
        _listPanel = new StackPanel { Spacing = 6, Children =
        {
            Row("Values", _listValues),
            Row("Cell range", _listSource),
            new TextBlock
            {
                Text = "Enter a comma-separated list, or a cell range to draw allowed values from. A range wins when both are filled.",
                Width = 330,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(88, 0, 0, 0),
            },
        } };

        _kind.SelectionChanged += (_, _) => UpdateVisibility();
        // The "And" operand only applies to between/not-between, so its enabled state depends on the
        // operator too — without this the second box stayed greyed when switching back to "between".
        _operator.SelectionChanged += (_, _) => UpdateVisibility();

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Close(Build());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Width = 340,
            Spacing = 10,
            Children =
            {
                Row("Allow", _kind),
                _listPanel,
                _numberPanel,
                Row("On invalid", _response),
                Row("Error text", _errorMessage),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        UpdateVisibility();
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center }, control },
    };

    /// <summary>Test seam: drive the controls and build a rule the way clicking OK would.</summary>
    internal DataValidation? BuildForTest(ValidationKind kind, ValidationOperator op, string operand1, string operand2 = "")
    {
        _kind.SelectedIndex = (int)kind;
        _operator.SelectedIndex = (int)op;
        _operand1.Text = operand1;
        _operand2.Text = operand2;
        return Build();
    }

    /// <summary>Test seam: drive the list controls and build a List rule the way clicking OK would.</summary>
    internal DataValidation? BuildListForTest(string values, string source = "")
    {
        _kind.SelectedIndex = (int)ValidationKind.List;
        _listValues.Text = values;
        _listSource.Text = source;
        return Build();
    }

    /// <summary>Test seam: whether the second ("And") operand box is enabled for the given operator.</summary>
    internal bool Operand2EnabledForTest(ValidationOperator op)
    {
        _operator.SelectedIndex = (int)op;
        return _operand2.IsEnabled;
    }

    private void UpdateVisibility()
    {
        var kind = (ValidationKind)_kind.SelectedIndex;
        _listPanel.IsVisible = kind == ValidationKind.List;
        _numberPanel.IsVisible = kind is ValidationKind.WholeNumber or ValidationKind.Decimal or ValidationKind.Date or ValidationKind.TextLength;
        bool between = _operator.SelectedIndex is 0 or 1;
        _operand2.IsEnabled = between;
    }

    private DataValidation? Build()
    {
        var kind = (ValidationKind)_kind.SelectedIndex;
        var rule = new DataValidation(kind, _range)
        {
            Operator = (ValidationOperator)_operator.SelectedIndex,
            Response = (ValidationResponse)_response.SelectedIndex,
            ErrorMessage = string.IsNullOrWhiteSpace(_errorMessage.Text) ? null : _errorMessage.Text,
        };

        if (kind == ValidationKind.List)
        {
            // A cell-range source takes precedence over a literal list when both are supplied.
            if (CellRange.TryParse(_listSource.Text?.Trim(), out var source))
            {
                rule = rule with { ListSource = source, ListValues = null };
            }
            else
            {
                rule = rule with
                {
                    ListValues = (_listValues.Text ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                };
            }
        }
        else if (kind != ValidationKind.Any)
        {
            rule = rule with
            {
                Operand1 = ParseOperand(_operand1.Text, kind),
                Operand2 = ParseOperand(_operand2.Text, kind),
            };
        }

        return rule;
    }

    /// <summary>
    /// Parse an operand to its stored numeric value. For a Date rule the constraint is a date serial,
    /// so a typed date (e.g. <c>2026-01-01</c>) is coerced via the same date parser the cell editor
    /// uses — users shouldn't have to hand-compute a serial. Plain numbers still parse directly.
    /// </summary>
    private static double ParseOperand(string? text, ValidationKind kind)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double number))
        {
            return number;
        }

        if (kind == ValidationKind.Date && !string.IsNullOrWhiteSpace(text))
        {
            var parsed = InputParser.Parse(text, CultureInfo.CurrentCulture);
            if (parsed.Value.IsNumber)
            {
                return parsed.Value.AsNumber;
            }
        }

        return 0;
    }
}
