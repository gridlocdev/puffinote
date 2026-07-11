# AGENTS.md

Guidance for an AI coding agent (and any human) working in the **Typage** feature
(`features/EditMarkdown/`). Read this first, then the linked docs. Keep this file open while you
work. This mirrors the conventions of the sibling **Cellestra** feature
(`features/EditSpreadsheets/AGENTS.md`) — same repo, same tooling, same golden-rule discipline.

## What this project is

**Typage** (from **type** + **page**) is a from-scratch, cross-platform **Markdown editor** with
**live inline preview** (Obsidian "Live Preview" style): the document reads as formatted prose and
syntax markers reveal only near the caret. It is **pre-v1** and is the first deliverable of a larger
Obsidian-class notes app — so the hardest, most reusable piece, the **editing engine**, is built and
tested first as a standalone, headless library.

The code is laid out as a **pure brain with two faces and a control**:

1. **`Typage.Engine`** — the pure brain: immutable document model (piece table), grapheme-aware
   navigation, undo/redo, the incremental Markdown tokenizer + checkpoint cache, and the HTML
   renderer. **Zero UI dependencies.** Headless-testable. This is where most logic and tests live.
2. **`Typage.Control`** — an Avalonia control library: the custom live-preview editing surface
   (`MarkdownEditor`/`MarkdownEditorView`) built on `Avalonia.Media.TextFormatting`, plus the two
   presentation seams (`MarkdownTheme`, `IEmbedResolver`). Embeddable, unchanged, into the future
   notes app.
3. **`Typage.Desktop`** — the standalone Avalonia shell (assembly `Typage`): single window, menu,
   toolbar, status bar, file dialogs, settings. A thin host around the control.
4. **`typage` CLI** (`Typage.Cli`) — a second consumer of the engine for scripts/CI: render to
   HTML, dump tokens, stats, outline. No Avalonia, no network.

Start by reading, in order:

1. **[README.md](README.md)** — front door: overview, build/run, project map.
2. **[docs/PRD.md](docs/PRD.md)** — *what* and *why*: the full product spec, the precise live-preview
   behavior (§7.4), the Markdown dialect (§8), non-goals (§2.2), and resolved decisions (§12).
3. **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — *how*: layering, the engine data structures,
   the tokenizer/checkpoint design, and the **folder structure**.

When this file and a linked doc disagree, the linked doc wins for design detail; this file wins for
process and guardrails. If a behavior isn't specified, check PRD §7.4 (live preview), §8 (dialect),
or §12 (decisions) before guessing; if it truly isn't there, **stop and ask**, then record the answer
in the doc.

## Tech stack & exact versions (do not change without being asked)

- **Runtime:** C# on **.NET 10** (`net10.0`).
- **UI:** **Avalonia 12.0.4**. Compiled bindings on by default.
- **Tests:** **xUnit v3**; headless UI via `Avalonia.Headless.XUnit` 12.0.4.
- **License:** MIT.

Versions are pinned centrally in the **repo-root `Directory.Packages.props`** (shared with Cellestra).
Don't hardcode versions in `.csproj` files.

## Golden rules (non-negotiable)

1. **`Typage.Engine` has ZERO UI dependencies.** No `Avalonia.*`. It is pure logic, testable without a
   window. Dependency direction is strictly **shell → control → engine**; no upward references.
2. **The engine emits semantic tokens only** — type, source range, and a `marker`/`content` role.
   Styling and concealment are the control's job (`MarkdownTheme`, live-preview rendering). Reference
   detection for embeds is engine-side; resolution to a control is control-side (`IEmbedResolver`).
3. **The editing surface is custom-drawn** — built on `Avalonia.Media.TextFormatting`, never a reused
   `TextBox`. MVVM/standard controls are for the shell chrome only.
4. **Guard the supply chain.** The trusted base is **.NET BCL + Avalonia (+ its native deps) + OS
   system services**. **No new NuGet package** above that line without a stated reason — no Markdown
   library, no editor library, no PDF library. Never add JavaScript/npm. (Same policy and pre-approved
   first-party-Avalonia-controls exception as Cellestra — see its AGENTS.md / ARCHITECTURE §2.1.)
5. **The document is the source of truth; rendering is derived.** Editing never rewrites untouched
   bytes. Saving writes exactly the buffer. Preserve encoding and line endings (LF/CRLF) byte-for-byte
   (PRD §7.1, §8.5).
6. **Immutable snapshots, no plain-string buffers.** The document model is an immutable piece table;
   edits produce new snapshots via structural sharing (PRD §9.1). Undo is session-only, capped at
   1,000 steps, never written to the file.
7. **Tolerant tokenizing.** Malformed/intermediate input (the common case while typing) renders
   gracefully as literal text — never errors or flicker (PRD §7.4). An unmatched marker is literal
   until validly closed.
8. **Don't build the non-goals** (PRD §2.2): no multi-file notebooks/tabs, no plugin system, no full
   CommonMark/GFM edge-case fidelity, no collaboration, no PDF library. The documented dialect (PRD §8.3)
   is firm — resist sliding toward full CommonMark. If a task seems to need a non-goal, **stop and ask**.

## Settled behavior you must follow (no guessing)

- **Vertical rhythm (PRD §8.3.1):** blocks carry their own metrics — row height, margins, inner padding — and
  the geometry the draw/hit-test paths read is a **pixel** prefix sum (`LayoutModel.BlockTops`), not a row
  count. (Rows survive only as the unit for Up/Down caret movement.) Margins **collapse** — the gap is the
  larger of the two neighbours', not their sum — and a collapsed blank separator is transparent to the rhythm,
  contributing neither height nor gap. A code block and a blockquote are one object spelled as one block per
  source line, so only a run's outer edges carry inner padding and their panels are drawn once per run.
- **Supported constructs:** exactly the table in **PRD §8.3** (headings — ATX **and setext**, bold/italic/bolditalic,
  strikethrough, highlight, inline + fenced code, blockquote, lists incl. task lists, links, autolinks,
  images, HR, GFM tables, hard breaks, escapes, front matter, and recognized-but-off-by-default
  wiki-links/embeds). Adding constructs is a separate, later task.
- **Reveal scopes (PRD §7.4):** inline marks reveal when the caret/selection intersects that node;
  line-prefix marks reveal when the caret is on the line; block constructs reveal when the caret/selection
  intersects the block range; selection forces reveal. *(Implemented: markers are truly **hidden** via a
  per-line offset↔visual map — inline marks reveal **per node**, line-prefix marks per line — and
  concealed markers render as rich decorations. Whole-line block fences and table rows still render their
  row; row-collapse / true block folding is deferred — see "What is and isn't built yet".)*
- **Tabs:** default inserts **spaces, width 4** (configurable in Settings). Indentation is interpreted
  on 4-space tab stops.
- **Front matter:** leading `---`…`---` only when a closing fence exists below; a lone `---` is a
  horizontal rule. Rendered as a collapsible "Properties" block (PRD §7.4).
- **Undo coalescing:** a run of single-character typing folds into one step, broken by a caret jump or a
  newline (PRD §7.2).
- **Save mechanics:** atomic write to a temp file in the target's own directory, then rename (PRD §7.1).

## Everyday commands

Run these from this feature folder (`features/EditMarkdown/`) — the solution (`Typage.slnx`) and every
project live here. Shared .NET tooling sits at the repo root and is picked up automatically.

```bash
dotnet restore
dotnet build
dotnet run    --project Typage.Desktop          # launch the app
dotnet run    --project Typage.Cli -- <args>     # the typage CLI (see below)
dotnet test                                       # all tests
dotnet test Typage.Engine.Tests                  # fast, no UI
dotnet test Typage.Cli.Tests                     # end-to-end CLI, in-process
dotnet test Typage.UiTests                       # headless Avalonia (xUnit v3)
```

**Definition of done for any task:** `dotnet build` is clean (no new warnings — `WarningsAsErrors=nullable`
is on solution-wide), `dotnet test` is green, and if you touched the tokenizer, the incremental-equals-full
rebuild tests still pass.

## Debugging with the `typage` CLI

`Typage.Cli/` is a **second consumer of `Typage.Engine`** (no Avalonia, no network) — the same brain as
the app. Reach for it to inspect tokenization, check the renderer, or export without launching the GUI.

```bash
typage tokens notes.md            # dump the semantic token stream (type, role, range, text)
typage html notes.md -o notes.html  # render self-contained HTML (PRD §7.8)
typage html notes.md --fragment   # just the <body> markup
typage outline notes.md           # the heading outline
typage stats notes.md             # words, characters (graphemes), lines, reading time
typage cat notes.md               # round-trip the raw source (proves no rewrite)
```

`tokens` is the high-leverage one when debugging the tokenizer: it's exactly what the live-preview
surface consumes, so what it prints is what the editor styles.

## Where things go

- **Engine logic** → `Typage.Engine/` (`Document/`, `Editing/`, `Markdown/`, `Html/`).
- **Editing surface + presentation seams** → `Typage.Control/` (`Controls/`, `Views/`, `Rendering/`).
- **Shell** → `Typage.Desktop/` (window, menu, dialogs, `Services/`).
- **CLI** → `Typage.Cli/` (`Commands/` + `Infrastructure/`).
- **Tests** → `Typage.Engine.Tests/`, `Typage.Cli.Tests/`, `Typage.UiTests/`.
- Don't invent new top-level projects; extend the existing structure. If a new home seems necessary,
  note it and ask.

## What is and isn't built yet (be honest in the code and docs)

This is pre-v1; the **engine is substantially built and well-tested**, the UI is an early surface. When you
extend it, keep the layering and update the docs.

- **Built and tested:** the immutable document model + line/offset services, grapheme navigation,
  undo/redo with coalescing, the incremental tokenizer + checkpoint cache (block state machine, inline
  constructs, GFM tables, tolerant/malformed handling) with an *incremental-equals-full-rebuild* test
  suite, the HTML renderer, the `typage` CLI, and the shared `DocumentStats`. The surface drives the
  engine **incrementally** (re-tokenizes from `DocumentBuffer.LastChangedLine` via `MarkdownModel.Update`)
  and is **line-virtualized** (`ILogicalScrollable`: only the lines in the viewport are drawn). The
  shell adds Cellestra-style patterns: a single **command catalog** + **Command Palette** (⌘⇧P,
  fuzzy-filtered), **Find/Replace** (plain + regex via the engine's `DocumentSearch`, with in-editor
  match highlighting), recent-files menu, **light/dark/high-contrast themes** (View ▸ Theme,
  OS-following), a **Preferences dialog** (theme, tab width, toolbar), and modal error dialogs. The menu
  is **platform-native** (PRD §7.5): a macOS `NativeMenu` (system bar, ⌘ key-equivalents) on macOS and
  the in-window menu elsewhere, both built from the one catalog; shortcuts are defined abstractly
  (`Commands/Shortcut.cs`) so they bind/render as ⌘ on macOS and Ctrl elsewhere (`ShortcutFormatter`).
- **Also built (later additions):** §7.2 **editing assists** (list/blockquote continuation + exit,
  ordered-list renumber, auto-indent, auto-pairing/wrap-selection, Tab/Shift+Tab indent/outdent — pure
  helpers in `Engine/Editing/LinePrefix.cs` + `OrderedListRenumber.cs`), **interactive task checkboxes**
  (click a `[ ]`/`[x]` to toggle), **mouse drag-select + double-click word select + a sticky goal
  column**, a per-document **raw-source view** toggle (+ Settings default), **focus / distraction-free
  mode** (F11), a **FormattedText draw cache** (alongside the width cache), a front-matter **"Properties"
  panel** (backdrop + label; true *fold* still rides with §7.4), **external-change detection**
  (FileSystemWatcher + three-way conflict prompt + self-save suppression, §7.1), **autosave/crash
  recovery** sidecar snapshots (§9.4), and a **dependency-allowlist CI gate** (`Directory.Build.targets`,
  §5/§9.3) + a `Typage.Benchmarks` BenchmarkDotNet harness.
- **Also built — §7.4 live-preview, now full WYSIWYG:** markers are **truly hidden** (not dimmed) and stay
  hidden **always** in normal mode — the caret and selections **never** reveal raw Markdown (a deliberate
  divergence from Obsidian toward a normal document editor). The control builds a per-line **`VisualLine`**
  (`Typage.Control/Rendering`) whose concealed marker runs collapse to **zero width**, giving the
  **bidirectional offset↔visual map** that drawing, caret, hit-test, and selection all read; the map is
  caret-independent. The caret **glides over hidden markers atomically** (Left/Right), and backspace/delete
  act on the **visible grapheme** (backspacing at the end of a bold word removes a letter, not an invisible
  `*`; at list-content start it strips the whole marker prefix). Concealed markers render as **rich
  decorations** — list bullets, drawn task checkboxes (click-to-toggle), a blockquote bar, a horizontal
  rule, and a fenced-code panel — on a hanging indent. **Ordered-list numbers are computed from list
  structure** (each nesting level restarts and resumes; a blank/non-list line starts a fresh list), not
  the literal source digits. Raw-source mode reveals everything (including the literal digits).
- **Also built — §7.3 word wrap + paragraph flow:** long lines **soft-wrap to the viewport width** by default
  with **no horizontal scrolling** (toggle via `MarkdownEditor.WordWrap`), and **consecutive plain-paragraph
  source lines join into one flowing paragraph** (the interior newline renders as a space). The layout unit
  is a **paragraph block** — a contiguous document range, so a block column is `offset − blockStart` and the
  whole offset map carries over; blank lines, headings, lists, quotes, code, tables, and hard breaks break
  the flow. `VisualLine` lays a block out into wrapped **rows**; the map is 2-D (`ColumnToX`/`RowOf` for the
  caret, row-local `XToColumn`/`Spans` for hit-testing and multi-row selection); the surface virtualizes over
  **variable block heights** (a per-block row-count cache + prefix sums, rebuilt on text/theme/width change —
  O(n) per edit, fine for note-sized docs); Up/Down move by **visual row**. Indented (4-space) code blocks
  are recognized (engine), code text has **no per-line background** (one block panel), and **Alt+Shift+Up/Down
  reorders the item *and its sub-items*** (subtree swap with the adjacent sibling — nested lists/checkboxes).
  **Tabs count as indentation** everywhere spaces do (column-aware tokenizer thresholds, literal-indent
  continuation, tab-preserving Tab/Shift+Tab, tabs drawn as `IndentWidth` spaces). A **standard caret-nav
  suite** is wired (platform-aware word move/delete, smart Home, visual-row Home/End, Page Up/Down, doc-extent
  jumps, Shift+Enter plain break, Esc collapses a selection, triple-click selects the paragraph).
- **Early / approximate / not built:** **whole-line block folding** (collapsing a fenced-code/front-matter
  fence row or rendering a real table grid — they still occupy their row), the front-matter **fold** and a
  true "Properties" header, **block objects** (inline images / rendered tables / embeds), **IME**, the
  **automation peer (accessibility)**, **smart paste** (HTML→MD, image paste, paste-as-plain), **spell
  check**, and the OS **print/PDF** path are the dedicated later phases (PRD §11) and are **not built
  yet**. Don't claim them done; build them on the layering that's here. (Pixel output can't be exercised
  by the headless CI here — no GPU — so the draw path is verified structurally, by `FormattedText`
  measurement under the headless stub font manager, and by the engine's incremental tests, not by pixel
  tests.)

## Conventions

- **Branching & commits:** commit straight to `main` (single linear history, like the rest of this repo).
  [Conventional Commits](https://www.conventionalcommits.org): `type(scope): summary`. Scopes here:
  `engine`, `tokenizer`, `document`, `control`, `surface`, `desktop`, `cli`, `html`, `theme`, `docs`.
- **C#:** follow the repo `.editorconfig`. Nullable on (warnings are errors). Prefer records for model
  values. Small, single-purpose types matching the folders.
- **Accessibility:** new controls expose automation peers and are keyboard-operable; concealment must
  never remove content from the accessibility tree (PRD §9.6).
- **Threading:** the UI thread owns edits; the background tokenizer reads an immutable snapshot, so it
  never races the editor (PRD §9.2).

## When in doubt

- A design detail is unclear → check PRD §7.4/§8/§12 and ARCHITECTURE first.
- A task seems to need a new dependency, a non-goal, or a golden-rule break → **stop and ask**.
- You find a real inaccuracy or genuinely unspecified behavior → flag it and **record the resolution in
  the doc**, don't just diverge in code. The docs are the source of truth.
