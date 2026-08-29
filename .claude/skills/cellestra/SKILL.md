---
name: cellestra
description: >-
  Create, edit, inspect, and verify OpenDocument spreadsheets (.ods) headlessly with the `cellestra`
  command-line tool — a real spreadsheet engine, no GUI needed. Use this whenever a task involves
  building a spreadsheet or workbook from data, filling cells or formulas, styling a sheet (bold
  headers, number formats, column widths, frozen rows), adding charts, sorting, tables, data
  validation, converting CSV ⇄ .ods, checking a workbook for formula errors, or diffing two .ods
  files — and whenever someone mentions .ods, Cellestra, "spreadsheet", or "workbook". Covers the
  build → check → read-back loop, the `apply` batch document, the `--json` reply shapes, and the
  gotchas (--in-place, addressing, quoting formulas, dates and locale).
---

# Working with `.ods` spreadsheets through `cellestra`

`cellestra` is the command-line face of the Cellestra spreadsheet editor: the same engine as the
app, with no GUI and no network, so what it computes is exactly what the app would show. Use it to
**create** workbooks from a script and to **verify** them without opening anything. This skill is
about doing that fast and accurately. The tool's own complete reference is `cellestra man` (one
page — read it once); `cellestra schema apply` and `cellestra schema report` print the two JSON
contracts on their own.

## Prerequisite

`cellestra` must be on `PATH` — check with `cellestra --version` or `cellestra man | head`. If it
is missing, tell the user: it installs from the Puffinote repository with `just install-cli` (or
`just install-cellestra-cli`), and this skill itself can be (re)installed from the tool with
`cellestra skill --install`. Do not try to build it from source unless you are inside that
repository and were asked to.

## The loop: build → check → read back

**Always pass `--json`.** Every reply becomes one JSON document with the same cell shape, and a
write's report includes **every error cell now in the workbook** — you learn about a `#NAME?` in
the same reply that wrote it, not on a later inspection. A user error under `--json` is
`{"ok": false, "error": "…"}` on stdout with exit 1, so there is nothing to parse on stderr.

```bash
cellestra new book.ods --sheets "Budget,Notes" --json           # refuses to overwrite an existing file
cellestra apply book.ods ops.json --in-place --json               # ⇐ the build step, see below
cellestra validate book.ods --json                                # exit 1 on any error cell / circular ref
cellestra cat book.ods --sheet Budget --json                      # rows (display strings) + cells (typed)
cellestra get book.ods Budget.D5 --json                           # one cell: value, display, formula, error
cellestra diff before.ods after.ods --json                        # exit 1 if they differ; lists the cells
```

Check `errors` in the apply report (or run `validate`) after **every** write, and `cat --json` the
range you built before you say it is done — the `display` string is exactly what a person opening
the file sees, so a wrong number format or an unformatted date shows up there.

## Building a sheet: `apply` — one document, one save

Do not `set` cells one at a time. One `apply` is one process, one load, one recalculation, one
atomic save, and it fails as a whole: the first bad op aborts the batch and nothing is written.
The document is `{"ops": [...]}` or a bare array; ops run in order. Addresses are `Sheet.B2` and
`Sheet.A1:C10`; without a sheet they mean the op's `"sheet"` if given, else the active/first sheet.
Spans are `"3:5"` (rows) and `"B:D"` (columns). Colors are `#RRGGBB`; sizes are points.

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
exactly as if typed into the app — `"=…"` is a formula, `"1,234.5"` / `"12%"` / `"2026-01-31"`
become numbers (a typed date also gets a date format), anything else is text; `null` clears the
cell. Formulas go in as plain strings — no shell quoting problems, because the document is a file
or stdin (`cat ops.json | cellestra apply book.ods - --in-place --json`).

Every op (`cellestra schema apply` is the authoritative list with every field): `set`, `clear`,
`style` (`bold`/`italic`/`underline`/`strikethrough`, `font`, `size`, `color`, `fill`, `align`,
`valign`, `wrap`, number `format`, `border`/`borderColor`), `width`, `height`, `freeze`,
`merge`/`unmerge`, `hide`/`show`, `add-sheet`/`rename-sheet`/`delete-sheet`,
`insert-rows`/`delete-rows`/`insert-cols`/`delete-cols`, `sort`, `table`, `validation`,
`name`/`unname`, `hyperlink`, `chart`. `--dry-run` applies in memory and returns the same report
(changed cells, errors) without writing — use it to test a batch of formulas against a real file.

For a big grid of data you already have as CSV: `cellestra import data.csv --into book.ods --sheet Data`
(creates the workbook if absent, replaces that sheet's contents), then `apply` the formulas and
styling on top. `cellestra convert a.csv b.ods` / `convert b.ods c.csv` converts whole files.

## Reading what is there

- `sheets --json` / `info --json` — sheet names, used ranges, frozen panes, tables, named ranges, locale.
- `cat --json` — `rows` (display strings for the bounding grid) and `cells` (populated cells only,
  typed, with `formula`/`error`/`format` when present). Narrow with `--range A1:D20`, `--sheet`.
- `find book.ods "VLOOKUP" --in formulas --json` — where a formula or value is used (exit 1 if none).
- `stats book.ods Budget.D2:D4 --json`; `functions SUMIFS --json` (signature and arguments);
  `eval "=ROUND(355/113, 4)" --json` — try a formula with no file at all.

## Gotchas that cost time

- **Nothing overwrites without `--in-place` (`-i`) or `-o <file>`** — every writing verb, `apply`
  and the sheet verbs included. The refusal is a clean exit-1 message; add the flag. `new` refuses
  to overwrite an existing file at all.
- **`--json` cannot combine with `-o -`** (both want stdout). Write to a file.
- **Addresses come back sheet-qualified** (`Budget.D5`) even when you wrote `D5`.
- **Formulas on the shell**: `cellestra set book.ods B2 '=IF(A1>0,"yes","no")' -i` — single-quote
  the whole thing. For more than one or two, use an `apply` document instead.
- **Dates**: give ISO strings (`"2026-01-31"`) and set a date `format` on the range
  (`"YYYY-MM-DD"`, `"MMM YYYY"`, `"DD/MM/YYYY"`); a raw serial number in `display` means the
  format is missing.
- **Locale**: pass `--locale en-US` when parsing `"1,234.5"`-style input or when output must match
  across machines; otherwise the document's locale, else the system's, decides separators.
- **Charts**: pie/doughnut take one series; scatter needs `xValues`; bubble also `bubbleSizes`;
  `stacking` only on column/bar/area. The op rejects anything else with a clear message.
- **`errors` is workbook-wide**: an apply report lists every error cell in the file, not just the
  ones you touched — a pre-existing `#REF!` elsewhere is still worth telling the user about.
- **Editing someone else's file is safe**: charts, pivots, images, and anything else the tool does
  not model round-trip verbatim through an edit. `diff` before/after to prove it.
