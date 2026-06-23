# Typage — Architecture

How Typage is put together, and why. For *what* and *why* at the product level, see
[PRD.md](PRD.md); for process and guardrails, see [../AGENTS.md](../AGENTS.md).

## 1. The big picture in one breath

Typage is a **pure brain with a control and two faces**:

1. **A pure brain (`Typage.Engine`)** — knows everything about the document (text storage, line/offset
   math, grapheme navigation, undo/redo, Markdown tokenization, HTML rendering) and **nothing** about
   windows, drawing, or input. No UI dependencies at all.
2. **A control (`Typage.Control`)** — an Avalonia control library that turns the engine's token stream
   into a live-preview editing surface, with the presentation seams (theme, embed resolver) the notes
   app will re-point.
3. **A graphical face (`Typage.Desktop`)** — the standalone Avalonia app that hosts the control in a
   window with a menu, toolbar, status bar, and file dialogs.
4. **A command-line face (`Typage.Cli`)** — the `typage` binary for scripts and CI. No Avalonia, no
   network. Because it reuses the same brain, `html` shares the renderer and `tokens` shares the exact
   tokenizer the editor styles — so CLI output and GUI behavior can't drift apart.

This separation is the most important decision in the project: the hard, bug-prone logic lives where it
can be tested instantly without a window, and the reusable surface is cleanly separable from the shell
(PRD §10). Dependency direction is strictly **shell → control → engine**; the engine never references
Avalonia.

```
   Typage.Desktop ─┐                 ┌─ Typage.Cli
                   │ hosts           │ calls
            Typage.Control ──────────┤
                   │ calls           │
                   └──── Typage.Engine ◄┘   (no Avalonia)
```

## 2. Tech stack, and why

- **Language & runtime: C# on .NET 10.** Statically typed, with a huge trustworthy standard library —
  `System.Globalization` gives grapheme-cluster segmentation for free, `System.Text` and
  `System.Text.Json` cover rendering and settings. No third party needed for the core.
- **UI: Avalonia 12.** A mature cross-platform .NET UI framework, MIT-licensed, essentially one
  dependency from one team — a good fit for the supply-chain goal. The editing surface is built on
  `Avalonia.Media.TextFormatting`, **not** a reused `TextBox`.
- **Tests: xUnit v3**, plus `Avalonia.Headless.XUnit` for the headless UI smoke tests.
- **License: MIT.**

**Versions** are pinned at the **repo root** in `Directory.Packages.props` (central package management),
shared with the Cellestra feature. Typage adds **no new packages** of its own — it uses only `Avalonia`,
`Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `System.CommandLine`, and the
xUnit/test packages already pinned there. That zero-additions fact *is* the supply-chain guarantee
(PRD §9.3); a CI dependency gate would enforce it.

## 3. The projects

- **`Typage.Engine`** — a plain class library; everything that defines what the *document* is and how
  Markdown is read. No Avalonia. Most tests point here.
- **`Typage.Control`** — depends on Avalonia + engine. The custom surface and the two presentation
  seams. Embeddable into a host app unchanged.
- **`Typage.Desktop`** — the Avalonia executable (assembly `Typage`): window, menu, toolbar, dialogs,
  settings. A thin host.
- **`Typage.Cli`** — the `typage` console executable. References only the engine + `System.CommandLine`;
  no Avalonia, no network. One static method per command, composed in `CliApplication` so tests drive
  the same surface in-process.

## 4. The engine data model (`Typage.Engine`)

### 4.1 `Document/` — text storage and navigation

- **`TextDocument`** — the document buffer as an **immutable piece table** (PRD §9.1). Each edit returns
  a *new* `TextDocument` that structurally shares the underlying character buffers with its predecessor,
  so an undo stack of snapshots costs only an array of piece references per step, never a text copy.
  Offsets are UTF-16 code units; lines are delimited by `\n` with a preceding `\r` kept verbatim so CRLF
  files round-trip byte-for-byte. Provides `GetText`, indexing, `Insert`/`Delete`/`Replace`, and lazy
  line/offset services (`GetLineText`, `GetPosition`, `GetOffset`).
- **`Grapheme`** — grapheme-cluster boundary math over `System.Globalization.StringInfo`, so caret
  movement, selection, and deletion treat emoji, combining marks, surrogate pairs, and `\r\n` as single
  units (PRD §7.2, §9.7).
- **`Selection`** / **`LinePosition`** — caret/anchor pair and 1-based line:col for the status bar.
- **`DocumentStats`** — words, grapheme characters, lines, reading time. One source of truth shared by
  the status bar and the CLI's `stats` command.

### 4.2 `Editing/` — reversible edits and undo

- **`TextEdit`** — one reversible change (`Start`, `Removed`, `Inserted`); applies to and inverts against
  the immutable document. Knows when it can coalesce with the next edit (a single-character typing run,
  broken by a newline on either side).
- **`EditHistory`** — the undo/redo stacks: session-only, capped at 1,000 steps, redo cleared on a new
  edit, typing runs coalesced (bounded by caret jumps / `BreakCoalescing`).
- **`DocumentBuffer`** — the editable model the control drives: current snapshot + selection + history +
  dirty flag, with `Insert`/`Backspace`/`Delete`/`ReplaceRange`/`Undo`/`Redo`/`Load`/`MarkSaved` and
  `TextChanged`/`Changed` events. The UI never mutates a `TextDocument` directly.

### 4.3 `Markdown/` — the tokenizer (PRD §8)

An **incremental, line-based tokenizer with block state and a checkpoint cache** — not a full AST.

- **`Token`** (`Start`, `Length`, `TokenType`, `TokenRole`, `Meta`) — a typed, role-tagged span by
  absolute offset. `TokenRole` is `Marker` (concealable syntax) or `Content` (rendered text). The engine
  emits *semantic tokens only*; styling/concealment live in the control.
- **`BlockState`** — the state carried line-to-line (`Normal`, `FencedCode` with its fence char/length,
  `FrontMatter`). Only constructs that truly span lines carry state; everything else is recognized per
  line.
- **`LineTokenizer`** — tokenizes one line given its entering `BlockState`, returns the next line's
  state. Handles front matter, fenced code, ATX headings, blockquotes, lists (incl. task lists),
  horizontal rules, and dispatches inline content to:
- **`InlineTokenizer`** — a tolerant left-to-right scanner for emphasis/strong, strikethrough, highlight,
  inline code, links (recursing into link text), images, autolinks (bracketed and bare), wiki-links,
  embeds, and escapes. An unmatched opening marker stays literal until validly closed.
- **`MarkdownModel`** — the document-level driver: stores per-line tokens and the **per-line entering
  block state (the checkpoint cache)**, recognizes GFM tables by lookahead (a header row followed by a
  delimiter row), and offers `Build` (full) and `Update` (incremental). `Update` reuses the untouched
  prefix and re-tokenizes from the first changed line; worst case is O(n) — e.g. an opening fence at the
  top flips the file to code — exactly as the PRD allows (§8.2). It backs up over contiguous table rows
  so a table is always re-evaluated as a unit.

The checkpoint cache is what makes "resume tokenizing any viewport from the nearest line above" cheap
(PRD §8.1, §9.1). Storing one checkpoint per line is the simple, correct form; a sparse interval is a
later memory optimization.

**Correctness is pinned by an `incremental == full rebuild` test suite** (`IncrementalTokenizerTests`):
every edit's incremental update must produce token-for-token the same result as a fresh `Build`.

### 4.4 `Html/` — export

- **`HtmlRenderer`** — renders a document to a self-contained HTML string (PRD §7.8) by walking the same
  `MarkdownModel` token stream the surface uses, so the export matches what the editor shows. Pure, no
  external CSS, no network. Pairs link/image structure back out of the flat token stream; escapes all
  text.

## 5. The control (`Typage.Control`)

- **`MarkdownEditor`** (`Controls/`) — the custom surface. Owns a `DocumentBuffer`, rebuilds a
  `MarkdownModel` on text change, and draws each line's tokens with `FormattedText` styled by the theme.
  It owns caret, selection, keyboard and mouse input, grapheme-aware navigation, and basic formatting
  commands. Markers **dim** when the caret is off their line; offsets stay **1:1** with the visual model,
  so caret hit-testing and mapping are exact and simple. It re-tokenizes **incrementally** on edit
  (`MarkdownModel.Update` from `DocumentBuffer.LastChangedLine`) and is **line-virtualized**: it
  implements `ILogicalScrollable` and draws only the lines inside the viewport, translated by the scroll
  offset, with the horizontal extent growing as wide lines come into view (no whole-document scan).
- **`MarkdownTheme`** (`Rendering/`) — the presentation seam mapping `TokenType` → typeface/size/colors.
  The engine stays styling-free; re-theming never touches it.
- **`IEmbedResolver`** — the seam that turns an engine-detected embed *reference* into an Avalonia
  control. Detection is engine-side; resolution is control-side. The standalone app injects
  `NullEmbedResolver` (embeds show as source); the notes app will inject a real one.
- **`MarkdownEditorView`** (`Views/`) — the embeddable component: the surface in a scroll viewer with a
  small stable public API (text, dirty, stats, caret, theme, embed resolver, undo/redo, events) and no
  file/app assumptions. Built in code (no XAML) so it carries no resource dependencies.

This layer is an **early Phase 3/4 implementation**. The PRD's full §7.4 concealment (true glyph-hiding
with a bidirectional offset↔visual map), IME, and the accessibility automation peer are the dedicated
later phases (PRD §11). The public API and engine seam are stable so that work slots in without a rewrite.

## 6. The shell (`Typage.Desktop`)

- **`App`** — built in code; adds the Fluent theme and creates the single `MainWindow`.
- **`MainWindow`** — a thin host: menu (File/Edit/Format/Help), a formatting toolbar, the editor view,
  and a status bar (modified state, line:col, word/char counts, reading time). Owns file operations
  (open/save/save-as/revert with `StorageProvider` dialogs and **atomic temp-then-rename writes**), the
  unsaved-changes guard, recent files, HTML export, and a Markdown help reference.
- **`Services/`** — `AppSettings` + `SettingsService` persist preferences (recent files, tab width,
  toolbar visibility) as JSON in the OS app-config location; failures are non-fatal.

## 7. The CLI (`Typage.Cli`)

`CliApplication.BuildRoot` composes the command tree; `Run` turns expected errors (bad input, missing
file) into a clean `typage: …` line with exit 1 and lets bugs surface loudly. `Infrastructure/Workspace`
loads text (`-` = stdin) and writes atomically (`-` = stdout). Commands: `html`, `cat`, `tokens`,
`stats`, `outline`.

## 8. Threading model

The UI thread owns edits. Because `TextDocument` snapshots are immutable, a background tokenizer can read
a snapshot without locks and never race the editor; results marshal back to the UI thread (PRD §9.2). The
current surface tokenizes incrementally but synchronously on edit (it re-runs only from the first changed
line, and only the visible lines are drawn); moving the re-tokenization of the off-screen tail to a
background thread is a further performance phase, and the immutable-snapshot design already supports it.

## 9. Folder structure

```
features/EditMarkdown/
  Typage.slnx
  AGENTS.md  README.md  docs/{PRD,ARCHITECTURE}.md
  Typage.Engine/
    Document/   TextDocument, Grapheme, Selection, LinePosition, DocumentStats
    Editing/    TextEdit, EditHistory, DocumentBuffer
    Markdown/   TokenType, Token, BlockState, LineTokenizer, InlineTokenizer, MarkdownModel
    Html/       HtmlRenderer
  Typage.Control/
    Controls/   MarkdownEditor
    Rendering/  MarkdownTheme
    Views/      MarkdownEditorView
    IEmbedResolver.cs
  Typage.Desktop/
    App.cs  Program.cs  RelayCommand.cs
    Views/      MainWindow
    Services/   AppSettings, SettingsService
  Typage.Cli/
    Program.cs  CliApplication.cs
    Commands/       MarkdownCommands
    Infrastructure/ Workspace, CliException
  Typage.Engine.Tests/   Document/ Markdown/ Html/
  Typage.Cli.Tests/
  Typage.UiTests/
```

Don't invent new top-level projects; extend this structure. If a new home seems necessary, note it and
ask.
