using Avalonia.Controls;
using Puffinote.Components.Commands;
using Puffinote.Text.Document;
using Typage.Control;
using Typage.Control.Views;
using Typage.Engine.Markdown;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Typage <see cref="MarkdownEditorView"/> unchanged, driving load/save through
/// its file-host seam <see cref="MarkdownDocumentSession"/> (which owns byte-faithful, atomic writes). This
/// is composition, not a fork: Puffinote adds no Markdown logic — editing, undo, and rendering are the
/// editor's; save mechanics are the session's.
/// </summary>
internal sealed class MarkdownPane : IEditorPane, IConfigurablePane
{
    private readonly MarkdownEditorView _view = new();
    private readonly MarkdownDocumentSession _session = new();

    public MarkdownPane(NoteConfig? config = null)
    {
        ApplyConfig(config ?? new NoteConfig());

        // Show Typage's formatting toolbar over the editor by default (the standalone app has its own; the
        // embeddable control keeps it off unless a host asks). Toggle via the shell's View command.
        _view.ShowCommandBar = true;

        // The editor raises TextChanged on every edit; re-surface it so the shell repaints the dirty dot.
        _view.TextChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Apply the resolved note settings (PRD §7.5): Typage's own config as the base, with any Puffinote
    /// override already layered in. A default config just reproduces the editor's own defaults. Called at
    /// construction and again whenever the overrides change, so a Preferences edit reaches the note you
    /// are looking at rather than only the next one you open.
    /// </summary>
    public void ApplyConfig(NoteConfig config)
    {
        _view.Editor.IndentWidth = config.TabWidth;
        _view.Editor.HorizontalPadding = config.EditorPadding;
        _view.Editor.VerticalPadding = config.EditorPaddingVertical;
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

    // Typage's whole catalog, published by the embeddable control itself (Typage.Control): formatting, find,
    // the link/image inserts, view modes, export, the reference sheet. The merged Command Palette shows them
    // next to Puffinote's, and the shell binds their gestures — so the note editor here behaves exactly like
    // the standalone app. Only genuinely shell-level commands (notebook, tabs, sidebar) are Puffinote's.
    public IReadOnlyList<AppCommand> Commands => _view.EditingCommands;

    /// <summary>The editor puts its link prompt and reference sheet in its own overlay, inside the pane.</summary>
    public bool IsModalOpen => _view.IsOverlayOpen;

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

        // The editor needs the path itself: an inserted image is written relative to the note's folder, and
        // an HTML export is named after it. A note always has one, so nothing here is ever staged.
        _view.DocumentPath = _session.CurrentPath;
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

    public void Rebind(string newPath)
    {
        _session.Rebind(newPath);
        _view.DocumentPath = _session.CurrentPath; // the note moved; relative image paths must follow it
    }

    public void Dispose()
    {
        // The editor holds only managed state; nothing unmanaged to release in Phase 1.
    }
}
