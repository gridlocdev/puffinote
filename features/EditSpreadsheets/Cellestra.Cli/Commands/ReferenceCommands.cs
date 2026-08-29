using System.CommandLine;
using System.Globalization;
using System.Text;
using Cellestra.Cli.Infrastructure;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Commands;

/// <summary>
/// The whole CLI on one page. <c>man</c> renders every command, argument, option, and default from
/// the live command tree (so it cannot drift from <c>--help</c>), framed by the conventions that
/// matter more than any one verb — addressing, the edit contract, exit codes, and the JSON shapes.
/// A script or an agent reads this once instead of walking <c>--help</c> a verb at a time.
/// <c>schema</c> prints the reference for one of the JSON contracts (<c>apply</c>, <c>report</c>).
/// </summary>
internal static class ReferenceCommands
{
    public static Command Man()
    {
        var json = Options.Json();
        var command = new Command("man", "Print the complete reference — every command, option, convention, and JSON shape — as one page")
        {
            json,
        };

        command.SetAction(parse =>
        {
            var root = CliApplication.BuildRoot();
            if (parse.GetValue(json))
            {
                Report.Write(new
                {
                    tool = "cellestra",
                    contract = Report.ContractVersion,
                    description = root.Description,
                    commands = Flatten(root, "cellestra").Select(c => new
                    {
                        name = c.Path,
                        description = c.Command.Description,
                        arguments = c.Command.Arguments.Select(a => new
                        {
                            name = a.Name,
                            description = a.Description,
                            optional = a.Arity.MinimumNumberOfValues == 0 ? true : (bool?)null,
                        }),
                        options = c.Command.Options.Where(o => !IsBuiltIn(o)).Select(o => new
                        {
                            name = o.Name,
                            aliases = o.Aliases.Count > 0 ? o.Aliases : null,
                            description = o.Description,
                            required = o.Required ? true : (bool?)null,
                            @default = DefaultText(o),
                            flag = o.ValueType == typeof(bool) ? true : (bool?)null,
                        }),
                    }),
                    exitCodes = ExitCodes,
                    schemas = new { apply = ApplyCommands.Reference, report = ReportReference },
                });
                return 0;
            }

            Console.Out.Write(RenderMarkdown(root));
            return 0;
        });

        return command;
    }

    public static Command Schema()
    {
        var which = new Argument<string>("contract") { Description = "apply | report" };
        var command = new Command("schema", "Print the reference for a JSON contract: the `apply` ops document, or the `--json` report") { which };

        command.SetAction(parse =>
        {
            string name = (parse.GetValue(which) ?? string.Empty).Trim().ToLowerInvariant();
            Console.Out.WriteLine(name switch
            {
                "apply" or "ops" => ApplyCommands.Reference,
                "report" or "json" or "cell" => ReportReference,
                _ => throw new CliException("Known contracts: apply, report."),
            });
            return 0;
        });

        return command;
    }

    /// <summary>
    /// The agent skill for this tool — a SKILL.md that Claude Code (and hosts with the same
    /// convention) load from <c>~/.claude/skills/&lt;name&gt;/SKILL.md</c>. It is embedded in the
    /// binary so an installed <c>cellestra</c> carries instructions that match its own version, and
    /// <c>--install</c> puts them where the user's agents look, on any machine, in any directory.
    /// </summary>
    public static Command Skill()
    {
        var install = new Option<bool>("--install") { Description = "Write the skill into the skills directory instead of printing it" };
        var directory = new Option<string?>("--dir")
        {
            Description = "Skills directory to install into (default: ~/.claude/skills; the skill goes in its own `cellestra/` folder)",
        };
        var command = new Command("skill", "Print the agent skill for this tool, or --install it into ~/.claude/skills/cellestra/") { install, directory };

        command.SetAction(parse =>
        {
            string text = SkillText();
            if (!parse.GetValue(install))
            {
                Console.Out.Write(text);
                return 0;
            }

            string root = parse.GetValue(directory)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills");
            string folder = Path.Combine(root, "cellestra");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "SKILL.md");
            Workspace.SaveText(text, path);
            Console.Error.WriteLine($"installed {path}");
            return 0;
        });

        return command;
    }

    private static string SkillText()
    {
        using var stream = typeof(ReferenceCommands).Assembly.GetManifestResourceStream("SKILL.md")
            ?? throw new InvalidOperationException("SKILL.md is not embedded in this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ---- Content ----------------------------------------------------------

    private static readonly Dictionary<string, string> ExitCodes = new()
    {
        ["0"] = "Success. For validate: no errors. For diff: identical. For find: at least one match.",
        ["1"] = "A user-caused problem (bad address, missing file, unknown sheet, refused overwrite, invalid JSON) — one `cellestra: …` line on stderr, and under --json a {\"ok\": false, \"error\"} document on stdout. Also: validate found errors; diff found differences; find found nothing; eval hit an error value.",
        ["other / stack trace"] = "A bug in Cellestra, not in your input.",
    };

    public const string ReportReference = """
        --json — the machine-readable reply
        ====================================

        Cell (the one shape used everywhere a cell appears: get, cat.cells, find.matches, validate.errors, the edit report's changed/errors)
          {"cell": "Sheet1.B2",      always sheet-qualified
           "value": 12.5,            typed: number | string | true/false; absent when empty or an error
           "display": "12.50",       what the GUI shows (number format + locale)
           "formula": "=B3*C3",      only for formula cells, with the leading =
           "error": "#NAME?",        only when the value is an error
           "format": "0.00",         only when the number format is not General
           "style": {"bold": true, "fill": "#EEEEEE", "align": "center", "border": "thin"}}
                                     only what differs from the default, using the same keys as apply's style op
                                     (bold, italic, underline, strikethrough, font, size, color, fill, align, valign, wrap, border, borderColor)

        Edit report (set, apply, sort, chart, clean-up, … — anything that writes, with --json)
          {"ok": true,
           "file": "book.ods",        where it was written (-o or the input with --in-place)
           "dryRun": true,            only on --dry-run: nothing was written
           "changed": [Cell, …],      the cells the ops touched, with their values AFTER recalculation
                                      (structural ops — insert/delete rows/cols/sheets, hide/show, freeze, width, height — list none)
           "recalculated": 12,        formula cells evaluated
           "errors": [Cell, …],       EVERY error cell now in the workbook — check this after each write
           "circular": ["Sheet1.A1"],
           "notes": ["…"],            the lines a human would have seen on stderr, when there are any
           …}                         verb-specific extras: applied, removed, sheet, table, sheets, range

        Reads
          get       → Cell
          cat       → {"sheet", "range", "rows": [[display…]…], "cells": [Cell…]}   cells = populated only, row-major
          find      → {"term", "matches": [Cell…]}
          validate  → {"ok", "file", "opens", "errors": [Cell…], "circular": [addr…]}    exit 1 when !ok
          diff      → {"same", "differences", "addedSheets", "removedSheets", "cells": [{"cell","before","after"}…]}   exit 1 when !same
          eval      → {"ok", "formula", "value", "display", "error"}
          sheets    → [{"name","usedRange","rows","columns","cells","chart","frozenRows","frozenColumns",
                        "tables":[{"name","range","header","headerColumn","bandedRows"}], "merges":["A1:D1"],
                        "widths":{"A":120}, "heights":{"1":28}, "hiddenColumns":["C"], "hiddenRows":["5"],
                        "validations":[{"range","kind","operator","value1","value2","list","source","response"}]}…]   (absent keys = default)
          info      → {"file", "sheets": […], "namedRanges": [{"name","target","scope"}…], "locale", "calculation", "populatedCells"}
          stats     → {"sheet","range","count","numeric","sum","average","min","max"}
          functions → [{"name","signature","category","returns","summary","arguments":[{"name","kind","required","repeatable","description"}],"example"}…]
          name list → [{"name","target","scope"}…]
          man       → the whole command tree

        Contract
          man --json carries "contract": 1 — the version of these shapes and of the apply document. Additive keys never bump it.

        Errors
          any verb  → {"ok": false, "error": "…"} on stdout (exit 1), plus the usual one-line stderr message.
        """;

    private const string Conventions = """
        ## Conventions (read these first)

        - **Addressing matches the app.** `Sheet1.B2` is a cell, `Sheet1.A1:C10` a range; omit the sheet
          (`B2`, `A1:C10`) to mean `--sheet` if given, else the active/first sheet. Rows are 1-based,
          columns are letters (`B`) or 1-based numbers. Spans: `3:5`, `B:D`.
        - **Reads go to stdout**, in a table by default; `--format csv|tsv|json` for pipes. `-` is stdin/stdout.
        - **Edits never overwrite without `--in-place` (`-i`) or `-o <file>`.** Every write is atomic (temp file
          + rename) and keeps unmodeled content — charts, pivots, anything Cellestra doesn't model — verbatim.
        - **`--json` on any command** replaces the text with one JSON document. A write reports the cells it
          changed *and every error cell left in the workbook*, so a bad formula is visible in the same reply.
          `cellestra schema report` shows the shapes. A user error under `--json` is `{"ok": false, "error"}`.
        - **`apply` is the batch verb**: one JSON document of typed ops (cells, blocks, styles, layout,
          structure, data, charts), one load, one recalculation, one save. `cellestra schema apply` lists the ops.
          Prefer it to a `set` per cell — it is faster and it fails as a whole, never half-written.
        - **Check after you write**: `validate` (exit 1 on any error cell or circular reference) and `diff`
          (exit 1 on any difference) are made for scripts and CI. `eval "=…"` tests a formula with no file.
        - **Same engine as the app**: evaluation, OpenFormula translation, and locale handling are identical;
          `--locale en-US` pins parsing/formatting for reproducible output.
        - **No network, ever.** Only the files named on the command line are touched.

        """;

    // ---- Rendering --------------------------------------------------------

    private sealed record Entry(string Path, Command Command);

    private static IEnumerable<Entry> Flatten(Command command, string path)
    {
        foreach (var child in command.Subcommands.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            string childPath = $"{path} {child.Name}";
            if (child.Subcommands.Count > 0)
            {
                foreach (var nested in Flatten(child, childPath))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return new Entry(childPath, child);
            }
        }
    }

    private static string RenderMarkdown(RootCommand root)
    {
        var sb = new StringBuilder();
        sb.Append("# cellestra — reference\n\n");
        sb.Append(root.Description).Append("\n\n");
        sb.Append("Generated from the live command tree by `cellestra man`; `cellestra <command> --help` shows any one command.\n\n");
        sb.Append(Conventions).Append('\n');

        sb.Append("## Commands\n\n");
        var entries = Flatten(root, "cellestra").ToList();
        foreach (var entry in entries)
        {
            sb.Append("- `").Append(entry.Path).Append("` — ").Append(entry.Command.Description).Append('\n');
        }

        sb.Append('\n');

        foreach (var entry in entries)
        {
            sb.Append("### ").Append(entry.Path).Append("\n\n");
            sb.Append(entry.Command.Description).Append("\n\n");
            sb.Append("    ").Append(Usage(entry)).Append("\n\n");

            if (entry.Command.Arguments.Count > 0)
            {
                foreach (var argument in entry.Command.Arguments)
                {
                    string name = argument.Arity.MinimumNumberOfValues == 0 ? $"[{argument.Name}]" : $"<{argument.Name}>";
                    sb.Append("- `").Append(name).Append("` — ").Append(argument.Description).Append('\n');
                }
            }

            var options = entry.Command.Options.Where(o => !IsBuiltIn(o)).ToList();
            foreach (var option in options)
            {
                sb.Append("- `").Append(OptionSpelling(option)).Append('`');
                if (option.Required)
                {
                    sb.Append(" (required)");
                }

                sb.Append(" — ").Append(option.Description);
                if (DefaultText(option) is { } d)
                {
                    sb.Append(" [default: ").Append(d).Append(']');
                }

                sb.Append('\n');
            }

            sb.Append('\n');
        }

        sb.Append("## Exit codes\n\n");
        foreach (var (code, meaning) in ExitCodes)
        {
            sb.Append("- `").Append(code).Append("` — ").Append(meaning).Append('\n');
        }

        sb.Append("\n## JSON contracts\n\n```\n").Append(ApplyCommands.Reference).Append("\n```\n\n```\n").Append(ReportReference).Append("\n```\n");
        return sb.ToString();
    }

    private static string Usage(Entry entry)
    {
        var sb = new StringBuilder(entry.Path);
        foreach (var argument in entry.Command.Arguments)
        {
            sb.Append(' ').Append(argument.Arity.MinimumNumberOfValues == 0 ? $"[{argument.Name}]" : $"<{argument.Name}>");
        }

        if (entry.Command.Options.Any(o => !IsBuiltIn(o)))
        {
            sb.Append(" [options]");
        }

        return sb.ToString();
    }

    private static string OptionSpelling(Option option)
    {
        var names = new List<string>();
        names.AddRange(option.Aliases.OrderBy(a => a.Length));
        names.Add(option.Name);
        string spelled = string.Join(", ", names);
        return option.ValueType == typeof(bool) ? spelled : $"{spelled} <{option.HelpName ?? "value"}>";
    }

    private static string? DefaultText(Option option)
    {
        if (!option.HasDefaultValue || option.ValueType == typeof(bool))
        {
            return null;
        }

        return option.GetDefaultValue() switch
        {
            null => null,
            string s when s.Length == 0 => null,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }

    /// <summary>System.CommandLine's own --help / --version, which every command carries and nobody needs listed.</summary>
    private static bool IsBuiltIn(Option option) => option.Name is "--help" or "--version";
}
