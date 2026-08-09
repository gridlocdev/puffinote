using Avalonia.Controls;
using Plaintain.Control.Views;
using Puffinote.Components.Commands;
using Puffinote.Text.Document;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Plaintain <see cref="PlainTextEditorView"/> unchanged. Composition, not a
/// fork: Puffinote adds no text logic — the editing, the undo, the encoding round-trip and the atomic
/// write are all the editor's or the shared text library's.
/// </summary>
internal sealed class PlainTextPane : IEditorPane, IConfigurablePane
{
    private readonly PlainTextEditorView _view = new();
    private TextFileFormat _format = TextFileFormat.Utf8NoBom;

    public PlainTextPane()
    {
        _view.SelectionChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        ApplyConfig(new EditorConfig()); // Plaintain's defaults until the host supplies the resolved config
    }

    /// <summary>
    /// Apply Plaintain's own settings (PRD §7.5), so a text file opens here the way it opens in the
    /// standalone app, and a Preferences edit reaches the file already in front of you.
    /// </summary>
    public void ApplyConfig(EditorConfig config)
    {
        _view.Editor.WordWrap = config.PlainText.WordWrap;
        _view.Editor.ShowLineNumbers = config.PlainText.ShowLineNumbers;
        _view.Editor.HighlightCurrentLine = config.PlainText.HighlightCurrentLine;
        _view.Editor.FontSize = config.PlainText.FontSize;
    }

    /// <summary>The editor's own reading zoom — the seam the shell remembers a tab's zoom through.</summary>
    public double Zoom
    {
        get => _view.Zoom;
        set => _view.Zoom = value;
    }

    public Control View => _view;

    public void Focus() => _view.FocusEditor();

    public string Title => Path is { } p ? System.IO.Path.GetFileName(p) : "Untitled";

    public string? Path { get; private set; }

    public bool IsDirty => _view.IsDirty;

    public IReadOnlyList<AppCommand> Commands => _view.EditingCommands;

    /// <summary>
    /// <b>Nothing, permanently</b> (proposal 0003 §9.4). Plaintext has no structure, and the heuristics that
    /// could invent some — indentation trees, comment banners — produce confident-looking noise on prose and
    /// logs. A blank panel is the honest answer for a file with no outline.
    /// </summary>
    public IReadOnlyList<OutlineItem> GetOutline() => [];

    public event EventHandler? DirtyChanged;

    public Task LoadAsync(string path)
    {
        var read = TextFileCodec.Read(File.ReadAllBytes(path));
        _format = read.Format;

        // Bytes that aren't valid UTF-8 were recovered through a total fallback, so the file is intact and
        // readable — but saving would write the guess back. The pane refuses the save rather than asking:
        // a notebook tab has no good place for an encoding prompt, and a file you can read but not write is
        // a better outcome than one silently rewritten.
        _view.Editor.IsReadOnlyEncoding = read.IsReadOnly;
        _view.LoadText(read.Text);
        Path = path;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        if (Path is not { } path || _view.Editor.IsReadOnlyEncoding)
        {
            return Task.CompletedTask;
        }

        // Atomic: a temp file in the target's own directory, then a rename — same directory because a
        // rename across filesystems is a copy, and a copy is not atomic.
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
        string temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.puffinote-tmp");
        File.WriteAllBytes(temp, TextFileCodec.Encode(_view.Text, _format));
        File.Move(temp, path, overwrite: true);

        _view.MarkSaved();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Rebind(string newPath) => Path = newPath;

    public void Dispose()
    {
        // The editor holds only managed state.
    }
}
