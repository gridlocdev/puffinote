# AGENTS.md

Orientation for an AI coding agent (and any human) working anywhere in the **Puffinote** repository. Read this first. It covers what's true across the whole repo; each area then has its own `AGENTS.md` with the detail that matters once you're working inside it — **read that area's guide before you touch its code.**

## What Puffinote is

A monorepo for **Puffinote**, a notebook-style workspace that composes independent, from-scratch native editors into one shell:

- **Typage** — a Markdown (`.md`) live-preview editor.
- **Cellestra** — an OpenDocument Spreadsheet (`.ods`) editor (macOS-first for 1.0).
- **Plaintain** — a plaintext (`.txt`, `.log`, anything decodable) editor.
- **Pivotask** — a task board (`.pivotask`).
- **Puffinote** — the composite shell/app that embeds them, plus a unifying CLI.

Each editor builds, tests, and ships on its own; the shell embeds them without reaching into their internals. Removing Puffinote must leave every editor fully working.

## Repository map

```
app/                          Puffinote — the composite shell (Desktop + CLI) that embeds the editors
features/EditMarkdown/        Typage — the Markdown editor (engine, control, desktop, CLI, tests)
features/EditSpreadsheets/    Cellestra — the spreadsheet editor (core, editor, charts, desktop, CLI, tests)
features/EditPlaintext/       Plaintain — the plaintext editor (control, desktop, CLI, tests)
features/ManageTasks/         Pivotask — the task board (core, control, desktop, CLI, tests)
shared/                       Cross-cutting libraries used by the shell and the editors:
                                Puffinote.Components (Avalonia: theming, icons, overlays, shell chrome)
                                Puffinote.Components.Core (non-UI: CLI helpers — safe for CLI projects)
                                Puffinote.Text (non-UI: piece table, graphemes, selection, undo, search, file codec)
docs/                         PRD / ARCHITECTURE (per-area) + proposals/ (repo-wide)
build/                        package-mac.sh — the shared macOS .app bundler for every app
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
| Plaintext editor (`features/EditPlaintext/`) | [`features/EditPlaintext/AGENTS.md`](features/EditPlaintext/AGENTS.md) | `features/EditPlaintext/Plaintain.slnx` |
| Task board (`features/ManageTasks/`) | [`features/ManageTasks/AGENTS.md`](features/ManageTasks/AGENTS.md) | `features/ManageTasks/Pivotask.slnx` |

`shared/` has no guide of its own because it isn't an area you work in — it's a dependency of every other one. Its projects are compiled into **every solution**, so a change there is a change to every area: build and test them all (`just check`) before you call it done.

**What belongs in `shared/`, and the things that deliberately don't.** Both editor proposals deferred a
rule-of-three review to Phase 5; it has been done, and its answers are worth not re-deriving:

| Candidate | Verdict |
|---|---|
| The **find bars** (Typage's and Plaintain's) | **Not promoted.** They look alike and are not: Typage's is a controller bound to its editor that also replaces text and reveals matches through concealed syntax; Plaintain's is a passive strip that raises events and lets the editor paint its own highlights. One control with two modes is two controls with extra steps. |
| The find bars' **match wording** ("3 of 17" / "No results") | **Promoted** → `Puffinote.Text`'s `SearchStatus`. Two apps showed the reader the same phrases, they agreed exactly, and nothing but luck was keeping them agreeing. The shared-vocabulary rule below covers a phrase for the same reason it covers a name. |
| Pivotask's **label chip** and **due pill** | **Not promoted.** They are already one function inside Pivotask (`TaskCard.Chip`) serving four callers, and no other app has anything of the shape. That is a local abstraction doing its job, not duplication. |
| The **one-file watcher** and **file stamp** | **Promoted** → `OpenFileWatcher`, `FileStamp`. Five apps needed the identical, fiddly plumbing. |
| The **crash-recovery snapshot** | **Promoted** → `RecoveryService`. Two text editors, nothing app-specific once the app name is a parameter. Stays text-shaped: a workbook is not a string. |
| The **macOS app menu** and **About card** | **Promoted** → `MacAppMenu`, `AboutDialog`. Five apps, four strings' difference. |

The rule that decided each of these: promote when the *duplication* is real, not when the *resemblance*
is. Two things that would have to grow a mode to share an implementation are two things.

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
just build-typage                         # one area     (verb-first: <verb>-puffinote | -typage | -cellestra | -plaintain | -pivotask)
just test-filter SomeTest typage          # one test / a subset — prefer this while iterating
just check                                # format-verify + build + test, every area
```

Every recipe is a thin wrapper over `dotnet build|test|format <area>.slnx` (e.g. `features/EditMarkdown/Typage.slnx`) — **if `just` isn't on `PATH`, run those directly**; they're equivalent, and some agent sandboxes aren't the devcontainer. (`just --dry-run <recipe>` prints the exact commands a recipe would run.) Likewise, if `dotnet` isn't on `PATH`, `export PATH="$PATH:/root/.dotnet"` (and `libicu` must be installed).

**Before running any test, export `CELLESTRA_HEADLESS_NULL_DRAWING=1` unless you are on CI or a real desktop.** Cellestra's headless UI tests draw with real Skia, which **hangs** where there is no display/GPU — and it hangs *silently*, before the first test reports, so it reads as "the tests are slow" rather than as an error. `.devcontainer/devcontainer.json` sets the variable via `containerEnv`, but **a shell that didn't come from the devcontainer does not get it** — which is most agent sandboxes and any `docker exec`. Check it, don't assume it:

```bash
echo "${CELLESTRA_HEADLESS_NULL_DRAWING:-<UNSET>}"   # <UNSET> ⇒ export it before testing
export CELLESTRA_HEADLESS_NULL_DRAWING=1
```

`just` does **not** set it either, so `just test`/`just check` hang the same way without it. Only Cellestra reads it — Typage, Plaintain, Pivotask, and Puffinote always use null drawing. Details and the `AssertRenders()` rule for render-capture tests: [`features/EditSpreadsheets/AGENTS.md`](features/EditSpreadsheets/AGENTS.md). Whole-suite timings with it set, as a sanity check: Cellestra ~10s, Typage ~4s, Puffinote ~4s — if a suite runs for minutes, it is hung, not busy.

**Shipping.** The same front door publishes and installs each app; all of it is macOS-first, and only the
`.app` targets require a Mac (the container can still publish):

```bash
just publish                              # CLI + desktop, self-contained Release → artifacts/<app>/<rid>/
just install                              # every CLI onto PATH (override: just PREFIX=/usr/local install)
just publish-typage-app                   # artifacts/typage/<rid>/Typage.app  (also: just install-app)
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

**Two ways a `*.UiTests` run hangs with no output**, both of which read as "the build lock again" and are
not. The run sits there and eventually dies at ~60s with `Xunit.Sdk.TestPipelineException`, or is
OOM-killed (exit 137); the offending class passes on its own under `--filter`, which is what makes it look
like a flake:

- **A plain `[Theory]`.** Use `[AvaloniaTheory]` in these projects. (`[Fact]` mixed in is fine.)
- **A modal left standing on an `OverlayHost`** — including a fire-and-forget
  `_ = _overlay.ShowModalAsync(…)` that nothing awaits. A headless run has nobody to dismiss it. Either
  keep the prompt out of the path under test (Typage's and Plaintain's external-change code split the
  *decision* from the *prompt* for exactly this reason, and test the decision) or dismiss it through a
  seam that calls `OverlayHost.Close()`.

Bisect with `dotnet test <project>.csproj --no-build --filter "FullyQualifiedName~<Name>"` before
suspecting anything else.

**Never run two `dotnet build`/`dotnet test` invocations at once** — concurrent runs deadlock on the MSBuild-node and build-output locks and hang indefinitely, and they are also what gets a container OOM-killed: each build brings MSBuild worker nodes (one per core by default), a Roslyn `VBCSCompiler` that outlives it, restore, and a `testhost` per test project. (This is why `just check` is serial — recipe dependencies run one at a time, in the order written.) If it happens: kill the `dotnet`/`testhost`/`MSBuild` processes, `dotnet build-server shutdown`, retry.

That rule holds *across* worktrees, not just within one — several agents in `.claude/worktrees/*` share one container's memory. Editing in parallel is fine; building in parallel is not. In a sandbox with no lock of its own, take one:

```bash
flock -w 900 /tmp/dotnet-build.lock dotnet build features/EditMarkdown/Typage.slnx
```

**If a build sits there having printed `agentbox: waiting for the dotnet build lock`, it is queued, not hung** — some container wraps `dotnet` in exactly that lock, so a build starts only once the one ahead of it finishes. Let it block. Killing `dotnet`/`MSBuild`/`testhost` or rerunning the command in another shell is the wrong move, and it is the move the "if a suite runs for minutes, it is hung" rule above would otherwise push you toward. The message names the holder (pid, working directory, command line); a `dotnet` that exits **199** never built anything — it timed out waiting, which is not a build failure, so re-run it (optionally with a longer `AGENTBOX_BUILD_LOCK_WAIT`) rather than going looking for a compile error. Locks are held by a live process and released when it exits, so there is never a stale lock file to clear by hand.

Memory-wise, a container running more than one worktree wants `DOTNET_gcServer=0` (workstation GC — the biggest single win, since Server GC reserves a heap per logical core), `MSBUILDDISABLENODEREUSE=1`, and `DOTNET_CLI_USE_MSBUILD_SERVER=0`; pass `-m:2` to `dotnet build` rather than letting MSBuild fan out to every core, and run `dotnet build-server shutdown` between tasks to reclaim the lingering compiler server. `.devcontainer/devcontainer.json` sets those three vars — a container it didn't build (an agent sandbox) does not have them.

**CI:** only `.github/workflows/typage-ci.yml` exists, and its push/PR triggers are commented out (`workflow_dispatch` only) — so nothing is gated for you on push. `just check` locally *is* the gate.

**Working with the analyzer/style rules.** The `.editorconfig` at the repo root is the single source of truth for style and diagnostic severities; `dotnet format` auto-fixes the large majority of violations, so run it first before fixing anything by hand. When something still fails the build:

- **Fix the code, don't silence the rule.** Don't add blanket `#pragma`/`NoWarn` or downgrade a severity just to go green. Some rules have no code-fixer and must be fixed by hand — e.g. `CA2000` (dispose), `CA1063` (IDisposable pattern). Only edit `.editorconfig` to change a rule when that's genuinely the right call, and say why in the commit.
- **`IDE0060` (unused parameter) is a non-blocking *warning*, not an error** (`WarningsNotAsErrors`), because the param is often required by a delegate/interface/override signature or is a placeholder for unwired behavior. Surface it — but **don't blind-delete a parameter to clear it**; a `sheet`/`hasHeader`-style unused param may be an incomplete feature, not cruft. Remove only when it's genuinely dead.
- **Don't re-enable XML-doc validation.** `GenerateDocumentationFile=true` exists only so `IDE0005` (unused usings) reports at build; the doc-prose warnings (`CS1591`, `CS15xx`) are `NoWarn`ed on purpose. Don't remove those from `NoWarn` or start writing `<param>` docs to satisfy them — this isn't a published-docs library.
- Test projects (`**/*Tests/**`) relax `CA1063`/`CA1816` — xUnit fixtures don't need the full `IDisposable` ceremony.

Build artifacts live under OS-specific `bin/<os>/` and `obj/<os>/` paths (so Linux devcontainer builds don't collide with native host builds) and are git-ignored — never commit them.

**Running.** The devcontainer is Linux and headless (no GPU), so launching a GUI (`just run-typage` and friends / `dotnet run --project *.Desktop`) fails *environmentally* — that's the container, not a regression in your change, and it is not something to debug. **Verify GUI behavior with headless UI tests, or through the CLI.** The CLI does run here:

```bash
dotnet run --project app/Puffinote.Cli -- info            # summarize the notebook in the current directory
dotnet run --project app/Puffinote.Cli -- search "todo"   # full-text search the notebook (notes + sheet cells)
dotnet run --project app/Puffinote.Cli -- md --help       # route into Typage's commands; `sheet …` routes to Cellestra
```

## Shared vocabulary

Five apps ship one family, so an element that appears in more than one of them has **one** name — in its
label, its command title, its tooltip, its settings row, **its key in the config file, its command id**,
and the PRD that specifies it. Two names for one thing is one thing the reader has to learn twice.

| Term | What it is |
|---|---|
| **Command Bar** | The horizontal strip of commands at the top of Typage and Cellestra — the adaptive bar with the search pill at its right end. Three states: visible, collapsed (its slim handle), hidden. Not "toolbar", not "formatting toolbar". |
| **Command Menu** | The searchable list of every command, opened with ⌘K or by clicking the Command Bar's "Search commands…" pill. Not "command palette" in anything a user reads. |

**A config file is a surface, not an implementation detail.** Its keys are read and hand-edited by people,
and a command id is the key a shortcut is rebound against in `shortcuts.json` and the value a Cellestra
radial slot stores — so both follow this table too. `Toolbar` is `CommandBar` in Typage's `settings.json`
(the name Cellestra's file already used), `view.commandPalette` is `view.commandMenu`, and Typage's
`view.toolbar*` ids are `view.commandBar*`, which is what Cellestra had all along. A key named after a word
the UI no longer uses is a third name for an element that is supposed to have one.

Renaming an id does cost something real: a rebound shortcut or a radial slot pointing at the old id stops
resolving, and the setting under the old key is not read. **No migration is provided** — that was a
deliberate call, taken while the family is still finding its names, and the fallback in every case is the
shipped default rather than a broken app.

What does *not* follow the table: C# type and member names that never reach a file (`PaletteListView`,
`PaletteCatalog`, `CommandPalette`, `IAppEnvironment.ShowCommandPalette`). They are free to be renamed and
free to lag; nothing outside the compiler reads them. And `docs/proposals/` is exempt — those are records
of what was decided when, and editing the words a past decision was written in is not a rename, it is a
forgery.

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

- Always create a git worktree from the `main` branch, and rebase onto `main` before merging. Do not merge `main` into your branch.
- Keep commits small and coherent; split unrelated changes into separate commits.
- Use **Conventional Commits** with an area scope, matching the existing history: `feat(puffinote): …`, `fix(cellestra): …`, `refactor(typage): …`, `test(typage): …`, `perf(…): …`.
- Add a co-author line for your commit signature, e.g. `Co-Authored-By: <name> <email>`.
- You may commit without approval, but do not commit if there are outstanding questions. Only commit work whose solution builds clean and tests green.
