using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;

namespace Pivotask.Core;

/// <summary>
/// One open board: the document, its undo history, where it came from, and whether it has unsaved
/// changes. The single seam every consumer edits through — the GUI, the CLI, and the Puffinote pane all
/// hold one of these, so "how do you change a board" has one answer and it is always undoable.
/// </summary>
public sealed class BoardDocument
{
    private readonly BoardHistory _history = new();

    public BoardDocument(Board? board = null, string? path = null)
    {
        Board = board ?? Starter();
        Path = path;
    }

    public Board Board { get; private set; }

    /// <summary>Where this board lives, or null for one that has never been saved.</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Unsaved changes. Compared against the <i>serialized</i> form last written rather than an edit
    /// counter, so a change and its undo leave the document clean — which is what a person means when
    /// they say they haven't changed anything.
    /// </summary>
    public bool IsDirty => BoardCodec.Write(Board) != _savedText;

    private string _savedText = string.Empty;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    /// <summary>Raised after any change — an edit, an undo, a load — so a view can rebuild.</summary>
    public event EventHandler? Changed;

    public void Execute(IBoardCommand command)
    {
        _history.Execute(Board, command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!_history.Undo(Board))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!_history.Redo(Board))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Read a board from text, replacing this document's contents and clearing its history.</summary>
    public void Load(string json, string? path)
    {
        Board = BoardCodec.Read(json);
        Path = path;
        _savedText = BoardCodec.Write(Board);
        _history.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Read a board from disk.</summary>
    public static BoardDocument Open(string path)
    {
        var document = new BoardDocument();
        document.Load(File.ReadAllText(path), path);
        return document;
    }

    /// <summary>Serialize the board, and remember that form as the clean baseline.</summary>
    public string Serialize()
    {
        string text = BoardCodec.Write(Board);
        _savedText = text;
        return text;
    }

    /// <summary>
    /// Write the board to <paramref name="path"/> (or its own). Atomic: a temp file in the target's own
    /// directory, then a rename — same directory, because a rename across filesystems is a copy, and a
    /// copy is not atomic.
    /// </summary>
    public void Save(string? path = null)
    {
        string target = path ?? Path ?? throw new InvalidOperationException("This board has no path to save to.");
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target)) ?? ".";
        string temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(target)}.pivotask-tmp");

        File.WriteAllText(temp, Serialize());
        File.Move(temp, target, overwrite: true);
        Path = target;
    }

    /// <summary>Rebind to a new path after the file moved on disk. No I/O — the bytes already moved.</summary>
    public void Rebind(string path) => Path = path;

    /// <summary>
    /// What a brand-new board starts as. Empty columns rather than sample tasks: a starter board's job
    /// is to be somewhere to put the first real task, not something to clear out first. The three names
    /// are the ones almost every board grows anyway.
    /// </summary>
    public static Board Starter()
    {
        var board = new Board();
        string[] names = ["Inbox", "This week", "Done"];
        for (int i = 0; i < names.Length; i++)
        {
            board.Groups.Add(new TaskGroup { Id = Ids.FromSeed(i), Name = names[i] });
        }

        return board;
    }
}
