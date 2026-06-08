# Cellestra — Product Requirements

> **Name:** "Cellestra" — from *cell* + *orchestra*, pronounced **cel-LES-trah**. (A spreadsheet is a lot of little cells playing together in tune.)
>
> **Status:** Draft / pre-v1. This document changes slowly. It describes *what* I'm building and *why*, not *how*. The "how" lives in `ARCHITECTURE.md`.

---

## 1. The problem

I'd like to edit spreadsheets on my Mac, but the current options each have something I don't like:

- **Microsoft Excel** requires a paid subscription, and I don't really use spreadsheets often enough to justify the cost.
- **LibreOffice Calc** works and is fully featured, I found the interface to feel too dated and heavy for me. Some features I was used to from Microsoft Office were explicit non-goals, when I switch between the two programs it can be jarring to have them behave differently for the same task.
- **OnlyOffice** is polished, but the developers bundle some cloud sync and AI stuff to help support the product that I personally don't need.
- **Apple Numbers** looks nice, but is built around a different mental model than a classic spreadsheet; lots of clicking, and editing the grid feels indirect.

I couldn't find a **modern-looking, offline, keyboard-focused** desktop app on macOS whose *first-class* file format is `.ods` and whose editing feels fast and direct.

Cellestra is the app I want: a clean, keyboard-friendly spreadsheet editor for `.ods` files, built from scratch so I control the experience and the software supply chain.

## 2. Who it's for

I'm building this for myself: a technically comfortable Mac owner who

- regularly opens, edits, and saves `.ods` files (my own, or files exchanged with LibreOffice users),
- cares about how my tools look and feel, and finds the current options frustrating,
- values a small, auditable dependency footprint and no questionable origins, and
- wants normal spreadsheet power (formulas, formatting, multiple sheets) without the bloat of a full office suite.

It is deliberately **not** built for large enterprises, heavy financial modelers who need pivot tables and macros, or people who live entirely in cloud spreadsheets. If it's useful to anyone with similar needs, great — but it's scoped to mine.

## 3. What success looks like

Cellestra v1 is a success if I can:

1. Double-click an `.ods` file and have it open quickly and correctly.
2. Read and edit it comfortably with the keyboard, the way a spreadsheet "should" feel.
3. Use everyday formulas and formatting without surprises.
4. Save the file back to `.ods` and have **LibreOffice open it without complaints or data loss**.

If I open a Cellestra-saved file in LibreOffice and notice nothing wrong, the bar is cleared.

### Concrete success criteria

- **Round-trip safety:** open → save → open in LibreOffice loses no data, formulas, or formatting that v1 supports, and preserves unsupported content per §8.
- **Speed (measured on Apple Silicon):** a 10,000-filled-cell sheet opens in **under 500 ms**; scrolling and selection stay at **60 fps**; a full recalculation of 10,000 dependent cells completes in **under 100 ms**. These are the benchmark targets in `Cellestra.Benchmarks`.
- **Correctness:** every v1 function (§8.2) returns the same result LibreOffice would for the same inputs; this is covered by tests.
- **Feel:** core editing (move, type, edit, copy, paste, fill) is fully keyboard-driven and matches spreadsheet muscle memory.
- **Trust:** the dependency list is short and explainable (see `ARCHITECTURE.md` for the exact set); nothing in the supply chain is a mystery.

## 4. Core features (v1)

### Files
- Open and save `.ods` files.
- Import and export **CSV** (precise format in §8.4).
- Recent-files list.
- Safe round-tripping (preserve content Cellestra doesn't model — see §8.1).

### The grid
- Multiple worksheets per workbook, with tabs to switch, add, rename, reorder, and delete.
- Fast scrolling over large sheets — vertical with the scroll wheel/trackpad, **horizontal with ⇧ + scroll wheel** (or a two-finger horizontal swipe on a trackpad).
- Resize rows and columns; insert and delete rows and columns.
- **Hide and unhide** rows and columns.
- **Merge and unmerge cells.**
- Freeze panes (keep header rows/columns visible while scrolling).
- Select cells, ranges, whole rows, whole columns.
- **Zoom** in/out of the sheet view (50%–400%).

### Editing
- Type directly into a cell; edit in place or in the formula bar.
- A **Name Box** that shows the active cell and lets the user jump to any cell or named range.
- Full keyboard navigation (arrows, Tab, Enter, Home/End, page keys, jump-to-edge).
- Cut / copy / paste, plus **Paste Special** (values only, formats only, formulas only).
- Clipboard interop with other apps as tab-separated text.
- **Fill handle** plus **Fill Down / Fill Right** (copy-fill; see §8.15).
- **AutoSum** and a **Format Painter** for copying formatting.
- **Insert hyperlinks** into cells (web/file links).
- **Undo / redo** for every edit (scope defined in §8.5).
- Find & Replace.

### Formulas
- A formula bar and in-cell formula entry starting with `=`.
- An **Insert Function** picker and **autocomplete with argument hints** while typing.
- The function library in §8.2.
- Cell and range references, including **absolute vs relative** (`$A$1` vs `A1`).
- **References across sheets** (`Sheet2.A1`) and **named ranges** (e.g. `TaxRate`).
- Automatic recalculation when inputs change, plus a manual "recalculate now" and an automatic/manual calculation toggle.
- Clear error values (`#DIV/0!`, `#REF!`, `#VALUE!`, `#NAME?`, `#N/A`) with readable explanations.
- References update correctly when you copy/fill formulas or insert/delete rows and columns.

### Data
- **Sort** a range or table by one or more columns (**multi-level** "then by"), ascending/descending, with a header-aware option (§8.11).
- **Filter** rows with per-column **AutoFilter** dropdowns — filter by value or by condition; filtered-out rows are **hidden, not deleted** (§8.12).
- **Tables (⌘T)** — turn a range into a named, **auto-expanding** table with banded-row styling and built-in filter headers; the table grows/shrinks as data is added or removed at its edges (§8.10).
- **Data validation** — restrict what a cell accepts (dropdown **pick lists**, number/date/text-length rules) with optional input and error messages (§8.13).
- **Remove duplicates** from a selection, and **Text to Columns** to split delimited text across columns (§8.14).

### Formatting
- Number formats (currency, percentage, decimals, dates) that respect locale per §8.3, with one-click toggles and increase/decrease-decimals.
- Font family, size, bold/italic/underline, text color.
- Cell background fills and borders.
- Text alignment (horizontal and vertical) and wrapping.

## 5. Key user flows

### Open and read
1. User double-clicks an `.ods` file (or uses File → Open).
2. The file loads; the first sheet appears immediately showing the **cached values stored in the file** (so display is instant). A background recalculation then runs unless calculation mode is Manual (§8.6).
3. User scrolls, clicks around, switches sheets.

### Edit a value
1. User clicks a cell (or arrows to it) and starts typing.
2. The cell enters edit mode; user types and presses Enter.
3. The value is stored, any dependent formulas recalculate, and the display updates.

### Enter a formula
1. User types `=` to start a formula.
2. User references cells/ranges (by typing or clicking) and confirms with Enter.
3. Cellestra parses it, computes the result, and shows it; the formula bar shows the formula when the cell is selected.

### Copy, paste, fill
1. User selects a range and copies.
2. User pastes elsewhere; relative references shift appropriately, absolute ones don't.
3. Alternatively, user drags the fill handle, or uses Fill Down / Fill Right.

### Format cells
1. User selects a range and uses the command bar (or Format menu / Format Cells dialog).
2. User changes number format, font, color, borders, alignment.
3. Changes apply immediately and are saved into the `.ods` styles on save.

### Sort, filter, and make a table
1. User selects a range of data with a header row and presses **⌘T** (or Data ▸ Table) to make it a table — banded rows and filter headers appear, and the table is named.
2. User clicks a column's filter dropdown to show only matching rows, and/or opens **Sort** to order by one or more columns.
3. As the user types new rows just below the table, it **auto-expands** to include them; sort/filter state is remembered and re-applied.

### Save
1. User saves (⌘S).
2. Cellestra writes a valid `.ods` file that LibreOffice opens cleanly — tables persist as `.ods` database ranges, filters and sort state included (§8.10–§8.12).

---

## 6. User interface

Cellestra keeps its chrome **compact and contextual**. It opens to the grid, the formula bar, the status bar, and a single **adaptive command bar** — one row of icon tools, not a multi-tab ribbon. Commands are reached through three surfaces that complement each other:

- The **native macOS menu bar** (top of the screen) — the *complete* list of commands, the way every good Mac app works.
- A **Command Palette** (⌘K) — a keyboard-first, type-to-find launcher for *any* command, modeled on VS Code's. The command bar's "Find a tool…" affordance opens it (§6.7).
- The in-window **adaptive command bar** — one task-grouped row (Edit · Text · Align · Numbers · Insert · Data). Each group shows its 2–3 most-used tools as icons and tucks the rest into a **drawer** behind a chevron (progressive disclosure). Tools are **context-aware** — disabled when they don't apply, lit when their toggle is on — and the whole bar **collapses to a slim handle** to give the grid more room (its state is remembered between sessions).

Everything reachable in the command bar is also reachable in the menu bar, the Command Palette, and by keyboard. No surface is the only path to anything.

> **On the command bar vs. the "minimalist by default" principle (heuristic #8):** the bar earns its always-present row by being Google-compact at rest and Excel-rich on demand — one row of context-relevant icons, with depth in the drawers rather than in extra tabs or vertical chrome. It's flat, monochrome line icons, generous spacing, and no 2007-era ribbon. Charts (a v1 non-goal, §9) will later get their own context — a workbook object that takes the place of a sheet tab — without the bar growing tabs.

### 6.1 Window layout (top to bottom)

1. **Title bar** — unified with the window in the macOS style; shows the file name and an "edited" dot when there are unsaved changes.
2. **Adaptive command bar** — one always-present row of task-grouped icon tools (§6.3), with overflow drawers and a collapse handle. A new window opens with it expanded; the last collapsed/expanded state is remembered between sessions.
3. **Formula bar row** —
   - **Name Box** (far left): shows the active cell reference (e.g. `B7`); typing a reference or named range here and pressing Enter jumps there.
   - **Cancel (✕)** and **Accept (✓)** buttons, and an **Insert Function (`fx`)** button.
   - **Formula input**: a wide field showing the active cell's formula or value, editable directly.
4. **Grid** — column headers (A, B, C…), row headers (1, 2, 3…), the cell area, frozen panes where set, and scrollbars. The active cell has a bold outline with a fill handle at its corner. Header labels scale with the zoom. Dragging a column's right border (or a row's bottom border) resizes it — the cursor changes to the resize handle on hover; double-clicking a column border autofits it. The inline editor matches the cell's display (fill, text colour, bold/italic) so editing looks the same as the drawn cell. **Right-clicking** opens a context menu of icon+label commands: on a cell, cut/copy/paste, insert rows/columns, and clear contents/formatting/all (acting on the cell under the cursor or the selection); on a **column or row header**, it selects that column/row and offers Insert and **Delete Columns/Rows**.
5. **Sheet tab strip** (bottom-left) — one tab per worksheet and a **＋** to add. Tabs **drag to reorder**. **Right-clicking a tab** activates it and offers Rename, Insert, and Delete (delete confirms, and a workbook always keeps at least one sheet). Context-menu items carry an icon and label.
6. **Status bar** (bottom) — see §6.5.

### 6.2 Menu bar

| Menu | Items (→ submenu shown indented) |
|---|---|
| **Cellestra** | About Cellestra · Settings… (⌘,) · Services · Hide (⌘H) · Hide Others (⌥⌘H) · Quit (⌘Q) |
| **File** | New (⌘N) · Open… (⌘O) · Open Recent → · Close (⌘W) · Save (⌘S) · Save As… (⇧⌘S) · Import → CSV… · Export → CSV… |
| **Edit** | Undo (⌘Z) · Redo (⇧⌘Z) · Cut (⌘X) · Copy (⌘C) · Paste (⌘V) · Paste Special… (⌥⌘V) · Delete · Select All (⌘A) · Find → Find… (⌘F), Find & Replace… (⌥⌘F) · Fill → Down (⌘D), Right (⌘R) |
| **Insert** | Rows Above · Rows Below · Columns Left · Columns Right · Sheet · Table (⌘T) · Function… (⌃⌥F) · Named Range… · Hyperlink… |
| **Format** | Cells… (⌘1) · Number → … · Bold (⌘B), Italic (⌘I), Underline (⌘U) · Alignment → … · Borders → … · Merge Cells · Rows → Height…, Hide (⌃9), Unhide (⌃⇧9) · Columns → Width…, Hide (⌃0), Unhide (⌃⇧0) |
| **Data** | Sort… · Filter (⇧⌘F) · Clear Filter · Remove Duplicates… · Text to Columns… · Data Validation… · Recalculate (F9) · Calculation → Automatic / Manual · Named Ranges… · Import CSV… · Export CSV… |
| **View** | Command Palette… (⌘K) · Go to Sheet… (⌘⇧P) · Zoom In (⌘=) · Zoom Out (⌘−) · Actual Size (⌘0) · Gridlines · Headers · Formula Bar · Status Bar · Freeze Panes |
| **Window** | Minimize (⌘M) · Zoom · (standard macOS window items) |
| **Help** | Cellestra Help · Function Reference… |

(Menus deliberately *absent*: "Page Layout"/print and "Review"/comments — those features are v1 non-goals, §9.)

**Find / Find & Replace** (⌘F / ⌥⌘F) is a modeless tool window: besides Find Next / Replace / Replace All, it lists **every cell whose displayed value contains the query** (address + value, updated as you type); clicking a row jumps the selection there and scrolls it into view.

### 6.3 The adaptive command bar

One row, grouped by task. Each **group** shows its 2–3 most-used tools as icons (its *primary* set); a chevron opens a **drawer** with the rest, each labelled and described (recognition over recall). Every tool binds to the one `AppCommands` catalog, so the bar, the menu, and the palette can never drift apart; the menu and palette remain the complete superset, while the bar surfaces what's touched most while editing. Icons follow §6.6.

| Group | Primary (on the bar) | In the drawer | Notes |
|---|---|---|---|
| **Edit** | Undo (⌘Z) · Redo (⇧⌘Z) | Cut (⌘X) · Copy (⌘C) · Paste (⌘V) · Clear Contents | — |
| **Text** | Bold (⌘B) · Italic (⌘I) · Underline (⌘U) | Cells… (⌘1) · **Font size** stepper · **Text colour** swatches · **Cell fill** swatches | Bold/Italic/Underline light up when on; size and colour preview live on hover (below) |
| **Align** | Align Left · Center · Right | Wrap Text · Merge Cells | The active alignment is the lit one |
| **Numbers** | Currency · Percent | Date | Disabled when the active cell holds text (error prevention) |
| **Insert** | Rows Above · Columns Left | Sheet · Table (⌘T) | — |
| **Data** | Sort Ascending · Filter (⇧⌘F) | Sort Descending · Sort… · Clear Filter · Remove Duplicates… · Text to Columns… · Data Validation… | Filter lights up while a filter is active |

On the right of the bar sit a **Find a tool…** affordance (opens the Command Palette, §6.7) and a **collapse** chevron.

**Behaviours that make it adaptive:**
- **Progressive disclosure** — primary tools are always one click away; the drawer holds each group's full set, labelled and described, so depth costs no extra vertical chrome until asked for.
- **Context-awareness** — tools reflect the selection: a tool that can't apply is *disabled, not hidden* (the Numbers tools grey out on text), keeping the layout stable.
- **Toggle state** — on/off tools (Bold, Wrap, a number format, Filter) render lit with an accent tint and a dot when active; the status line narrates the result (§6.5).
- **Live preview** — hovering a font size or colour swatch shows the would-be result on the selected cells without committing; nothing touches the model or the undo stack (§8.5) until you click.
- **Collapse** — the collapse chevron folds the bar to a slim handle to reclaim room for the grid; the choice persists between sessions. (There is no separate full-hide toggle.)

Tooltips on every tool carry its name, shortcut, and one-line description; drawer rows show the same. Charts are a v1 non-goal (§9); when added, they'll bring their own command-bar context rather than another tab.

### 6.4 Keyboard shortcuts (full reference)

Shortcuts use macOS modifiers (⌘ Command, ⇧ Shift, ⌥ Option, ⌃ Control). The single source of truth is the `AppCommands` catalog (see `ARCHITECTURE.md`); the table below lists the defaults, and **all are remappable in Settings**.

**Files & app**
| Action | Shortcut |
|---|---|
| New / Open / Save | ⌘N / ⌘O / ⌘S |
| Save As / Close | ⇧⌘S / ⌘W |
| Settings / Quit | ⌘, / ⌘Q |

**Editing**
| Action | Shortcut |
|---|---|
| Undo / Redo | ⌘Z / ⇧⌘Z |
| Cut / Copy / Paste | ⌘X / ⌘C / ⌘V |
| Paste Special | ⌥⌘V |
| Select All | ⌘A |
| Edit active cell | Return or F2 |
| Confirm & move down / up | Return / ⇧Return |
| Confirm & move right / left | Tab / ⇧Tab |
| New line inside a cell | ⌃⌥Return |
| Cancel edit | Esc |
| Fill Down / Right | ⌘D / ⌘R |
| Delete contents | Delete |
| Hide rows / columns | ⌃9 / ⌃0 |
| Unhide rows / columns | ⌃⇧9 / ⌃⇧0 |
| Insert hyperlink | ⌘K |

**Navigation & selection**
| Action | Shortcut |
|---|---|
| Move one cell | Arrow keys |
| Jump to edge of data | ⌘ + Arrow |
| To start of row / to A1 | Fn ← / Fn ⌘ ← |
| Page up / down | Fn ↑ / Fn ↓ |
| Extend selection | ⇧ + Arrow |
| Extend to edge | ⇧⌘ + Arrow |
| Select column / row | ⌃Space / ⇧Space |
| Previous / next sheet | ⌥⌘← / ⌥⌘→ |
| Go to Sheet (quick switcher) | ⌘⇧P |
| Go To (focus Name Box) | ⌃G |

**Formatting**
| Action | Shortcut |
|---|---|
| Bold / Italic / Underline | ⌘B / ⌘I / ⌘U |
| Format Cells dialog | ⌘1 |
| Currency / Percent / Comma | ⌃⇧$ / ⌃⇧% / ⌃⇧! |
| Date / General format | ⌃⇧# / ⌃⇧~ |

**Data & tables**
| Action | Shortcut |
|---|---|
| Create Table | ⌘T |
| Toggle Filter (AutoFilter) | ⇧⌘F |
| Sort… (Custom Sort dialog) | — |

**Formulas & view**
| Action | Shortcut |
|---|---|
| Command Palette | ⌘K |
| Go to Sheet | ⌘⇧P |
| Insert Function | ⌃⌥F |
| AutoSum | ⌥⌘= |
| Recalculate all | F9 |
| Find / Find & Replace | ⌘F / ⌥⌘F |
| Zoom in / out / reset | ⌘= / ⌘− / ⌘0 |
| Keyboard Shortcuts reference | ? |

### 6.5 Status bar

Left to right: a **last-action indicator** (a dot plus a short narration of the most recent action — "Selected B2", "Bold on · B2", "Font size 18 · B2" — a quick reference to what just happened); the **active cell reference**; in the middle, **live selection stats** — **Count** (number of non-empty cells in the selection), and, when at least one numeric cell is selected, **Sum** and **Average** over the numeric cells; on the right, a **calculation-mode badge** (Auto/Manual), a **zoom percentage**, and a **save-state indicator** ("Edited" / "Saved HH:mm" / "Not saved").

### 6.6 Iconography

- **Style:** flat, **monochrome line icons** with a consistent stroke weight, no fills or gradients, ~17 px in the command bar. They adapt automatically to light/dark mode (icons inherit the themed text colour) and use an accent tint for active/toggled states.
- **States:** toggled tools (Bold, Wrap, a number format, Filter) show a clear tinted state plus an accent dot; disabled tools are dimmed, not hidden, so the layout stays stable.
- **Set:** Cellestra uses **Lucide** (ISC license, MIT-compatible) bundled as **local SVG assets** under `Assets/icons` — never fetched at runtime. Each is parsed into a stroked geometry and rendered at the right size and colour by `Controls/Icons`. On macOS a small number of menu-bar items map to **SF Symbols** where it improves native feel. No third-party brand or proprietary icons ship with the app.
- **Consistency:** every command that appears in both the command bar and a menu uses the same icon and label.

### 6.7 Command Palette and sheet switcher

Two keyboard-summoned overlays are the fast path to "do a thing" and "go somewhere" — both modeled on VS Code's palettes. Each appears as a floating, centered input with a filtered result list below it; ↑/↓ move the highlight, Return activates it, Esc dismisses without changing anything.

**Command Palette — ⌘K (run any command).**
- Opens an input over the grid; typing **fuzzy-filters every command in the `AppCommands` catalog** by name (see `ARCHITECTURE.md`). The command bar's "Find a tool…" affordance opens the same palette.
- Each row shows the command's **icon**, its **label**, a one-line **description**, and its **current keyboard shortcut** — so the palette doubles as searchable, in-context documentation.
- Return runs the highlighted command against the current selection, then closes the palette. Because it reads the one command catalog, it can never drift out of sync with the menu bar or command bar.

**Sheet switcher — ⌘⇧P (go to a sheet).**
- Opens a similar input whose list is the **worksheets in the current workbook**, in tab order, active sheet first.
- Typing **fuzzy-filters the sheet names**; Return switches to the highlighted sheet (Esc leaves the active sheet unchanged). With an empty query it behaves like a most-recently-used sheet switcher.
- The Command Palette takes **⌘K** (the same shortcut as the command bar's "Find a tool…" box, so the two feel like one feature); the sheet switcher sits on ⌘⇧P. Both palette shortcuts are remappable in Settings (§6.4).

Neither palette is the *only* way to do anything: every command is still in the menu bar and the command bar, and sheets are still switchable from the tab strip (§6.1).

## 7. User experience principles (Jakob Nielsen's 10 heuristics)

I use Nielsen's heuristics as a checklist to keep the design honest. Each below is a *product requirement*. (Implementation is in `ARCHITECTURE.md`.)

1. **Visibility of system status** — the status bar always shows the active cell, selection stats, calculation mode, and a dirty indicator; a spinner/message shows while loading or recalculating.
2. **Match with the real world** — familiar `A1` references, known function names, plain-English error explanations, the conventional row/column/sheet model.
3. **User control and freedom** — multi-level **undo/redo** (§8.5); **Esc** cancels an in-progress edit; every dialog has an obvious Cancel; long open/save operations can be cancelled.
4. **Consistency and standards** — macOS conventions (⌘S, ⌘Z, menu bar) *and* spreadsheet conventions (Tab/Enter, fill handle, `=`); identical icons/labels across command bar and menus.
5. **Error prevention** — destructive-action confirmations per §8.7; number-format tools disabled on text cells; formula validated before commit so a typo doesn't silently become text.
6. **Recognition rather than recall** — the command bar's drawers pair every icon with a label and description; the Command Palette surfaces all commands by name; **function autocomplete and argument hints**; browsable Name Manager and Function Reference; disabled (not hidden) controls.
7. **Flexibility and efficiency of use** — every command reachable by mouse/menu *and* keyboard; the **Command Palette (⌘K)** and **sheet switcher (⌘⇧P)** as accelerators; remappable shortcuts; fill handle, AutoSum, keyboard-only editing; a collapsible command bar.
8. **Aesthetic and minimalist design** — clean, flat, uncluttered; one compact, context-aware command bar instead of a multi-tab ribbon; depth behind progressive disclosure (drawers and dialogs); the bar collapses to a slim handle when the grid needs the room.
9. **Help users recognize, diagnose, and recover from errors** — formula errors point at *what* and *where*; error cells show a readable reason on hover, not just `#VALUE!`.
10. **Help and documentation** — tooltips on commands, an in-app Function Reference (purpose + arguments per function), and a reachable Help link.

### Accessibility (a first-class requirement)
- All controls expose **accessibility labels and roles** for **VoiceOver**.
- The app is **fully operable from the keyboard**.
- Respects system **text-size/zoom**, **reduced-motion**, and **increased-contrast** settings; the sheet has its own zoom too.
- Clear, visible **focus indicators** on the active cell and focused controls.

### UX success criteria
- A new user can open, edit, and save a file without reading any instructions.
- Every destructive action is either reversible (undo) or confirmed.
- No dead ends: there is always a visible way back or out.
- The main window never feels crowded; the command bar stays one row and collapses when not needed.
- The whole app can be driven by keyboard and read by VoiceOver.

## 8. Defaults and precise behavior

This section pins down behaviors that would otherwise be guessed at.

### 8.1 Preserving unsupported `.ods` content
On open, any element or attribute Cellestra does not model is **retained verbatim** and **written back unchanged** on save, so round-tripping never silently drops features (charts, pivot tables, etc.) that a file may contain. Cellestra never discards user data it doesn't understand; it only declines to *edit* it. If an edit makes verbatim preservation of a specific element impossible, the user's cell values, formulas, and styles still survive.

### 8.2 The v1 function library
- **Math/stats:** SUM, AVERAGE, MIN, MAX, COUNT, COUNTA, PRODUCT, ABS, INT, MOD, ROUND, ROUNDUP, ROUNDDOWN, POWER, SQRT, SUMIF, COUNTIF.
- **Logical:** IF, IFERROR, AND, OR, NOT, TRUE, FALSE.
- **Text:** LEFT, RIGHT, MID, LEN, LOWER, UPPER, TRIM, SUBSTITUTE, TEXT, VALUE, CONCAT (and the `&` operator).
- **Lookup:** VLOOKUP, HLOOKUP, INDEX, MATCH.
- **Date/time:** TODAY, NOW, DATE, YEAR, MONTH, DAY, WEEKDAY, NETWORKDAYS.

These are the v1 commitment. Adding functions later is expected and cheap (one registry entry + tests).

### 8.3 Locale and number formats
Number, date, and currency formatting follow the **document's locale** when the `.ods` file specifies one. For a **new** document, and for any format the file leaves unspecified, Cellestra uses the **system locale**. The decimal/thousands separators and currency symbol follow the active locale.

### 8.4 CSV format
- **Encoding:** UTF-8. On import, a UTF-8 BOM is detected and stripped; other encodings are read as UTF-8 (best effort).
- **Delimiter:** comma by default; the Import/Export dialog lets the user pick comma, semicolon, or tab.
- **Quoting:** RFC 4180 — fields containing the delimiter, quotes, or newlines are wrapped in double quotes, with embedded quotes doubled.
- **Line endings:** import accepts LF or CRLF; export writes LF.
- **Values:** export writes the **displayed/computed** values (not formulas). Import infers numbers and dates from the active locale; everything else is text.

### 8.5 Undo/redo scope
Undo/redo is **session-only** (never written into the `.ods` file) and covers all document edits. History holds up to **1,000 steps**, after which the oldest are dropped. History is cleared when the document is closed. (Undo does not cover view-only state like scroll position or zoom.)

### 8.6 Calculation on open
On open, cached values from the file are shown immediately. If calculation mode is **Automatic** (the default), a background recalculation runs once to confirm values; if **Manual**, cached values are kept until the user recalculates (F9).

### 8.7 Destructive-action confirmations
- **Always confirm:** deleting a worksheet; overwriting an existing file via Save As; closing/quitting with unsaved changes (Save / Don't Save / Cancel).
- **Confirm only above a threshold:** clearing or deleting a range is silent (undo covers it) **unless** it would remove data from **more than 100 non-empty cells**, in which case Cellestra asks first.
- Everything else relies on undo rather than nagging.

### 8.8 New-workbook and cell defaults
- A new workbook has **one worksheet named `Sheet1`**.
- **Default cell style:** Helvetica Neue, 10 pt, black text, no fill, "General" number format.
- **Default alignment:** text left-aligned, numbers right-aligned, vertically bottom-aligned, no wrap.
- **Default sizing:** column width ≈ 64 px (about 8 characters at the default font); row height ≈ 20 px.
- New sheets added later are named `Sheet2`, `Sheet3`, … (next unused number).

### 8.9 Scroll-wheel and trackpad behavior
- **Vertical scroll** is the default: turning the scroll wheel (or a two-finger vertical swipe on a trackpad) scrolls the grid up and down.
- **Horizontal scroll** happens when **⇧ (Shift) is held while turning the scroll wheel** — the same wheel motion then scrolls the grid left and right. This is for mice whose wheel scrolls only one axis; on a trackpad, a two-finger horizontal swipe scrolls horizontally without any modifier.
- Scroll speed follows the system "natural scrolling" direction setting; one wheel detent moves a few rows (or columns, when shifted). Scrolling never changes the active cell or selection, and it is **not** undoable (view-only state, per §8.5).
- ⇧ + scroll affects the **grid** only; it does not interact with the ⇧-modified keyboard shortcuts, which fire on key presses rather than wheel events.

### 8.10 Tables
- **⌘T** turns the selected range into a **table**: a named region with **banded-row styling** and a **filter header** on each column. If the selection has an obvious header row it's used; otherwise generic headers (`Column1`, `Column2`, …) are added.
- A table **auto-expands**: typing a value in the row directly below it (or the column directly to its right) absorbs that row/column into the table, extending banding, filters, and any whole-column formulas. Deleting edge rows/columns shrinks it.
- Tables are stored in `.ods` as a **named database range** (`table:database-range`) plus the banded styling — the native ODF concept LibreOffice uses — so they round-trip cleanly. **Excel-style structured references (`Table[Column]`) are not supported** (§9); formulas reference table cells with ordinary A1 or named-range references.
- A sheet may hold multiple tables; tables may not overlap each other or merged regions.

### 8.11 Sorting
- Sort applies to the selected range, or the whole table when the selection is inside one. A **header row** is auto-detected (and the choice is overridable in the Sort dialog).
- **Multi-level:** the Custom Sort dialog supports a primary "Sort by" plus additional "Then by" levels; each level is a column, a key (values), and a direction (ascending/descending). Sorting is **stable** across levels.
- Sort order follows the active locale's collation; numbers, dates, and text sort in their natural orders, with empty cells last.
- Sorting **moves cell content and styles together** and adjusts formula references per §8 (the same `ReferenceAdjuster` rules). Sort is undoable.

### 8.12 Filtering (AutoFilter)
- **⇧⌘F** toggles filter dropdowns on the header row of the range/table. Each dropdown filters **by value** (checklist of distinct values) or **by condition** (e.g. text contains, number/date comparisons).
- Filtering **hides** non-matching rows (row visibility), it never deletes them; **Clear Filter** shows all rows again. Filter state is saved in the `.ods` database range.
- Hidden-by-filter rows behave like manually hidden rows for display and selection. (Aggregating only visible rows — Excel's `SUBTOTAL`/`AGGREGATE` — is **not** in the v1 function set per §8.2; it's a likely early addition.)

### 8.13 Data validation
- A cell or range can carry a **validation rule**: a **list** (literal values or a range reference, shown as an in-cell dropdown), or a **number / date / text-length** constraint with an operator (between, ≥, =, …).
- Each rule has an optional **input message** (shown on selection) and an **error response** on invalid entry: **Stop** (reject), **Warning**, or **Information** (allow with a prompt). Default is Stop.
- Rules are stored as ODF **content validations** (`table:content-validation`) and round-trip. Validation checks input only; it does not retroactively flag values already present (e.g. from import).

### 8.14 Hide/unhide and data tools
- **Hide/unhide rows & columns** set ODF row/column **visibility**; hidden lines are skipped in layout but their data and formulas are untouched. Formulas referencing hidden cells still compute.
- **Remove Duplicates** scans the selected range (header-aware) and deletes rows that duplicate earlier rows across the chosen columns, keeping the first occurrence. It is undoable and reports how many rows were removed.
- **Text to Columns** splits the selected single-column text into multiple columns by a chosen delimiter (or fixed width), with the same quoting rules as CSV import (§8.4); it warns before overwriting non-empty cells to the right.

### 8.15 Fill handle behavior
- Dragging the fill handle, like **Fill Down / Fill Right**, performs a **copy-fill**: values and styles are copied and relative references shift per §8 (absolute `$` refs don't). 
- v1 does **not** extrapolate series (e.g. `1, 2, 3…` or `Jan, Feb…`) from the fill handle — that's deferred (§9). To repeat a pattern, select the pattern cells before filling.

### 8.16 Command-line interface (`cellestra`)

A second face on the same brain (`ARCHITECTURE.md` §1): the `cellestra` binary makes Cellestra useful in scripts, CI, and build pipelines. It lives in `src/Cellestra.Cli/`, references only Core + `System.CommandLine`, and has **no Avalonia and no network**. Because it reuses the engine, `convert` shares the serializers, `eval` shares the formula engine, and `functions` reads the same `FunctionRegistry` as the app's autocomplete — so CLI output and GUI behaviour can't drift. Cell addressing matches the app (`Sheet1.B2`, ranges `Sheet1.A1:C10`; omit the sheet for the active/first one). No args, `-h`, or `--help` shows help.

**Baseline (the genuinely useful core):**
- `convert <in> <out>` — `.ods ⇄ CSV`, both directions (`--from/--to`, `--sheet`, `--delimiter`).
- `cat <file>` — print a sheet/range (`--sheet`, `--range`, `--format`, `--formula`).
- `get <file> <cell>` — one cell's value, or its source with `--formula`.
- `set <file> <cell> <value>` — edit a cell and save (`--in-place` or `-o`).
- `recalc <file>` — refresh the file's cached values (ideal in CI).
- `sheets` / `info` — worksheet dimensions; workbook overview (sheets, named ranges, locale, cell counts).
- `eval "<formula>"` — evaluate with no file at all (pure engine).
- `validate <file>` — opens? any formula errors / `#REF!`s / circular refs? **non-zero exit on problems.**

**Extras:** `new`, `import`/`export`, `stats`, `find` (`--regex`, `--in values|formulas`), `diff` (**non-zero exit on differences**), `functions` (list / describe — same metadata as the in-app reference), `add-sheet` / `rename-sheet` / `delete-sheet`, `apply <file> <edits.json>` (batch edits, `-` for stdin), `open` (hand off to the GUI), `completions <shell>`.

**Conventions (these matter more than any single command):**
- **Pipe-friendly:** `--format table|csv|tsv|json`; read commands default to stdout; `-` means stdin/stdout.
- **Edits are explicit and safe:** nothing overwrites without `--in-place` or `-o`; all writes are **atomic** (temp file + rename, matching the app's save rule §8.7).
- **Edits preserve unmodeled content:** a `set`/`recalc` on a file with charts/pivots keeps them **verbatim** (§8.1) — covered by a test.
- **Exit codes are first-class:** `validate` and `diff` return non-zero so they slot into CI and pre-commit hooks.
- **Same engine, same rules:** evaluation, OpenFormula translation, and locale handling are identical to the app, with a `--locale` override for scripting; `recalc` matches what the GUI would show.
- **No network, ever.**

## 9. Non-goals (deliberately NOT in v1)

Intentional exclusions, not oversights.

- **Printing and page layout / print-to-PDF.** (Why it's called out: users may expect it, and it's why the menu bar has no "Page Layout.")
- **Autosave / crash auto-recovery.** v1 relies on manual save plus the unsaved-changes warnings in §8.7.
- **Charts and graphs.**
- **Pivot tables.**
- **Macros / scripting.**
- **Real-time collaboration / cloud sync.**
- **Other file formats as first-class citizens** — `.xlsx` and Google Sheets are out; `.ods` is the home format.
- **Array / spill formulas** and **iterative calculation** for intentional circular references.
- **Excel-style structured references** (`Table[Column]` syntax). Tables exist (§8.10), but `.ods` has no equivalent reference syntax, so formulas use ordinary A1/named references; adding structured refs would break the LibreOffice round-trip (§3).
- **Fill-handle series extrapolation** (auto-incrementing numbers/dates/months). The fill handle copy-fills only (§8.15).
- **Cell comments / notes** (why there's no "Review" menu).
- **Cross-platform builds.** Mac first; the architecture keeps the door open but Windows/Linux are not a v1 promise.
- **Mobile / web versions.**
- **Conditional formatting.**

## 10. Decisions (previously open questions, now resolved)

| Question | Decision |
|---|---|
| Fidelity for unsupported `.ods` content | Preserve verbatim and write back unchanged (§8.1). |
| Exact v1 function list | The list in §8.2. |
| Locale / number-format behavior | Document locale when specified, else system locale (§8.3). |
| Icon set / SF Symbols | Lucide (local SVGs); map a few menu items to SF Symbols on macOS (§6.6). |
| Undo depth & persistence | Session-only, 1,000 steps, not saved in the file (§8.5). |
| Default UI / discovery surface | One compact, context-aware **adaptive command bar** (collapsible, §6.3) plus the **Command Palette (⌘K)** and **sheet switcher (⌘⇧P)** as the keyboard-first surfaces (§6, §6.7). |
| Sort, filter, and tables | **In v1** (§8.10–§8.12) — everyday features. Previously deferred; now core. |
| How tables map to `.ods` | Tables are native **database ranges** (`table:database-range`) with banded styling and filters; **no Excel structured references** (§8.10, §9), to protect the LibreOffice round-trip. |
| Data validation, hide/unhide, hyperlinks, remove-duplicates, text-to-columns | **In v1** (§4 Data, §8.13–§8.14). |
| Fill-handle behavior | Copy-fill only; **no series extrapolation** in v1 (§8.15). |
| Autosave | None in v1 (§9). |
| CSV specifics | UTF-8, comma default, RFC 4180 quoting, exports computed values (§8.4). |

If a new ambiguity surfaces during build, resolve it here (or in `ARCHITECTURE.md` for technical detail) rather than guessing in code.

### 10.1 Editing-feel decisions (resolved during build)

These "everyone expects it but nobody wrote it down" behaviors were specified and built to make
editing match spreadsheet muscle memory (§3 *Feel*). Each is implemented and tested.

| Behavior | Decision |
|---|---|
| `-2^2` precedence | Unary minus binds **looser** than `^` (OpenFormula / LibreOffice Calc), so `-2^2 = -4`. Deliberately *not* Excel's `4`. This corrected an earlier parser bug. |
| Leading apostrophe | Forces the rest to literal text (`'007`, `'=x`); the apostrophe is an input flag, stripped before storage and never displayed. |
| Date entry | A typed slash date (`6/7/2026`, locale day/month order) or ISO date (`2026-06-07`) coerces to a serial and stamps a date format on a still-General cell. Bounded to slash/ISO so month names and hyphenated codes stay text. **Bare times are left as text** (the number-format mapper treats `mm` as month) — a known limitation for a later number-format pass. |
| Cut vs copy | Cut is a deferred **move** consumed by its first paste (an unused cut leaves the data); copy pastes repeatedly until Esc. Both show a marching-ants marquee (green=copy, gray=cut) that an edit or Esc clears. Return pastes once and consumes the clipboard. |
| Paste shape rules | One cell fills a multi-cell selection; a block on a single cell anchors and expands; a block on an exact-multiple selection tiles; any other mismatch pastes once and warns. In-app paste carries values, formulas, and styles; external paste falls back to TSV. |
| Fill handle | Drag copy-fills along one axis; double-click fills down to the adjacent column's length. **No series extrapolation** (still a non-goal, §8.15 / §9) — a multi-cell source tiles. |
| `#####` / spill | A number too wide for its column shows `#####`; left-aligned text overflows into empty neighbours and clips at an occupied one. |
| Selection | Cmd+A expands region → whole sheet; Cmd+click builds discontiguous ranges; status-bar Count/Sum/Average aggregate the union; F4 cycles a reference `A1 → $A$1 → A$1 → $A1`; Tab-then-Enter returns to the tab-origin column. Undo restores the selection to the edit site. |
| Editing aids | Point mode (click a cell while editing a formula to insert its reference), live reference highlighting on the grid, and column type-ahead autocomplete. Colouring tokens *inside* the editor needs a rich-text control and is deferred. |
| Inserted rows/cols | Inherit the formatting of the line before them. |
| Sheet names | Validated: non-empty, ≤31 chars, no `: \ / ? * [ ]`, no edge apostrophe; uniqueness enforced. Deleting a referenced sheet rewrites dependents to `#REF!`. |

### 10.2 File-safety decisions (resolved during build)

| Behavior | Decision |
|---|---|
| Atomic save | Write to a sibling temp file then rename over the target, so a crash mid-write never corrupts the original `.ods`. (This is write-safety, not the autosave that remains a non-goal.) |
| LibreOffice locks | Detect a `.~lock.<name>#` and warn on open; place our own compatible lock while a file is open and remove it on close. Advisory, best-effort. |
| Cached vs recompute | On open, cached values are shown immediately then recomputed; cells whose recompute disagrees with the file cache are counted and surfaced (a function-coverage signal), rather than silently masked. |
| Locale separator | Formula syntax stays canonical (`,` argument separator, `.` decimal) regardless of locale — locale affects display only. Typing `SUM(1;2)` in comma-decimal locales is **not** supported in v1 (a deliberate scope limit, not an oversight). |

## 11. Possible later versions (not commitments)

Printing/PDF, autosave/recovery, conditional formatting, charts, pivot tables, `.xlsx` import, cell comments/notes, Excel-style structured references, fill-handle series, more functions (e.g. `SUBTOTAL`/`SUMIFS`/`XLOOKUP`), and eventually other platforms — once the core is rock-solid.
