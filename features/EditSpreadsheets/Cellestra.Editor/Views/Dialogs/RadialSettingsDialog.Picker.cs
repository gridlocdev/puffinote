using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.Icons;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The command-palette-style picker for binding one wedge. Kept as a partial (not extracted to its own
/// type): its commit path mutates the dialog's shared <c>_bindings</c>/<c>SelectedSegmentForTest</c> and
/// calls <c>RebuildBindingRows</c>, and several <c>…ForTest</c> seams on the root type reach directly into
/// <c>_pickerList</c>/<c>_commandPicker</c> — so a clean extraction can't happen without changing the test
/// surface or introducing a callback-based redesign, both of which the refactor guardrails forbid.
/// </summary>
public sealed partial class RadialSettingsDialog
{
    /// <summary>One choice in the wedge's command picker; <see cref="Id"/> null is the "unbind" option.</summary>
    private sealed record PickerItem(string? Id, string Label, string? Icon, string Description);

    /// <summary>
    /// A command-palette-style picker — a search box over the same icon + name + description rows — for
    /// choosing the command a wedge runs. A distinct accent header ("Bind command to Segment N", with a
    /// crosshair and a "won't run now" note) makes clear you're <em>assigning</em> a command, not running
    /// one. Layered over the body; commits on click/Enter, dismisses on Esc/backdrop/Cancel.
    /// </summary>
    private Border BuildCommandPicker()
    {
        _pickerTitle = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14 };
        var header = new Border
        {
            Background = SelectedRowBrush,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new LucideIcon { IconKey = "crosshair", Size = 18, VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            _pickerTitle,
                            new TextBlock
                            {
                                Text = "Choose the command this wedge runs — it won't run now.",
                                Foreground = PickerMuted,
                                FontSize = 11.5,
                                TextWrapping = TextWrapping.Wrap,
                            },
                        },
                    },
                },
            },
        };

        _pickerSearch = new TextBox { PlaceholderText = "Search commands…" };
        _pickerSearch.TextChanged += (_, _) => PopulatePicker(_pickerSearch.Text ?? string.Empty);
        _pickerSearch.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    HideCommandPicker();
                    e.Handled = true;
                    break;
                case Key.Return:
                    CommitPicker();
                    e.Handled = true;
                    break;
                case Key.Down:
                    MovePicker(+1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MovePicker(-1);
                    e.Handled = true;
                    break;
            }
        };

        _pickerList = new ListBox
        {
            MaxHeight = 280,
            ItemTemplate = new FuncDataTemplate<PickerItem>((item, _) => BuildPickerRow(item)),
        };
        // A click lands selection on press, so by release SelectedItem is the clicked row — commit it,
        // but only when the click actually hit a row (not the empty space below a short list).
        _pickerList.PointerReleased += (_, e) =>
        {
            if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            {
                CommitPicker();
            }
        };

        var card = new Border
        {
            Width = 440,
            Background = PickerCardBrush,
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x60, 0, 0, 0) }),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    header,
                    _pickerSearch,
                    _pickerList,
                    new TextBlock { Text = "↵ assign · Esc cancel", Foreground = PickerMuted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right },
                },
            },
        };

        var scrim = new Border
        {
            IsVisible = false,
            Background = ScrimBrush,
            Child = card,
        };
        scrim.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, scrim))
            {
                HideCommandPicker(); // clicking the dim backdrop cancels
            }
        };
        // Trap Tab inside the picker so it cycles its search box / list, not the dialog body behind it.
        KeyboardNavigation.SetTabNavigation(scrim, KeyboardNavigationMode.Cycle);

        return scrim;
    }

    private void ShowCommandPicker(int segment)
    {
        if (!InRange(segment))
        {
            return;
        }

        CommandPickerSegmentForTest = segment;
        _pickerTitle.Text = $"Bind command to Segment {segment + 1}";
        _pickerSearch.Text = string.Empty;
        PopulatePicker(string.Empty);
        _commandPicker.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _pickerSearch.Focus());
    }

    private void HideCommandPicker()
    {
        _commandPicker.IsVisible = false;
        CommandPickerSegmentForTest = -1;
    }

    /// <summary>Rank the commands against <paramref name="query"/> (fuzzy), with an "unbind" row on top.</summary>
    private void PopulatePicker(string query)
    {
        var items = new List<PickerItem>();
        if (string.IsNullOrEmpty(query) || FuzzyMatcher.Score("None", query) >= 0)
        {
            items.Add(new PickerItem(null, "— None —", null, "Leave this wedge empty"));
        }

        items.AddRange((_commands?.All ?? [])
            .Select(c => (Command: c, Score: FuzzyMatcher.Score(c.Title, query)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .Select(x => new PickerItem(x.Command.Id, x.Command.Title, x.Command.Icon, x.Command.Description)));

        _pickerList.ItemsSource = items;
        _pickerList.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private void MovePicker(int direction)
    {
        int next = _pickerList.SelectedIndex + direction;
        if (next >= 0 && next < _pickerList.ItemCount)
        {
            _pickerList.SelectedIndex = next;
            _pickerList.ScrollIntoView(next);
        }
    }

    /// <summary>Assign the highlighted command (or unbind) to the wedge being edited, then close.</summary>
    private void CommitPicker()
    {
        if (CommandPickerSegmentForTest < 0 || _pickerList.SelectedItem is not PickerItem item)
        {
            return;
        }

        _bindings[CommandPickerSegmentForTest] = item.Id;
        SelectedSegmentForTest = CommandPickerSegmentForTest;
        HideCommandPicker();
        RebuildBindingRows();
    }

    private Control BuildPickerRow(PickerItem? item)
    {
        // The ListBox can build the template with a null item while recycling containers — render nothing.
        if (item is null)
        {
            return new Control();
        }

        var dock = new DockPanel { LastChildFill = true };

        if (CommandPickerSegmentForTest >= 0 && item.Id is { } id && id == _bindings[CommandPickerSegmentForTest])
        {
            var current = new TextBlock
            {
                Text = "Current",
                Foreground = PickerMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(current, Dock.Right);
            dock.Children.Add(current);
        }

        if (!string.IsNullOrEmpty(item.Icon))
        {
            var icon = new LucideIcon { IconKey = item.Icon, Size = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 11, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
        }

        dock.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = item.Label, FontWeight = FontWeight.Medium },
                new TextBlock { Text = item.Description, Foreground = PickerMuted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        });

        return dock;
    }
}
