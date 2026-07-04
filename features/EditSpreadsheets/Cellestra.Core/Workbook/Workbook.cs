using Cellestra.Charts;
using Cellestra.Core.Styling;

namespace Cellestra.Core.Model;

/// <summary>
/// The whole document: an ordered list of worksheets, workbook-wide named ranges, the shared
/// style pool, and document settings. Pure model — it knows nothing about windows or drawing
/// (ARCHITECTURE: Core has zero UI dependencies).
/// </summary>
public sealed class Workbook
{
    private readonly List<Worksheet> _worksheets = [];
    private readonly List<NamedRange> _namedRanges = [];

    public IReadOnlyList<Worksheet> Worksheets => _worksheets;

    public IReadOnlyList<NamedRange> NamedRanges => _namedRanges;

    /// <summary>The shared style pool; every cell's <c>StyleId</c> indexes into it.</summary>
    public StylePool Styles { get; } = new();

    public DocumentSettings Settings { get; } = new();

    public int SheetCount => _worksheets.Count;

    /// <summary>The active worksheet per <see cref="DocumentSettings.ActiveSheetIndex"/>.</summary>
    public Worksheet ActiveSheet
    {
        get
        {
            if (_worksheets.Count == 0)
            {
                throw new InvalidOperationException("Workbook has no worksheets.");
            }

            int index = Math.Clamp(Settings.ActiveSheetIndex, 0, _worksheets.Count - 1);
            return _worksheets[index];
        }
    }

    /// <summary>Create a new, empty workbook with a single <c>Sheet1</c> (PRD §8.8).</summary>
    public static Workbook CreateNew()
    {
        var workbook = new Workbook();
        workbook.AddSheet(Defaults.FirstSheetName);
        workbook.Settings.ActiveSheetIndex = 0;
        return workbook;
    }

    public Worksheet AddSheet(string name)
    {
        SheetName.Validate(name);
        if (FindSheet(name) is not null)
        {
            throw new InvalidOperationException($"A worksheet named '{name}' already exists.");
        }

        var sheet = new Worksheet(name);
        _worksheets.Add(sheet);
        return sheet;
    }

    /// <summary>
    /// Rename a worksheet, enforcing the <see cref="SheetName"/> rules and uniqueness (a name
    /// equal to the sheet's current one is a no-op and allowed). Throws on an invalid or
    /// duplicate name so the UI can surface the reason.
    /// </summary>
    public void RenameSheet(Worksheet sheet, string newName)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        SheetName.Validate(newName);

        var existing = FindSheet(newName);
        if (existing is not null && !ReferenceEquals(existing, sheet))
        {
            throw new InvalidOperationException($"A worksheet named '{newName}' already exists.");
        }

        sheet.Name = newName;
    }

    /// <summary>Add a worksheet with the next unused default name (Sheet2, Sheet3, …).</summary>
    public Worksheet AddSheet()
    {
        string name = Defaults.NextSheetName([.. _worksheets.Select(s => s.Name)]);
        return AddSheet(name);
    }

    /// <summary>
    /// Add a chart sheet carrying <paramref name="chart"/>, named with the next unused
    /// <c>Chart1</c>, <c>Chart2</c>, … . The sheet appears in the tab order like any other.
    /// </summary>
    public Worksheet AddChartSheet(ChartDefinition chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        string name = Defaults.NextChartSheetName([.. _worksheets.Select(s => s.Name)]);
        var sheet = AddSheet(name);
        sheet.Chart = chart;
        return sheet;
    }

    /// <summary>
    /// Add a sheet while loading a document: enforces uniqueness only, not the interactive
    /// <see cref="SheetName"/> rules, so a foreign file with an unusual sheet name still opens
    /// and round-trips verbatim (PRD §8.1).
    /// </summary>
    internal Worksheet AddSheetForLoad(string name)
    {
        if (FindSheet(name) is not null)
        {
            throw new InvalidOperationException($"A worksheet named '{name}' already exists.");
        }

        var sheet = new Worksheet(name);
        _worksheets.Add(sheet);
        return sheet;
    }

    /// <summary>
    /// Duplicate a worksheet (task 7): insert a deep copy immediately after the original under a
    /// fresh "<i>Name</i> copy" name and return it. Works for data sheets and chart sheets alike.
    /// </summary>
    public Worksheet DuplicateSheet(Worksheet source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int index = _worksheets.IndexOf(source);
        if (index < 0)
        {
            throw new InvalidOperationException("The sheet to duplicate is not in this workbook.");
        }

        var copy = source.Clone(UniqueDuplicateName(source.Name));

        // Tables persist as document-global ODF database ranges, so give the copies fresh names rather
        // than letting two sheets share one (which is invalid in the saved file). Table names aren't
        // used in formulas (Golden Rule #9), so renaming them is safe.
        var takenTableNames = new HashSet<string>(
            _worksheets.SelectMany(s => s.Tables).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var table in copy.Tables.ToList())
        {
            if (!takenTableNames.Add(table.Name))
            {
                string fresh = UniqueTableName(table.Name, takenTableNames);
                copy.ReplaceTable(table, table with { Name = fresh });
                takenTableNames.Add(fresh);
            }
        }

        _worksheets.Insert(index + 1, copy);
        return copy;
    }

    private static string UniqueTableName(string baseName, ISet<string> taken)
    {
        for (int n = 2; ; n++)
        {
            string candidate = $"{baseName}_{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>A unique "<i>base</i> copy" name (then "copy 2", "copy 3", …), trimmed to the length cap.</summary>
    private string UniqueDuplicateName(string baseName)
    {
        for (int n = 0; ; n++)
        {
            string suffix = n == 0 ? " copy" : $" copy {n + 1}";
            int headMax = Math.Max(1, SheetName.MaxLength - suffix.Length);
            string head = baseName.Length > headMax ? baseName[..headMax] : baseName;
            string candidate = head + suffix;
            if (FindSheet(candidate) is null)
            {
                return candidate;
            }
        }
    }

    public void InsertSheet(int index, Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        SheetName.Validate(sheet.Name);
        if (FindSheet(sheet.Name) is not null)
        {
            throw new InvalidOperationException($"A worksheet named '{sheet.Name}' already exists.");
        }

        _worksheets.Insert(Math.Clamp(index, 0, _worksheets.Count), sheet);
    }

    public bool RemoveSheet(Worksheet sheet)
    {
        if (_worksheets.Count <= 1)
        {
            throw new InvalidOperationException("A workbook must keep at least one worksheet.");
        }

        int index = _worksheets.IndexOf(sheet);
        if (index < 0)
        {
            return false;
        }

        _worksheets.RemoveAt(index);
        if (Settings.ActiveSheetIndex >= _worksheets.Count)
        {
            Settings.ActiveSheetIndex = _worksheets.Count - 1;
        }

        return true;
    }

    public void MoveSheet(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _worksheets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        }

        toIndex = Math.Clamp(toIndex, 0, _worksheets.Count - 1);
        var sheet = _worksheets[fromIndex];
        _worksheets.RemoveAt(fromIndex);
        _worksheets.Insert(toIndex, sheet);
    }

    public Worksheet? FindSheet(string name) =>
        _worksheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public int IndexOf(Worksheet sheet) => _worksheets.IndexOf(sheet);

    // ---- Named ranges -----------------------------------------------------

    /// <summary>Resolve a name to a range, preferring a sheet-scoped name over a global one.</summary>
    public NamedRange? FindNamedRange(string name, string? sheetScope = null)
    {
        if (sheetScope is not null)
        {
            var local = _namedRanges.FirstOrDefault(n =>
                string.Equals(n.Scope, sheetScope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
            if (local is not null)
            {
                return local;
            }
        }

        return _namedRanges.FirstOrDefault(n =>
            n.IsGlobal && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void AddNamedRange(NamedRange namedRange)
    {
        ArgumentNullException.ThrowIfNull(namedRange);

        // Duplicate check is by EXACT scope — a sheet-local name may shadow a global
        // one of the same name (that's the whole point of sheet scope).
        bool exists = _namedRanges.Any(n =>
            string.Equals(n.Name, namedRange.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.Scope, namedRange.Scope, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            throw new InvalidOperationException($"A named range '{namedRange.Name}' already exists in this scope.");
        }

        _namedRanges.Add(namedRange);
    }

    public bool RemoveNamedRange(string name, string? scope = null)
    {
        var existing = _namedRanges.FirstOrDefault(n =>
            string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.Scope, scope, StringComparison.OrdinalIgnoreCase));
        return existing is not null && _namedRanges.Remove(existing);
    }
}
