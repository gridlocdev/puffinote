# PRD — Standalone Markdown Editor with Live Inline Preview

> **Working name:** **Typage** (type + page → Typage)
> **Document status:** Draft v2 (revised after design review)

---

## 1. Summary

A desktop application whose single job is to **open, edit, and save one Markdown file at a time**, in the spirit of a word processor like Microsoft Word or LibreOffice Writer — but Markdown-native. Editing uses **live inline rendering** (Obsidian "Live Preview" style): the document reads as formatted prose, syntax markers reveal only where the caret is, and no separate raw/preview split is required for normal use.

The application is built on **Avalonia UI / .NET**. Its dependency surface above the **.NET BCL + Avalonia (and Avalonia's native deps)** base is zero NuGet packages; the only additional trusted components are **OS-provided system services** (spell check, print, clipboard, file dialogs), accessed via platform interop. The Markdown engine, editing surface, document model, and live-preview logic are all owned in-house.

The app runs as a **single window editing a single document** and is designed so the editing surface is a **self-contained, reusable control** that can later drop into a larger notes application unchanged. The standalone app is a thin shell around that control.

The design is explicitly evaluated against **Jakob Nielsen's 10 usability heuristics** (see §6).

---

## 2. Goals and non-goals

### 2.1 Goals

- A fast, focused, single-file Markdown editor that feels like a document editor, not a code editor.
- True **live inline rendering**: caret-aware concealment, in-place styling, and rich block rendering, with precisely defined behavior.
- A **minimal, auditable dependency surface** (.NET BCL + Avalonia + OS system services only).
- The editing surface ships as a **clean, embeddable component** with a stable API, separable from the standalone shell.
- Cross-platform (Windows, macOS, Linux) from one codebase.
- **No data loss** under any path (atomic saves, conflict handling, crash recovery, encoding safety).
- **Strong usability** measured against Nielsen's heuristics.

### 2.2 Non-goals (this app, v1)

- Multi-file notebooks, tabs, backlinks, graph view, note linking. (Notes-app territory.)
- Multiple documents or multiple windows per process (single-window by decision — §5).
- A plugin system or third-party extension marketplace.
- Full, byte-for-byte CommonMark / GFM conformance (see §8.5); we ship a documented pragmatic dialect.
- Real-time collaboration, comments, track-changes, cloud sync, accounts.
- Rendering/editing of `.ods` spreadsheets. The **embed-hosting plumbing** is in scope (§7.6); the spreadsheet renderer is deferred to notes-app integration.
- A merge/diff conflict resolver (a non-destructive three-way _choice_ is in scope; automatic merging is not — §7.1).

---

## 3. Background and motivation

This app is the first deliverable in a larger effort to build an Obsidian-class notes application in Avalonia. Rather than build the full notes app first, we isolate the hardest, most reusable piece — the live-inline editing surface — as a standalone product that is independently useful and testable.

Three constraints drive the architecture:

1. **Supply-chain control.** The trusted base is intentionally limited to **.NET runtime + Avalonia (+ its native deps) + OS system services**. We add no NuGet packages above that line — no Markdown library, no editor library, no docking library, no PDF library. This is justified primarily by the need for deep customization (off-the-shelf editor controls resist syntax concealment and inline block-swapping) and secondarily by auditability. OS services (spell check, print, clipboard) are part of the platform, not added dependencies.

2. **Reusability.** The editing surface must drop into the notes app without a rewrite, forcing a clean split between _engine/control_ (reusable) and _application shell_ (replaceable).

3. **Usability.** As a Writer-class tool, it must meet baseline document-editor expectations (formatting affordances, spell check, status visibility, error recovery), evaluated against Nielsen's heuristics.

---

## 4. Target users and use cases

| User | Primary use case |
|---|---|
| Writers / note-takers | Open a `.md`, write and edit with formatting visible inline, save. |
| Technical writers / developers | Edit READMEs, docs, specs with correct code, table, and tab handling. |
| Future: notes-app users | Use the same surface inside a multi-file knowledge base. |

**Core journey:** open (file association, dialog, CLI arg, or drag-drop) → content renders formatted immediately → click in; surrounding syntax reveals; edits render live → save (atomic, encoding/line-endings preserved) → close (prompt if unsaved).

---

## 5. Product principles and constraints

- **Single window, single document.** One file, one window, one process. Opening another file reuses the window and prompts to save if there are unsaved changes.
- **Document-first, not code-first.** Default presentation is formatted; raw syntax is an editing affordance.
- **Minimal dependencies, enforced.** A CI gate fails the build on any NuGet dependency outside the allowlist (§9.3). OS system services are permitted via interop.
- **Data integrity over features.** Any feature risking user data is gated behind atomic, recoverable, non-destructive flows.
- **Reusable by construction.** Dependency direction is one-way: shell → control → engine. No upward references; the engine is Avalonia-free.
- **Usability is a first-class requirement**, evaluated per §6.

---

## 6. Usability principles (Nielsen's 10 heuristics)

| # | Heuristic | How the design satisfies it |
|---|---|---|
| 1 | **Visibility of system status** | Persistent **status bar**: saved/modified state, word & character count, reading time, cursor line:col, selection count, encoding, line-ending style. Large-file **tokenization progress**; **autosave/recovery** indicator; spell-check squiggles. (§7.5) |
| 2 | **Match between system and real world** | Familiar **menu bar** (File/Edit/Format/View/Help), standard shortcuts, plain-language labels, front matter surfaced as **"Properties."** (§7.5, §7.4) |
| 3 | **User control and freedom** | Undo/redo, **Revert to saved**, Cancel on every dialog, **Paste as plain text**, **three-way conflict choice**, easy exit from raw-source mode and block-source editing. (§7.1, §7.2, §7.6) |
| 4 | **Consistency and standards** | Platform-native menus, shortcuts, and dialogs per OS; consistent command behavior across the app. (§7.5, §9.5) |
| 5 | **Error prevention** | Confirm on unsaved close/open/new; **save-before-asset-insert**; read-only detection; **encoding guard**; atomic saves; watcher **self-save suppression**. (§7.1, §7.6) |
| 6 | **Recognition over recall** | Visible (toggleable) **formatting toolbar** + menus; right-click spell suggestions; tooltips showing shortcuts; offline syntax/shortcut reference. (§7.5, §7.7, §7.8) |
| 7 | **Flexibility and efficiency** | Full keyboard shortcuts, **smart paste**, **list continuation & auto-pairing**, recent files, hideable chrome / **focus mode**, configurable settings. (§7.2, §7.6, §7.9) |
| 8 | **Aesthetic and minimalist design** | Single-file focus, slim hideable toolbar, clean live-preview default, optional distraction-free **focus mode**. (§7.5) |
| 9 | **Recognize, diagnose, recover from errors** | Error dialogs state the **OS reason + a suggested action** (e.g., "permission denied → Save As"); encoding fallback flow; no dead-ends. (§7.1) |
| 10 | **Help and documentation** | **Help menu** with a built-in, **offline** Markdown syntax + keyboard-shortcut reference; toolbar tooltips. (§7.5) |

---

## 7. Functional requirements

### 7.1 File operations

- **Open** via CLI argument, OS file association / "Open With," in-app dialog, and **drag-drop**. Opening while dirty prompts (save / discard / cancel).
- **New**, **Save**, **Save As**, **Revert to saved**, **Recent files** (MRU, persisted).
- **Dirty indicator** in title and status bar; prompt on every unsaved exit path (heuristic 5).
- **External-change detection** via `FileSystemWatcher`, with **self-save suppression** (a "saving" guard window plus content-hash comparison).
- **Conflict handling:** if the file changed on disk _and_ there are unsaved local edits, present a non-destructive **three-way choice** — _Keep my changes (overwrite)_, _Discard mine and reload_, or _Save mine to a new file_. (Automatic merge is out of scope.)
- **Encoding (decision tree):** BOM present → use it and preserve on save. No BOM → assume **UTF-8** and validate. Invalid UTF-8 → **open read-only** with a non-destructive warning; offer a fallback encoding chosen by the user; saving is blocked until confirmed. Detected encoding and **line-ending style (LF/CRLF)** are preserved byte-for-byte on save; never silently normalized. New documents default to UTF-8 (no BOM) and the platform's native line ending (configurable).
- **Save mechanics:** atomic write to a temp file **in the target's own directory** then rename. On failure show the OS reason and offer **Save As**; the in-memory buffer and recovery snapshot are never discarded.
- **Read-only files:** detected on open; indicated in the status bar; editing allowed but saving routes to **Save As**.
- **Large files:** open without blocking the UI; above a configurable ceiling, warn and offer read-only open.

### 7.2 Editing experience

- Live inline rendering for all supported constructs (§8.3) with precisely defined reveal behavior (§7.4).
- **Caret/selection** operate on **grapheme clusters** (via `System.Globalization`).
- **IME composition** is a first-class requirement, with the candidate window at the caret.
- **Bidirectional / RTL text** supported using Avalonia's bidi; caret movement is visual-order aware.
- **Undo/redo** with sensible grouping: typing runs coalesce (bounded by a short idle timeout and by caret jumps); structural edits and command operations are discrete. Revert and external reload clear the redo stack and reset the undo baseline.
- **Editing assists:** list continuation; blockquote/indent continuation on Enter; auto-pairing of `*` `_` `` ` `` `[` `(` with wrap-selection; Tab/Shift+Tab indent/outdent; auto-indent.
- **Tabs and indentation:** default inserts **spaces, width 4** (configurable), interpreted on 4-space tab stops. Literal tabs typed inside code blocks are preserved.
- **Find / Replace:** plain and **regex** (.NET BCL `Regex`); matches highlight; Replace/Replace All operate on source offsets even where markers are concealed.
- **Clipboard:** copy yields Markdown **source** by default (round-trippable). Smart paste and "paste as plain text" per §7.6.
- **Raw source toggle:** per-document, **session-only** View toggle (default off = live preview); never written into the file. A global "start new documents in source mode" default lives in Settings.
- Theme support: light / dark / high-contrast, honoring OS preference.

### 7.3 The editing surface (custom control)

A custom Avalonia `Control` built **on top of `Avalonia.Media.TextFormatting`** (`TextFormatter`, `TextLine`, `TextRun`, `ITextSource`, `TextRunProperties`). It does **not** reuse `TextBox` for layout and does **not** reimplement shaping, bidi, or font fallback. Responsibilities: build per-line `TextRun`s from the document via a custom `ITextSource`; own caret, selection, hit-testing, input (keyboard, mouse, IME), clipboard; own vertical scrolling and **line virtualization**; host **inline block objects** (images, tables, embeds).

**Accessibility is designed into this layer, not deferred.** The surface exposes an **automation peer** implementing the platform text pattern (UIA Text on Windows; AX equivalents on macOS/Linux), surfacing the **document text, caret, and selection in document offsets**.

### 7.4 Live inline preview behavior (precise spec)

**Reveal scope** — what "reveal the source" means depends on the construct:

- **Inline marks** (emphasis, strikethrough, highlight, inline code, links, autolinks): markers reveal when the caret/selection **intersects that specific inline node**; otherwise concealed and the styled form renders.
- **Line-prefix marks** (heading `#`, blockquote `>`, list/task markers): reveal when the caret is **on that line**.
- **Block constructs** (fenced code, tables, images, front matter, horizontal rules): the **block's raw source** is shown when the caret/selection **intersects the block's source range**; a rich rendering shows otherwise.

**Selection forces reveal.** Any node a selection intersects is treated as revealed for the duration of the selection.

**Caret traversal of concealed markers.** While concealed, a marker run is treated as **atomic** — Left/Right navigation skips over it. Once a node is revealed, its markers are normal, per-character navigable text.

**Offset↔visual mapping.** Because the document and visual models diverge under concealment, the surface maintains a **bidirectional mapping** so clicks, arrow keys, selection, and caret movement always resolve to correct document offsets. This is the highest-risk component and carries a dedicated property-based + example-based test suite.

**Intermediate / malformed states (the common case while typing).** The tokenizer is **tolerant and best-effort**: an unmatched opening marker renders as literal text until validly closed; an unterminated fenced code block extends to EOF and renders as code; partial links/images render their visible text literally until closed. No flicker or layout thrash.

**Front matter** (YAML between leading `---` delimiters) renders as a **dimmed, collapsible "Properties" block** at the top, collapsed by default; expanding (or moving the caret in) shows the raw YAML. No structured key/value editor in v1.

**Status (implemented).** Concealment is built: the engine tags each inline construct's tokens with a per-line node id, and the control's per-line `VisualLine` collapses concealed marker runs to zero width — the bidirectional offset↔visual map that keeps caret/click/selection correct. Inline marks reveal per node, line-prefix marks per line, and concealed markers render as rich decorations (bullets, drawn checkboxes, a blockquote bar, a horizontal rule, a fenced-code band). Still deferred to a later phase: collapsing *whole-line* block rows (fenced-code/front-matter fences, real table grids) — i.e. the front-matter *fold* and a true "Properties" header — plus inline-image/embed block objects.

### 7.5 Commands, UI chrome, and status

- **Menu bar** (platform-native): File, Edit, Format, View, Help.
- **Formatting toolbar:** slim, toggleable, default on; hideable for focus. Buttons: bold, italic, code, link, headings, lists, quote, table.
- **Keyboard commands:** Ctrl/Cmd+B bold, +I italic, +E inline code, +K link, +Shift+K code block, Ctrl/Cmd+1…6 headings, list/quote toggles, indent/outdent.
- **Status bar:** modified/saved state, word & character count, reading-time estimate, cursor line:col, selection count, encoding, line-ending style, read-only flag, large-file tokenization-progress indicator.
- **Focus / distraction-free mode:** hides toolbar and chrome, centers the text column.
- **Help menu:** built-in **offline** Markdown syntax reference and keyboard-shortcut sheet.

### 7.6 Smart paste and asset management

**Smart paste**, with a **Paste as plain text** escape hatch (Ctrl/Cmd+Shift+V): HTML → convert to Markdown via an in-house, dependency-free converter; image data → write into the asset folder and insert a relative `![](path)`; a URL with text selected → wrap the selection as `[selection](url)`; otherwise insert plain text.

**Asset model:** relative paths resolve against the directory of the open file; pasted/dropped images are copied into an attachments subfolder (default `./attachments/`, configurable) and referenced relatively; if the document is new/unsaved, prompt to **save first**; drag-drop of image files behaves like image paste.

### 7.7 Spell check

Uses **OS-native spell services** (Windows Spell Checking API, macOS `NSSpellChecker`, Linux system dictionaries). Misspellings get a red squiggle; right-click shows suggestions, **Add to dictionary**, and **Ignore**. Spell check skips code spans, code blocks, URLs, and front matter. Toggleable and language-selectable; degrades gracefully where no API is available.

### 7.8 Export and print

- **Export to HTML** — fully in-house (render the document to a self-contained HTML string). **In scope for v1.**
- **Print and Export to PDF** — routed through the **OS print system** ("Print to PDF"). Marked **v1.1 / fast-follow**; the interim path is "Export HTML → open in browser → print/PDF."

### 7.9 Settings and preferences

A local JSON preferences store with a Settings UI: theme mode; toolbar visibility; focus-mode defaults; tab width and tabs-vs-spaces; new-document line-ending and encoding defaults; autosave/recovery interval; attachments folder name; copy-vs-link default; spell-check enable + language; dialect flags (wiki-links / embeds on-off; off by default in standalone); raw-source default; selection-reveal behavior; recent-files list.

---

## 8. Markdown processing specification

An **incremental, line-based tokenizer with block state and a checkpoint cache** — not a full document AST. Inline tokenization runs within each line.

### 8.1 Block state machine and checkpoint cache

The tokenizer carries explicit block state across lines: `Normal`, `FencedCode` (fence char + length), `IndentedCode`, `Blockquote` (depth), `List` (kind, marker, indent, loose/tight), `Table`, `FrontMatter`, `HtmlBlock` (limited).

A **line-state checkpoint cache** stores the entering block state at regular intervals (e.g., every N lines and at `Normal`-state block boundaries). This lets rendering of any viewport **resume tokenizing from the nearest checkpoint above it** instead of from the top of the file. Checkpoints are invalidated from the first edited line downward and rebuilt incrementally.

### 8.2 Incremental re-tokenization and worst case

On edit, re-tokenize from the first affected line and stop once the carried block state **re-converges** with the prior result. **Worst case is O(n)** (e.g., typing an opening fence at the top flips the rest of the file into a code block). The latency budget survives this by tokenizing the **visible region synchronously** and refreshing the **tail in the background**; the UI never blocks on a full-document re-tokenization.

### 8.3 Supported constructs (v1)

| Construct | Syntax | Block/Inline | Live rendering | Concealment |
|---|---|---|---|---|
| Heading | `#`…`######` | Block | Larger weighted text | Hide `#`+space when caret off line |
| Bold | `**x**` / `__x__` | Inline | Bold | Hide markers when caret off node |
| Italic | `*x*` / `_x_` | Inline | Italic | Hide markers when caret off node |
| Bold+Italic | `***x***` | Inline | Bold italic | Hide markers when caret off node |
| Strikethrough | `~~x~~` | Inline | Struck | Hide markers when caret off node |
| Highlight | `==x==` | Inline | Highlighted | Hide markers when caret off node |
| Inline code | `` `x` `` | Inline | Monospace, subtle bg | Hide backticks when caret off node |
| Code fence | ` ```lang ` | Block | Monospace block + language label | Source when caret inside block |
| Blockquote | `> x` | Block | Indented, accented | Hide `>` when caret off line |
| Unordered list | `-`/`*`/`+` | Block | Bullet glyph | Raw marker shown when editing it |
| Ordered list | `1.` | Block | Number | Raw shown when editing |
| Task list | `- [ ]`/`- [x]` | Block | Interactive checkbox | Toggle edits source `[ ]`/`[x]` |
| Link | `[text](url)` | Inline | Styled, clickable | Hide `[]()`+URL when caret off node |
| Autolink | `<https://…>` / bare URL | Inline | Styled, clickable | Hide brackets when caret off node |
| Image | `![alt](url)` | Block/inline | Rendered image | Source when caret inside |
| Horizontal rule | `---`/`***` | Block | Divider | Source when caret on line |
| Table | GFM pipe tables | Block | Rendered grid | Source when caret inside |
| Hard break | trailing `\` / two spaces | Inline | Line break | n/a |
| Escape | `\*`, `\_`, … | Inline | Literal char | Hide backslash when caret off node |
| Front matter | leading `---` YAML `---` | Block | Collapsible "Properties" | Source when caret inside |
| Wiki-link* | `[[target]]` | Inline | Styled link | Hide brackets when caret off node |
| Embed* | `![[target]]` | Block | Host-resolved (image/table now) | Source when caret inside |

\* Recognized by the tokenizer always; **rendering depends on the host's embed resolver** and is **off by default** in the standalone app (on in the notes app).

### 8.4 Out of scope / deferred

Full CommonMark/GFM edge-case fidelity (lazy continuation, complete emphasis precedence, link reference definitions, nested HTML blocks); arbitrary raw HTML rendering; footnotes, definition lists, math, diagram blocks.

### 8.5 Determinism and round-trip

Editing never rewrites untouched bytes; the document is the source of truth and rendering is derived. Saving writes exactly the buffer. Formatting commands and checkbox toggles edit the source minimally and predictably.

---

## 9. Non-functional requirements

### 9.1 Performance

- **Typing latency** within one frame budget (~16 ms at 60 Hz), via incremental re-tokenization (affected lines, checkpoint-resumed) and render virtualization.
- **Scroll** into any position of a large file is bounded by checkpoint distance, not document length.
- **Open responsiveness** on large files (tens of thousands of lines / multi-MB) without blocking the UI.
- **Memory:** document model is a **piece tree / rope** with cheap edits and undo snapshots; plain-string document buffers are disallowed.

### 9.2 Concurrency / threading model

The document model is an **immutable-snapshot piece tree**: an edit produces a new snapshot via structural sharing; the **UI thread owns edits**. The **background tokenizer reads a snapshot**, so it never races the editor; results are marshaled back to the UI thread. No locks on the editing hot path.

### 9.3 Security and supply chain

- **Allowlist:** .NET BCL + Avalonia (+ transitive natives) + **OS system services via interop**. No other runtime NuGet packages.
- **Build-time enforcement:** CI fails on any non-allowlisted dependency; versions pinned via lockfile; per-release SBOM.
- **No network at rest:** no network calls during editing; Help is offline. External links delegate to the OS browser on explicit user intent.
- **File safety:** atomic same-volume writes; never partial overwrites.

### 9.4 Reliability / data safety

Autosave/recovery snapshot to a sidecar location at a configurable interval; offer recovery on crash restart. External-change detection with self-save suppression and the three-way conflict choice. Unsaved-change prompts on every exit path.

### 9.5 Cross-platform

Windows, macOS, Linux from one codebase; menus, shortcuts, dialogs, and file associations follow each OS's conventions.

### 9.6 Accessibility

Designed in from Phase 3. Automation peer implements the platform text pattern, exposing document text, caret, and selection. Full keyboard operability; theme/high-contrast; respects OS theme and reduced-motion. Concealment must not remove content from the accessibility tree.

### 9.7 Internationalization

Grapheme-cluster-aware caret, selection, and deletion; IME composition support; RTL and mixed-direction editing via Avalonia bidi; encoding detection/preservation per §7.1.

---

## 10. Componentization and reuse

### 10.1 Layering and the engine/presentation boundary

- **Engine library** (pure .NET, Avalonia-free, headless-testable): document model, tokenizer + dialect, checkpoint cache, line/offset services. **Emits semantic tokens only** — types, source ranges, roles (`marker` vs `content`), and embed _references_ (target strings). No UI, no styling.
- **Control library** (depends on Avalonia + engine): the custom surface, rendering, concealment, input, automation peer, and the two presentation-side seams:
  - a **`MarkdownTheme`** mapping token types → visual treatment;
  - an **`IEmbedResolver`** mapping an embed reference → an Avalonia renderable. **Reference detection is engine-side; resolution to a control is control-side.**
- **Standalone shell** (depends on control): window, menus, dialogs, recent files, file watching, settings. **Replaceable** by the notes app.

Dependency direction is strictly **shell → control → engine**; no upward references.

### 10.2 Reuse requirements

- The control exposes a stable public API: set/get text, dirty flag, selection/caret, `TextChanged`/`SelectionChanged`/`SaveRequested` events, undo/redo + formatting commands, theme, and embed-resolver injection.
- No hard-coded file/app assumptions in the control; file I/O lives in the shell.
- Verified by a minimal host harness driving the control with the standalone shell removed.

---

## 11. Milestones (build order)

1. **Phase 0 — Skeleton & guardrails:** solution layout, dependency allowlist + CI gate, theming baseline, headless test harness, settings store.
2. **Phase 1 — Document model:** immutable-snapshot piece tree, line/offset services, undo/redo, grapheme navigation. Fully unit-tested, no UI.
3. **Phase 2 — Tokenizer, dialect & checkpoints:** incremental tokenizer, block state machine, checkpoint cache, tolerant/malformed handling, against a test corpus.
4. **Phase 3 — Surface (static) + a11y foundation:** custom control over `TextFormatting` with caret, selection, scrolling, virtualization, input, IME — and the automation peer / offset exposure.
5. **Phase 4 — Live preview / concealment:** reveal scopes, caret traversal, selection-forces-reveal, offset↔visual mapping.
6. **Phase 5 — Blocks, embeds & assets:** images, table rendering, code styling, `IEmbedResolver`, inline control hosting, asset/attachments model.
7. **Phase 6 — Editing power & input UX:** smart paste, HTML→MD converter, editing assists, find/replace, formatting commands, toolbar, status bar, focus mode.
8. **Phase 7 — Shell & data safety:** open/save/save-as/revert, recent files, watcher + self-save suppression, conflict three-way, encoding flow, autosave/recovery, file association.
9. **Phase 8 — Services & export:** OS spell check, HTML export, OS print/PDF path, Help reference.
10. **Phase 9 — Hardening:** performance tuning, accessibility validation, packaging/installers per OS.

> **Implementation status (this repo).** Phases 0–2 are substantially built and unit-tested in
> `Typage.Engine`; HTML export (Phase 8) and a baseline shell/CLI exist. The surface is an early Phase 3/4
> approximation: marker dimming with 1:1 offsets, but it does drive the engine **incrementally** (§8.2)
> and is **line-virtualized** (§9.1). IME and the automation peer are not built yet. See
> [../README.md](../README.md) and [../AGENTS.md](../AGENTS.md) for the honest built/not-built breakdown.

---

## 12. Risks and resolved questions

**Resolved (was open in v1):**

- **Window model →** single window, single document, single process.
- **Raw-source toggle →** per-document, session-only View toggle; global default in Settings; never persisted into the file.
- **Front matter →** dimmed, collapsible "Properties" block; raw YAML on caret-enter; no structured editor in v1.

**Remaining risks:**

- **Offset↔visual mapping correctness** — dominant bug source; mitigated by a large property-based + example test suite.
- **Accessibility depth** — peer designed early, full screen-reader parity staged.
- **OS spell-check coverage** — Linux support varies; graceful degradation required.
- **Print/PDF maturity** — Avalonia printing is limited; OS print path is fast-follow.
- **HTML→Markdown converter fidelity** — in-house and necessarily limited.
- **IME / complex-script input** — validate on all three platforms early.
- **Inline interactive controls in a virtualized surface** — focus/sizing/scroll need early prototyping.
- **Dialect scope creep** — keep the documented subset firm; resist sliding toward full CommonMark.

---

## 13. Acceptance criteria (v1)

- Opening a `.md` renders all §8.3 constructs with correct concealment; editing any construct reveals and round-trips its source with no data loss.
- Reveal behavior matches §7.4 exactly: inline-node, line-prefix, and block reveal scopes; selection forces reveal; concealed markers are skipped on arrow navigation.
- Partial/malformed input renders gracefully per §7.4 with no errors or flicker.
- Saving preserves original encoding and line-ending style byte-for-byte outside edited regions; undecodable files open read-only with the fallback flow.
- The app's own saves never trigger a "changed on disk" prompt; external changes with local edits present the three-way choice.
- Typing in a large document stays within the latency budget; scrolling into any position is bounded by checkpoint distance.
- Caret/selection/deletion operate on grapheme clusters; IME composition works on each platform.
- Smart paste converts HTML, inserts pasted images into the attachments folder, and wraps URLs over a selection; "paste as plain text" bypasses all of it.
- Spell check shows squiggles and suggestions where an OS service is available, and degrades cleanly where it is not.
- HTML export produces a self-contained file; the OS print/PDF or browser-export path is reachable from the Export menu.
- The status bar reports modified state, counts, cursor position, encoding, and line endings; all destructive actions are confirmable and reversible.
- A screen reader can read the document and report caret/selection on at least one platform.
- The build fails if any non-allowlisted dependency is added.
- The editor control runs entirely through its public API with the standalone shell removed (verified by a host harness).
- Runs on Windows, macOS, and Linux from one codebase.
