using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// A keyboard-navigable list for the sidebar panels (Recent, Outline). Like the file tree, keyboard focus
/// and the key map live on the UserControl (Up/Down/Home/End move the selection, Enter activates) while the
/// inner <see cref="ListBox"/> is pointer-only (<c>Focusable=false</c>) — so navigation is deterministic and
/// doesn't depend on which item container holds focus (which is also what makes it drivable under headless
/// tests). Shows <see cref="EmptyText"/> when there are no items.
/// </summary>
internal sealed class KeyboardListView : UserControl
{
    private readonly ListBox _list;
    private readonly TextBlock _empty;

    public KeyboardListView(IDataTemplate itemTemplate)
    {
        _list = new ListBox
        {
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = itemTemplate,
        };
        _list.DoubleTapped += (_, _) => ActivateSelected();

        _empty = new TextBlock
        {
            Foreground = Tokens.Dark.MutedText,
            Margin = new Thickness(12, 8),
            IsVisible = false,
        };

        Focusable = true;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        Content = new Panel { Children = { _list, _empty } };
    }

    /// <summary>Raised when the selected item is activated (Enter / double-click).</summary>
    public event Action<object>? Activated;

    /// <summary>The placeholder shown when the list is empty.</summary>
    public string EmptyText
    {
        get => _empty.Text ?? string.Empty;
        set => _empty.Text = value;
    }

    public object? SelectedItem => _list.SelectedItem;

    /// <summary>Load <paramref name="items"/>, selecting the first row.</summary>
    public void SetItems(System.Collections.IEnumerable items)
    {
        var list = items.Cast<object>().ToList();
        _list.ItemsSource = list;
        _list.SelectedIndex = list.Count > 0 ? 0 : -1;
        _empty.IsVisible = list.Count == 0;
        _list.IsVisible = list.Count > 0;
    }

    /// <summary>Move keyboard focus into the list, selecting the first row if none is selected.</summary>
    public void FocusList()
    {
        Focus();
        if (_list.SelectedIndex < 0 && _list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(+1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Home:
                Select(0);
                break;
            case Key.End:
                Select(_list.ItemCount - 1);
                break;
            case Key.Enter:
                ActivateSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Move(int delta)
    {
        if (_list.ItemCount > 0)
        {
            Select(Math.Clamp((_list.SelectedIndex < 0 ? 0 : _list.SelectedIndex) + delta, 0, _list.ItemCount - 1));
        }
    }

    private void Select(int index)
    {
        if (index >= 0 && index < _list.ItemCount)
        {
            _list.SelectedIndex = index;
            _list.ScrollIntoView(index);
        }
    }

    private void ActivateSelected()
    {
        if (_list.SelectedItem is { } item)
        {
            Activated?.Invoke(item);
        }
    }
}
