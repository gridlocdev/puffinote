# Proposal 0003 — **Plaintain**, a plaintext editor for the Puffinote family

- **Status:** **Phase 0 landed** — `shared/Puffinote.Text` exists, all four areas stand on it, and the
  `TextFileCodec` decision tree is complete. Phases 1–5 followed (§9.6 was re-opened by a direct request
  to build the editor now rather than after the family's 1.0; the sequencing argument in §9.6 was about
  *when*, not *whether*, and nothing in §5's design changed).
- **Author:** (you) · drafted with Claude
- **Affects:** `shared/` (new `Puffinote.Text`), `features/EditMarkdown` (Typage — engine split),
  new `features/EditPlaintext` (Plaintain), `app/` (Puffinote shell — a third pane type), `build/`, `justfile`

---

## 1. Summary

Add **Plaintain** (**plain** + **plantain**) — a Notepad-class plaintext editor — as the family's
**third standalone editor**, alongside Typage and Cellestra, composed by Puffinote like the other two.
CLI binary: **`pltn`**. It claims `.txt` and friends in Finder and opens *any* decodable text file.

The reusable half of `Typage.Engine` — the piece table, grapheme navigation, selection, undo, search,
stats, and the file codec — moves to a new **`shared/Puffinote.Text`**. Both editors stand on it.

The **editing surface is deliberately not shared.** Typage's surface is block-and-rhythm-shaped
(collapsing margins, concealment maps, rich decorations); Plaintain's is uniform-row-shaped. Forcing a
common base would be a ~5,300-line behaviour-preserving refactor of Typage's hardest code for no
product payoff. See §5.

Six phases, each ending with every area green and something runnable.

---

## 2. Why a separate app, not a mode in Typage

The question "should this be a Typage mode?" has a structural answer: **Puffinote already exists as the
composer.** Opening many file types in one window is its entire stated reason to exist. A Typage that
grows plaintext support builds a second, worse composer *inside* the thing Puffinote composes.

Beyond that:

- **Typage's thesis is live preview.** Plaintext editing pulls toward line numbers, monospace-by-default,
  no-wrap, huge-log handling, and eventually code highlighting. Every one of those is an anti-feature for
  a prose Markdown editor, and each would relitigate Typage PRD §2.2.
- **The repo's shape is already "independent editors, one shell."** A third editor is the native move
  here, not the exception. Removing Puffinote must still leave three working editors.
- **File-type claims want separate bundles.** `.txt` should open Plaintain, `.md` should open Typage.
  One bundle claiming both makes the Finder story worse, not better.

Non-goals inherited from the family: no plugin system, no collaboration, no new NuGet above the trusted
base (Typage's `Directory.Build.targets` allowlist gets a Plaintain twin), no JavaScript.

---

## 3. What Plaintain is

A **swiss-army reader/editor for text of any kind** — the tool you reach for when a file is "just text"
and Typage would only get in the way.

| Trait | Decision |
|---|---|
| Default font | Monospace, uniform row height (no prose rhythm) |
| Word wrap | Off by default, toggleable — the Notepad expectation for logs |
| Line numbers | Yes, in a gutter (Typage has none and shouldn't) |
| Syntax highlighting | **Out of scope.** Revisit only with evidence; it is the one feature that would make Plaintain a genuinely different product. |
| Format-aware rendering | **None, ever** (§9.2). No CSV tables, no JSON folding. A `.csv` is Cellestra's job; a `.json` is a text file. |
| Large files | **50 MB** is the held target (§9.5) — real log territory, and roughly 100 MB of UTF-16 in memory. Viable on the piece table as designed, but the load path is a design constraint from Phase 1, not an afterthought. |
| Encoding | Detect on open, round-trip byte-faithfully, surface it in the status bar; undecodable input opens read-only rather than silently corrupting |
| Line endings | Preserve LF/CRLF/mixed exactly, as Typage does |

Everything in the family's golden rules still applies: the document is the source of truth, saves are
atomic temp-file-then-rename, undo is session-only, immutable snapshots throughout.

---

## 4. The split: what moves to `shared/`

`Typage.Engine` is already two libraries wearing one name. Verified by inspection:

| Current path | Lines | Markdown-coupled? | Destination |
|---|---|---|---|
| `Document/TextDocument.cs`, `Selection.cs`, `Grapheme.cs`, `LinePosition.cs`, `DocumentSearch.cs`, `DocumentStats.cs`, `TextFileCodec.cs` | ~7 files | **No** — zero markdown references; `using`s are BCL-only | → `shared/Puffinote.Text/Document/` |
| `Editing/DocumentBuffer.cs`, `EditHistory.cs`, `TextEdit.cs` | 400 | **No** — the sole hit is one `<see cref="Markdown.MarkdownModel.Update"/>` doc comment | → `shared/Puffinote.Text/Editing/` |
| `Editing/LinePrefix.cs`, `OrderedListRenumber.cs` | 309 | Yes — list/blockquote prefixes | stays in `Typage.Engine` |
| `Markdown/*` (8 files), `Html/HtmlRenderer.cs` | 1,748 | Yes, entirely | stays in `Typage.Engine` |

So roughly **10 of 21 engine files move, and only one doc comment needs editing.** This is as close to a
free extraction as refactors get, which is why it leads.

Naming follows the existing shared pair — `Puffinote.Components` (Avalonia) / `Puffinote.Components.Core`
(non-UI). **`Puffinote.Text` is non-UI**: no Avalonia reference, ever. Namespaces become
`Puffinote.Text.Document` / `Puffinote.Text.Editing`.

One deliberate scope addition while we're here: `TextFileCodec` currently implements BOM sniffing and
leaves the full Typage PRD §7.1 decision tree (validate UTF-8, fall back for legacy encodings, open
read-only on undecodable input) marked "a later phase". **Plaintain is the app that forces it** — a
plaintext swiss-army knife that mangles a Latin-1 file is broken. That work lands in Phase 0, benefiting
Typage for free.

---

## 5. The editing surface: shared or duplicated?

The tempting observation is that **a plaintext editor is Typage with an empty token stream** — no
markers, so nothing conceals, no reveal logic fires, every block is a paragraph. Behaviourally, true.

But it can't be *had* without one of two moves, and both are worse than they look:

**(a) Move the surface to `shared/`.** Violates proposal 0001 §2 — `shared/` may not depend on any app,
and `MarkdownEditor` is 5,300 lines of markdown.

**(b) Split it into a shared base + a Typage subclass.** A behaviour-preserving refactor of the
codebase's hardest, most-tested code (`MarkdownEditor` × 14 partials, `LayoutModel` 686, `BlockLayoutBuilder`
559, `VisualLine` 413), whose payoff is a base class that Plaintain would then fight:

- Typage's geometry is a **pixel prefix sum over variable-height blocks with collapsing margins**
  (AGENTS.md, PRD §8.3.1). Plaintain's is `row × lineHeight` — arithmetic, not a prefix sum.
- Typage carries an **offset↔visual map** for concealed markers. Plaintain conceals nothing; that map is
  pure cost.
- Typage's load path is tuned for prose documents. Plaintain's headline case is a 50MB log.
- It would couple Plaintain's internals to Typage's release cadence permanently.

**Decision: Plaintain writes its own surface** (`PlainTextEditor`) over `shared/Puffinote.Text`. The hard
*semantics* — grapheme boundaries, word boundaries, selection, undo coalescing, search — are all
engine-side and genuinely shared. What gets rewritten is the geometry and paint, which is far smaller
under a uniform-row model. Estimate: ~1,200–1,500 lines against Typage's ~5,300.

**Selective control-level sharing** is still on the table, promoted to `Puffinote.Components` only when
duplication is *observed*, per this repo's own rule-of-three culture (0001 §3). The obvious candidates,
to revisit at the end of Phase 1: `TextMeasurer` (120 lines, already format-blind) and `FindBar` (277).
Neither is a Phase-0 commitment.

---

## 6. Phases

Each phase ends with `just check` green across every area. Each lands as its own commit series on `main`.

**Only Phase 0 is scheduled** (§9.6). Phases 1–5 are committed in direction and sequence, not in date;
they begin after the family's 1.0.

### Phase 0 — Extract `shared/Puffinote.Text`

No new product. Move the 10 format-agnostic engine files out of `Typage.Engine`, renamespace, repoint
`Typage.Engine`/`Typage.Control`/`Typage.Cli` and the shell, and add the project to all three `.slnx`
files. Split `Typage.Engine.Tests` so the moved types' tests travel with them into a new
`shared/Puffinote.Text.Tests`.

Then complete the `TextFileCodec` decision tree (§4): UTF-8 validation, legacy fallback, read-only on
undecodable input.

**Done when:** Typage behaviour is bit-identical, all three areas green, no Avalonia reference in
`Puffinote.Text`.

### Phase 1 — `Plaintain.Control`: the surface

New area `features/EditPlaintext/` with `Plaintain.slnx`, its own `Directory.Build.targets` allowlist (a copy
of Typage's, minus what it doesn't use), `AGENTS.md`, `README.md`, `docs/PRD.md`, `docs/ARCHITECTURE.md`
— the per-area convention Cellestra and Typage both follow.

`Plaintain.Control` ships `PlainTextEditor` (custom-drawn, uniform rows, gutter, wrap toggle) and
`PlainTextEditorView` (the embeddable composite: editor + find bar + its own command catalog
`PlainTextEditingCommands`). Same golden rule as Typage: **every command that acts on the document lives
here, never in a shell** — that is what keeps the standalone app and the Puffinote pane identical.

`Plaintain.UiTests` (headless Avalonia) is the gate; the devcontainer can't launch a GUI.

**Done when:** headless tests drive typing, caret movement, selection, wrap toggle, find, undo/redo.

### Phase 2 — `Plaintain.Desktop` + packaging → **first user-visible milestone**

Thin shell around the control: single window, menu, status bar (encoding, line endings, caret position),
open/save dialogs, recent files, settings. Opens `args[0]` on launch, mirroring `Typage.Desktop/App.cs`.

Packaging lands in the same phase, because a `.app` that Finder respects *is* the milestone:

- `build/package-mac.sh` — one new `case` arm (`plaintain) APP_NAME="Plaintain"; …`). The script already
  generalises; do not fork it.
- `Plaintain.Desktop/Info.plist` — bundle id `com.plaintain.Plaintain`, and the document types of §7.
- `justfile` — the full recipe set for a fourth area, verb-first, with every install's exact inverse
  (`publish`/`unpublish`, `install`/`uninstall`, `install-app`/`uninstall-app` including the `lsregister -u`
  claim withdrawal). Add `plaintain` to all nine aggregate recipes.
- Root `AGENTS.md` — repo map and the area table grow a fourth row.

**Done when:** `just install-plaintain-app`, then double-clicking a `.txt` in Finder opens it.

### Phase 3 — the `pltn` CLI

`pltn <file>` opens the file in Plaintain.Desktop — a launcher, per the stated scope. On macOS that is
`open -a Plaintain.app <path>`; elsewhere, spawn the published desktop binary. With no argument, launch
empty.

Add `install-plaintain` / `uninstall-plaintain` to the justfile (single self-contained file into
`PREFIX/bin`, like the other three).

Because a launcher is nearly untestable in a headless container, the CLI also gets **exactly one**
inspection verb (§9.3) — `pltn info <file>`, printing encoding, line endings, and line/word/byte counts
straight from `Puffinote.Text`. That keeps the devcontainer's "verify through the CLI" discipline
available and gives `Plaintain.Cli.Tests` something to assert. It is a deliberate stopping point: `pltn`
is a launcher plus one read-only probe, and does not grow a verb surface like `typage` has.

### Phase 4 — Puffinote integration

`PlainTextPane : IEditorPane` in `app/Puffinote.Desktop/Panes/`, a thin adapter over
`PlainTextEditorView` — composition, never a fork, exactly as `MarkdownPane` wraps Typage. `GetOutline()`
returns empty, permanently (§9.4): plaintext has no structure, and the sidebar's Outline panel simply
stays blank for these tabs. No indentation or comment-banner heuristics.

`EditorPaneFactory` grows a third branch. Its extension matching becomes a **registry plus a fallback**:
a known-extension list routes fast, and anything else that `TextFileCodec` decodes cleanly *also* opens
in Plaintain. That is what makes Puffinote genuinely "opens any text file" without maintaining an
infinite whitelist. `CanOpen` becomes a decode probe on the first few KB, not a pure string test.

**Done when:** a `.log`, a `.conf`, and an extensionless `LICENSE` all open in a Puffinote tab.

### Phase 5 — Notepad-grade hardening

The features that make it a real utility rather than a demo: large-file load path held to the **50 MB**
target of §9.5 (measure before optimising), go-to-line, wrap toggle persisted in settings, line-number gutter polish,
encoding conversion on save (explicit user action, never implicit), mixed-line-ending detection and
normalisation offered but not forced.

Also the §5 re-evaluation: with both surfaces built, look at actual duplication and promote
`TextMeasurer` / `FindBar` to `Puffinote.Components` **only if** the duplication is real.

---

## 7. File-type claims

Two rules keep this from becoming a Finder brawl:

**Owner, narrowly.** Plaintain claims `LSHandlerRank: Owner` for only the extensions no one else wants:
`txt`, `text`, `log`, plus the `public.plain-text` UTI. Typage keeps `md`/`markdown`/`mdown`/`mkd` as
Owner; Puffinote continues to claim nothing, per the comment already in its `Info.plist`.

**Alternate, broadly.** A second `CFBundleDocumentTypes` entry with `LSHandlerRank: Alternate` covers the
long tail — `conf`, `cfg`, `ini`, `env`, `csv`, `tsv`, `yaml`, `yml`, `toml`, `xml`, `json`, `sh`, `md5`,
and so on. Alternate means Plaintain shows up under "Open With" without stealing defaults from anyone's
editor or IDE. **Do not claim `json`/`yaml`/`sh`/`xml` as Owner** — that fight is unwinnable and users
would hate it.

Inside Puffinote, extensions matter less: the decode probe of Phase 4 is the real gate.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| Phase 0 touches every area at once (`shared/` fans out to all three solutions) | It is a pure move — one doc comment is the only content edit. `just check` all areas; land as its own commit series before any Plaintain code exists. |
| Surface duplication drifts (Plaintain's caret behaves subtly unlike Typage's) | The *semantics* live in `Puffinote.Text` and are shared. Add a cross-area conformance test suite over the shared primitives. |
| Fourth area multiplies ceremony — ~15 justfile recipes, plists, docs, CI | Real and accepted; it is the price of the repo's chosen architecture. The aggregate recipes absorb most of it. |
| Scope creep into a code editor | Syntax highlighting is an explicit 1.0 non-goal (§3). Record it in Plaintain's PRD non-goals so it needs a decision, not a drift. |
| Large-file performance discovered late | Phase 5 measures it, but write the load path in Phase 1 with a known target rather than retrofitting. |

---

## 9. Resolved decisions

Settled at drafting time. Treat these as firm — reopening one needs a reason recorded here, not a
drive-by change. (Same convention as Typage PRD §12.)

**9.1 — Feature folder is `features/EditPlaintext/`.** Names the format rather than the medium, so it
can't be read as overlapping Typage's domain. Slightly longer than `EditText/`; worth it for the clarity.

**9.2 — No format-aware rendering, ever.** No CSV-as-table, no JSON/YAML folding, no syntax highlighting.
Plaintext stays plaintext. A delimited file is Cellestra's question; structured config is still text.
This is the line that keeps Plaintain from drifting into a code editor or a viewer suite, and it is the
single most load-bearing decision in this proposal — every "small" exception to it reopens §3.

**9.3 — `pltn` is a launcher plus exactly one read-only verb.** `pltn <file>` opens the app;
`pltn info <file>` prints encoding, line endings, and counts. The verb exists so headless CI and the
Linux devcontainer have something to assert against — not as the start of a CLI surface.

**9.4 — Plaintain contributes nothing to the Outline panel.** `GetOutline()` returns empty. Heuristics
(indentation trees, comment banners) were considered and rejected: they produce confident-looking noise
on prose and logs, and a blank panel is the honest answer for a file with no structure.

**9.5 — The large-file target is 50 MB.** Real log territory, ~100 MB of UTF-16 resident. Viable on the
piece table as designed, so no streaming or memory-mapped architecture — but the load path is written
against this number from Phase 1, and Phase 5 measures it rather than assuming.

**9.6 — Phase 0 lands now; Phases 1–5 are post-1.0.** The engine extraction only gets more expensive as
Typage accretes code against the un-split engine, and it pays for itself immediately (the completed
`TextFileCodec` decision tree is a Typage improvement on its own). Plaintain itself does not gate the
family's 1.0 — so `shared/Puffinote.Text` and its tests are the near-term work, and
`features/EditPlaintext/` doesn't exist until Typage and Cellestra have shipped.

*Consequence for reviewers:* only Phase 0 is being proposed for immediate execution. Phases 1–5 are
committed in direction, not in schedule, and should be re-read before starting — Typage's pre-1.0
changes may move details in §5 and §6.
