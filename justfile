# Puffinote monorepo — the task runner (https://just.systems)
#
# There is no root solution. Each area builds, tests, and ships on its own:
#   app/                        Puffinote — composite shell (Desktop + CLI)
#   features/EditMarkdown/      Typage    — Markdown editor
#   features/EditSpreadsheets/  Cellestra — spreadsheet editor
#   features/EditPlaintext/     Plaintain — plaintext editor
#   features/ManageTasks/       Pivotask  — task board
#
# Common flows:
#   just                  list every recipe (grouped)
#   just build            build every area
#   just test             test every area
#   just format           auto-format every area
#   just check            format-verify + build + test all areas (CI gate / definition of done)
#   just check-nodisplay  ...the same, in a container/sandbox with no display or GPU  ← use this here
#   just test-nodisplay   test every area with no display or GPU
#   just test-typage      one area's suite (recipes read verb-first: test-puffinote, publish-cellestra-app)
#   just test-filter SomeTest typage    run a subset while iterating
#   just publish          self-contained Release builds of every app into artifacts/
#   just install          put the CLIs (puffinote, typage, cellestra, pltn, ptask) on PATH
#   just publish-app      macOS .app bundles for every app (macOS only)
#   just install-app      ...and copy them into /Applications
#
# Every install has an inverse that undoes exactly what it did, and nothing else:
#   just publish ↔ just unpublish            what publish wrote under artifacts/, for this RID
#   just install ↔ just uninstall            the CLIs in PREFIX/bin
#   just install-app ↔ just uninstall-app    the .app bundles in APPS_DIR (also withdraws their
#                                            Launch Services file-type claims)
# `unpublish` is RID-precise, so a cross-published osx-x64 tree survives `just unpublish` on an
# arm64 host; `just clean` is the blunt one and removes artifacts/ entirely.
# Neither uninstall touches PREFIX/bin itself (it predates us and is on your PATH) or an app's
# user data in ~/Library/Application Support — removing a tool is not consent to delete documents,
# settings, or unsaved-work snapshots.
#
# Recipe dependencies run in the order written, one at a time — which is required here, because
# concurrent `dotnet build`/`dotnet test` invocations deadlock on the MSBuild-node locks.
#
# Warnings are errors here (TreatWarningsAsErrors=true), so "build" means "no warnings".
#
# Cellestra's headless UI tests draw with real Skia, which HANGS where there is no display or GPU —
# silently, before the first test reports, so it reads as "slow" rather than "broken". The plain
# `test`/`check` recipes leave the drawing backend alone so CI and a real desktop keep real Skia and
# its render-capture assertions; the `-nodisplay` variants set CELLESTRA_HEADLESS_NULL_DRAWING=1 for
# the run instead. The devcontainer exports that var, but a shell it did not start (agent sandbox,
# docker exec) does not have it — if `just test` sits there with no output, you want `-nodisplay`.

# ---- Solutions ----------------------------------------------------------------
PUFFINOTE_SLN := "app/Puffinote.slnx"
TYPAGE_SLN    := "features/EditMarkdown/Typage.slnx"
CELLESTRA_SLN := "features/EditSpreadsheets/Cellestra.slnx"
PLAINTAIN_SLN := "features/EditPlaintext/Plaintain.slnx"
PIVOTASK_SLN  := "features/ManageTasks/Pivotask.slnx"

# ---- CLI entry projects (run headless in the Linux devcontainer) --------------
PUFFINOTE_CLI := "app/Puffinote.Cli"
TYPAGE_CLI    := "features/EditMarkdown/Typage.Cli"
CELLESTRA_CLI := "features/EditSpreadsheets/Cellestra.Cli"
PLAINTAIN_CLI := "features/EditPlaintext/Plaintain.Cli"
PIVOTASK_CLI  := "features/ManageTasks/Pivotask.Cli"

# ---- Desktop (GUI) entry projects (run on a native host, not the container) ---
PUFFINOTE_DESKTOP := "app/Puffinote.Desktop"
TYPAGE_DESKTOP    := "features/EditMarkdown/Typage.Desktop"
CELLESTRA_DESKTOP := "features/EditSpreadsheets/Cellestra.Desktop"
PLAINTAIN_DESKTOP := "features/EditPlaintext/Plaintain.Desktop"
PIVOTASK_DESKTOP  := "features/ManageTasks/Pivotask.Desktop"

# ---- BenchmarkDotNet harnesses (development-time; Release, and they take minutes) ----
TYPAGE_BENCH    := "features/EditMarkdown/Typage.Benchmarks"
CELLESTRA_BENCH := "features/EditSpreadsheets/Cellestra.Benchmarks"
PLAINTAIN_BENCH := "features/EditPlaintext/Plaintain.Benchmarks"

DOTNET := "dotnet"

# ---- Packaging: where publish writes, and where install puts things -----------
# Override any of these on the command line, e.g.
#   just RID=osx-x64 CONFIG=Debug publish-cellestra
#   just PREFIX=/usr/local install
CONFIG    := "Release"
ARTIFACTS := "artifacts"
PREFIX    := env("HOME") / ".local"
BINDIR    := PREFIX / "bin"
APPS_DIR  := "/Applications"

# Runtime identifier for the host, unless one is passed in.
RID_OS   := if os() == "macos" { "osx" } else { "linux" }
RID_ARCH := if arch() == "aarch64" { "arm64" } else { "x64" }
RID      := RID_OS + "-" + RID_ARCH

PUFFINOTE_OUT := ARTIFACTS / "puffinote" / RID
TYPAGE_OUT    := ARTIFACTS / "typage" / RID
CELLESTRA_OUT := ARTIFACTS / "cellestra" / RID
PLAINTAIN_OUT := ARTIFACTS / "plaintain" / RID
PIVOTASK_OUT  := ARTIFACTS / "pivotask" / RID

# Both surfaces publish self-contained, so an artifact runs on a machine with no .NET installed.
# The CLI additionally publishes as a single self-extracting file (native libraries included) so
# installing it is one copy onto PATH; the desktop apps keep their Avalonia/Skia natives beside
# the executable, which is what a macOS .app bundle expects (see build/package-mac.sh).
PUBLISH_CLI_FLAGS     := "-c " + CONFIG + " -r " + RID + " --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
PUBLISH_DESKTOP_FLAGS := "-c " + CONFIG + " -r " + RID + " --self-contained true"

# Removing a .app should also withdraw the file-type claims its Info.plist registered (Typage owns
# .md, Cellestra .ods), so a stale association can't outlive the bundle. Best-effort: the path is
# macOS-only and uninstall must not fail on a machine without it.
LSREGISTER := "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"

# Bundling a desktop app as a macOS .app is one script shared by all three apps — it owns the
# bundle layout, code-signing, and notarization; this file only passes the knobs through.
# Each app supplies its own bundle metadata in <App>.Desktop/Info.plist.
PACKAGE_MAC := "build/package-mac.sh"

# Show every recipe, grouped (the default when you run bare `just`).
default:
    @just --list --list-heading $'Puffinote — run `just <recipe>`\n'

# ==============================================================================
# Aggregate recipes — act on all three areas
# ==============================================================================

# Build and test every area
[group('all areas')]
all: build test

# Restore NuGet packages for every area
[group('all areas')]
restore: restore-puffinote restore-typage restore-cellestra restore-plaintain restore-pivotask

# Build every area (warnings are errors)
[group('all areas')]
build: build-puffinote build-typage build-cellestra build-plaintain build-pivotask

# Test every area
[group('all areas')]
test: test-puffinote test-typage test-cellestra test-plaintain test-pivotask

# Auto-format every area
[group('all areas')]
format: format-puffinote format-typage format-cellestra format-plaintain format-pivotask

# Verify formatting for every area (no changes)
[group('all areas')]
format-check: format-check-puffinote format-check-typage format-check-cellestra format-check-plaintain format-check-pivotask

# Remove build artifacts for every area, including everything publish wrote (all RIDs)
[group('all areas')]
clean: clean-puffinote clean-typage clean-cellestra clean-plaintain clean-pivotask
    rm -rf "{{ ARTIFACTS }}"

# Definition of done: formatting verified, all areas build clean and green
[group('all areas')]
check: format-check build test

# Test every area where there is no display/GPU (container, agent sandbox, plain ssh)
[group('all areas')]
test-nodisplay: test-puffinote test-typage test-cellestra-nodisplay test-plaintain test-pivotask

# `just check` where there is no display/GPU — the definition of done in a container
[group('all areas')]
check-nodisplay: format-check build test-nodisplay

# Publish every app (CLI + desktop, self-contained) to artifacts/
[group('all areas')]
publish: publish-puffinote publish-typage publish-cellestra publish-plaintain publish-pivotask

# Remove every app's published output for this RID (the inverse of publish)
[group('all areas')]
unpublish: unpublish-puffinote unpublish-typage unpublish-cellestra unpublish-plaintain unpublish-pivotask

# Install every CLI into PREFIX/bin (default ~/.local/bin)
[group('all areas')]
install: install-puffinote install-typage install-cellestra install-plaintain install-pivotask

# Remove every installed CLI from PREFIX/bin
[group('all areas')]
uninstall: uninstall-puffinote uninstall-typage uninstall-cellestra uninstall-plaintain uninstall-pivotask

# Build every macOS .app bundle (macOS only)
[group('all areas')]
publish-app: publish-puffinote-app publish-typage-app publish-cellestra-app publish-plaintain-app publish-pivotask-app

# Install every macOS .app into APPS_DIR (macOS only)
[group('all areas')]
install-app: install-puffinote-app install-typage-app install-cellestra-app install-plaintain-app install-pivotask-app

# Remove every macOS .app from APPS_DIR (the inverse of install-app; macOS only)
[group('all areas')]
uninstall-app: uninstall-puffinote-app uninstall-typage-app uninstall-cellestra-app uninstall-plaintain-app uninstall-pivotask-app

# ==============================================================================
# Puffinote — the composite shell (app/)
# ==============================================================================

# Restore Puffinote packages
[group('puffinote')]
restore-puffinote:
    {{ DOTNET }} restore {{ PUFFINOTE_SLN }}

# Build the Puffinote shell
[group('puffinote')]
build-puffinote:
    {{ DOTNET }} build {{ PUFFINOTE_SLN }}

# Test the Puffinote shell
[group('puffinote')]
test-puffinote:
    {{ DOTNET }} test {{ PUFFINOTE_SLN }}

# Auto-format the Puffinote shell
[group('puffinote')]
format-puffinote:
    {{ DOTNET }} format {{ PUFFINOTE_SLN }}

# Verify Puffinote formatting (fails if unformatted)
[group('puffinote')]
format-check-puffinote:
    {{ DOTNET }} format {{ PUFFINOTE_SLN }} --verify-no-changes

# Clean Puffinote build artifacts
[group('puffinote')]
clean-puffinote:
    {{ DOTNET }} clean {{ PUFFINOTE_SLN }}

# Run the Puffinote desktop shell (native host — GUI won't launch in the container)
[group('puffinote')]
run-puffinote:
    {{ DOTNET }} run --project {{ PUFFINOTE_DESKTOP }}

# Run the Puffinote CLI: just run-puffinote-cli info | search "todo"
[group('puffinote')]
run-puffinote-cli *ARGS:
    {{ DOTNET }} run --project {{ PUFFINOTE_CLI }} -- {{ ARGS }}

# Publish the Puffinote CLI + shell to artifacts/puffinote/<rid>/
[group('puffinote')]
publish-puffinote: publish-puffinote-cli publish-puffinote-desktop

# Publish the `puffinote` CLI (single self-contained file)
[group('puffinote')]
publish-puffinote-cli:
    {{ DOTNET }} publish {{ PUFFINOTE_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ PUFFINOTE_OUT }}/cli

# Publish the Puffinote desktop shell (self-contained)
[group('puffinote')]
publish-puffinote-desktop:
    {{ DOTNET }} publish {{ PUFFINOTE_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ PUFFINOTE_OUT }}/desktop

# Build Puffinote.app (macOS only)
[group('puffinote')]
publish-puffinote-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ PUFFINOTE_OUT }} {{ PACKAGE_MAC }} puffinote

# Install the `puffinote` CLI into PREFIX/bin (default ~/.local/bin)
[group('puffinote')]
install-puffinote: publish-puffinote-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ PUFFINOTE_OUT }}/cli/puffinote "{{ BINDIR }}/puffinote"
    @echo "installed {{ BINDIR }}/puffinote — make sure {{ BINDIR }} is on your PATH"

# Install Puffinote.app into APPS_DIR (default /Applications, macOS only)
[group('puffinote')]
install-puffinote-app: publish-puffinote-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Puffinote.app"
    cp -R {{ PUFFINOTE_OUT }}/Puffinote.app "{{ APPS_DIR }}/Puffinote.app"
    @echo "installed {{ APPS_DIR }}/Puffinote.app"

# Remove Puffinote's published output for this RID (the inverse of publish-puffinote)
[group('puffinote')]
unpublish-puffinote:
    rm -rf "{{ PUFFINOTE_OUT }}"
    @rmdir "{{ ARTIFACTS }}/puffinote" 2>/dev/null || true
    @rmdir "{{ ARTIFACTS }}" 2>/dev/null || true
    @echo "removed {{ PUFFINOTE_OUT }}"

# Remove the installed `puffinote` CLI from PREFIX/bin
[group('puffinote')]
uninstall-puffinote:
    rm -f "{{ BINDIR }}/puffinote"
    @echo "removed {{ BINDIR }}/puffinote"

# Remove Puffinote.app from APPS_DIR (the inverse of install-puffinote-app; macOS only)
[group('puffinote')]
uninstall-puffinote-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Puffinote.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Puffinote.app"
    @echo "removed {{ APPS_DIR }}/Puffinote.app"

# ==============================================================================
# Typage — the Markdown editor (features/EditMarkdown/)
# ==============================================================================

# Restore Typage packages
[group('typage')]
restore-typage:
    {{ DOTNET }} restore {{ TYPAGE_SLN }}

# Build the Typage editor
[group('typage')]
build-typage:
    {{ DOTNET }} build {{ TYPAGE_SLN }}

# Test the Typage editor
[group('typage')]
test-typage:
    {{ DOTNET }} test {{ TYPAGE_SLN }}

# Auto-format the Typage editor
[group('typage')]
format-typage:
    {{ DOTNET }} format {{ TYPAGE_SLN }}

# Verify Typage formatting (fails if unformatted)
[group('typage')]
format-check-typage:
    {{ DOTNET }} format {{ TYPAGE_SLN }} --verify-no-changes

# Clean Typage build artifacts
[group('typage')]
clean-typage:
    {{ DOTNET }} clean {{ TYPAGE_SLN }}

# Run the Typage desktop editor (native host — GUI won't launch in the container)
[group('typage')]
run-typage:
    {{ DOTNET }} run --project {{ TYPAGE_DESKTOP }}

# Run the Typage CLI: just run-typage-cli html notes.md -o notes.html
[group('typage')]
run-typage-cli *ARGS:
    {{ DOTNET }} run --project {{ TYPAGE_CLI }} -- {{ ARGS }}

# Measure the engine hot paths behind caret/scroll latency (Release; minutes)
[group('typage')]
bench-typage *ARGS:
    {{ DOTNET }} run -c Release --project {{ TYPAGE_BENCH }} -- {{ ARGS }}

# Publish the Typage CLI + editor to artifacts/typage/<rid>/
[group('typage')]
publish-typage: publish-typage-cli publish-typage-desktop

# Publish the `typage` CLI (single self-contained file)
[group('typage')]
publish-typage-cli:
    {{ DOTNET }} publish {{ TYPAGE_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ TYPAGE_OUT }}/cli

# Publish the Typage desktop editor (self-contained)
[group('typage')]
publish-typage-desktop:
    {{ DOTNET }} publish {{ TYPAGE_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ TYPAGE_OUT }}/desktop

# Build Typage.app (macOS only)
[group('typage')]
publish-typage-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ TYPAGE_OUT }} {{ PACKAGE_MAC }} typage

# Install the `typage` CLI into PREFIX/bin (default ~/.local/bin)
[group('typage')]
install-typage: publish-typage-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ TYPAGE_OUT }}/cli/typage "{{ BINDIR }}/typage"
    @echo "installed {{ BINDIR }}/typage — make sure {{ BINDIR }} is on your PATH"

# Install Typage.app into APPS_DIR (default /Applications, macOS only)
[group('typage')]
install-typage-app: publish-typage-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Typage.app"
    cp -R {{ TYPAGE_OUT }}/Typage.app "{{ APPS_DIR }}/Typage.app"
    @echo "installed {{ APPS_DIR }}/Typage.app"

# Remove Typage's published output for this RID (the inverse of publish-typage)
[group('typage')]
unpublish-typage:
    rm -rf "{{ TYPAGE_OUT }}"
    @rmdir "{{ ARTIFACTS }}/typage" 2>/dev/null || true
    @rmdir "{{ ARTIFACTS }}" 2>/dev/null || true
    @echo "removed {{ TYPAGE_OUT }}"

# Remove the installed `typage` CLI from PREFIX/bin
[group('typage')]
uninstall-typage:
    rm -f "{{ BINDIR }}/typage"
    @echo "removed {{ BINDIR }}/typage"

# Remove Typage.app from APPS_DIR (the inverse of install-typage-app; macOS only)
[group('typage')]
uninstall-typage-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Typage.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Typage.app"
    @echo "removed {{ APPS_DIR }}/Typage.app"

# ==============================================================================
# Cellestra — the spreadsheet editor (features/EditSpreadsheets/)
# ==============================================================================

# Restore Cellestra packages
[group('cellestra')]
restore-cellestra:
    {{ DOTNET }} restore {{ CELLESTRA_SLN }}

# Build the Cellestra editor
[group('cellestra')]
build-cellestra:
    {{ DOTNET }} build {{ CELLESTRA_SLN }}

# Test the Cellestra editor
[group('cellestra')]
test-cellestra:
    {{ DOTNET }} test {{ CELLESTRA_SLN }}

# Test Cellestra where there is no display/GPU — its UI tests hang on real Skia without one
[group('cellestra')]
test-cellestra-nodisplay:
    CELLESTRA_HEADLESS_NULL_DRAWING=1 {{ DOTNET }} test {{ CELLESTRA_SLN }}

# Auto-format the Cellestra editor
[group('cellestra')]
format-cellestra:
    {{ DOTNET }} format {{ CELLESTRA_SLN }}

# Verify Cellestra formatting (fails if unformatted)
[group('cellestra')]
format-check-cellestra:
    {{ DOTNET }} format {{ CELLESTRA_SLN }} --verify-no-changes

# Clean Cellestra build artifacts
[group('cellestra')]
clean-cellestra:
    {{ DOTNET }} clean {{ CELLESTRA_SLN }}

# Run the Cellestra desktop editor (native host — GUI won't launch in the container)
[group('cellestra')]
run-cellestra:
    {{ DOTNET }} run --project {{ CELLESTRA_DESKTOP }}

# Run the Cellestra CLI: just run-cellestra-cli get book.ods A1
[group('cellestra')]
run-cellestra-cli *ARGS:
    {{ DOTNET }} run --project {{ CELLESTRA_CLI }} -- {{ ARGS }}

# Measure the recalculation and serialization hot paths (Release; minutes)
[group('cellestra')]
bench-cellestra *ARGS:
    {{ DOTNET }} run -c Release --project {{ CELLESTRA_BENCH }} -- {{ ARGS }}

# Publish the Cellestra CLI + editor to artifacts/cellestra/<rid>/
[group('cellestra')]
publish-cellestra: publish-cellestra-cli publish-cellestra-desktop

# Publish the `cellestra` CLI (single self-contained file)
[group('cellestra')]
publish-cellestra-cli:
    {{ DOTNET }} publish {{ CELLESTRA_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ CELLESTRA_OUT }}/cli

# Publish the Cellestra desktop editor (self-contained)
[group('cellestra')]
publish-cellestra-desktop:
    {{ DOTNET }} publish {{ CELLESTRA_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ CELLESTRA_OUT }}/desktop

# Build Cellestra.app (macOS only)
[group('cellestra')]
publish-cellestra-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ CELLESTRA_OUT }} {{ PACKAGE_MAC }} cellestra

# Install the `cellestra` CLI into PREFIX/bin (default ~/.local/bin)
[group('cellestra')]
install-cellestra: publish-cellestra-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ CELLESTRA_OUT }}/cli/cellestra "{{ BINDIR }}/cellestra"
    @echo "installed {{ BINDIR }}/cellestra — make sure {{ BINDIR }} is on your PATH"

# Install Cellestra.app into APPS_DIR (default /Applications, macOS only)
[group('cellestra')]
install-cellestra-app: publish-cellestra-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Cellestra.app"
    cp -R {{ CELLESTRA_OUT }}/Cellestra.app "{{ APPS_DIR }}/Cellestra.app"
    @echo "installed {{ APPS_DIR }}/Cellestra.app"

# Remove Cellestra's published output for this RID (the inverse of publish-cellestra)
[group('cellestra')]
unpublish-cellestra:
    rm -rf "{{ CELLESTRA_OUT }}"
    @rmdir "{{ ARTIFACTS }}/cellestra" 2>/dev/null || true
    @rmdir "{{ ARTIFACTS }}" 2>/dev/null || true
    @echo "removed {{ CELLESTRA_OUT }}"

# Remove the installed `cellestra` CLI from PREFIX/bin
[group('cellestra')]
uninstall-cellestra:
    rm -f "{{ BINDIR }}/cellestra"
    @echo "removed {{ BINDIR }}/cellestra"

# Remove Cellestra.app from APPS_DIR (the inverse of install-cellestra-app; macOS only)
[group('cellestra')]
uninstall-cellestra-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Cellestra.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Cellestra.app"
    @echo "removed {{ APPS_DIR }}/Cellestra.app"


# ==============================================================================
# Plaintain — the plaintext editor (features/EditPlaintext/)
# ==============================================================================

# Restore Plaintain packages
[group('plaintain')]
restore-plaintain:
    {{ DOTNET }} restore {{ PLAINTAIN_SLN }}

# Build the Plaintain editor
[group('plaintain')]
build-plaintain:
    {{ DOTNET }} build {{ PLAINTAIN_SLN }}

# Test the Plaintain editor
[group('plaintain')]
test-plaintain:
    {{ DOTNET }} test {{ PLAINTAIN_SLN }}

# Auto-format the Plaintain editor
[group('plaintain')]
format-plaintain:
    {{ DOTNET }} format {{ PLAINTAIN_SLN }}

# Verify Plaintain formatting (fails if unformatted)
[group('plaintain')]
format-check-plaintain:
    {{ DOTNET }} format {{ PLAINTAIN_SLN }} --verify-no-changes

# Clean Plaintain build artifacts
[group('plaintain')]
clean-plaintain:
    {{ DOTNET }} clean {{ PLAINTAIN_SLN }}

# Run the Plaintain desktop editor (native host — GUI won't launch in the container)
[group('plaintain')]
run-plaintain:
    {{ DOTNET }} run --project {{ PLAINTAIN_DESKTOP }}

# Run the pltn CLI: just run-plaintain-cli info server.log
[group('plaintain')]
run-plaintain-cli *ARGS:
    {{ DOTNET }} run --project {{ PLAINTAIN_CLI }} -- {{ ARGS }}

# Measure the 50 MB load path and the geometry claim (Release; minutes). Pass BenchmarkDotNet args:
#   just bench-plaintain --filter "*Load*" --job short
[group('plaintain')]
bench-plaintain *ARGS:
    {{ DOTNET }} run -c Release --project {{ PLAINTAIN_BENCH }} -- {{ ARGS }}

# Publish the pltn CLI + Plaintain shell to artifacts/plaintain/<rid>/
[group('plaintain')]
publish-plaintain: publish-plaintain-cli publish-plaintain-desktop

# Publish the `pltn` CLI (single self-contained file)
[group('plaintain')]
publish-plaintain-cli:
    {{ DOTNET }} publish {{ PLAINTAIN_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ PLAINTAIN_OUT }}/cli

# Publish the Plaintain desktop editor (self-contained)
[group('plaintain')]
publish-plaintain-desktop:
    {{ DOTNET }} publish {{ PLAINTAIN_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ PLAINTAIN_OUT }}/desktop

# Build Plaintain.app (macOS only)
[group('plaintain')]
publish-plaintain-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ PLAINTAIN_OUT }} {{ PACKAGE_MAC }} plaintain

# Install the `pltn` CLI into PREFIX/bin (default ~/.local/bin)
[group('plaintain')]
install-plaintain: publish-plaintain-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ PLAINTAIN_OUT }}/cli/pltn "{{ BINDIR }}/pltn"
    @echo "installed {{ BINDIR }}/pltn — make sure {{ BINDIR }} is on your PATH"

# Install Plaintain.app into APPS_DIR (default /Applications, macOS only)
[group('plaintain')]
install-plaintain-app: publish-plaintain-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Plaintain.app"
    cp -R {{ PLAINTAIN_OUT }}/Plaintain.app "{{ APPS_DIR }}/Plaintain.app"
    @echo "installed {{ APPS_DIR }}/Plaintain.app"

# Remove Plaintain's published output for this RID (the inverse of publish-plaintain)
[group('plaintain')]
unpublish-plaintain:
    rm -rf "{{ PLAINTAIN_OUT }}"
    @rmdir "{{ ARTIFACTS }}/plaintain" 2>/dev/null || true
    @rmdir "{{ ARTIFACTS }}" 2>/dev/null || true
    @echo "removed {{ PLAINTAIN_OUT }}"

# Remove the installed `pltn` CLI from PREFIX/bin
[group('plaintain')]
uninstall-plaintain:
    rm -f "{{ BINDIR }}/pltn"
    @echo "removed {{ BINDIR }}/pltn"

# Remove Plaintain.app from APPS_DIR (the inverse of install-plaintain-app; macOS only)
[group('plaintain')]
uninstall-plaintain-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Plaintain.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Plaintain.app"
    @echo "removed {{ APPS_DIR }}/Plaintain.app"


# ==============================================================================
# Pivotask — the task board (features/ManageTasks/)
# ==============================================================================

# Restore Pivotask packages
[group('pivotask')]
restore-pivotask:
    {{ DOTNET }} restore {{ PIVOTASK_SLN }}

# Build Pivotask
[group('pivotask')]
build-pivotask:
    {{ DOTNET }} build {{ PIVOTASK_SLN }}

# Test Pivotask
[group('pivotask')]
test-pivotask:
    {{ DOTNET }} test {{ PIVOTASK_SLN }}

# Auto-format Pivotask
[group('pivotask')]
format-pivotask:
    {{ DOTNET }} format {{ PIVOTASK_SLN }}

# Verify Pivotask formatting (fails if unformatted)
[group('pivotask')]
format-check-pivotask:
    {{ DOTNET }} format {{ PIVOTASK_SLN }} --verify-no-changes

# Clean Pivotask build artifacts
[group('pivotask')]
clean-pivotask:
    {{ DOTNET }} clean {{ PIVOTASK_SLN }}

# Run the Pivotask desktop editor (native host — GUI won't launch in the container)
[group('pivotask')]
run-pivotask:
    {{ DOTNET }} run --project {{ PIVOTASK_DESKTOP }}

# Run the ptask CLI: just run-pivotask-cli list --due-soon
[group('pivotask')]
run-pivotask-cli *ARGS:
    {{ DOTNET }} run --project {{ PIVOTASK_CLI }} -- {{ ARGS }}

# Publish the ptask CLI + Pivotask app to artifacts/pivotask/<rid>/
[group('pivotask')]
publish-pivotask: publish-pivotask-cli publish-pivotask-desktop

# Publish the `ptask` CLI (single self-contained file)
[group('pivotask')]
publish-pivotask-cli:
    {{ DOTNET }} publish {{ PIVOTASK_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ PIVOTASK_OUT }}/cli

# Publish the Pivotask desktop app (self-contained)
[group('pivotask')]
publish-pivotask-desktop:
    {{ DOTNET }} publish {{ PIVOTASK_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ PIVOTASK_OUT }}/desktop

# Build Pivotask.app (macOS only)
[group('pivotask')]
publish-pivotask-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ PIVOTASK_OUT }} {{ PACKAGE_MAC }} pivotask

# Check that Launch Services types a .pivotask file as Pivotask's own document (macOS only — that is the
# point). Prints PASS, or FAIL with the fallback proposal 0004 §10.3 already settled.
[group('pivotask')]
verify-pivotask-file-claim *APP:
    ./build/verify-mac-file-claim.sh {{ APP }}

# Install the `ptask` CLI into PREFIX/bin (default ~/.local/bin)
[group('pivotask')]
install-pivotask: publish-pivotask-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ PIVOTASK_OUT }}/cli/ptask "{{ BINDIR }}/ptask"
    @echo "installed {{ BINDIR }}/ptask — make sure {{ BINDIR }} is on your PATH"

# Install Pivotask.app into APPS_DIR (default /Applications, macOS only)
[group('pivotask')]
install-pivotask-app: publish-pivotask-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Pivotask.app"
    cp -R {{ PIVOTASK_OUT }}/Pivotask.app "{{ APPS_DIR }}/Pivotask.app"
    @echo "installed {{ APPS_DIR }}/Pivotask.app"

# Remove Pivotask's published output for this RID (the inverse of publish-pivotask)
[group('pivotask')]
unpublish-pivotask:
    rm -rf "{{ PIVOTASK_OUT }}"
    @rmdir "{{ ARTIFACTS }}/pivotask" 2>/dev/null || true
    @rmdir "{{ ARTIFACTS }}" 2>/dev/null || true
    @echo "removed {{ PIVOTASK_OUT }}"

# Remove the installed `ptask` CLI from PREFIX/bin
[group('pivotask')]
uninstall-pivotask:
    rm -f "{{ BINDIR }}/ptask"
    @echo "removed {{ BINDIR }}/ptask"

# Remove Pivotask.app from APPS_DIR (the inverse of install-pivotask-app; macOS only)
[group('pivotask')]
uninstall-pivotask-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Pivotask.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Pivotask.app"
    @echo "removed {{ APPS_DIR }}/Pivotask.app"

# ==============================================================================
# Iterating on a single test / subset (prefer this while developing)
# ==============================================================================

# Run a filtered subset: just test-filter SomeTest [puffinote|typage|cellestra|plaintain|pivotask]
[group('all areas')]
test-filter FILTER AREA="puffinote":
    {{ DOTNET }} test {{ if AREA == "typage" { TYPAGE_SLN } else if AREA == "cellestra" { CELLESTRA_SLN } else if AREA == "plaintain" { PLAINTAIN_SLN } else if AREA == "pivotask" { PIVOTASK_SLN } else { PUFFINOTE_SLN } }} --filter "FullyQualifiedName~{{ FILTER }}"
