# AGENTS.md

Guidance for an AI coding agent (and any human) working in the **Puffinote** app (`app/`). Read the
repo-wide [`AGENTS.md`](../AGENTS.md) first (build/test/format gates, git workflow), then this, then
the docs. This mirrors the discipline of the sibling features
([`features/EditMarkdown/AGENTS.md`](../features/EditMarkdown/AGENTS.md),
[`features/EditSpreadsheets/AGENTS.md`](../features/EditSpreadsheets/AGENTS.md)) — same repo, same tooling.

## What this is

**Puffinote** is the composite shell that embeds Typage (`.md`) and Cellestra (`.ods`) into one
notebook-based workspace. Start with [`docs/puffinote/PRD.md`](../docs/puffinote/PRD.md) (what/why) and
[`docs/puffinote/ARCHITECTURE.md`](../docs/puffinote/ARCHITECTURE.md) (how). When this file and a doc
disagree, the doc wins for design detail; this file wins for process.

## Golden rules (non-negotiable)

1. **Compose, don't fork.** Embed the *existing* `Typage.Control` and `Cellestra.Editor` surfaces
   unchanged. If an editor needs a hook to be embeddable, add it to the **editor** or
   `Puffinote.Components` — never a reach-in from Puffinote.
2. **Puffinote owns the shell, never the domain.** No Markdown or spreadsheet logic here; all document
   ops (save, undo, editing) delegate to the embedded editor. No new document engine.
3. **One-way layering.** Puffinote references down into the editors + shared library; **nothing
   references Puffinote**. Removing Puffinote must leave both sub-apps fully working (PRD §7.8).
4. **Guard the supply chain.** Trusted base = .NET BCL + Avalonia (+ native deps) + OS services. No new
   NuGet package without a stated reason; **never** npm. First-party Avalonia controls are fine.
5. **macOS-first for 1.0** (PRD §5) — Cellestra is macOS-only today.

## Tech stack (do not change without being asked)

.NET 10 · Avalonia 12.0.4 (compiled bindings) · xUnit v3 (+ `Avalonia.Headless.XUnit`) · MIT. Versions
are pinned in the repo-root `Directory.Packages.props`; `Directory.Build.props` gives `net10.0`, nullable,
and **`TreatWarningsAsErrors=true`** — *every* warning fails the build, analyzer and style ones included
(the lone exception is `IDE0060`, unused parameter). **Clean build means zero warnings.**

## Everyday commands (from the repo root)

```bash
export PATH="$PATH:/root/.dotnet"      # devcontainer: dotnet isn't on PATH; libicu-dev must be installed
just puffinote-build                   # = dotnet build app/Puffinote.slnx
just puffinote-test
just check                             # format-verify + build + test ALL three areas — the gate below
```

**Definition of done:** `just check` — Puffinote formatted, clean (0 warnings), green, **and both sub-app
solutions still build & test green independently**. That last clause is the point of `just check` over
`just puffinote-*`: this shell compiles the editors, so it's the place a break in them surfaces.
Never run two builds/tests concurrently (MSBuild deadlock — see the root guide).

Put shell logic in pure/testable helpers (`Puffinote.Tests`) and keep the UI a thin call: `Puffinote.UiTests`
runs on Avalonia's null drawing (no GPU here), and launching the GUI fails environmentally in the
container — that's not a regression, so verify with tests, not by running the app.

## Where things go

`Puffinote.Desktop/` — `Shell/` (notebook/config/shortcut services), `Panes/` (the `IEditorPane` embedding
seam — Phase 1+), `Controls/`, `Panels/`, `Views/`. `Puffinote.Cli/` — the router (Phase 6).
`Puffinote.Tests` / `Puffinote.UiTests` / `Puffinote.Cli.Tests`. Don't invent new top-level folders;
extend the tree in ARCHITECTURE §13. When in doubt, **stop and ask**, then record the resolution in the doc.
