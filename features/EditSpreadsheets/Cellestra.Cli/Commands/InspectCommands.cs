using System.CommandLine;
using System.Globalization;
using Cellestra.Cli.Infrastructure;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Formulas;
using Cellestra.Core.Formulas.Ast;
using Cellestra.Core.Formulas.Parsing;
using Cellestra.Core.Formulas.References;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// Looking at a workbook the way someone debugging it does: <c>trace</c> follows a cell's formula
/// to what it reads (or who reads it), and <c>lint</c> finds the mistakes that are not errors —
/// a number stored as text, a SUM that stops one row short, a formula that breaks the pattern of
/// its column. <c>validate</c> says whether the file is broken; these say whether it is right.
/// </summary>
internal static class InspectCommands
{
    // ---- trace ------------------------------------------------------------

    public static Command Trace()
    {
        var file = Options.File();
        var cell = new Argument<string>("cell") { Description = "The cell to trace from, e.g. Sheet1.D5 (or a single-cell name)" };
        var dependents = new Option<bool>("--dependents") { Description = "Follow who reads this cell, instead of what it reads (precedents)" };
        var depth = new Option<int>("--depth")
        {
            Description = "How many levels to follow (0 = all the way)",
            DefaultValueFactory = _ => 1,
        };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("trace", "Follow a formula to its precedents (what it reads) or dependents (who reads it)")
        {
            file, cell, dependents, depth, locale, json,
        };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            Workspace.Recalculate(loaded.Workbook);
            var effective = culture ?? loaded.Workbook.Settings.EffectiveLocale;
            var workbook = loaded.Workbook;
            var (sheet, address) = Cells.ResolveCell(workbook, parse.GetValue(cell)!);
            bool downstream = parse.GetValue(dependents);
            int maxDepth = parse.GetValue(depth);
            if (maxDepth < 0)
            {
                throw new CliException("--depth must be 0 (all) or a positive number.");
            }

            var index = FormulaIndex.Build(workbook);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = new Node(sheet.Name, new CellRange(new CellAddress(address.Column, address.Row)));
            var budget = new NodeBudget();
            var tree = Walk(workbook, index, root, downstream, maxDepth == 0 ? int.MaxValue : maxDepth, 0, visited, effective, budget);

            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    cell = tree.Address,
                    direction = downstream ? "dependents" : "precedents",
                    depth = maxDepth,
                    truncated = budget.Exhausted ? true : (bool?)null,
                    tree,
                });
                return 0;
            }

            Print(tree, downstream, 0);
            if (budget.Exhausted)
            {
                Console.Error.WriteLine($"note: stopped after {NodeBudget.Limit} nodes; narrow it with --depth.");
            }

            return 0;
        });

        return command;
    }

    /// <summary>A cell, or a whole range kept as one node so <c>SUM(D2:D4)</c> reads as one line.</summary>
    private sealed record Node(string Sheet, CellRange Range)
    {
        public string Address => Range.RowCount == 1 && Range.ColumnCount == 1
            ? Report.Address(Sheet, Range.Top, Range.Left)
            : $"{Sheet}.{Range.WithSheet(null)}";

        public bool IsCell => Range.RowCount == 1 && Range.ColumnCount == 1;
    }

    private sealed class TraceNode
    {
        public string Address { get; init; } = string.Empty;

        public string? Formula { get; init; }

        public string? Display { get; init; }

        public string? Error { get; init; }

        public int? Cells { get; init; }

        public bool? Truncated { get; init; }

        public bool? Cycle { get; init; }

        public List<TraceNode>? Precedents { get; init; }

        public List<TraceNode>? Dependents { get; init; }
    }

    /// <summary>A ceiling on how much of a fan-out tree one trace prints, so a hub cell can't run away.</summary>
    private sealed class NodeBudget
    {
        public const int Limit = 2000;

        private int _left = Limit;

        public bool Exhausted { get; private set; }

        /// <summary>Claim room for <paramref name="count"/> more nodes; false once the budget is spent.</summary>
        public bool Take(int count)
        {
            if (count > _left)
            {
                Exhausted = true;
                return false;
            }

            _left -= count;
            return true;
        }
    }

    private static TraceNode Walk(Workbook workbook, FormulaIndex index, Node node, bool downstream, int maxDepth, int depth, HashSet<string> visited, CultureInfo culture, NodeBudget budget)
    {
        var sheet = workbook.FindSheet(node.Sheet);
        var cell = node.IsCell ? sheet?.Find(node.Range.Top, node.Range.Left) : null;
        bool cycle = !visited.Add(node.Address);
        string? display = cell is null || cell.Value.IsEmpty ? null : Cells.Display(workbook, sheet!, node.Range.Top, node.Range.Left, culture);

        List<TraceNode>? children = null;
        bool truncated = false;
        if (!cycle)
        {
            // One lookup either way: asking twice doubles the work on a wide fan-out.
            var next = downstream ? index.DependentsOf(node) : index.PrecedentsOf(node);
            if (next.Count > 0)
            {
                if (depth < maxDepth && budget.Take(next.Count))
                {
                    children = [.. next.Select(n => Walk(workbook, index, n, downstream, maxDepth, depth + 1, visited, culture, budget))];
                }
                else
                {
                    truncated = true;
                }
            }
        }

        visited.Remove(node.Address); // a cell may appear again on another branch; only a true loop is a cycle

        return new TraceNode
        {
            Address = node.Address,
            Formula = cell is { IsFormula: true } ? "=" + cell.Formula : null,
            Display = display,
            Error = cell is { Value.IsError: true } ? cell.Value.AsError.Token : null,
            Cells = node.IsCell ? null : node.Range.RowCount * node.Range.ColumnCount,
            Truncated = truncated ? true : null,
            Cycle = cycle ? true : null,
            Precedents = downstream ? null : children,
            Dependents = downstream ? children : null,
        };
    }

    private static void Print(TraceNode node, bool downstream, int indent)
    {
        var line = new System.Text.StringBuilder();
        line.Append(' ', indent * 2).Append(node.Address);
        if (node.Cells is { } n)
        {
            line.Append(" (").Append(n).Append(" cells)");
        }

        if (node.Formula is not null)
        {
            line.Append("  ").Append(node.Formula);
        }

        if (node.Display is not null)
        {
            line.Append("  → ").Append(node.Display);
        }

        if (node.Cycle == true)
        {
            line.Append("  [cycle]");
        }

        if (node.Truncated == true)
        {
            line.Append("  …");
        }

        Console.Out.WriteLine(line.ToString());
        foreach (var child in (downstream ? node.Dependents : node.Precedents) ?? [])
        {
            Print(child, downstream, indent + 1);
        }
    }

    /// <summary>Every formula in the workbook, parsed once, with the cells and ranges it reads.</summary>
    private sealed class FormulaIndex
    {
        private readonly Dictionary<string, (Worksheet Sheet, int Row, int Column, Expr Expr, List<Node> Reads)> _formulas = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The other direction: which formula cells read a given single cell.</summary>
        private readonly Dictionary<(string Sheet, int Row, int Column), List<Node>> _readers = [];

        /// <summary>Range reads, kept apart — few enough to scan, and they can't be keyed by one cell.</summary>
        private readonly List<(Node Read, Node Formula)> _rangeReaders = [];

        private readonly Dictionary<string, Dictionary<(int Row, int Column), Expr>> _bySheet = new(StringComparer.OrdinalIgnoreCase);

        public static FormulaIndex Build(Workbook workbook)
        {
            var index = new FormulaIndex();
            foreach (var sheet in workbook.Worksheets)
            {
                foreach (var (row, column, cell) in sheet.EnumerateCells())
                {
                    if (!cell.IsFormula || !Parser.TryParse(cell.Formula!, out var expr, out _) || expr is null)
                    {
                        continue;
                    }

                    var reads = new List<Node>();
                    CollectReads(expr, sheet.Name, workbook, reads);
                    index._formulas[Report.Address(sheet.Name, row, column)] = (sheet, row, column, expr, reads);

                    if (!index._bySheet.TryGetValue(sheet.Name, out var onSheet))
                    {
                        index._bySheet[sheet.Name] = onSheet = [];
                    }

                    onSheet[(row, column)] = expr;

                    var self = new Node(sheet.Name, new CellRange(new CellAddress(column, row)));
                    foreach (var read in reads)
                    {
                        if (read.IsCell)
                        {
                            var key = (read.Sheet.ToUpperInvariant(), read.Range.Top, read.Range.Left);
                            if (!index._readers.TryGetValue(key, out var readers))
                            {
                                index._readers[key] = readers = [];
                            }

                            readers.Add(self);
                        }
                        else
                        {
                            index._rangeReaders.Add((read, self));
                        }
                    }
                }
            }

            return index;
        }

        /// <summary>The parsed formulas of one sheet, by position — parsed once, in <see cref="Build"/>.</summary>
        public IReadOnlyDictionary<(int Row, int Column), Expr> OnSheet(Worksheet sheet) =>
            _bySheet.TryGetValue(sheet.Name, out var formulas) ? formulas : [];

        public IReadOnlyList<Node> PrecedentsOf(Node node) =>
            node.IsCell && _formulas.TryGetValue(node.Address, out var entry) ? entry.Reads : [];

        /// <summary>Formula cells that read any cell of <paramref name="node"/> — a lookup, not a sweep.</summary>
        public IReadOnlyList<Node> DependentsOf(Node node)
        {
            var result = new List<Node>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(Node reader)
            {
                if (seen.Add(reader.Address))
                {
                    result.Add(reader);
                }
            }

            if (node.IsCell)
            {
                if (_readers.TryGetValue((node.Sheet.ToUpperInvariant(), node.Range.Top, node.Range.Left), out var direct))
                {
                    foreach (var reader in direct)
                    {
                        Add(reader);
                    }
                }
            }
            else
            {
                foreach (var ((sheet, row, column), readers) in _readers)
                {
                    if (string.Equals(sheet, node.Sheet, StringComparison.OrdinalIgnoreCase) && node.Range.Contains(row, column))
                    {
                        foreach (var reader in readers)
                        {
                            Add(reader);
                        }
                    }
                }
            }

            foreach (var (read, formula) in _rangeReaders)
            {
                if (string.Equals(read.Sheet, node.Sheet, StringComparison.OrdinalIgnoreCase) && read.Range.Intersects(node.Range))
                {
                    Add(formula);
                }
            }

            return result;
        }

        public IEnumerable<(Worksheet Sheet, int Row, int Column, Expr Expr, IReadOnlyList<Node> Reads)> All =>
            _formulas.Values.Select(v => (v.Sheet, v.Row, v.Column, v.Expr, (IReadOnlyList<Node>)v.Reads));

        private static void CollectReads(Expr expr, string home, Workbook workbook, List<Node> into)
        {
            switch (expr)
            {
                case ReferenceExpr r:
                    into.Add(new Node(r.Address.Sheet ?? home, new CellRange(r.Address.WithoutSheet().Relative())));
                    break;
                case RangeExpr r:
                    into.Add(new Node(r.Range.Sheet ?? home, r.Range.WithSheet(null)));
                    break;
                case NameExpr n when workbook.FindNamedRange(n.Name, home)?.Target is { } target:
                    into.Add(new Node(target.Sheet ?? home, target.WithSheet(null)));
                    break;
                case UnaryExpr u:
                    CollectReads(u.Operand, home, workbook, into);
                    break;
                case BinaryExpr b:
                    CollectReads(b.Left, home, workbook, into);
                    CollectReads(b.Right, home, workbook, into);
                    break;
                case FunctionCallExpr f:
                    foreach (var argument in f.Arguments)
                    {
                        CollectReads(argument, home, workbook, into);
                    }

                    break;
            }
        }
    }

    // ---- lint -------------------------------------------------------------

    public static Command Lint()
    {
        var file = Options.File();
        var sheet = Options.Sheet();
        var strict = new Option<bool>("--strict") { Description = "Exit 1 on warnings too, not only on errors" };
        var locale = Options.Locale();
        var json = Options.Json();
        var command = new Command("lint", "Find likely mistakes that are not errors: numbers as text, short ranges, inconsistent formulas, bad headers, rule violations")
        {
            file, sheet, strict, locale, json,
        };

        command.SetAction(parse =>
        {
            var culture = Locale.Resolve(parse.GetValue(locale));
            var loaded = Workspace.Load(parse.GetValue(file)!, null, culture);
            var result = Workspace.Recalculate(loaded.Workbook);
            var effective = culture ?? loaded.Workbook.Settings.EffectiveLocale;
            var workbook = loaded.Workbook;
            string? only = parse.GetValue(sheet);
            if (only is not null)
            {
                Cells.PickSheet(workbook, only); // validates the name
            }

            var findings = new List<Finding>();
            var sheets = workbook.Worksheets.Where(s => !s.IsChartSheet && (only is null || string.Equals(s.Name, only, StringComparison.OrdinalIgnoreCase))).ToList();
            var index = FormulaIndex.Build(workbook);

            // Errors — what validate reports, so lint is a superset.
            foreach (var s in sheets)
            {
                foreach (var (row, column, cell) in s.EnumerateCells())
                {
                    if (cell.Value.IsError)
                    {
                        findings.Add(new("error", "error-value", Report.Address(s.Name, row, column), $"{cell.Value.AsError.Token}{(cell.IsFormula ? " from =" + cell.Formula : "")}"));
                    }
                }
            }

            foreach (var c in result.CircularCells.Where(c => only is null || string.Equals(c.Sheet, only, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new("error", "circular", Report.Address(c.Sheet, c.Row, c.Column), "part of a circular reference"));
            }

            foreach (var s in sheets)
            {
                CheckStoredAsText(workbook, s, effective, findings);
                CheckTableHeaders(s, findings);
                CheckValidationRules(s, effective, findings);
                CheckFormulaPatterns(index, s, findings);
            }

            CheckFormulas(workbook, index, sheets, findings);
            CheckUnusedNames(workbook, index, findings);

            findings.Sort((a, b) => Rank(a.Severity).CompareTo(Rank(b.Severity)));
            int errors = findings.Count(f => f.Severity == "error");
            int warnings = findings.Count(f => f.Severity == "warning");
            bool failed = errors > 0 || (parse.GetValue(strict) && warnings > 0);

            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    ok = !failed,
                    file = parse.GetValue(file),
                    errors,
                    warnings,
                    infos = findings.Count - errors - warnings,
                    findings,
                });
                return failed ? 1 : 0;
            }

            foreach (var f in findings)
            {
                Console.Out.WriteLine($"{f.Severity}\t{f.Cell}\t{f.Code}\t{f.Message}");
            }

            Console.Out.WriteLine(findings.Count == 0
                ? "OK — nothing to report."
                : $"{errors} error(s), {warnings} warning(s), {findings.Count - errors - warnings} note(s).");
            return failed ? 1 : 0;
        });

        return command;
    }

    private sealed record Finding(string Severity, string Code, string Cell, string Message);

    private static int Rank(string severity) => severity switch { "error" => 0, "warning" => 1, _ => 2 };

    private static void CheckStoredAsText(Workbook workbook, Worksheet sheet, CultureInfo culture, List<Finding> findings)
    {
        foreach (var (row, column, cell) in sheet.EnumerateCells())
        {
            if (!cell.Value.IsText || cell.IsFormula)
            {
                continue;
            }

            string text = cell.Value.AsText.Trim();
            if (text.Length == 0 || workbook.Styles.Get(cell.StyleId).NumberFormat.Kind == NumberFormatKind.Text)
            {
                continue; // an explicit text format means it's text on purpose
            }

            var parsed = InputParser.Parse(text, culture);
            if (parsed.IsFormula || !parsed.Value.IsNumber)
            {
                continue;
            }

            bool date = parsed.SuggestedFormat is { } f && f.Kind is NumberFormatKind.Date or NumberFormatKind.DateTime or NumberFormatKind.Time;
            findings.Add(new("warning", date ? "date-as-text" : "number-as-text", Report.Address(sheet.Name, row, column),
                $"\"{text}\" is text; it won't add up or sort as a {(date ? "date" : "number")} — re-enter it, or apply a set op with a typed value"));
        }
    }

    private static void CheckTableHeaders(Worksheet sheet, List<Finding> findings)
    {
        foreach (var table in sheet.Tables.Where(t => t.HasHeader))
        {
            for (int column = table.Range.Left; column <= table.Range.Right; column++)
            {
                var value = sheet.GetValue(table.Range.Top, column);
                if (!value.IsText || value.AsText.Trim().Length == 0)
                {
                    findings.Add(new("warning", "table-header", Report.Address(sheet.Name, table.Range.Top, column),
                        $"table '{table.Name}' header is {(value.IsEmpty ? "empty" : "not text")}"));
                }
            }
        }
    }

    private static void CheckValidationRules(Worksheet sheet, CultureInfo culture, List<Finding> findings)
    {
        if (sheet.Validations.Count == 0)
        {
            return;
        }

        // One pass over what's populated, asking the sheet which rule covers each cell — not a pass per rule.
        foreach (var (row, column, cell) in sheet.EnumerateCells())
        {
            if (cell.IsFormula || cell.Value.IsEmpty || sheet.FindValidation(row, column) is not { } rule)
            {
                continue;
            }

            if (!Validator.IsValid(rule, cell.Value, culture, range => ResolveValues(sheet, range)))
            {
                findings.Add(new("warning", "validation", Report.Address(sheet.Name, row, column),
                    $"\"{cell.Value.ToInvariantString()}\" violates the {rule.Kind.ToString().ToLowerInvariant()} rule on {rule.Target}"));
            }
        }
    }

    private static IEnumerable<Cellestra.Core.Values.CellValue> ResolveValues(Worksheet sheet, CellRange range)
    {
        foreach (var (row, column) in range.Positions())
        {
            yield return sheet.GetValue(row, column);
        }
    }

    /// <summary>Formula-level checks: all-empty inputs, and an aggregate over a run that stops just short of more data.</summary>
    private static void CheckFormulas(Workbook workbook, FormulaIndex index, List<Worksheet> sheets, List<Finding> findings)
    {
        var names = new HashSet<string>(sheets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var (sheet, row, column, expr, reads) in index.All)
        {
            if (!names.Contains(sheet.Name))
            {
                continue;
            }

            string address = Report.Address(sheet.Name, row, column);
            if (reads.Count > 0 && reads.All(r => IsEmpty(workbook, r)))
            {
                findings.Add(new("warning", "empty-inputs", address, $"={sheet.Find(row, column)!.Formula} reads only empty cells"));
            }

            if (expr is FunctionCallExpr call && call.Name.ToUpperInvariant() is "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "COUNTA")
            {
                foreach (var argument in call.Arguments.OfType<RangeExpr>())
                {
                    var range = argument.Range;
                    var target = workbook.FindSheet(range.Sheet ?? sheet.Name);
                    if (target is null)
                    {
                        continue;
                    }

                    // A single column (or row) run: is the next cell past its end populated and not the formula itself?
                    if (range.ColumnCount == 1 && range.RowCount > 1)
                    {
                        int next = range.Bottom + 1;
                        if (!(target == sheet && next == row && range.Left == column) && target.GetValue(next, range.Left).IsNumber)
                        {
                            findings.Add(new("warning", "short-range", address, $"{call.Name}({range.WithSheet(null)}) stops at row {range.Bottom + 1} but {Report.Address(target.Name, next, range.Left)} holds a number"));
                        }
                    }
                    else if (range.RowCount == 1 && range.ColumnCount > 1)
                    {
                        int next = range.Right + 1;
                        if (!(target == sheet && next == column && range.Top == row) && target.GetValue(range.Top, next).IsNumber)
                        {
                            findings.Add(new("warning", "short-range", address, $"{call.Name}({range.WithSheet(null)}) stops at column {SheetDimensions.ColumnToLetters(range.Right)} but {Report.Address(target.Name, range.Top, next)} holds a number"));
                        }
                    }
                }
            }
        }
    }

    private static bool IsEmpty(Workbook workbook, Node node)
    {
        var sheet = workbook.FindSheet(node.Sheet);
        if (sheet is null)
        {
            return true;
        }

        // A small range: probe it. A big one (a whole column, say): walk what's populated instead.
        if ((long)node.Range.RowCount * node.Range.ColumnCount <= 4096)
        {
            foreach (var (row, column) in node.Range.Positions())
            {
                if (!sheet.GetValue(row, column).IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }

        return !sheet.EnumerateCells().Any(c => node.Range.Contains(c.Row, c.Column) && !c.Cell.Value.IsEmpty);
    }

    /// <summary>
    /// A formula that breaks the pattern of its neighbours: the cells above and below (or left and
    /// right) agree once their relative references are shifted onto this row, and this one differs.
    /// </summary>
    private static void CheckFormulaPatterns(FormulaIndex index, Worksheet sheet, List<Finding> findings)
    {
        var formulas = index.OnSheet(sheet); // parsed once in Build; re-parsing the sheet here doubled the work

        foreach (var ((row, column), expr) in formulas)
        {
            string here = FormulaWriter.Write(expr);
            if (Breaks(formulas, (row - 1, column), (row + 1, column), 0, 1, here) || Breaks(formulas, (row, column - 1), (row, column + 1), 1, 0, here))
            {
                findings.Add(new("warning", "inconsistent-formula", Report.Address(sheet.Name, row, column),
                    $"={sheet.Find(row, column)!.Formula} differs from the pattern of its neighbours"));
            }
        }
    }

    private static bool Breaks(IReadOnlyDictionary<(int Row, int Column), Expr> formulas, (int, int) before, (int, int) after, int columnDelta, int rowDelta, string here)
    {
        if (!formulas.TryGetValue(before, out var a) || !formulas.TryGetValue(after, out var b))
        {
            return false;
        }

        string fromBefore = FormulaWriter.Write(ReferenceAdjuster.ForCopy(a, columnDelta, rowDelta));
        string fromAfter = FormulaWriter.Write(ReferenceAdjuster.ForCopy(b, -columnDelta, -rowDelta));
        return string.Equals(fromBefore, fromAfter, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fromBefore, here, StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckUnusedNames(Workbook workbook, FormulaIndex index, List<Finding> findings)
    {
        if (workbook.NamedRanges.Count == 0)
        {
            return;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, _, expr, _) in index.All)
        {
            CollectNames(expr, used);
        }

        foreach (var named in workbook.NamedRanges)
        {
            if (!used.Contains(named.Name))
            {
                findings.Add(new("info", "unused-name", $"{named.Name}", $"named range {named.Name} = {named.Target} is not used by any formula"));
            }
        }
    }

    private static void CollectNames(Expr expr, HashSet<string> into)
    {
        switch (expr)
        {
            case NameExpr n:
                into.Add(n.Name);
                break;
            case UnaryExpr u:
                CollectNames(u.Operand, into);
                break;
            case BinaryExpr b:
                CollectNames(b.Left, into);
                CollectNames(b.Right, into);
                break;
            case FunctionCallExpr f:
                foreach (var a in f.Arguments)
                {
                    CollectNames(a, into);
                }

                break;
        }
    }
}
