using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Desktop.Views.Overlays;

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
        _listPanel = new StackPanel { Spacing = 6, Children = { Row("Values", _listValues) } };

        _kind.SelectionChanged += (_, _) => UpdateVisibility();

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
            rule = rule with
            {
                ListValues = (_listValues.Text ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            };
        }
        else if (kind != ValidationKind.Any)
        {
            double.TryParse(_operand1.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double o1);
            double.TryParse(_operand2.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double o2);
            rule = rule with { Operand1 = o1, Operand2 = o2 };
        }

        return rule;
    }
}
