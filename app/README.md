# Puffinote

The composite second-brain notes app of the family — an Obsidian-like **sidebar-and-editor** shell that
**composes** the two editors this repo builds:

- **Markdown (`.md`)** via **Typage** (`features/EditMarkdown`)
- **Spreadsheets (`.ods`)** via **Cellestra** (`features/EditSpreadsheets`)

on top of the shared **`Puffinote.Components`** design system. Puffinote owns the *shell* (notebook, file
tree, tabs, merged palette, shortcuts) and **no document engine** — every document operation is delegated
to an embedded editor. Both sub-apps stay independently compilable and runnable (PRD §7.8).

**Docs:** [`docs/puffinote/PRD.md`](../docs/puffinote/PRD.md) (what/why) ·
[`docs/puffinote/ARCHITECTURE.md`](../docs/puffinote/ARCHITECTURE.md) (how) · `AGENTS.md` (guardrails).

## Status

**Phase 0 (shell scaffold) — in progress.** Window + menu + `Puffinote.Components` theming; open a notebook
and render a **read-only file tree**. No editors embedded yet (Phase 1).

## Build & run

```bash
dotnet restore Puffinote.slnx
dotnet build   Puffinote.slnx
dotnet run --project Puffinote.Desktop -- <notebook-folder>   # open a notebook (macOS-first; see AGENTS.md)
dotnet test    Puffinote.slnx                              # Puffinote.Tests (pure) + Puffinote.UiTests (headless)
```

## Shipping

From the repo root:

```bash
just puffinote-publish          # CLI + shell, self-contained → artifacts/puffinote/<rid>/
just puffinote-install          # the `puffinote` CLI onto PATH
just puffinote-install-app      # Puffinote.app → /Applications (macOS only)
```

The bundle declares itself a handler for *folders*, not for `.md`/`.ods` — Puffinote opens a notebook
(see `Puffinote.Desktop/Info.plist`), and the file associations stay with the standalone editors.
