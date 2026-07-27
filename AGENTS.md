# AGENTS.md

Orientation for an AI coding agent (and any human) working anywhere in the **Puffinote** repository. Read this first. It covers what's true across the whole repo; each area then has its own `AGENTS.md` with the detail that matters once you're working inside it — **read that area's guide before you touch its code.**

## What Puffinote is

A monorepo for **Puffinote**, a notebook-style workspace that composes two independent, from-scratch native editors into one shell:

- **Typage** — a Markdown (`.md`) live-preview editor.
- **Cellestra** — an OpenDocument Spreadsheet (`.ods`) editor (macOS-first for 1.0).
- **Puffinote** — the composite shell/app that embeds both, plus a unifying CLI.

Each editor builds, tests, and ships on its own; the shell embeds them without reaching into their internals. Removing Puffinote must leave both editors fully working.

## Repository map

```
app/                          Puffinote — the composite shell (Desktop + CLI) that embeds both editors
features/EditMarkdown/        Typage — the Markdown editor (engine, control, desktop, CLI, tests)
features/EditSpreadsheets/    Cellestra — the spreadsheet editor (core, editor, charts, desktop, CLI, tests)
shared/                       Cross-cutting libraries used by the shell and both editors:
                                Puffinote.Components (Avalonia: theming, icons, overlays, shell chrome)
                                Puffinote.Components.Core (non-UI: CLI helpers — safe for CLI projects)
docs/                         PRD / ARCHITECTURE (per-area) + proposals/ (repo-wide)
build/                        package-mac.sh — the shared macOS .app bundler for all three apps
justfile                      Build/test/format/ship entry points for every area — prefer these
Directory.Build.props         Shared MSBuild settings (framework, nullable, analyzers, OS-specific bin/obj)
Directory.Packages.props      Central NuGet version pins — the single source of truth for versions
```

There is no single root solution. Each area has its own:

| Working on… | Read its guide | Build/test solution |
|---|---|---|
| The shell / CLI (`app/`) | [`app/AGENTS.md`](app/AGENTS.md) | `app/Puffinote.slnx` |
| Markdown editor (`features/EditMarkdown/`) | [`features/EditMarkdown/AGENTS.md`](features/EditMarkdown/AGENTS.md) | `features/EditMarkdown/Typage.slnx` |
| Spreadsheet editor (`features/EditSpreadsheets/`) | [`features/EditSpreadsheets/AGENTS.md`](features/EditSpreadsheets/AGENTS.md) | `features/EditSpreadsheets/Cellestra.slnx` |

`shared/` has no guide of its own because it isn't an area you work in — it's a dependency of all three. Both `Puffinote.Components*` projects are compiled into **all three solutions**, so a change there is a change to every area: build and test all three (`just check`) before you call it done.

When this file and an area guide or a linked doc disagree, the deeper doc wins for design detail; this file wins for repo-wide process.

## Tech stack (do not change without being asked)

- **C# on .NET 10** (`net10.0`); SDK pinned in `global.json`.
- **Avalonia 12.0.4** for UI (compiled bindings on); SkiaSharp 3.
- **xUnit v3** for tests; headless UI via `Avalonia.Headless.XUnit`.
- **MIT** licensed.

Versions are pinned centrally in `Directory.Packages.props` (central package management is on) — never hardcode a version elsewhere. `Directory.Build.props` makes the SDK analyzers an enforced gate: `nullable` enable, `EnableNETAnalyzers`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, and **`TreatWarningsAsErrors=true`** — so any analyzer or style warning fails the build (see [Build & test](#build--test)).

## Build & test

The `justfile` is the front door — it knows every area's solution path, so you don't have to. Bare `just` lists every recipe, grouped by area, and `just` searches upward for it, so recipes work from any directory in the repo (no `-C` dance).

```bash
just build                                # every area   (also: just test, just format)
just typage-build                         # one area     (prefix: puffinote- | typage- | cellestra-)
just test-filter SomeTest typage          # one test / a subset — prefer this while iterating
just check                                # format-verify + build + test, all areas
```

Every recipe is a thin wrapper over `dotnet build|test|format <area>.slnx` (e.g. `features/EditMarkdown/Typage.slnx`) — **if `just` isn't on `PATH`, run those directly**; they're equivalent, and some agent sandboxes aren't the devcontainer. (`just --dry-run <recipe>` prints the exact commands a recipe would run.) Likewise, if `dotnet` isn't on `PATH`, `export PATH="$PATH:/root/.dotnet"` (and `libicu` must be installed).

**Shipping.** The same front door publishes and installs each app; all of it is macOS-first, and only the
`.app` targets require a Mac (the container can still publish):

```bash
just publish                              # CLI + desktop, self-contained Release → artifacts/<app>/<rid>/
just install                              # the three CLIs onto PATH (override: just PREFIX=/usr/local install)
just typage-publish-app                   # artifacts/typage/<rid>/Typage.app  (also: just install-app)
```

Bundle metadata is per app (`<App>.Desktop/Info.plist`); the bundling, signing, and notarization logic is
shared in `build/package-mac.sh` — extend that script rather than adding a per-area copy.

Everything here has an exact inverse — `just unpublish` for what publish wrote under `artifacts/`,
`just uninstall` for the CLIs, `just uninstall-app` for the bundles (which also withdraws their Launch
Services file-type claims). `unpublish` is RID-precise, so a cross-published `osx-x64` tree survives it;
`just clean` is the blunt one and takes `artifacts/` entirely. No inverse removes `PREFIX/bin` itself or
an app's user data under `~/Library/Application Support`: uninstalling a tool is not consent to delete
someone's documents, settings, or unsaved-work snapshots.

**Definition of done:** `just check` passes for every area you touched — i.e. formatting verified, solution builds clean, tests green. Warnings **are** errors here (`TreatWarningsAsErrors=true`), so a warning fails the build — "builds" already means "no warnings," this is not advisory. Formatting is a gate too: `just format-check` runs `dotnet format --verify-no-changes`, and a non-zero exit is a failure to fix, not a suggestion.

**Never run two `dotnet build`/`dotnet test` invocations at once** — concurrent runs deadlock on the MSBuild-node and build-output locks and hang indefinitely. (This is why `just check` is serial — recipe dependencies run one at a time, in the order written.) If it happens: kill the `dotnet`/`testhost`/`MSBuild` processes, `dotnet build-server shutdown`, retry.

**CI:** only `.github/workflows/typage-ci.yml` exists, and its push/PR triggers are commented out (`workflow_dispatch` only) — so nothing is gated for you on push. `just check` locally *is* the gate.

**Working with the analyzer/style rules.** The `.editorconfig` at the repo root is the single source of truth for style and diagnostic severities; `dotnet format` auto-fixes the large majority of violations, so run it first before fixing anything by hand. When something still fails the build:

- **Fix the code, don't silence the rule.** Don't add blanket `#pragma`/`NoWarn` or downgrade a severity just to go green. Some rules have no code-fixer and must be fixed by hand — e.g. `CA2000` (dispose), `CA1063` (IDisposable pattern). Only edit `.editorconfig` to change a rule when that's genuinely the right call, and say why in the commit.
- **`IDE0060` (unused parameter) is a non-blocking *warning*, not an error** (`WarningsNotAsErrors`), because the param is often required by a delegate/interface/override signature or is a placeholder for unwired behavior. Surface it — but **don't blind-delete a parameter to clear it**; a `sheet`/`hasHeader`-style unused param may be an incomplete feature, not cruft. Remove only when it's genuinely dead.
- **Don't re-enable XML-doc validation.** `GenerateDocumentationFile=true` exists only so `IDE0005` (unused usings) reports at build; the doc-prose warnings (`CS1591`, `CS15xx`) are `NoWarn`ed on purpose. Don't remove those from `NoWarn` or start writing `<param>` docs to satisfy them — this isn't a published-docs library.
- Test projects (`**/*Tests/**`) relax `CA1063`/`CA1816` — xUnit fixtures don't need the full `IDisposable` ceremony.

Build artifacts live under OS-specific `bin/<os>/` and `obj/<os>/` paths (so Linux devcontainer builds don't collide with native host builds) and are git-ignored — never commit them.

**Running.** The devcontainer is Linux and headless (no GPU), so launching a GUI (`just *-run` / `dotnet run --project *.Desktop`) fails *environmentally* — that's the container, not a regression in your change, and it is not something to debug. **Verify GUI behavior with headless UI tests, or through the CLI.** The CLI does run here:

```bash
dotnet run --project app/Puffinote.Cli -- info            # summarize the notebook in the current directory
dotnet run --project app/Puffinote.Cli -- search "todo"   # full-text search the notebook (notes + sheet cells)
dotnet run --project app/Puffinote.Cli -- md --help       # route into Typage's commands; `sheet …` routes to Cellestra
```

## Repo-wide golden rules

1. **One-way layering.** Puffinote → editors + `shared/`; **nothing references Puffinote.** Editors don't depend on each other. If the shell needs a hook to embed an editor, add it to the *editor* or to `shared/`, never as a reach-in from Puffinote.
2. **Compose, don't fork.** Embed the existing editor surfaces unchanged rather than copying them.
3. **Guard the supply chain.** Trusted base is the .NET BCL + Avalonia (+ its native deps). No new NuGet package without a stated reason in the PR/commit; **never** add JavaScript/npm. First-party Avalonia-team controls (MIT) are pre-approved.
4. **macOS-first for 1.0.** Ship quality on macOS first; other platforms must still build. (For why you can't launch a GUI to check your work, see [Build & test](#build--test).)
5. **Nothing left to interpretation.** If a behavior isn't specified, check the area's PRD/ARCHITECTURE (`docs/`); if it truly isn't there, **stop and ask**, then record the answer in the relevant doc.

## Agent workflow

Delegate by read vs. write, mapped onto this repo's structure:

- **Exploring or researching across the areas** (`app/`, `EditMarkdown/`, `EditSpreadsheets/`) → fan out: a subagent per area gathers findings that you synthesize centrally. Reads don't collide, so parallelism is a clean win here.
- **Implementing a change** — especially one that spans the shell and an editor, or touches the one-way layering contract (golden rule #1) → keep it single-threaded. This is the write-heavy case where parallel agents make conflicting implicit decisions that are costly to reconcile, and the layering has to stay coherent across the edit.
- **Whatever the shape: only one agent builds or tests at a time.** Fanned-out subagents must read, not `dotnet build`/`dotnet test` — concurrent MSBuild runs deadlock (see [Build & test](#build--test)). Verify centrally, after the fan-in.

## Git workflow

- **Commit directly on `main`. Do not create branches.** This repo is trunk-based: all work — including refactors and multi-commit efforts — lands as commits straight on `main`. Do not open feature branches, and do not use merge commits.
- Keep commits small and coherent; split unrelated changes into separate commits.
- Use **Conventional Commits** with an area scope, matching the existing history: `feat(puffinote): …`, `fix(cellestra): …`, `refactor(typage): …`, `test(typage): …`, `perf(…): …`.
- Add a co-author line for your commit signature, e.g. `Co-Authored-By: <name> <email>`.
- You may commit without approval, but do not commit if there are outstanding questions. Only commit work whose solution builds clean and tests green.
