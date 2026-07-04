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
shared/                       Cross-cutting libraries (Puffinote.Components, …) used by the shell/editors
docs/                         PRD / ARCHITECTURE / proposals (repo-wide and per-area)
Directory.Build.props         Shared MSBuild settings (framework, nullable, analyzers, OS-specific bin/obj)
Directory.Packages.props      Central NuGet version pins — the single source of truth for versions
```

There is no single root solution. Each area has its own:

| Working on… | Read its guide | Build/test solution |
|---|---|---|
| The shell / CLI (`app/`) | [`app/AGENTS.md`](app/AGENTS.md) | `app/Puffinote.slnx` |
| Markdown editor (`features/EditMarkdown/`) | [`features/EditMarkdown/AGENTS.md`](features/EditMarkdown/AGENTS.md) | `features/EditMarkdown/Typage.slnx` |
| Spreadsheet editor (`features/EditSpreadsheets/`) | [`features/EditSpreadsheets/AGENTS.md`](features/EditSpreadsheets/AGENTS.md) | `features/EditSpreadsheets/Cellestra.slnx` |

When this file and an area guide or a linked doc disagree, the deeper doc wins for design detail; this file wins for repo-wide process.

## Tech stack (do not change without being asked)

- **C# on .NET 10** (`net10.0`); SDK pinned in `global.json`.
- **Avalonia 12.0.4** for UI (compiled bindings on); SkiaSharp 3.
- **xUnit v3** for tests; headless UI via `Avalonia.Headless.XUnit`.
- **MIT** licensed.

Versions are pinned centrally in `Directory.Packages.props` (central package management is on) — never hardcode a version elsewhere. `Directory.Build.props` makes the SDK analyzers an enforced gate: `nullable` enable, `EnableNETAnalyzers`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, and **`TreatWarningsAsErrors=true`** — so any analyzer or style warning fails the build (see [Build & test](#build--test)).

## Build & test

```bash
dotnet build <area>.slnx                                        # e.g. app/Puffinote.slnx
dotnet test  <area>.slnx                                        # whole suite for that area
dotnet test  <area>.slnx --filter "FullyQualifiedName~SomeTest" # one test / a subset — prefer this while iterating
```

**Definition of done:** the relevant solution builds clean and its tests are green. Warnings **are** errors here (`TreatWarningsAsErrors=true`), so a warning fails the build — "builds" already means "no warnings," this is not advisory.

**Format before committing.** Run `dotnet format <area>.slnx` on the area you changed, then confirm `dotnet format <area>.slnx --verify-no-changes` exits zero — a non-zero exit means unformatted code, which is a failure to fix before you commit.

**Working with the analyzer/style rules.** The `.editorconfig` at the repo root is the single source of truth for style and diagnostic severities; `dotnet format` auto-fixes the large majority of violations, so run it first before fixing anything by hand. When something still fails the build:

- **Fix the code, don't silence the rule.** Don't add blanket `#pragma`/`NoWarn` or downgrade a severity just to go green. Some rules have no code-fixer and must be fixed by hand — e.g. `CA2000` (dispose), `CA1063` (IDisposable pattern). Only edit `.editorconfig` to change a rule when that's genuinely the right call, and say why in the commit.
- **`IDE0060` (unused parameter) is a non-blocking *warning*, not an error** (`WarningsNotAsErrors`), because the param is often required by a delegate/interface/override signature or is a placeholder for unwired behavior. Surface it — but **don't blind-delete a parameter to clear it**; a `sheet`/`hasHeader`-style unused param may be an incomplete feature, not cruft. Remove only when it's genuinely dead.
- **Don't re-enable XML-doc validation.** `GenerateDocumentationFile=true` exists only so `IDE0005` (unused usings) reports at build; the doc-prose warnings (`CS1591`, `CS15xx`) are `NoWarn`ed on purpose. Don't remove those from `NoWarn` or start writing `<param>` docs to satisfy them — this isn't a published-docs library.
- Test projects (`**/*Tests/**`) relax `CA1063`/`CA1816` — xUnit fixtures don't need the full `IDisposable` ceremony.

Build artifacts live under OS-specific `bin/<os>/` and `obj/<os>/` paths (so Linux devcontainer builds don't collide with native host builds) and are git-ignored — never commit them.

**Running.** The devcontainer is Linux and headless (no GPU), so GUI `dotnet run` may fail environmentally — verify GUI behavior with headless tests, not by launching the app. The CLI does run headless; to exercise it:

```bash
dotnet run --project app/Puffinote.Cli -- info            # summarize the notebook in the current directory
dotnet run --project app/Puffinote.Cli -- search "todo"   # full-text search the notebook (notes + sheet cells)
dotnet run --project app/Puffinote.Cli -- md --help       # route into Typage's commands; `sheet …` routes to Cellestra
```

## Repo-wide golden rules

1. **One-way layering.** Puffinote → editors + `shared/`; **nothing references Puffinote.** Editors don't depend on each other. If the shell needs a hook to embed an editor, add it to the *editor* or to `shared/`, never as a reach-in from Puffinote.
2. **Compose, don't fork.** Embed the existing editor surfaces unchanged rather than copying them.
3. **Guard the supply chain.** Trusted base is the .NET BCL + Avalonia (+ its native deps). No new NuGet package without a stated reason in the PR/commit; **never** add JavaScript/npm. First-party Avalonia-team controls (MIT) are pre-approved.
4. **macOS-first for 1.0.** The devcontainer is Linux and headless (no GPU) — GUI `dotnet run` may fail environmentally there; verify behavior with headless tests, not by launching the app.
5. **Nothing left to interpretation.** If a behavior isn't specified, check the area's PRD/ARCHITECTURE (`docs/`); if it truly isn't there, **stop and ask**, then record the answer in the relevant doc.

## Agent workflow

Delegate by read vs. write, mapped onto this repo's structure:

- **Exploring or researching across the areas** (`app/`, `EditMarkdown/`, `EditSpreadsheets/`) → fan out: a subagent per area gathers findings that you synthesize centrally. Reads don't collide, so parallelism is a clean win here.
- **Implementing a change** — especially one that spans the shell and an editor, or touches the one-way layering contract (golden rule #1) → keep it single-threaded. This is the write-heavy case where parallel agents make conflicting implicit decisions that are costly to reconcile, and the layering has to stay coherent across the edit.

## Git workflow

- **Commit directly on `main`. Do not create branches.** This repo is trunk-based: all work — including refactors and multi-commit efforts — lands as commits straight on `main`. Do not open feature branches, and do not use merge commits.
- Keep commits small and coherent; split unrelated changes into separate commits.
- Use **Conventional Commits** with an area scope, matching the existing history: `feat(puffinote): …`, `fix(cellestra): …`, `refactor(typage): …`, `test(typage): …`, `perf(…): …`.
- Add a co-author line for your commit signature, e.g. `Co-Authored-By: <name> <email>`.
- Commit or push only when asked. Only commit work whose solution builds clean and tests green.
