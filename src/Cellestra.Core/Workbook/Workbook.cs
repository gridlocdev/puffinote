using Cellestra.Core.Styling;

namespace Cellestra.Core.Model;

/// <summary>
/// The whole document: an ordered list of worksheets, workbook-wide named ranges, the shared
/// style pool, and document settings. Pure model — it knows nothing about windows or drawing
/// (ARCHITECTURE: Core has zero UI dependencies).
/// </summary>
public sealed class Workbook
{
    private readonly List<Worksheet> _worksheets = new();
    private readonly List<NamedRange> _namedRanges = new();

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
        string name = Defaults.NextSheetName(_worksheets.Select(s => s.Name).ToList());
        return AddSheet(name);
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
