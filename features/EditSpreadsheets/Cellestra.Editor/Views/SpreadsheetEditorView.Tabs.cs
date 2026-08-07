using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Model;

namespace Cellestra.Editor;

public partial class SpreadsheetEditorView
{
    /// <summary>
    /// The surface for a sheet tab. Inactive tabs are explicitly Transparent rather than null so they
    /// stay hit-testable and their pointer events reach the strip.
    /// </summary>
    private IBrush TabBackground(bool active) => active ? ThemedSurface : Brushes.Transparent;

    /// <summary>
    /// The active tab's underline. Inactive tabs keep the same 2px edge in Transparent so activating a
    /// tab only recolors it — the strip never changes height and no tab moves.
    /// </summary>
    private IBrush TabUnderline(bool active) => active ? ThemedAccent : Brushes.Transparent;

    /// <summary>
    /// The accent for the active tab's underline: the app's green, lightened for dark themes. The flat
    /// #15734F sits at only 2.6:1 against the dark tab surface — under the 3:1 WCAG 2.2 §1.4.11 floor for
    /// a non-text indicator — and this underline is now the active tab's main signal, so it has to read.
    /// </summary>
    private IBrush ThemedAccent => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
        ? new SolidColorBrush(Color.FromRgb(0x3F, 0xB0, 0x84))
        : new SolidColorBrush(Color.FromRgb(0x15, 0x73, 0x4F));

    /// <summary>
    /// Repaint the existing tabs for the current theme variant.
    ///
    /// <see cref="ThemedSurface"/> reads <c>ActualThemeVariant</c>, which only reports the real variant
    /// once the view is attached to a visual tree. The constructor's <see cref="BuildSheetTabs"/> runs
    /// before that and sees the default (light), so in a dark shell the active tab would bake in a white
    /// surface and keep it until something else rebuilt the strip (clicking a tab, say). Repainting on
    /// ActualThemeVariantChanged fixes both that first paint and a later theme switch.
    ///
    /// This recolors in place instead of calling BuildSheetTabs so an open inline-rename editor — or an
    /// in-flight drag — survives a theme change.
    /// </summary>
    private void ApplyTabTheme()
    {
        int sheets = Session.Workbook.Worksheets.Count;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            if (SheetTabs.Children[i] is Border tab)
            {
                bool active = ReferenceEquals(Session.Workbook.Worksheets[i], Session.ActiveSheet);
                tab.Background = TabBackground(active);
                tab.BorderBrush = TabUnderline(active);
            }
        }
    }

    private void BuildSheetTabs()
    {
        SheetTabs.Children.Clear();
        foreach (var sheet in Session.Workbook.Worksheets)
        {
            bool active = ReferenceEquals(sheet, Session.ActiveSheet);
            // Tabs are Borders (not Buttons) so pointer events bubble to the strip for click-to-switch
            // and drag-to-reorder; the active/click/drag logic lives in the SheetTabs* handlers.
            var tab = new Border
            {
                Padding = new Thickness(10, 2),
                Background = TabBackground(active),
                // The active tab is marked by its surface plus an accent underline — not by weight. Bold
                // text made the active tab wider than the same tab inactive, so switching sheets nudged
                // every tab to its right sideways; the underline says the same thing at a fixed width.
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = TabUnderline(active),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = sheet.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var captured = sheet;
            tab.ContextRequested += (_, e) =>
            {
                // Right-clicking a tab activates it, then offers sheet actions (incl. delete — there's
                // no other way to remove a sheet). Anchor on the stable strip, since activating the
                // sheet rebuilds the tab elements.
                Session.SetActiveSheet(captured);
                Grid.Refresh();
                OnSelectionChanged();
                BuildCommandFlyout("sheet.rename", "sheet.duplicate", null, "insert.sheet", "insert.chartSheet", null, "sheet.delete").ShowAt(SheetTabs, showAtPointer: true);
                e.Handled = true;
            };
            // Double-click renames inline — but that's driven from SheetTabsPointerPressed (ClickCount==2),
            // not a per-tab DoubleTapped handler: the first click activates the sheet, which rebuilds the
            // whole strip and orphans this Border before its gesture recognizer could see the second click.
            SheetTabs.Children.Add(tab);
        }

        var add = new Button { Content = "＋", Padding = new Thickness(8, 2) };
        add.Click += (_, _) => AddSheet();
        SheetTabs.Children.Add(add);
    }

    /// <summary>True while an inline sheet-tab rename editor is open, so the strip drag/click logic backs off.</summary>
    private bool _inlineRenaming;

    /// <summary>
    /// Swap a sheet tab's label for an inline editable <see cref="TextBox"/> (task: double-click renames a
    /// sheet inline, like cell editing). Commits on Enter/blur through the validating rename path, cancels
    /// on Esc, and restores the tab either way.
    /// </summary>
    private void BeginInlineSheetRename(Border tab, Worksheet sheet)
    {
        if (_inlineRenaming)
        {
            return;
        }

        // Editing implies focusing this sheet. But SetActiveSheet raises StructureChanged, which runs
        // BuildSheetTabs and rebuilds the strip — orphaning the `tab` we were handed. So switch FIRST
        // (only when needed), then re-find the freshly-built tab Border to host the editor.
        if (!ReferenceEquals(Session.ActiveSheet, sheet))
        {
            Session.SetActiveSheet(sheet); // rebuilds the strip via StructureChanged
            Grid.Refresh();
            OnSelectionChanged();

            int index = Session.Workbook.IndexOf(sheet);
            if (index < 0 || index >= SheetTabs.Children.Count || SheetTabs.Children[index] is not Border rebuilt)
            {
                return; // strip changed out from under us; nothing safe to edit
            }

            tab = rebuilt;
        }

        _inlineRenaming = true;

        var editor = new TextBox
        {
            Text = sheet.Name,
            MinWidth = 60,
            Padding = new Thickness(2, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(editor, "Sheet name");

        bool finished = false;

        void Finish(bool commit)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            _inlineRenaming = false;

            string name = (editor.Text ?? string.Empty).Trim();
            if (commit && name.Length > 0 && name != sheet.Name)
            {
                // A failed rename (duplicate/invalid) surfaces a message; rebuild the strip to drop the editor.
                if (!CommitSheetRename(sheet, name))
                {
                    BuildSheetTabs();
                }

                // Success raises StructureChanged → BuildSheetTabs already ran.
            }
            else
            {
                BuildSheetTabs(); // restore the original label
            }
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Finish(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Finish(commit: false);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => Finish(commit: true);

        tab.Child = editor;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    // ---- Sheet tab click / drag-to-reorder (handled at the strip level) ---

    private int _tabDragFrom = -1;
    private double _tabDragStartX;
    private bool _tabDragging;
    private Worksheet? _tabDragSheet;

    /// <summary>Index of the worksheet tab under <paramref name="point"/> (in strip coordinates), or -1 (e.g. the ＋ button).</summary>
    private int TabIndexAt(Point point)
    {
        int sheets = Session.Workbook.Worksheets.Count;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            if (SheetTabs.Children[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void SheetTabsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_inlineRenaming)
        {
            return; // an inline rename editor owns the strip; don't start a drag/click gesture
        }

        if (!e.GetCurrentPoint(SheetTabs).Properties.IsLeftButtonPressed)
        {
            return; // right-click is handled per-tab via ContextRequested
        }

        var point = e.GetPosition(SheetTabs);
        int index = TabIndexAt(point);
        if (index < 0)
        {
            return; // the ＋ button has its own Click
        }

        // Double-click anywhere on a tab → rename it inline (like editing a cell). Detected here, at
        // the strip level, because the first click already rebuilt the strip via activation, so a
        // per-tab DoubleTapped never fires. BeginInlineSheetRename re-finds the live tab by index.
        if (e.ClickCount == 2 && SheetTabs.Children[index] is Border doubleClicked)
        {
            _tabDragFrom = -1; // cancel any pending click/drag gesture from the first press
            BeginInlineSheetRename(doubleClicked, Session.Workbook.Worksheets[index]);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // Record the gesture but DON'T activate yet — activating rebuilds the strip, which would
        // invalidate the tab bounds the release needs to compute the drop target. Activation (and any
        // reorder) happens on release, against the still-laid-out tabs.
        _tabDragFrom = index;
        _tabDragSheet = Session.Workbook.Worksheets[index];
        _tabDragStartX = point.X;
        _tabDragging = false;
        e.Pointer.Capture(SheetTabs);
    }

    private void SheetTabsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDragFrom < 0 || !e.GetCurrentPoint(SheetTabs).Properties.IsLeftButtonPressed)
        {
            return;
        }

        double x = e.GetPosition(SheetTabs).X;
        if (!_tabDragging && System.Math.Abs(x - _tabDragStartX) > 5)
        {
            // Past the threshold this is a drag, not a click: lift the grabbed tab and show a grabbing
            // cursor (drag-and-drop best practice — make the grabbed item and drop point obvious).
            _tabDragging = true;
            if (_tabDragFrom < SheetTabs.Children.Count)
            {
                SheetTabs.Children[_tabDragFrom].Opacity = 0.4;
            }

            SetTabCursors(_grabbingCursor);
        }

        if (_tabDragging)
        {
            UpdateTabDropIndicator(x);
        }
    }

    private void SheetTabsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tabDragFrom >= 0 && _tabDragSheet is { } dragged)
        {
            if (_tabDragging)
            {
                // Bounds are valid here (no rebuild happened during the gesture).
                int target = TabDropSlot(e.GetPosition(SheetTabs).X);
                if (target > _tabDragFrom)
                {
                    target--; // account for removing the dragged tab before re-inserting
                }

                if (target != _tabDragFrom)
                {
                    Session.MoveSheet(_tabDragFrom, target);
                }
            }

            Session.SetActiveSheet(dragged); // click-to-switch / activate the dragged tab (rebuilds the strip)
            Grid.Refresh();
            OnSelectionChanged();
        }

        EndTabDragVisual(); // clears opacity/cursor/marker; the rebuilt strip already starts clean
        e.Pointer.Capture(null);
    }

    // Reordering is horizontal, so the move cursor reads naturally as "I'm carrying this tab."
    private readonly Avalonia.Input.Cursor _grabbingCursor = new(Avalonia.Input.StandardCursorType.DragMove);
    private readonly Avalonia.Input.Cursor _tabHandCursor = new(Avalonia.Input.StandardCursorType.Hand);

    /// <summary>The insertion slot (0..count) the pointer at <paramref name="x"/> would drop into.</summary>
    private int TabDropSlot(double x)
    {
        int sheets = Session.Workbook.Worksheets.Count;
        int slot = 0;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            if (x > SheetTabs.Children[i].Bounds.Center.X)
            {
                slot++;
            }
        }

        return slot;
    }

    /// <summary>Place the green insertion marker in the gap where the dragged tab would land.</summary>
    private void UpdateTabDropIndicator(double x)
    {
        int sheets = Session.Workbook.Worksheets.Count;
        if (sheets == 0 || SheetTabs.Children.Count == 0)
        {
            return;
        }

        int slot = TabDropSlot(x);
        double edge = slot == 0
            ? SheetTabs.Children[0].Bounds.Left
            : slot < sheets && slot < SheetTabs.Children.Count
                ? SheetTabs.Children[slot].Bounds.Left
                : SheetTabs.Children[sheets - 1].Bounds.Right + 1;

        TabDropIndicator.Margin = new Thickness(System.Math.Max(0, edge - 1), 0, 0, 0);
        TabDropIndicator.IsVisible = true;
    }

    private void SetTabCursors(Avalonia.Input.Cursor cursor)
    {
        int sheets = Session.Workbook.Worksheets.Count;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            SheetTabs.Children[i].Cursor = cursor;
        }
    }

    // ---- Test seams (headless UI tests; InternalsVisibleTo Cellestra.UiTests) ----

    /// <summary>The sheet tab at <paramref name="index"/>, or null if there's no such tab.</summary>
    internal Border? TabForTest(int index) =>
        index >= 0 && index < SheetTabs.Children.Count ? SheetTabs.Children[index] as Border : null;

    /// <summary>The background brush on the tab at <paramref name="index"/>, or null if there's no such tab.</summary>
    internal IBrush? TabBackgroundForTest(int index) => TabForTest(index)?.Background;

    /// <summary>Clear all drag affordances — opacity, cursor, drop marker — and reset the gesture state.</summary>
    private void EndTabDragVisual()
    {
        TabDropIndicator.IsVisible = false;
        if (_tabDragging)
        {
            if (_tabDragFrom >= 0 && _tabDragFrom < SheetTabs.Children.Count)
            {
                SheetTabs.Children[_tabDragFrom].Opacity = 1.0;
            }

            SetTabCursors(_tabHandCursor);
        }

        _tabDragFrom = -1;
        _tabDragSheet = null;
        _tabDragging = false;
    }
}
