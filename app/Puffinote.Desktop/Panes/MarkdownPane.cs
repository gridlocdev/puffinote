using Avalonia.Controls;
using Puffinote.Components.Commands;
using Typage.Control;
using Typage.Control.Views;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Typage <see cref="MarkdownEditorView"/> unchanged, driving load/save through
/// its file-host seam <see cref="MarkdownDocumentSession"/> (which owns byte-faithful, atomic writes). This
/// is composition, not a fork: Puffinote adds no Markdown logic — editing, undo, and rendering are the
/// editor's; save mechanics are the session's.
/// </summary>
internal sealed class MarkdownPane : IEditorPane
{
    private readonly MarkdownEditorView _view = new();
    private readonly MarkdownDocumentSession _session = new();

    public MarkdownPane(NoteConfig? config = null)
    {
        // Apply the resolved note settings (PRD §7.5): Typage's own config as the base, with any Puffinote
        // override already layered in. A default config just reproduces the editor's own defaults.
        var cfg = config ?? new NoteConfig();
        _view.Editor.IndentWidth = cfg.TabWidth;
        _view.Editor.HorizontalPadding = cfg.EditorPadding;
        _view.Editor.VerticalPadding = cfg.EditorPaddingVertical;

        // Show Typage's formatting toolbar over the editor by default (the standalone app has its own; the
        // embeddable control keeps it off unless a host asks). Toggle via the shell's View command.
        _view.ShowCommandBar = true;

        // The editor raises TextChanged on every edit; re-surface it so the shell repaints the dirty dot.
        _view.TextChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the formatting toolbar is shown over the editor (shell View ▸ Toggle command).</summary>
    public bool ToolbarVisible
    {
        get => _view.ShowCommandBar;
        set => _view.ShowCommandBar = value;
    }

    public Control View => _view;

    /// <summary>Put the caret back in the note's text area (delegates to Typage's editor).</summary>
    public void Focus() => _view.Editor.Focus();

    public string Title =>
        _session.CurrentPath is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path => _session.CurrentPath;

    public bool IsDirty => _view.IsDirty;

    // Typage's editing/formatting catalog, now published by the embeddable control itself (Typage.Control),
    // so the merged Command Palette shows the note's commands next to Puffinote's — the same way SheetPane
    // surfaces Cellestra's catalog. Shell commands (file, find, dialogs) stay Puffinote's.
    public IReadOnlyList<AppCommand> Commands => _view.EditingCommands;

    /// <summary>Heading outline (PRD §7.2.3): the same headings <c>typage outline</c> lists, reusing the engine.</summary>
    public IReadOnlyList<OutlineItem> GetOutline()
    {
        var document = TextDocument.FromString(_view.Text);
        var model = MarkdownModel.Build(document);
        var items = new List<OutlineItem>();
        for (var line = 0; line < model.LineCount; line++)
        {
            var tokens = model.LineTokens(line);
            if (tokens.Count < 1 || tokens[0].Type != TokenType.HeadingMarker)
            {
                continue;
            }

            var level = int.TryParse(tokens[0].Meta, out var parsed) ? parsed : 1;
            var text = document.GetText(tokens[0].End, document.GetLineEnd(line) - tokens[0].End).Trim();
            var offset = document.GetLineStart(line);
            items.Add(new OutlineItem(
                text.Length == 0 ? "(untitled)" : text,
                level,
                () =>
                {
                    _view.Editor.Select(offset, 0);
                    _view.Editor.Focus();
                }));
        }

        return items;
    }

    public event EventHandler? DirtyChanged;

    public Task LoadAsync(string path)
    {
        // Note-sized files: read on the UI thread (the session's atomic I/O is fast) and set the editor's
        // saved baseline. LoadText marks the text clean, so a freshly opened doc isn't dirty.
        _view.LoadText(_session.Load(path));
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        _session.Save(_view.Text);
        _view.MarkSaved();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Rebind(string newPath) => _session.Rebind(newPath);

    public void Dispose()
    {
        // The editor holds only managed state; nothing unmanaged to release in Phase 1.
    }
}
