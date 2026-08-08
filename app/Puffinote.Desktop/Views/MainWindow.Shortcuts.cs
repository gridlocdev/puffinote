using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Commands;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// Reserved global shortcuts win over the focused editor (PRD §7.6): on the tunnelling route — before
    /// the editor's own key handling — a reserved gesture runs its shell command and stops the event. While
    /// an overlay is open, keys belong to the overlay, so the router stands down — including an overlay the
    /// <em>editor</em> owns (Typage's link prompt), or ⌘K would open the palette on top of it.
    /// </summary>
    private void OnReservedKeyDown(object? sender, KeyEventArgs e)
    {
        if (AnyOverlayOpen)
        {
            return;
        }

        if (ShortcutRouter.Match(_reservedBindings, e.Key, e.KeyModifiers) is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true; // reserved: the editor never sees it
        }
    }

    /// <summary>
    /// When the active document's editor first appears, warn once if any of its shortcuts collide with a
    /// reserved global key — the reserved key wins, so the editor binding is shadowed (PRD §7.6).
    /// </summary>
    private void ScanForShortcutConflicts()
    {
        // Don't scan or warn while an overlay owns the screen — retry on the next pane change, so the
        // once-guard is never burned without the warning actually appearing (C4).
        if (_conflictWarned || _overlayHost.IsOpen)
        {
            return;
        }

        if (_tabHost.ActivePane is not { } pane || ReferenceEquals(pane, _lastScannedPane))
        {
            return;
        }

        _lastScannedPane = pane;
        // Warn only about reserved keys the shell doesn't already supersede (skip ⌘K palette / ⌘O quick-open).
        var reserved = _shellCommands.Where(c =>
            ShellCommandCatalog.IsReserved(c.Id) && !ShellCommandCatalog.ShellSupersedesEditor.Contains(c.Id));
        var conflicts = ShortcutConflictScanner.Find(reserved, pane.Commands);
        if (conflicts.Count == 0)
        {
            return;
        }

        _conflictWarned = true; // latch only now that we're actually presenting the warning
        _ = WarnAboutShortcutConflictsAsync(conflicts);
    }

    /// <summary>
    /// The friendly, actionable conflict notice (PRD §7.6): name the shadowed editor shortcuts, say the
    /// reserved shell key wins, and offer a one-click path to the shortcuts editor to rebind (or keep as-is).
    /// </summary>
    private async Task WarnAboutShortcutConflictsAsync(IReadOnlyList<ShortcutConflict> conflicts)
    {
        var lines = conflicts
            .Select(c => $"•  {c.Editor.ShortcutText}  {c.Editor.Title}  →  “{c.Reserved.Title}” (Puffinote)")
            .ToList();
        var openEditor = await _overlayHost.ShowModalAsync(new ConfirmDialog(
            "Some editor shortcuts are overridden",
            "This editor defines shortcuts that Puffinote reserves for the shell, so Puffinote's shortcut "
                + "wins and the editor's is shadowed here:\n\n" + string.Join("\n", lines),
            confirmLabel: "Open Shortcuts Editor",
            denyLabel: "Keep as-is",
            confirmIsDefault: false));

        if (openEditor)
        {
            ShowShortcutsEditor();
        }
    }

    /// <summary>
    /// Open the shared shortcuts editor (PRD §7.6): every shell binding plus the active editor's, grouped and
    /// with the reserved global keys marked. Rebinding a shell command persists to the keymap and takes
    /// effect immediately; editor bindings are listed for reference (they're the editor's own to change).
    /// </summary>
    private void ShowShortcutsEditor()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        var view = new ShortcutsEditorView();
        view.Rebound += OnShortcutRebound;
        view.SetBindings(BuildShortcutRows());
        _shortcutsView = view;

        var card = new Border
        {
            Width = 560,
            Background = Tokens.Active.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 60, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Keyboard Shortcuts", FontWeight = FontWeight.SemiBold, FontSize = 15 },
                    view,
                },
            },
        };

        _overlayHost.Closed += ForgetShortcutsView;
        _overlayHost.ShowContent(card);
        Dispatcher.UIThread.Post(() => view.Focus());
    }

    private void ForgetShortcutsView(object? sender, EventArgs e)
    {
        _shortcutsView = null;
        _overlayHost.Closed -= ForgetShortcutsView;
    }

    /// <summary>The rows the shortcuts editor shows: Puffinote's shell catalog, then the active editor's.</summary>
    private List<ShortcutBinding> BuildShortcutRows()
    {
        var rows = _shellCommands.Select(c => new ShortcutBinding(
            c.Id, c.Title, "Puffinote", c.Shortcut,
            IsReserved: ShellCommandCatalog.IsReserved(c.Id),
            IsRebindable: !ShellCommandCatalog.IsReserved(c.Id),
            IsCustomized: _keymap.For(c.Id) is not null)).ToList();

        if (_tabHost.ActivePane is { } pane)
        {
            rows.AddRange(pane.Commands.Select(c => new ShortcutBinding(
                c.Id, c.Title, "Editor", c.Shortcut, IsReserved: false, IsRebindable: false, IsCustomized: false)));
        }

        return rows;
    }

    /// <summary>Apply a rebinding from the shortcuts editor: persist it and re-derive menu/keys/palette live.</summary>
    private void OnShortcutRebound(string id, Shortcut? shortcut)
    {
        if (ShellCommandCatalog.IsReserved(id))
        {
            return; // reserved global keys are fixed (PRD §7.6) — never rebound from within Puffinote
        }

        _keymap.Set(id, shortcut);
        BuildShellCommands();   // re-resolve every command's gesture through the updated keymap
        ApplyShellShortcuts();  // the new key binding takes effect immediately
        RefreshMenus();         // and the menu labels stay truthful
        RefreshCommandStates();
        _shortcutsView?.SetBindings(BuildShortcutRows()); // refresh the open editor's rows
    }
}
