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

1. **`Typage.Engine`** — the Markdown brain: the incremental tokenizer + checkpoint cache, the inline
   model, the list/quote line-prefix rules, and the HTML renderer. **Zero UI dependencies.**
   Headless-testable. Everything here knows what the text *means*; everything true of text before
   that — the immutable document model (piece table), grapheme-aware navigation, selection, undo/redo,
   search, stats, and the file codec — lives in **`shared/Puffinote.Text`** (proposal 0003 §4), which
   Plaintain stands on too. Types you used to find in `Typage.Engine.Document`/`.Editing` are now in
   `Puffinote.Text.Document`/`.Editing`; nothing else about them changed.
2. **`Typage.Control`** — an Avalonia control library: the custom live-preview editing surface
   (`MarkdownEditor`/`MarkdownEditorView`) built on `Avalonia.Media.TextFormatting`, the two
   presentation seams (`MarkdownTheme`, `IEmbedResolver`), and — crucially — **everything that acts on
   the open document**: the one command catalog (`MarkdownEditingCommands`), the icon-driven command bar
   (`MarkdownCommandBar`), the find/replace bar, and the link / image / reference dialogs, over the
   surface's own overlay layer. Embeddable, unchanged, into Puffinote.
   *If you are adding an editing command, it goes here — never in a shell.* That is what keeps the
   standalone app and Puffinote presenting the same editor instead of two that drift.
3. **`Typage.Desktop`** — the standalone Avalonia shell (assembly `Typage`): single window, menu,
   status bar, open/save dialogs, recent files, settings. A thin host around the control — it contributes
   only shell commands and merges in `MarkdownEditorView.EditingCommands`.
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
- **Task states (PRD §8.3.2):** a task box holds **one of three** characters — `[ ]` not started, `[/]` in
  progress, `[x]` done — and anything else (`[a]`, and also `[?]`/`[>]`/`[-]`) is not a box at all: it stays
  literal text. The vocabulary lives in the engine (`Markdown/TaskState.cs`), which is where the tokenizer,
  `LinePrefix`, and the HTML export all read it from; the **colour and the drawn shape are the control's**
  (`MarkdownTheme`, `Rendering/TaskGlyphs.cs`), per golden rule 2. The box is a rounded square and the design is
  **single-hue**: one accent, shared by the in-progress dash and the done fill; shape, not colour, is what tells
  the three apart (§9.6). A click **completes** (it does not stop at in-progress); in progress comes from the
  picker (⌥⌘L or a secondary click on the box). Don't grow this vocabulary without reading §8.3.2 — the three
  are a deliberate stopping point, not an unfinished six.
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
project live here. Shared .NET tooling sits at the repo root and is picked up automatically. (`make
build-typage` / `test-typage` / `format-typage` do the same from the root; bare `just` lists them.)

```bash
export PATH="$PATH:/root/.dotnet"                 # devcontainer: dotnet isn't on PATH
dotnet restore
dotnet build
dotnet format --verify-no-changes                 # formatting is a gate, not a suggestion
dotnet run    --project Typage.Desktop           # launch the app (fails in the container — no GPU)
dotnet run    --project Typage.Cli -- <args>      # the typage CLI (see below)
dotnet test                                       # all tests
dotnet test Typage.Engine.Tests                  # fast, no UI
dotnet test Typage.Cli.Tests                     # end-to-end CLI, in-process
dotnet test Typage.UiTests                       # headless Avalonia (xUnit v3), null drawing — no GPU needed
```

Never run two `dotnet build`/`dotnet test` invocations at once — concurrent MSBuild runs deadlock and hang.

**Definition of done for any task:** the code is formatted (`dotnet format`), `dotnet build` is clean, and
`dotnet test` is green — and if you touched the tokenizer, the incremental-equals-full rebuild tests still
pass. "Clean" means **zero** warnings: `TreatWarningsAsErrors=true` repo-wide fails the build on any warning,
analyzer and code-style ones included (only `IDE0060`, unused parameter, is exempt). Touching
`Typage.Control` or `shared/` means Puffinote is affected too — build `app/Puffinote.slnx`, or just
`just check`.

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

- **Markdown logic** → `Typage.Engine/` (`Markdown/`, `Html/`, and the list/quote rules in `Editing/`).
- **Anything true of text regardless of format** — the piece table, graphemes, selection, undo, search,
  the file codec → **`shared/Puffinote.Text/`**. If a change would read the same in a plaintext editor,
  it belongs there; if it mentions a `#` or a `-`, it belongs in the engine.
- **Editing surface + presentation seams + every editing command** → `Typage.Control/` (`Commands/`,
  `Controls/`, `Views/`, `Rendering/`). Puffinote embeds *this*, so a command added to the shell instead
  would be invisible there — see the note in "What this project is."
- **Shell** → `Typage.Desktop/` (window, menu, dialogs, `Services/`) — shell commands only.
- **CLI** → `Typage.Cli/` (`Commands/` + `Infrastructure/`).
- **Anything Cellestra or Puffinote would also want** — theming, icons/glyphs, overlays, shared shell
  chrome → **`shared/Puffinote.Components/`** (or `Puffinote.Components.Core/` for non-UI code the CLI can
  use). Both are in `Typage.slnx`, so you can edit them from here — but they're in Cellestra's and
  Puffinote's solutions too, so a change there is a change to all three: run `just check`, not only
  `just test-typage`.
- **Tests** → `Typage.Engine.Tests/`, `Typage.Cli.Tests/`, `Typage.UiTests/` (and
  `shared/Puffinote.Text.Tests/`, which the moved types' tests travelled into).
- Don't invent new top-level projects; extend the existing structure. If a new home seems necessary,
  note it and ask.

## What is and isn't built yet (be honest in the code and docs)

Pre-v1. The **engine is substantially built and well-tested**; the surface is well past its first
approximation, but the later phases (PRD §11) are genuinely absent. Two rules: **never claim a "not built"
row is done**, and **update this table in the same commit that changes an answer** — a stale status section
is worse than none, because it's what the next agent plans against.

| Area | Status |
|---|---|
| Document model, line/offset services, grapheme nav, undo/redo + coalescing | **Built**, unit-tested |
| Incremental tokenizer + checkpoint cache (block machine, inline, GFM tables, tolerant input) | **Built** — guarded by an *incremental-equals-full-rebuild* suite. Touch it and those tests must stay green |
| HTML renderer · `typage` CLI · `DocumentStats` | **Built** |
| Live preview / concealment (§7.4) · word wrap + paragraph flow (§7.3) | **Built** — full WYSIWYG; see the invariants below |
| Editing assists (§7.2) · three-state task boxes (§8.3.2) · the caret-nav suite · raw-source toggle · focus mode | **Built** |
| One command catalog (`Typage.Control/Commands`) + Command Menu (⌘K) · find/replace · platform-native menu (§7.5) · themes · preferences · recent files | **Built** — hosts merge the catalog in; they don't define commands |
| File safety: atomic save, watcher + three-way conflict, autosave/recovery (§9.4), dependency-allowlist gate (§5/§9.3) | **Built** |
| Image **insert** + staging for unsaved docs (`ImageStaging`) | **Built** — picking and inserting only; *pasting* an image is not |
| Whole-line block **folding** — collapsing a code/front-matter fence row, a real table grid, a true front-matter "Properties" fold | **Not built** — those rows still render as rows |
| **Block objects** — inline images, rendered tables, embeds | **Not built** |
| **IME** · **automation peer** (accessibility) | **Not built** (PRD §11, phases 3 and 9) |
| **Smart paste** — HTML→MD, image paste, paste-as-plain | **Not built** — `PasteAsync` is plain text |
| **Spell check** · OS **print/PDF** | **Not built** (phase 8) |

**Verification here has no pixels.** The headless CI has no GPU, so the draw path is checked *structurally* —
`FormattedText` measurement under the headless stub font manager, plus the engine's incremental tests — and
never by pixel comparison. Don't add a golden-image test; it cannot run here.

### Invariants the surface is built on (don't regress these)

Not status — the design the rest of the surface assumes. Read these before touching `Typage.Control/Rendering`.

- **Markers are hidden, always** (§7.4) — not dimmed, and the caret/selection **never** reveal raw Markdown.
  That is a deliberate divergence from Obsidian toward a normal document editor. Raw-source mode is the only
  reveal (it shows the literal ordered-list digits too).
- **`VisualLine` owns the offset↔visual map.** Concealed marker runs collapse to zero width, giving one
  bidirectional, **caret-independent** map that drawing, hit-testing, caret motion, and selection all read.
  It's the seam everything hangs off — extend it; never compute a second mapping alongside it.
- **The caret and deletes move in *visible* graphemes.** The caret glides over a hidden marker atomically;
  backspace at the end of a bold word takes a letter, not an invisible `*`; at list-content start it strips
  the whole marker prefix.
- **Concealed markers become rich decorations** on a hanging indent — bullets, task boxes, the blockquote
  bar, the rule, the fenced-code panel. **Ordered-list numbers are computed from list structure** (each
  nesting level restarts and resumes; a blank or non-list line starts a fresh list), not read from the digits
  in the source.
- **The layout unit is a paragraph block, not a line** (§7.3). Consecutive plain-paragraph source lines flow
  into one paragraph, so a block column is `offset − blockStart` and the map is 2-D. Soft wrap to the
  viewport, no horizontal scrolling; Up/Down move by **visual row**; the surface virtualizes over **variable
  block heights** (per-block row-count cache + prefix sums, rebuilt on text/theme/width change).
- **Tabs count as indentation everywhere spaces do** — column-aware tokenizer thresholds, literal-indent
  continuation, tab-preserving Tab/Shift+Tab, tabs drawn as `IndentWidth` spaces.

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
