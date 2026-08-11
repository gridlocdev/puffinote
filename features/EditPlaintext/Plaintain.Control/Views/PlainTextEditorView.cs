using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Plaintain.Control.Commands;
using Plaintain.Control.Controls;
using Puffinote.Components.Commands;
using Puffinote.Components.Input;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;

namespace Plaintain.Control.Views;

/// <summary>
/// The embeddable composite: the editing surface, its find strip, and the one command catalog that acts
/// on the open document. A host — the standalone Plaintain window, or a Puffinote tab — embeds *this*
/// and gets the whole editor; anything a host adds instead would exist in only one of them.
/// </summary>
public sealed class PlainTextEditorView : UserControl
{
    public PlainTextEditorView()
    {
        Editor = new PlainTextEditor();

        var scroller = new ScrollViewer
        {
            Content = Editor,

            // The editor is an IScrollable, so the viewer hands it an offset instead of arranging a
            // million rows: Auto on both axes, and the surface decides its own extent.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Find.Changed += (_, _) => RunSearch(resetToFirst: true);
        Find.Step += (_, direction) => StepMatch(direction);
        Find.Dismissed += (_, _) =>
        {
            Matches = [];
            CurrentMatch = -1;
            Editor.SetMatches(Matches, CurrentMatch);
            Editor.Focus();
        };

        Editor.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);

        // The trackpad half of zoom, over the same seam the ⌘= / ⌘− catalog uses (see PinchZoom).
        PinchZoom.Attach(this, () => Zoom, zoom => Zoom = zoom);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(Find, Dock.Top);
        root.Children.Add(Find);
        root.Children.Add(scroller);
        Content = root;
    }

    /// <summary>The surface itself, for a host that needs to focus it or read its caret.</summary>
    public PlainTextEditor Editor { get; }

    /// <summary>Raised when the caret, selection, or document changes — for a host's status bar.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The document text.</summary>
    public string Text
    {
        get => Editor.Text;
        set => Editor.Text = value;
    }

    public bool IsDirty => Editor.Buffer.IsDirty;

    public bool CanUndo => Editor.Buffer.CanUndo;

    public bool CanRedo => Editor.Buffer.CanRedo;

    /// <summary>Load a document, marking it clean (or dirty, for a crash-recovery restore).</summary>
    public void LoadText(string? text, bool markSaved = true) => Editor.Load(text, markSaved);

    /// <summary>Take the version on disk as one undoable step (an external-change reload).</summary>
    public void ReloadText(string? text) => Editor.ReloadFrom(text);

    public void MarkSaved() => Editor.Buffer.MarkSaved();

    public void FocusEditor() => Editor.Focus();

    /// <summary>Word/character/line counts and the line-ending style, for a status bar.</summary>
    public DocumentStats Stats => Editor.Buffer.Stats;

    /// <summary>The caret's 1-based line and column, for a status bar.</summary>
    public LinePosition CaretPosition => Editor.Buffer.Document.GetPosition(Editor.Buffer.Selection.Caret);

    /// <summary>
    /// The editor's command catalog, built once per view so a bar, a palette, and a key binding all share
    /// one command instance (and one enabled state).
    /// </summary>
    public IReadOnlyList<AppCommand> EditingCommands => field ??= PlainTextEditingCommands.Build(this);

    public void Undo() => Editor.Undo();

    public void Redo() => Editor.Redo();

    /// <summary>
    /// Rewrite every line ending as <paramref name="style"/>, as one undoable edit. A no-op when the
    /// document already uses only that convention — a command that dirtied a file it did not change would
    /// be the worst kind of "did that do anything?".
    /// </summary>
    public void NormalizeLineEndings(NewlineStyle style)
    {
        var buffer = Editor.Buffer;
        string text = buffer.Document.GetText();
        string normalized = LineEndings.Normalize(text, style);
        if (ReferenceEquals(text, normalized) || string.Equals(text, normalized, StringComparison.Ordinal))
        {
            return;
        }

        buffer.ReplaceRange(0, text.Length, normalized);
    }

    /// <summary>
    /// The reading zoom, on the family's shared ladder. Delegates to the editor, which multiplies the
    /// configured font size by it; the host persists whatever it lands on.
    /// </summary>
    public double Zoom
    {
        get => Editor.Zoom;
        set
        {
            double clamped = ZoomScale.Clamp(value);
            if (Editor.Zoom == clamped)
            {
                return;
            }

            Editor.Zoom = clamped;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="Zoom"/> changes, so a host can persist it as the new default.</summary>
    public event EventHandler? ZoomChanged;

    public void ToggleWordWrap() => Editor.WordWrap = !Editor.WordWrap;

    public void ToggleLineNumbers() => Editor.ShowLineNumbers = !Editor.ShowLineNumbers;

    /// <summary>Open the find strip, seeded with the selection when there is one (the usual courtesy).</summary>
    public void ShowFind()
    {
        var sel = Editor.Buffer.Selection;
        string? seed = sel.IsEmpty || sel.Length > 200 ? null : Editor.Buffer.Document.GetText(sel.Start, sel.Length);
        Find.Show(seed);
    }

    /// <summary>Move the caret to a 1-based line number and centre it.</summary>
    public void GoToLine(int displayLine) => Editor.RevealLine(displayLine - 1);

    /// <summary>The find strip. Internal, not private, so a headless test can drive it as a user would.</summary>
    internal FindBar Find { get; } = new();

    /// <summary>Every match of the current query, in document order (empty while there is no query).</summary>
    internal IReadOnlyList<SearchMatch> Matches { get; private set; } = [];

    /// <summary>Which of <see cref="Matches"/> the caret is sitting on, or -1 when there are none.</summary>
    internal int CurrentMatch { get; private set; } = -1;

    private void RunSearch(bool resetToFirst)
    {
        string query = Find.Query;
        Matches = query.Length == 0
            ? []
            : DocumentSearch.FindAll(Editor.Buffer.Document.GetText(), query, Find.UseRegex, Find.MatchCase);

        if (resetToFirst)
        {
            // Land on the first match at or after the caret, so opening find where you are reading does
            // not throw you back to the top of a 50,000-line log.
            int caret = Editor.Buffer.Selection.Start;
            CurrentMatch = -1;
            for (int i = 0; i < Matches.Count; i++)
            {
                if (Matches[i].Start >= caret)
                {
                    CurrentMatch = i;
                    break;
                }
            }

            if (CurrentMatch < 0 && Matches.Count > 0)
            {
                CurrentMatch = 0;
            }
        }

        Editor.SetMatches(Matches, CurrentMatch);
        Find.ReportMatches(Math.Max(0, CurrentMatch), Matches.Count);
        RevealCurrent();
    }

    private void StepMatch(int direction)
    {
        if (Matches.Count == 0)
        {
            return;
        }

        CurrentMatch = ((CurrentMatch + direction) % Matches.Count + Matches.Count) % Matches.Count; // wraps both ways
        Editor.SetMatches(Matches, CurrentMatch);
        Find.ReportMatches(CurrentMatch, Matches.Count);
        RevealCurrent();
    }

    private void RevealCurrent()
    {
        if (CurrentMatch < 0 || CurrentMatch >= Matches.Count)
        {
            return;
        }

        var match = Matches[CurrentMatch];

        // Select the match without stealing focus from the query box — you are still typing in it.
        Editor.Buffer.Selection = new Selection(match.Start, match.End);
        Editor.BringCaretIntoView();
        Editor.InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && Find.IsVisible)
        {
            Find.Dismiss();
            e.Handled = true;
        }
    }
}
