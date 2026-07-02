# Proposal 0001 — `Puffinote.Components`, a shared app-shell / design-system library

- **Status:** Phases 0–1 **landed** (`FuzzyMatcher`, `RelayCommand` shared); Phase 2 **foundation landed** (token contract + first chrome migrated); Phase 3 **largely landed** (palette, icon system, tooltip card, overlay/modal host + shared `ConfirmDialog`; both apps converged); Phases 4–5 proposed (see §7)
- **Author:** (you) · drafted with Claude
- **Affects:** `features/EditSpreadsheets` (Cellestra), `features/EditMarkdown` (Typage), and the
  planned **Puffinote** composite app

---

## 1. Summary

Cellestra and Typage already share a substantial amount of **app-shell and design-system** code — some
of it copied verbatim. We propose extracting that layer into a single shared library,
**`Puffinote.Components`**, living at `shared/Puffinote.Components/`. The two existing apps and the
future Puffinote app will share one command system, one set of UI primitives, and one design language.
The **domain engines and editing surfaces stay separate**; only the shell and design system are shared.

Migration is **phased and leaf-first**, keeping every app green at each step.

---

## 2. Context: the Puffinote family

**Puffinote** is a planned "second brain" notes app that **composes both Typage and Cellestra** —
embedding the live-preview Markdown editor and the spreadsheet into one workspace. The shared library
is named `Puffinote.Components` because it is the **foundation of that family**, consumed by three
sibling apps:

```
                 ┌──────────────────────────────┐
                 │      Puffinote.Components      │   ← this proposal
                 │  (design system + app shell)   │
                 └──────────────────────────────┘
                    ▲           ▲            ▲
        ┌───────────┘           │            └────────────┐
   Cellestra (standalone)   Typage (standalone)      Puffinote (composite)
   sheets                   notes                    notes + sheets, embeds both
```

Two consequences for this proposal:

- **Namespace root** is `Puffinote.Components` (e.g. `Puffinote.Components.Commands`,
  `Puffinote.Components.Theming`, `Puffinote.Components.Controls`).
- The library must stay **app-agnostic**: it may not depend on Cellestra, Typage, *or* Puffinote. It is
  the shared floor all three stand on. The act of *composing* the two editors into Puffinote is the
  Puffinote app's job, not this library's (see §4.3, Deferred).

---

## 3. Problem: the duplication is real and growing

Not speculative — a survey found these same-concept files in **both** apps today:

| Concept | Cellestra | Typage | Similarity | Tier |
|---|---|---|---|---|
| `FuzzyMatcher` | `Cellestra.Editor/Views/Overlays` | `Typage.Desktop/Commands` | **Identical** (Typage's comment says *"ported from Cellestra"*) | A → ✅ **moved (Phase 1)** |
| `RelayCommand` | `Cellestra.Editor/Commands` | `Typage.Desktop` | **Identical** | A → ✅ **moved (Phase 1)** |
| `ShortcutFormatter` | `Cellestra.Editor/Commands` | `Typage.Desktop/Commands` | **Different input** — Cellestra formats a `KeyGesture`, Typage formats its own `Shortcut` | C (with `Shortcut`) |
| `Shortcut` | ✗ (Cellestra uses `KeyGesture` directly) | `Typage.Desktop/Commands` | **Typage-only — not a duplicate**; tied to the `AppCommand` unification | C |
| Command Palette | `ShowPalette` / overlay | `CommandPalette` panel | Same pattern, parallel code | B |
| Modal / overlay primitives + Tab focus-trap | `ModalDialog`, `OverlayHost` | palette overlay | Same pattern | B |
| Tooltip cards (`BuildTip`) | ✓ | ✓ *(verify)* | Same pattern | B |
| Theme tokens (colors) | hardcoded literals | hardcoded literals | Copy-pasted values | B |
| `SettingsService` / `AppSettings` | ✓ | ✓ | Same concept | B |
| `Workspace` (recent files / file state) | ✓ | ✓ | Same concept | B |
| CLI scaffolding (`CliApplication`, `CliException`, `Program`) | ✓ | ✓ | Same concept, **non-UI** | B |
| `EditHistory` (undo/redo) | `Cellestra.Core` (Workbook + `IEditCommand`) | `Typage.Engine` (`TextEdit`+`Selection`, coalescing) | **Engine-layer, domain-coupled — same name, different design** | ✗ Do not share |
| Icon system (`LucideIcons`) | ✓ | ✓ *(verify)* | Same concept | B |
| `AppCommand` | `class`, threads `IAppEnvironment`, has `Icon`/`IsActive` | `record`, env-free `Execute()` | **Diverged in shape** | C |
| `Token` / `TokenType` | formula tokens | markdown tokens | **Coincidental name collision** | ✗ Do not share |

Token copy-paste is visible: the muted-gray `Color.FromRgb(0x88,0x88,0x88)` literal appears 5× across
the repo, accent blue `0x2A,0x7D,0xE1` 4×, etc. With Puffinote on the horizon, we're past the
rule-of-three trigger.

---

## 4. Scope

The guiding principle: **`Puffinote.Components` owns *how an app in this family looks and behaves as a
shell* — not *what any app does*.** Patterns and chrome are shared; domain logic and content are not.

### 4.1 In scope — what `Puffinote.Components` will own

Organized by capability area. Each is something all three apps need and currently duplicate or would
duplicate.

**A. Command system**
- The `AppCommand` *contract* (unified, env-agnostic — see §5), `Shortcut`, `ShortcutFormatter`,
  `RelayCommand`, `FuzzyMatcher`.
- The **Command Palette** control (search + fuzzy-ranked list + keyboard model).
- The catalog *machinery* (how a list of commands is filtered, displayed, and run).
- **Not** the catalog *contents* — each app supplies its own commands (preserves golden rule #4).

**B. Design tokens & theming**
- A theme contract (`IAppTheme` and/or named Avalonia resources): accent, surface, muted text,
  borders, spacing scale, corner radii, type ramp, elevation/shadow.
- Light/dark handling and the plumbing for an app to supply its own token values at startup.
- **No hardcoded `Color.FromRgb(...)` in any primitive** — everything reads tokens.

**C. Overlay & dialog primitives**
- The internal-modal base (`ModalDialog`-style), the overlay host, the **Tab focus-trap** (the work we
  just did in both apps), backdrop-dismiss, and Esc handling.
- Reusable confirm/prompt dialogs.

**D. Shared micro-controls**
- Tooltip card (the `BuildTip` name + shortcut + description layout), the icon system (`LucideIcons`),
  palette/list row templates, section headers, and other small chrome used across surfaces.

**E. Shell service contracts (and default implementations)**
- Settings persistence (`SettingsService`/`AppSettings` base — a contract plus a JSON default; each app
  extends with its own keys).
- Workspace / recent-files / file-state helpers.
- CLI scaffolding (`CliApplication`, `CliException`, the `Program` harness) — see boundary note in §4.4.

### 4.2 Out of scope — explicitly excluded (and why)

- **Domain engines** — `Cellestra.Core`, `Typage.Engine`. Different domains; the whole point of the
  layering is to keep these independent.
- **Editing surfaces** — the spreadsheet grid, the live-preview Markdown surface. App-specific,
  performance-critical, and tightly bound to their engines.
- **Domain models** — cells, formulas, ranges, Markdown AST, `Token`/`TokenType`. (The `Token` name
  collision is coincidental, not a shared concept.)
- **`EditHistory` (undo/redo)** — lives in the *engine* layer in both apps and is domain-coupled:
  Cellestra's is a command-pattern stack over a `Workbook` (`IEditCommand.Undo(workbook)`), Typage's is
  `(TextEdit, Selection)` records with edit-coalescing. Same name, fundamentally different design — a
  `Token`-style collision, not shared behavior.
- **App-specific command *contents*** and menus — the catalog *type* is shared; the entries are not.
- **Cross-editor composition / embedding** — wiring a sheet into a note, the Puffinote multi-pane
  workspace. That's the Puffinote app's responsibility (and partly Deferred, §4.3).
- **External distribution** — no public NuGet packaging, no semver contract. This is an *internal*
  in-repo library for now.

### 4.3 Deferred — likely later, deliberately not now

These become relevant once Puffinote exists. Building them speculatively would over-commit the design,
so they are explicitly **not** part of this work:

- **Embedding/host protocol** — a seam (`IEmbedHost`/`IEmbedResolver`-style) letting one editor render
  inside another's document. Typage already has an `IEmbedResolver` seam; a cross-app version would be
  designed against Puffinote's real needs, not guessed at now.
- **Shared cross-app document / workspace model** — a unified notion of a "document" spanning notes and
  sheets.
- **Composite app shell** — tabs/panes/command routing for hosting multiple editors at once.
- **Plugin/extension surface.**

### 4.4 Boundary rules (layering)

These keep the library a *floor*, not a tangle:

1. **No upward references.** `Puffinote.Components` references Avalonia and its own code only — never an
   app, engine, or surface. Apps reference it.
2. **The Avalonia / CLI boundary.** CLIs in this repo are "no Avalonia, no network." Anything a CLI
   needs to share (e.g. the `AppCommand` contract, `FuzzyMatcher`, `Shortcut`) must not force an
   Avalonia-UI dependency. ⇒ **Likely split point:** a non-UI `Puffinote.Components.Core` holding the
   command contract + matcher + settings model, with the Avalonia controls in `Puffinote.Components`.
   We *start as one UI assembly* (Phase-1 contents are all UI-adjacent) and split at this seam **only
   when** the CLI actually needs to share a primitive — documented now so we don't paint ourselves in.
3. **Tokens over literals.** No primitive hardcodes a color/size; it reads the theme contract.
4. **Contracts over concretes for app-extensible services.** Settings/workspace expose a base/interface
   apps extend, rather than a sealed implementation.

### 4.5 Definition of done (for the overall effort, across phases)

- All three Tier-A duplicates exist once, in `Puffinote.Components`, referenced by both apps; the old
  copies are deleted.
- No primitive contains a hardcoded theme literal; Cellestra and Typage each render with their own
  tokens via the theme contract.
- The command palette, modal/overlay primitives, and Tab focus-trap have a single implementation.
- A unified `AppCommand`; Cellestra and Typage both build their catalogs against it.
- Both apps build clean (0 warnings) with green tests at the end of **every** phase.
- A new app could stand up a recognizable shell (palette + themed modals) by referencing
  `Puffinote.Components` and supplying tokens + a command catalog — nothing else.

---

## 5. `AppCommand` unification (the one real design task)

The two `AppCommand`s diverged:
- **Cellestra:** `class`, `Execute(IAppEnvironment)`, `CanExecute(IAppEnvironment)`, `IsActive` (toggle
  state), `Label`, `Icon`.
- **Typage:** `record`, env-free `Execute()`, `CanExecute()`, `Title`, no icon.

**Recommendation:** the shared `AppCommand` is **environment-agnostic** — `Action Execute`,
`Func<bool>? CanExecute`, optional `Func<bool>? IsActive` (toggles), optional `Icon`, one name field
(`Title`). Cellestra adapts by **capturing its `IAppEnvironment` at command-construction** (closing
over it) — which is already how Typage builds commands. This avoids a generic `AppCommand<TEnv>`, keeps
the palette/menu env-free, and lets the same type live in a non-UI assembly later. This is Tier C and
lands **after** the easy wins prove the workflow.

---

## 6. Repository layout

Canonical code lives under `features/` (the top-level `src/` directory is stale — 0 projects). The repo
already provides central package management (`Directory.Packages.props`), shared `Directory.Build.props`
(`net10.0`, nullable, `WarningsAsErrors=nullable`), and `global.json`, so the new project inherits all
conventions for free.

```
shared/
  Puffinote.Components/            ← new (Avalonia UI: controls, theming, palette, modals, shell)
  Puffinote.Components.Core/       ← later, only if the Avalonia/CLI seam (§4.4) forces it
features/
  EditSpreadsheets/ …             ← references Puffinote.Components
  EditMarkdown/ …                 ← references Puffinote.Components
```

Each app solution adds a `ProjectReference` to `shared/Puffinote.Components`. Internal folders mirror
the capability areas: `Commands/`, `Theming/`, `Overlays/`, `Controls/`, `Shell/`.

---

## 7. Migration plan (leaf-first, green-to-green)

Each phase ends with **all** apps building clean and tests green; land a phase only when it does.

- **Phase 0 — Scaffold. ✅ Done.** Created `shared/Puffinote.Components`, wired into both solutions,
  referenced (empty) from both Desktop projects. Build plumbing proven.
- **Phase 1 — Tier A leaves. ✅ Done.** Moved `FuzzyMatcher` and `RelayCommand` (both byte-for-byte
  identical) into `Puffinote.Components.Commands`; deleted both copies of each; repointed all consumers;
  both solutions build clean with green tests. **As-built note:** `ShortcutFormatter` and `Shortcut`
  were *not* moved — they are not actually trivial duplicates (Cellestra has no `Shortcut` type and its
  `ShortcutFormatter` formats a `KeyGesture`, while Typage's formats its own `Shortcut`). They are
  reclassified Tier C and folded into **Phase 4**.
- **Phase 2 — Design tokens + theme contract. ✅ Foundation done.** Added a variant-aware token
  contract in `Puffinote.Components.Theming` — `ThemePalette` (Accent / AccentSubtle / Surface /
  SurfaceBorder / MutedText / Scrim), `IAppTheme`, a default `PuffinoteTheme` seeded by accent, and the
  `Tokens` accessor (named `Tokens`, not `AppTheme`, to avoid colliding with apps' own theme-preference
  enums) — mirroring the editor's `GridPalette.For(ThemeVariant)`. Each app registers its theme at
  startup (Cellestra `0x2A7DE1`, Typage `0x579DF6`). First chrome migrated onto tokens: Cellestra's
  `RadialSettingsDialog` and Typage's `CommandPalette`. **Remaining:** the rest of the shell literals
  migrate incrementally — most land in Phase 3 as the chrome itself moves to shared. Domain-surface
  colors (grid cells, Markdown syntax via `MarkdownTheme`) stay app-specific (§4.2), not tokenized.
- **Phase 3 — UI primitives. ✅ Largely landed.** Shared, token-skinned: the **command palette**
  (`PaletteListView`, adopted by both apps); the **icon system** (`LucideIcon`/`LucideIcons` + the 76
  vendored SVGs, now `avares://Puffinote.Components/...`); the **tooltip card** (`TooltipCard`, reading
  `MutedText` instead of the copy-pasted `0x888888`); and the **overlay/modal stack** — `IModalDialog`
  + `ModalDialog<TResult>` base, a shared **`OverlayHost`** (themed card chrome + Tab focus-trap +
  backdrop/Esc dismissal, with Opened/Closed hooks for app-specific blur/shortcut-suspend/focus), and the
  shared dialog set **`ConfirmDialog`** / **`ChoiceDialog<T>`** (multi-way) / **`MessageDialog`** /
  **`FormDialog`** (body + OK/Cancel). Cellestra runs its palette **and** every internal modal through the
  shared host. Typage was fully migrated off native `Window.ShowDialog`: confirm prompts, the tri-state
  unsaved/conflict choices, the error box, the link prompt (a Typage-local `LinkDialog`), and the
  Preferences form are all internal modals now (the Markdown-reference window stays a *modeless* secondary
  window — not a modal dialog). Typage's command bindings (keyboard / in-window menu / macOS native menu)
  are gated on "an overlay is open" so a shortcut can't fire under a palette or modal. **As-built note:** Phase 3 turned out *not* to be a pile of
  duplicates — a survey found icon/tooltip were **Cellestra-only** and the modal host **structurally
  diverged** (Cellestra internal overlay vs Typage native windows). The Cellestra-only primitives were
  lifted anyway, anticipating the Puffinote app.
- **Phase 4 — `AppCommand` + shortcut unification (Tier C).** Env-agnostic command (Cellestra captures
  its env at construction); a shared `Shortcut` representation; migrate `ShortcutFormatter` and the
  catalog wiring onto it.
- **Phase 5 — Shell services.** Settings/workspace base, CLI scaffolding (resolve the §4.4 split here if
  the CLI needs the primitives). (`EditHistory` is **not** in this list — it's engine-layer and
  domain-coupled; see §4.2.)

Stop after any phase that delivers value; later phases are independent.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| **Premature / over-abstraction** | Extract only *proven* duplication (the Tier A/B inventory). Bar to add: "duplicated in ≥2 apps and stable." |
| **Library becomes a dumping ground** | Small, reviewed API surface; engine/surface/domain code explicitly out (§4.2). |
| **Composite app leaks into the floor** | Embedding/composition is Deferred (§4.3); the library may not reference any app. |
| **Coupling release cadence** | Same monorepo, same-commit changes; no external versioning yet. Version only if apps later ship independently. |
| **CLI accidentally pulls Avalonia** | The §4.4 Avalonia/CLI seam + the optional `.Core` split. |
| **Over-genericizing `AppCommand`** | Env-capture, not generics; do it last (Phase 4). |
| **Churn / regressions** | Leaf-first, green-to-green; each phase independently revertible. |

---

## 9. Decisions

1. ~~**Name**~~ — **Decided: `Puffinote.Components`.**
2. ~~**Location**~~ — **Decided: `shared/Puffinote.Components/` at repo root.**
3. ~~**One project vs. split**~~ — **Decided: one UI project.** A non-UI `Puffinote.Components.Core` is
   added *only if* the CLI/no-Avalonia seam (§4.4) later forces it — not now.
4. ~~**Scope of the first landing**~~ — **Decided: Phase 0 + Phase 1** as a reversible proof; re-evaluate
   Phases 2+ once the workflow is proven.
5. ~~**`EditHistory`**~~ — **Decided: out of scope** (engine-layer, domain-coupled; see §4.2).

---

## 10. First step — landed

**Phase 0 + Phase 1 are done:** `shared/Puffinote.Components` (one UI project) is stood up and wired
into both solutions, and the two genuinely-identical leaves — `FuzzyMatcher` and `RelayCommand` — now
live there once, consumed by both apps, green-to-green. `ShortcutFormatter`/`Shortcut` turned out not to
be trivial duplicates and were deferred to Phase 4 (see §7). Phases 2–5 remain proposed.

---

## Appendix A — Tier legend

- **Tier A** — byte-for-byte (or trivially) identical; safe mechanical move.
- **Tier B** — same concept, light divergence; unify during the move.
- **Tier C** — diverged in shape and/or coupled to app internals; needs a small unifying design.
- **✗** — looks shared by name but isn't (e.g. `Token`/`TokenType`); keep separate.
