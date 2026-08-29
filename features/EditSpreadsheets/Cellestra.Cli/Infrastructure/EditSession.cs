using System.Globalization;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Infrastructure;

/// <summary>
/// The one edit flow every writing command goes through: load, apply edits (remembering which cells
/// they touched), recalculate, save atomically to the resolved target, and then say what happened —
/// as one-line notes on stderr, or with <c>--json</c> as a single report on stdout that lists the
/// changed cells with their new values, the recalculation count, and every error cell left in the
/// workbook. That last part is the point: an agent that just wrote <c>=SUMM(B2:B9)</c> learns about
/// the <c>#NAME?</c> in the same reply, not on a later <c>validate</c>.
/// </summary>
public sealed class EditSession
{
    private readonly List<CellRef> _changed = [];
    private readonly List<string> _notes = [];
    private readonly Dictionary<string, object?> _fields = [];

    private EditSession(LoadedWorkbook loaded, CultureInfo culture)
    {
        Loaded = loaded;
        Culture = culture;
    }

    public LoadedWorkbook Loaded { get; }

    public Workbook Workbook => Loaded.Workbook;

    /// <summary>The effective culture: <c>--locale</c> if given, else the document's.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Apply one edit command and remember the cells it reports as changed.</summary>
    public void Apply(IEditCommand edit)
    {
        var result = edit.Apply(Workbook);
        _changed.AddRange(result.ChangedCells);
    }

    /// <summary>Remember a cell changed by a direct model mutation (no <see cref="IEditCommand"/>).</summary>
    public void Touched(string sheet, int row, int column) => _changed.Add(new CellRef(sheet, row, column));

    /// <summary>A human-readable line, printed to stderr in text mode and carried as <c>notes</c> in JSON.</summary>
    public void Note(string message) => _notes.Add(message);

    /// <summary>An extra top-level field for the JSON report (e.g. <c>removed: 3</c>).</summary>
    public void Field(string name, object? value) => _fields[name] = value;

    /// <summary>
    /// Run an edit against <paramref name="path"/> and write the result to <paramref name="target"/>.
    /// <paramref name="edit"/> does the work through <see cref="Apply"/>, <see cref="Note"/>, and
    /// <see cref="Field"/>; everything before and after it is the shared contract.
    /// </summary>
    public static int Run(string path, string target, CultureInfo? locale, bool json, Action<EditSession> edit, WorkbookFormat? format = WorkbookFormat.Ods, bool dryRun = false)
    {
        if (json && target == "-" && !dryRun)
        {
            throw new CliException("--json writes the report to stdout, so the workbook can't go there too — give -o <file> or --in-place.");
        }

        var loaded = Workspace.Load(path, format, locale);
        var session = new EditSession(loaded, locale ?? loaded.Workbook.Settings.EffectiveLocale);
        edit(session);
        return session.Finish(target, json, dryRun);
    }

    /// <summary>As <see cref="Run(string,string,CultureInfo?,bool,Action{EditSession},WorkbookFormat?)"/> for a workbook that is already in memory (a new file, an import).</summary>
    public static int Run(LoadedWorkbook loaded, string target, CultureInfo? locale, bool json, Action<EditSession> edit)
    {
        if (json && target == "-")
        {
            throw new CliException("--json writes the report to stdout, so the workbook can't go there too — give -o <file> or --in-place.");
        }

        var session = new EditSession(loaded, locale ?? loaded.Workbook.Settings.EffectiveLocale);
        edit(session);
        return session.Finish(target, json, dryRun: false);
    }

    /// <summary>
    /// Recalculate, save (unless this is a dry run), and report. A dry run does everything but the
    /// write, so the report — changed cells, errors — is exactly what a real run would have produced.
    /// </summary>
    private int Finish(string target, bool json, bool dryRun)
    {
        var result = Workspace.Recalculate(Workbook);
        if (!dryRun)
        {
            Workspace.SaveOds(Workbook, Loaded.Package, target);
        }

        if (!json)
        {
            foreach (var note in _notes)
            {
                Console.Error.WriteLine(note);
            }

            if (dryRun)
            {
                Console.Error.WriteLine("Dry run: nothing written.");
            }

            if (result.HasCircularReference)
            {
                Console.Error.WriteLine($"Warning: {result.CircularCells.Count} circular reference(s).");
            }

            return 0;
        }

        // Distinct, in first-touched order; a cell whose sheet was deleted has nothing to report.
        var changed = _changed.Distinct()
            .Where(c => Workbook.FindSheet(c.Sheet) is not null)
            .Select(c => Report.Cell(Workbook, c, Culture))
            .ToList();
        var errors = Report.Errors(Workbook, Culture);

        var payload = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["file"] = target,
            ["dryRun"] = dryRun ? true : null,
            ["changed"] = changed,
            ["recalculated"] = result.Evaluated,
            ["errors"] = errors,
            ["circular"] = result.CircularCells.Select(c => Report.Address(c.Sheet, c.Row, c.Column)).ToList(),
        };
        if (_notes.Count > 0)
        {
            payload["notes"] = _notes;
        }

        foreach (var (key, value) in _fields)
        {
            payload[key] = value;
        }

        Report.Write(payload);
        return 0;
    }
}
