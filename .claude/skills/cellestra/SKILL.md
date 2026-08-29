---
name: cellestra
description: >-
  Create, edit, inspect, and verify OpenDocument spreadsheets (.ods) headlessly with the `cellestra`
  CLI — Cellestra's own engine, no GUI. Use this whenever a task involves building a spreadsheet or
  workbook from data, filling cells or formulas, styling a sheet (bold headers, number formats,
  widths, frozen rows), adding charts, sorting/tables/validation, converting CSV ⇄ .ods, checking a
  workbook for formula errors, or diffing two .ods files — and whenever someone mentions .ods,
  Cellestra, "spreadsheet", "workbook", or asks to verify that a spreadsheet change worked without
  launching the app. Covers the build → check → fix loop, the `apply` batch document, the `--json`
  reply shapes, and the gotchas (--in-place, addressing, quoting formulas).
---

# Working with `.ods` spreadsheets through `cellestra`

`cellestra` is a third consumer of `Cellestra.Core` — the same brain as the desktop app, with no
Avalonia and no network. What it computes is what the app would show, so it is the right tool both
for **creating** workbooks from a script and for **verifying** a change to Cellestra itself without
a display. This skill is about doing that fast and accurately; the CLI's own one-page reference is
`cellestra man` (read it once; `cellestra schema apply` / `schema report` print the two JSON
contracts on their own).

## Finding the binary

In preference order:

1. `cellestra` on `PATH` (after `just install-cli`).
2. The last build: `features/EditSpreadsheets/Cellestra.Cli/bin/<os>/Debug/net10.0/cellestra`
   (`<os>` is `osx` or `linux` — bin/obj are OS-specific here). Build it with
   `dotnet build features/EditSpreadsheets/Cellestra.Cli` (or `just build-cellestra`).
3. `dotnet run --project features/EditSpreadsheets/Cellestra.Cli -- <args>` — slowest; fine for one call.

`cellestra --help` if you doubt a name; `cellestra man` for everything.

## The loop: build → check → read back

**Always add `--json`.** It turns every reply into one document with the same cell shape, and a
write's report includes **every error cell now in the workbook** — you learn about `#NAME?` in the
same reply that wrote it, not on a later inspection. A user error under `--json` is
`{"ok": false, "error": "…"}` on stdout (exit 1), so there is nothing to parse on stderr.

```bash
cellestra new book.ods --sheets "Budget,Notes" --json           # refuses to overwrite an existing file
cellestra apply book.ods ops.json --in-place --json               # ⇐ the build step, see below
cellestra validate book.ods --json                                # exit 1 on any error cell / circular ref
cellestra cat book.ods --sheet Budget --json                      # rows (display strings) + cells (typed)
cellestra get book.ods Budget.D14 --json                          # one cell: value, display, formula, error
cellestra diff before.ods after.ods --json                        # exit 1 if they differ; lists the cells
```

Check `errors` in the apply report (or run `validate`) after **every** write, and `cat --json` the
range you built before you claim it is done — the `display` string is exactly what a person opening
the file sees, so a wrong number format or an unformatted date is visible there.

## Building a sheet: `apply`, one document, one save

Do not `set` cells one at a time. One `apply` is one process, one load, one recalculation, one
atomic save, and it fails as a whole — the first bad op aborts the batch and nothing is written.
`{"ops": [...]}` or a bare array; ops run in order. Addresses: `Sheet.B2`, `Sheet.A1:C10`; without a
sheet they mean the op's `"sheet"` if given, else the active/first sheet. Spans: `"3:5"`, `"B:D"`.

```json
{"ops": [
  {"op":"set", "at":"Budget.A1", "values":[
    ["Month","Rent","Food","Total"],
    ["2026-01-01", 1500, 420, "=B2+C2"],
    ["2026-02-01", 1500, 465, "=B3+C3"],
    ["2026-03-01", 1500, 390, "=B4+C4"]]},
  {"op":"set", "cell":"Budget.A5", "value":"Total"},
  {"op":"set", "cell":"Budget.D5", "value":"=SUM(D2:D4)"},
  {"op":"style", "range":"Budget.A1:D1", "bold":true, "fill":"#EEEEEE", "align":"center", "border":"thin"},
  {"op":"style", "range":"Budget.B2:D5", "format":"#,##0.00"},
  {"op":"style", "range":"Budget.A2:A4", "format":"MMM YYYY"},
  {"op":"style", "range":"Budget.A5:D5", "bold":true},
  {"op":"width", "sheet":"Budget", "columns":"A:D", "width":90},
  {"op":"freeze", "sheet":"Budget", "rows":1},
  {"op":"table", "range":"Budget.A1:D4", "name":"Spend"},
  {"op":"name", "name":"GrandTotal", "range":"Budget.D5"},
  {"op":"chart", "type":"column", "title":"Monthly spend", "from":"Budget.A1:C4", "yTitle":"USD"}
]}
```

Value typing in `set`: a JSON **number** or **bool** is stored as that type; a **string** is parsed
exactly as if typed into the GUI — `"=…"` is a formula, `"1,234.5"` / `"12%"` / `"2026-01-31"`
become numbers (a typed date also gets a date format), anything else is text; `null` clears.
Formulas go in as strings — no shell quoting problems, because the document is a file or stdin
(`cat ops.json | cellestra apply book.ods - --in-place --json`).

Every op the verbs offer is available (`cellestra schema apply` is the authoritative list):
`set`, `clear`, `style` (bold/italic/underline/strikethrough, `font`, `size`, `color`, `fill`,
`align`, `valign`, `wrap`, `format`, `border`/`borderColor`), `width`, `height`, `freeze`,
`merge`/`unmerge`, `hide`/`show`, `add-sheet`/`rename-sheet`/`delete-sheet`,
`insert-rows`/`delete-rows`/`insert-cols`/`delete-cols`, `sort`, `table`, `validation`,
`name`/`unname`, `hyperlink`, `chart`. `--dry-run` applies in memory and gives the same report
(changed cells, errors) without writing — use it to test a formula batch against a real file.

For a big grid from data you already have, `cellestra import data.csv --into book.ods --sheet Data`
(creates the workbook if absent, replaces the sheet's contents) and then `apply` the formulas and
styling on top. `convert a.csv b.ods` / `convert b.ods c.csv` for whole-file conversion.

## Reading what is there

- `sheets --json` / `info --json` — names, used ranges, frozen panes, tables, named ranges, locale.
- `cat --json` — `rows` (display strings, the bounding grid) and `cells` (populated only, typed,
  with `formula`/`error`/`format` when present). `--range A1:D20`, `--sheet`, `--formula`.
- `find book.ods "=VLOOKUP" --in formulas --json` — where a formula or value is used; exit 1 when nothing matches.
- `stats book.ods Budget.D2:D4 --json`; `functions SUMIFS --json` — signature and arguments from the
  same registry the app's autocomplete uses; `eval "=ROUND(355/113, 4)" --json` — a formula with no file.

## Gotchas that cost time

- **Nothing overwrites without `--in-place` (`-i`) or `-o <file>`** — every writing verb, `apply`
  and the sheet verbs included. The refusal is a clean exit-1 line; add the flag. `new` refuses to
  overwrite an existing file at all.
- **`--json` cannot combine with `-o -`** (both want stdout). Give a file.
- **Addresses are sheet-qualified in every reply** (`Budget.D5`), even when you wrote `D5`.
- **Formulas on the shell**: `cellestra set book.ods B2 '=IF(A1>0,"yes","no")' -i` — single-quote
  the whole thing. For more than one or two, put them in an `apply` document instead.
- **Dates**: give ISO strings (`"2026-01-31"`) and set a `format` (`"YYYY-MM-DD"`, `"MMM YYYY"`,
  `"DD/MM/YYYY"`) on the range; a raw serial in `display` means the format is missing.
- **Locale**: pass `--locale en-US` when parsing `"1,234.5"`-style input or comparing output across
  machines; otherwise the document's locale, else the system's, decides decimal separators.
- **Pie/doughnut charts take one series; scatter needs `xValues`; bubble also `bubbleSizes`;
  `stacking` only on column/bar/area** — the op rejects anything else with a clear message.
- **Whole-workbook errors**: an apply report's `errors` lists every error cell in the workbook, not
  just yours — a pre-existing `#REF!` elsewhere is still worth mentioning to the user.
- **Unmodeled content survives edits** (charts, pivots, images Cellestra doesn't model round-trip
  verbatim), so editing a file someone else made is safe; `diff` the before/after to prove it.

## When the task is about Cellestra itself

Use the CLI to verify engine, serialization, or formatting changes without the GUI:
`cellestra recalc`, `validate`, and `diff before.ods after.ods` across an open→save→reopen show
exactly which cells changed where Git sees only a binary blob. Read
`features/EditSpreadsheets/AGENTS.md` first — build/test gates (`CELLESTRA_HEADLESS_NULL_DRAWING=1`
before any test), where CLI code lives (`Cellestra.Cli/Commands/`, `Infrastructure/EditSession.cs`
and `Report.cs` for the `--json` contract, `ApplyCommands.cs` for the ops), and the rule that new
logic goes in Core with the CLI as a thin caller.
