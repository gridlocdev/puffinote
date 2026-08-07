namespace Puffinote.Text.Document;

/// <summary>
/// A 0-based line/column position in a document. The UI surfaces this as 1-based
/// "line:col" in the status bar (PRD §7.5, heuristic 1).
/// </summary>
public readonly record struct LinePosition(int Line, int Column)
{
    /// <summary>1-based line number for display.</summary>
    public int DisplayLine => Line + 1;

    /// <summary>1-based column number for display.</summary>
    public int DisplayColumn => Column + 1;

    public override string ToString() => $"{DisplayLine}:{DisplayColumn}";
}
