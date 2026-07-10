using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Model;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;
using ColumnFilter = Cellestra.Core.Data.ColumnFilter;
using DataTable = Cellestra.Core.Data.Table;
using FilterEngine = Cellestra.Core.Data.FilterEngine;
using FilterSpec = Cellestra.Core.Data.FilterSpec;
using SortLevel = Cellestra.Core.Data.SortLevel;
using SortSpec = Cellestra.Core.Data.SortSpec;

namespace Cellestra.Core.Serialization.Ods;

public static partial class OdsContentXml
{
    private const string AnonymousDatabaseRangeName = "__Anonymous_Sheet__";

    private static readonly (string Prefix, string Operator)[] ConditionOperators =
    [
        (">=", ">="), ("<=", "<="), ("<>", "!="), (">", ">"), ("<", "<"), ("=", "="),
    ];

    // ---- table:database-range (filter + sort, PRD §8.10–§8.12) -----------

    /// <summary>
    /// Emit one <c>table:database-range</c> per worksheet table and per sheet-level AutoFilter,
    /// carrying the filter region, the active filter conditions, and the sort order so they
    /// round-trip. Filtering sets row visibility (written as <c>table:visibility="filter"</c> by
    /// the row writer); reopening reapplies the filter so hidden rows stay hidden.
    /// </summary>
    private static void AddDatabaseRanges(XElement spreadsheet, Workbook workbook)
    {
        var ranges = new List<XElement>();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var table in sheet.Tables)
            {
                ranges.Add(WriteDatabaseRange(sheet.Name, table.Name, table.Range, table.HasHeader, table.Filter, table.Sort));
            }

            if (sheet.ActiveFilter is { } region)
            {
                bool hasHeader = sheet.ActiveFilterSpec?.HasHeader ?? sheet.ActiveSort?.HasHeader ?? true;
                ranges.Add(WriteDatabaseRange(sheet.Name, name: null, region, hasHeader, sheet.ActiveFilterSpec, sheet.ActiveSort));
            }
        }

        if (ranges.Count == 0)
        {
            return;
        }

        var container = new XElement(Table + DatabaseRanges);
        foreach (var range in ranges)
        {
            container.Add(range);
        }

        spreadsheet.Add(container);
    }

    private static XElement WriteDatabaseRange(string sheetName, string? name, CellRange range, bool hasHeader, FilterSpec? filter, SortSpec? sort)
    {
        var element = new XElement(Table + DatabaseRange,
            new XAttribute(Table + TargetRangeAddress, OdfRangeAddress(range.WithSheet(sheetName), sheetName)),
            new XAttribute(Table + DisplayFilterButtons, "true"),
            new XAttribute(Table + ContainsHeader, hasHeader ? "true" : "false"));

        // table:name is required by ODF; anonymous sheet AutoFilters get a stable sentinel
        // so a reader can tell a real table apart from a bare filter region.
        element.Add(new XAttribute(Table + "name", name ?? AnonymousDatabaseRangeName));

        if (sort is { Levels.Count: > 0 })
        {
            element.Add(WriteSort(sort, range.Left));
        }

        if (filter is { Columns.Count: > 0 })
        {
            element.Add(WriteFilter(filter, range.Left));
        }

        return element;
    }

    private static XElement WriteSort(SortSpec sort, int rangeLeft)
    {
        var element = new XElement(Table + Sort);
        foreach (var level in sort.Levels)
        {
            element.Add(new XElement(Table + SortBy,
                new XAttribute(Table + FieldNumber, level.Column - rangeLeft),
                new XAttribute(Table + Order, level.Ascending ? "ascending" : "descending")));
        }

        return element;
    }

    private static XElement WriteFilter(FilterSpec filter, int rangeLeft)
    {
        // AND across columns; within a column, a value checklist is OR'd '=' conditions.
        var and = new XElement(Table + FilterAnd);
        foreach (var (column, columnFilter) in filter.Columns.OrderBy(kv => kv.Key))
        {
            int field = column - rangeLeft;

            if (columnFilter.AllowedValues is { Count: > 0 } allowed)
            {
                if (allowed.Count == 1)
                {
                    and.Add(Condition(field, "=", allowed.First()));
                }
                else
                {
                    var or = new XElement(Table + FilterOr);
                    foreach (var value in allowed)
                    {
                        or.Add(Condition(field, "=", value));
                    }

                    and.Add(or);
                }
            }

            if (columnFilter.Condition is { Length: > 0 } condition)
            {
                var (op, value) = SplitCondition(condition);
                and.Add(Condition(field, op, value));
            }
        }

        return new XElement(Table + Filter, and);

        static XElement Condition(int field, string op, string value) => new(
            Table + FilterCondition,
            new XAttribute(Table + FieldNumber, field),
            new XAttribute(Table + Operator, op),
            new XAttribute(Table + "value", value));
    }

    /// <summary>
    /// Split a SUMIF-style criteria string (<c>"&gt;5"</c>, <c>"&lt;=10"</c>, <c>"&lt;&gt;x"</c>, plain
    /// <c>"text"</c>) into an ODF operator and its value. Unprefixed criteria are an equality match.
    /// </summary>
    private static (string Operator, string Value) SplitCondition(string condition)
    {
        foreach (var (prefix, op) in ConditionOperators)
        {
            if (condition.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (op, condition[prefix.Length..]);
            }
        }

        return ("=", condition);
    }

    private static string JoinCondition(string op, string value) => op switch
    {
        "=" => value,
        "!=" => "<>" + value,
        _ => op + value,
    };

    // ---- table:database-range (read side) --------------------------------

    private static void ReadDatabaseRanges(XElement container, Workbook workbook)
    {
        foreach (var element in container.Elements(Table + DatabaseRange))
        {
            ReadDatabaseRange(element, workbook);
        }
    }

    private static void ReadDatabaseRange(XElement element, Workbook workbook)
    {
        string? address = element.Attribute(Table + TargetRangeAddress)?.Value;
        if (address is null)
        {
            return;
        }

        string internalRef = OpenFormulaTranslator.FromOpenFormula("of:=[" + address.TrimStart('$') + "]");
        if (!CellRange.TryParse(internalRef, out var range) || range.Sheet is null)
        {
            return;
        }

        var sheet = workbook.FindSheet(range.Sheet);
        if (sheet is null)
        {
            return;
        }

        bool hasHeader = element.Attribute(Table + ContainsHeader)?.Value != "false";

        // Normalize to a plain relative, sheet-less range so it compares equal to one built in code.
        var localRange = new CellRange(
            new CellAddress(range.Left, range.Top),
            new CellAddress(range.Right, range.Bottom));

        var sort = ReadSort(element, localRange.Left, hasHeader);
        var filter = ReadFilter(element, localRange.Left, hasHeader);

        string? name = element.Attribute(Table + "name")?.Value;
        if (name is not null && name != AnonymousDatabaseRangeName)
        {
            // A named database range is a Cellestra table; merge filter/sort onto it (or add it).
            var existing = sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (existing is not null)
            {
                sheet.ReplaceTable(existing, existing with { Filter = filter, Sort = sort });
            }
            else if (!sheet.Tables.Any(t => t.Range.Intersects(localRange)))
            {
                sheet.AddTable(new DataTable(name, localRange, hasHeader) { Filter = filter, Sort = sort });
            }
        }
        else
        {
            // An anonymous database range is the sheet-level AutoFilter region.
            sheet.ActiveFilter = localRange;
            sheet.ActiveFilterSpec = filter;
            sheet.ActiveSort = sort;
        }

        // Reapply the filter so rows it hides come back hidden on open (PRD §8.12).
        if (filter is { Columns.Count: > 0 })
        {
            FilterEngine.Apply(sheet, localRange, filter, CultureInfo.InvariantCulture);
        }
    }

    private static SortSpec? ReadSort(XElement databaseRange, int rangeLeft, bool hasHeader)
    {
        var sortElement = databaseRange.Element(Table + Sort);
        if (sortElement is null)
        {
            return null;
        }

        var levels = new List<SortLevel>();
        foreach (var by in sortElement.Elements(Table + SortBy))
        {
            if (int.TryParse(by.Attribute(Table + FieldNumber)?.Value, out int field))
            {
                bool ascending = by.Attribute(Table + Order)?.Value != "descending";
                levels.Add(new SortLevel(rangeLeft + field, ascending));
            }
        }

        return levels.Count > 0 ? new SortSpec(levels, hasHeader) : null;
    }

    private static FilterSpec? ReadFilter(XElement databaseRange, int rangeLeft, bool hasHeader)
    {
        var filterElement = databaseRange.Element(Table + Filter);
        var top = filterElement?.Element(Table + FilterAnd) ?? filterElement?.Element(Table + FilterOr);
        if (top is null)
        {
            return null;
        }

        // Per field, gather '=' values into a checklist and the first relational op into a condition.
        var allowed = new Dictionary<int, HashSet<string>>();
        var conditions = new Dictionary<int, string>();

        void Absorb(XElement condition)
        {
            if (!int.TryParse(condition.Attribute(Table + FieldNumber)?.Value, out int field))
            {
                return;
            }

            string op = condition.Attribute(Table + Operator)?.Value ?? "=";
            string value = condition.Attribute(Table + "value")?.Value ?? string.Empty;
            int column = rangeLeft + field;

            if (op == "=")
            {
                (allowed.TryGetValue(column, out var set) ? set : allowed[column] = new HashSet<string>(StringComparer.Ordinal)).Add(value);
            }
            else if (!conditions.ContainsKey(column))
            {
                conditions[column] = JoinCondition(op, value);
            }
        }

        foreach (var node in top.Elements())
        {
            if (node.Name == Table + FilterCondition)
            {
                Absorb(node);
            }
            else if (node.Name == Table + FilterOr || node.Name == Table + FilterAnd)
            {
                foreach (var nested in node.Elements(Table + FilterCondition))
                {
                    Absorb(nested);
                }
            }
        }

        var columns = new Dictionary<int, ColumnFilter>();
        foreach (int column in allowed.Keys.Union(conditions.Keys))
        {
            columns[column] = new ColumnFilter
            {
                AllowedValues = allowed.TryGetValue(column, out var set) ? set : null,
                Condition = conditions.TryGetValue(column, out var cond) ? cond : null,
            };
        }

        return columns.Count > 0 ? new FilterSpec(columns, hasHeader) : null;
    }
}
