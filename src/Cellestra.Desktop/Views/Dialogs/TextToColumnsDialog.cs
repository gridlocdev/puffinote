using Avalonia.Controls;
using Avalonia.Layout;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>Text to Columns dialog (PRD §8.14): pick the delimiter to split the selected column on.</summary>
public sealed class TextToColumnsDialog : Window
{
    private readonly RadioButton _comma;
    private readonly RadioButton _semicolon;
    private readonly RadioButton _tab;
    private readonly RadioButton _space;

    public TextToColumnsDialog()
    {
        Title = "Text to Columns";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _comma = new RadioButton { Content = "Comma", GroupName = "delim", IsChecked = true };
        _semicolon = new RadioButton { Content = "Semicolon", GroupName = "delim" };
        _tab = new RadioButton { Content = "Tab", GroupName = "delim" };
        _space = new RadioButton { Content = "Space", GroupName = "delim" };

        var ok = new Button { Content = "Split", IsDefault = true };
        ok.Click += (_, _) => Close((char?)Delimiter());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Split the selected column on:" },
                _comma, _semicolon, _tab, _space,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
    }

    private char Delimiter()
    {
        if (_semicolon.IsChecked == true) return ';';
        if (_tab.IsChecked == true) return '\t';
        if (_space.IsChecked == true) return ' ';
        return ',';
    }
}
