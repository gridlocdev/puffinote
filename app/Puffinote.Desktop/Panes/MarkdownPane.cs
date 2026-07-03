using Avalonia.Controls;
using Avalonia.Input;
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

    public MarkdownPane()
    {
        // The editor raises TextChanged on every edit; re-surface it so the shell repaints the dirty dot.
        _view.TextChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public Control View => _view;

    public string Title =>
        _session.CurrentPath is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path => _session.CurrentPath;

    public bool IsDirty => _view.IsDirty;

    // A minimal editor catalog for the merged palette. Typage's full command catalog lives in its Desktop
    // shell today; extracting it into Typage.Control (like the file-host seam) is a follow-up — until then
    // the pane exposes the operations the embeddable control already offers.
    public IReadOnlyList<AppCommand> Commands =>
    [
        new AppCommand("md.undo", "Undo", "Undo the last edit", Shortcut.Cmd(Key.Z),
            () => _view.Undo(), () => _view.CanUndo, icon: "undo"),
        new AppCommand("md.redo", "Redo", "Redo the last undone edit", Shortcut.Cmd(Key.Z, KeyModifiers.Shift),
            () => _view.Redo(), () => _view.CanRedo, icon: "redo"),
    ];

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
                () => { _view.Editor.Select(offset, 0); _view.Editor.Focus(); }));
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
