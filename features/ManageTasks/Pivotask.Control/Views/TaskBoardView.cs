using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pivotask.Control.Commands;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Query;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Input;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The embeddable composite: the filter/search strip, the board (or list) under it, the task dialog over
/// it, and the one command catalog that acts on the document. A host — the standalone Pivotask window,
/// or a Puffinote tab — embeds <i>this</i> and gets the whole editor.
/// </summary>
public sealed class TaskBoardView : UserControl
{
    private readonly ContentControl _surface = new();
    private readonly LayoutTransformControl _zoomHost = new();
    /// <summary>"Search tasks", the prototype's words — "Search" alone leaves open what it searches.</summary>
    private readonly TextBox _search = new() { PlaceholderText = "Search tasks" };
    private readonly StackPanel _filterRows = new() { Spacing = 2 };
    private readonly Button _filterButton = new();
    private readonly ContentControl _switcher = new();
    private TopLevel? _keyHost;

    /// <summary>The slices, in <see cref="TaskFilter"/>'s own order — the one order the panel, the View
    /// menu and Preferences all list them in. One choice with five answers.</summary>
    private static readonly (TaskFilter Filter, string Label)[] Slices =
    [
        (TaskFilter.Unfinished, "Unfinished"),
        (TaskFilter.All, "All"),
        (TaskFilter.Starred, "Starred"),
        (TaskFilter.DueTomorrow, "Due tomorrow"),
        (TaskFilter.DueThisWeek, "Due this week"),
    ];

    public TaskBoardView()
    {
        BoardForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);
        ListForTest.TaskOpened += (_, task) => _ = OpenTaskAsync(task);
        BoardForTest.DeleteGroupRequested += (_, group) => _ = DeleteGroupAsync(group);
        ListForTest.DeleteGroupRequested += (_, group) => _ = DeleteGroupAsync(group);

        _search.TextChanged += (_, _) =>
        {
            BoardForTest.Query = _search.Text ?? string.Empty;
            ListForTest.Query = _search.Text ?? string.Empty;
            RefreshFilterFace(); // the button says when something is narrowing the board
        };

        // Two controls, at the two ends: how you are reading the board on the left, what you are reading
        // of it on the right. Everything that *narrows* — the search box and the four slices — is behind
        // the Filter button, because narrowing is one job with several settings and a strip that laid all
        // of them out flat spent its width on four pills that are three-quarters "not this one". You look
        // at a board far more often than you filter it.
        BuildLayoutSwitch();
        var palette = Tokens.Active;
        _filterButton.Flyout = FilterMenu;

        var bar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_switcher, Dock.Left);
        DockPanel.SetDock(_filterButton, Dock.Right);
        bar.Children.Add(_switcher);
        bar.Children.Add(_filterButton);

        // Chrome, not paper: the strip is a raised surface with a hairline under it, and the board below
        // is the recessed sheet the columns sit on. That contrast is the prototype's `--surface` bar over
        // its `--paper` body, and it is the thing that says which layer you are looking at — the strip had
        // been painting nothing at all, so it borrowed whatever the window was wearing and the two layers
        // read as one.
        var strip = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8),
            Child = bar,
        };

        // The board is scaled, the strip above it is not: zoom magnifies the tasks, and a filter row that
        // grew with them would eat the room the reader zoomed out to get. A LayoutTransformControl (rather
        // than a RenderTransform) is what makes the columns re-measure at the new size, so the cards
        // re-wrap and the scroll extent is honest instead of a magnified picture of the old layout.
        _zoomHost.Child = _surface;

        // The paper is painted here as well as inside each view, because zoomed out the scaled view no
        // longer fills the stage and the gap it leaves must be paper too, not the window's own colour.
        var stage = new Border { Background = palette.SurfaceInset, Child = _zoomHost };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Top);
        root.Children.Add(strip);
        root.Children.Add(stage);

        Content = new Panel { Children = { root, OverlayForTest } };

        // Two halves of one gesture: the way down notes which field had the caret before anything reacted
        // to the press, the way back up takes it out if the press landed elsewhere. handledEventsToo on
        // both, because a press the target already dealt with still means the reader has moved on.
        AddHandler(PointerPressedEvent, OnPressStarted, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPressedAnywhere, RoutingStrategies.Bubble, handledEventsToo: true);

        // The trackpad half of zoom, over the same seam the ⌘= / ⌘− catalog uses (see PinchZoom).
        PinchZoom.Attach(this, () => Zoom, zoom => Zoom = zoom);
        RefreshFilters();
        ApplyLayout();
        Document = Document;
    }

    /// <summary>The open board.</summary>
    public BoardDocument Document
    {
        get; set
        {
            field = value;
            BoardForTest.Document = value;
            ListForTest.Document = value;
            AdoptRecordedLayout();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            value.Changed += (_, _) =>
            {
                // Also on every change, so undoing a view switch takes the view back with it — and so a
                // board that arrived from disk under the edits is read in the reading it names.
                AdoptRecordedLayout();
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            };
        }
    } = new();

    /// <summary>Raised on any change to the document, so a host can repaint its dirty indicator.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>
    /// Board or list. One filter set and one search box serve both — which is why the list view was an
    /// addition in Phase 5 rather than a rework (proposal 0004 §10.6).
    /// <para>
    /// The <b>list</b> is what a board opens in when nothing has said otherwise. Columns are the better
    /// picture of a board you already know the shape of; rows are the better reading of one you don't,
    /// which every board is on the day it is made — and a person who wants columns says so once, in
    /// Preferences or with a press of V, and the board file remembers it from then on.
    /// </para>
    /// </summary>
    public BoardLayout Layout
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ApplyLayout();
        }
    } = BoardLayout.List;

    /// <summary>
    /// Switch the view <em>because somebody asked for it</em> — the switch, the V key, the View menu — and
    /// record the choice in the board, where it survives the session.
    ///
    /// <para>Distinct from setting <see cref="Layout"/>, which is a host applying a setting or restoring a
    /// state and must leave no trace: pushing "columns" at a brand-new board because that is what this
    /// machine's <c>Open in</c> says would make an untitled board dirty before anyone had typed anything
    /// into it, and would overwrite the choice a <em>different</em> board had already recorded.</para>
    /// </summary>
    internal void ChooseLayout(BoardLayout layout)
    {
        Layout = layout;
        if (Document.Board.View != layout)
        {
            Document.Execute(BoardCommands.SetView(Document.Board, layout));
        }
    }

    /// <summary>Open in the reading this board names, if it names one. A board that has never been
    /// switched says nothing, and the reader's own <c>Open in</c> preference stands.</summary>
    private void AdoptRecordedLayout()
    {
        if (Document.Board.View is { } recorded)
        {
            Layout = recorded;
        }
    }

    public TaskFilter Filter
    {
        get => BoardForTest.Filter;
        set
        {
            BoardForTest.Filter = value;
            ListForTest.Filter = value;
            RefreshFilters();
        }
    }

    public string Query
    {
        get => _search.Text ?? string.Empty;
        set => _search.Text = value;
    }

    /// <summary>
    /// How much larger than authored the board draws, on the family's shared ladder
    /// (<see cref="ZoomScale"/>). A board has no single "font size" to scale the way a text editor does —
    /// it is cards, columns and badges — so it scales the whole surface's layout instead, which is the
    /// same thing the reader asked for and keeps the card design in one place.
    /// </summary>
    public double Zoom
    {
        get;
        set
        {
            double clamped = ZoomScale.Clamp(value);
            if (clamped == field)
            {
                return;
            }

            field = clamped;
            _zoomHost.LayoutTransform = clamped == ZoomScale.Default ? null : new ScaleTransform(clamped, clamped);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    } = ZoomScale.Default;

    /// <summary>Raised after <see cref="Zoom"/> changes, so a host can report or persist it.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>The catalog, built once per view so every surface shares one command instance.</summary>
    public IReadOnlyList<AppCommand> EditingCommands => field ??= TaskEditingCommands.Build(this);

    /// <summary>The groups, for a host's outline panel. Unlike plaintext, a board has real structure.</summary>
    public IReadOnlyList<TaskGroup> Groups => Document.Board.Groups;

    /// <summary>Scroll the board to a group — what an outline item does when clicked.</summary>
    public void Reveal(TaskGroup group)
    {
        Layout = BoardLayout.Board;
        BoardForTest.Rebuild();
        _ = group; // the board lays every column out at once; revealing is bringing the board forward
    }

    /// <summary>True while the task dialog is up, so a host stands its shortcuts down.</summary>
    public bool IsModalOpen => OverlayForTest.IsOpen;

    /// <summary>
    /// Abandon a drag in flight in whichever view is showing. Public because the gesture belongs to the
    /// board and the key that ends it arrives at the window — and because a host may want to end a drag
    /// for its own reasons (a tab switching out from under one).
    /// </summary>
    public bool CancelDrag() => BoardForTest.CancelDrag() || ListForTest.CancelDrag();

    internal BoardView BoardForTest { get; } = new();

    internal TaskListView ListForTest { get; } = new();

    internal OverlayHost OverlayForTest { get; } = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };

    /// <summary>Today, injectable so tests pin what "due soon" means rather than racing the clock.</summary>
    internal DateOnly Today
    {
        get => BoardForTest.Today;
        set
        {
            BoardForTest.Today = value;
            ListForTest.Today = value;
            BoardForTest.Rebuild();
        }
    }

    /// <summary>Open the task dialog on <paramref name="task"/>.</summary>
    public Task OpenTaskAsync(TaskItem task) => OverlayForTest.ShowModalAsync(new TaskDialog(Document, task, Today));

    /// <summary>
    /// Delete a group, asking first when it still holds tasks — the prototype's rule, and the right one:
    /// an empty column is a click to remake, a full one is work. Deleting the last group leaves a fresh
    /// empty one behind rather than a board with nowhere to put anything, which is also what it does.
    /// </summary>
    internal async Task DeleteGroupAsync(TaskGroup group)
    {
        if (group.Tasks.Count > 0)
        {
            bool go = await OverlayForTest.ShowModalAsync(new ConfirmDialog(
                "Delete list",
                $"Delete “{group.Name}” and its {group.Tasks.Count} task{(group.Tasks.Count == 1 ? string.Empty : "s")}?",
                confirmLabel: "Delete",
                denyLabel: "Cancel",
                confirmIsDefault: false));

            if (!go)
            {
                return;
            }
        }

        Document.Execute(BoardCommands.DeleteGroup(Document.Board, group));
        if (Document.Board.Groups.Count == 0)
        {
            Document.Execute(BoardCommands.AddGroup("Tasks"));
        }
    }

    /// <summary>
    /// A bare key has no owner until something is focused, so the two unmodified shortcuts are watched at
    /// the window rather than on this control — the prototype watches the document for the same reason.
    /// The guard in <see cref="OnBareKey"/> is what keeps that from reaching across a host: in a Puffinote
    /// tab, another editor's keystrokes are not ours to read.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyHost = TopLevel.GetTopLevel(this);
        _keyHost?.AddHandler(KeyDownEvent, OnBareKey, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _keyHost?.RemoveHandler(KeyDownEvent, OnBareKey);
        _keyHost = null;
    }

    /// <summary>
    /// The two unmodified keys the prototype binds: <c>/</c> opens the filter panel with the caret in its
    /// search box, and <c>V</c> flips between board and list.
    /// <para>
    /// Both are suppressed while you are typing — into the search box, a card's add-a-task field, a group
    /// name, or anything else that takes text — and while the task dialog is up, because a bare letter has
    /// to be a letter wherever a letter is expected. A modifier held means the chord belongs to the host's
    /// catalog, so those are left alone too.
    /// </para>
    /// </summary>
    private void OnBareKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || IsModalOpen || IsTyping() || !FocusIsOurs())
        {
            return;
        }

        // Escape puts a card back. A drag you have started and thought better of otherwise had only one
        // way out — drop it exactly where it came from and hope the no-op check catches it — and no way
        // out at all once the pointer had reached another column. Checked first: while a card is in the
        // air, Escape means this and nothing else.
        if (e.Key == Key.Escape)
        {
            e.Handled = CancelDrag();
            return;
        }

        if (e.Key == Key.Oem2 || e.Key == Key.Divide)
        {
            OpenFilters(); // the search box lives in the filter panel now, so / has to open it first
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            ChooseLayout(Layout == BoardLayout.Board ? BoardLayout.List : BoardLayout.Board);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A press anywhere outside the field you were typing in takes the caret out of it — which commits
    /// what you typed, closes the add-a-task field, or settles a group's new name, exactly as tabbing
    /// away would.
    ///
    /// <para>Avalonia only moves focus when the press lands on something that <em>takes</em> focus, and
    /// most of a board doesn't: the space beside the columns, a column's own background, the "empty" note,
    /// a group heading. Clicking any of those left the caret blinking in a field the reader had visibly
    /// finished with, and its edit uncommitted.</para>
    ///
    /// <para>Handled on the way back up, and never marked handled: whatever the click was going to do
    /// happens first, and then the field settles. Doing it on the way down would have the commit's rebuild
    /// tear down the control the press was headed for, so the click would be swallowed — which is a
    /// second bug, not a fix for this one.</para>
    /// </summary>
    private void OnPressStarted(object? sender, PointerPressedEventArgs e) =>
        _caretAtPress = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;

    private void OnPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        var box = _caretAtPress;
        _caretAtPress = null;

        // Only a field that already had the caret when the press began. Without that this would take the
        // caret straight back out of a field the very same press had just opened — pressing a group
        // heading turns it into a text box on the way down, and this runs on the way up.
        if (box is null
            || TopLevel.GetTopLevel(this)?.FocusManager is not { } focus
            || !ReferenceEquals(focus.GetFocusedElement(), box))
        {
            return;
        }

        // Inside the field itself — its own scrollbar, its clear button — is not outside it.
        if (e.Source is Visual source && (ReferenceEquals(source, box) || box.IsVisualAncestorOf(source)))
        {
            return;
        }

        focus.Focus(null);
    }

    /// <summary>Which field held the caret when the current press began (see <see cref="OnPressedAnywhere"/>).</summary>
    private TextBox? _caretAtPress;

    /// <summary>Whether the keyboard currently belongs to a text field somewhere inside this view.</summary>
    private bool IsTyping() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Whether this board is the thing the keyboard is pointed at. Nothing focused counts as ours — that
    /// is a freshly-opened window with the board the only thing in it — but focus sitting in somebody
    /// else's control does not, so a board in a Puffinote tab can't answer a key meant for the sidebar.
    /// </summary>
    private bool FocusIsOurs() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused
        || this.IsVisualAncestorOf(focused);

    private void ApplyLayout()
    {
        _surface.Content = Layout == BoardLayout.Board ? BoardForTest : ListForTest;
        BuildLayoutSwitch(); // the switch lights the view in force, so it is rebuilt whenever that changes
    }

    /// <summary>
    /// Everything that narrows the board, in one panel hung off the Filter button: the search box, and
    /// under it the four slices with a tick against whichever is in force.
    ///
    /// <para>They were four pills laid out flat in the strip beside the search box. Four pills is a
    /// control that is three-quarters "not this one" — it spends the width of half a narrow window
    /// showing you the three answers you didn't pick, permanently, to a question you ask occasionally.
    /// Search and the slices are also one job, not two: both answer "which of these tasks am I looking
    /// at", and putting them in one panel is what lets you type a word and tick "Unfinished" without
    /// crossing the strip twice.</para>
    ///
    /// <para>A tick rather than a filled pill, because in a vertical list that is what "this is the one"
    /// looks like — and it survives a reader who cannot tell the fill from the ground (WCAG 2.2 §1.4.1),
    /// which a colour difference alone does not.</para>
    /// </summary>
    internal Flyout FilterMenu => field ??= BuildFilterMenu();

    private Flyout BuildFilterMenu()
    {
        var palette = Tokens.Active;

        var panel = new StackPanel { Spacing = 8, MinWidth = 232 };
        panel.Children.Add(_search);
        panel.Children.Add(new Border
        {
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Children.Add(_filterRows);
        Avalonia.Automation.AutomationProperties.SetName(panel, "Filter tasks");

        var flyout = new Flyout
        {
            Content = panel,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        };

        // The caret lands in the search box on open, because typing is what most people came here to do
        // and the slices are a click away either way. Posted, so the popup's own focus pass is finished
        // before this one runs.
        flyout.Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _search.Focus();
            _search.SelectAll();
        });

        return flyout;
    }

    /// <summary>Open the filter panel and put the caret in its search box — what <c>/</c> and ⌘F do.</summary>
    public void OpenFilters()
    {
        if (!FilterMenu.IsOpen)
        {
            FilterMenu.ShowAt(_filterButton);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _search.Focus();
            _search.SelectAll();
        });
    }

    /// <summary>The filter panel's rows and the button's face, both of which state which slice is in force.</summary>
    private void RefreshFilters()
    {
        var palette = Tokens.Active;
        _filterRows.Children.Clear();

        foreach (var (filter, label) in Slices)
        {
            bool active = Filter == filter;

            var row = new DockPanel { LastChildFill = true };
            var tick = new LucideIcon
            {
                IconKey = "check",
                Size = 14,
                Foreground = palette.Accent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),

                // Opacity, not IsVisible: the tick keeps its slot either way, so the four labels line up
                // in one column instead of shuffling sideways as the choice moves between them.
                Opacity = active ? 1 : 0,
            };
            DockPanel.SetDock(tick, Dock.Left);
            row.Children.Add(tick);
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? palette.Text : palette.MutedText,
            });

            var button = new Button
            {
                Content = row,
                Padding = new Thickness(8, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            Avalonia.Automation.AutomationProperties.SetHelpText(button, active ? "selected" : null);

            // The panel stays open on a pick: the search box is in it, the tick has just moved, and the
            // board behind has already changed — closing would hide all three answers at once.
            var captured = filter;
            button.Click += (_, _) => Filter = captured;
            _filterRows.Children.Add(button);
        }

        RefreshFilterFace();
    }

    /// <summary>
    /// The button itself, which has to say whether anything is being hidden. A board showing a slice of
    /// itself looks exactly like a board with fewer tasks on it, and the four pills at least said which
    /// one was lit — so the button that replaced them names the slice in force and wears the accent
    /// whenever a slice or a search is narrowing what you can see.
    /// </summary>
    private void RefreshFilterFace()
    {
        var palette = Tokens.Active;
        string query = Query;
        bool narrowed = Filter != TaskFilter.All || query.Length > 0;
        string slice = Array.Find(Slices, s => s.Filter == Filter).Label;

        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        face.Children.Add(new LucideIcon
        {
            IconKey = "filter",
            Size = 13,
            IsFilled = narrowed,
            VerticalAlignment = VerticalAlignment.Center,
        });
        face.Children.Add(new TextBlock
        {
            Text = Filter == TaskFilter.All ? "Filter" : slice,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _filterButton.Content = face;
        _filterButton.Padding = new Thickness(12, 6);
        _filterButton.CornerRadius = new CornerRadius(6);
        _filterButton.BorderThickness = new Thickness(1);
        _filterButton.BorderBrush = narrowed ? palette.Accent : Brushes.Transparent;
        _filterButton.Background = narrowed ? palette.SurfaceInset : Brushes.Transparent;
        _filterButton.Foreground = narrowed ? palette.Accent : palette.MutedText;
        _filterButton.FontWeight = narrowed ? FontWeight.SemiBold : FontWeight.Normal;

        string state = query.Length > 0 ? $"{slice} · “{query}”" : slice;
        ToolTip.SetTip(_filterButton, narrowed ? $"Filter tasks — {state} (/)" : "Filter tasks (/)");
        Avalonia.Automation.AutomationProperties.SetName(_filterButton, "Filter");
        Avalonia.Automation.AutomationProperties.SetHelpText(_filterButton, narrowed ? state : null);
    }

    /// <summary>
    /// The view switch as a segmented pair — Board and List side by side with the current one lit, not one
    /// button that says "Board / List" and leaves you to work out which you are in. A control that shows
    /// the state and the way to change it in the same glance is the whole point of a segmented switch,
    /// and it is what the prototype has.
    /// </summary>
    private Avalonia.Controls.Control BuildLayoutSwitch()
    {
        var palette = Tokens.Active;
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Avalonia.Automation.AutomationProperties.SetName(group, "Choose a view");

        foreach (var (layout, label, icon) in
                 new[] { (BoardLayout.Board, "Board", "layout-grid"), (BoardLayout.List, "List", "list") })
        {
            bool active = Layout == layout;

            // Icon and word together, as the prototype has them — and the word is what goes when the strip
            // runs out of room, because these two icons are the shapes they name.
            var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            face.Children.Add(new LucideIcon
            {
                IconKey = icon,
                Size = 13,
                IsFilled = true,
                VerticalAlignment = VerticalAlignment.Center,
            });
            face.Children.Add(new TextBlock
            {
                Text = label.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 11.5,
                LetterSpacing = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button
            {
                Content = face,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = active ? palette.Accent : Brushes.Transparent,
                Background = active ? palette.SurfaceInset : Brushes.Transparent,
                Foreground = active ? palette.Accent : palette.MutedText,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };

            ToolTip.SetTip(button, $"{label} view (V)"); // the chord the prototype advertises here too
            Avalonia.Automation.AutomationProperties.SetName(button, $"{label} view");
            Avalonia.Automation.AutomationProperties.SetHelpText(button, active ? "selected" : null);

            var captured = layout;
            button.Click += (_, _) => ChooseLayout(captured);
            group.Children.Add(button);
        }

        _switcher.Content = group;
        return _switcher;
    }
}
