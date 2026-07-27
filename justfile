# Puffinote monorepo — the task runner (https://just.systems)
#
# There is no root solution. Each area builds, tests, and ships on its own:
#   app/                        Puffinote — composite shell (Desktop + CLI)
#   features/EditMarkdown/      Typage    — Markdown editor
#   features/EditSpreadsheets/  Cellestra — spreadsheet editor
#
# Common flows:
#   just                  list every recipe (grouped)
#   just build            build all three areas
#   just test             test all three areas
#   just format           auto-format all three areas
#   just check            format-verify + build + test all areas (CI gate / definition of done)
#   just typage-test      one area's suite (also: puffinote-*, cellestra-*)
#   just test-filter SomeTest typage    run a subset while iterating
#   just publish          self-contained Release builds of all three apps into artifacts/
#   just install          put the three CLIs (puffinote, typage, cellestra) on PATH
#   just publish-app      macOS .app bundles for all three apps (macOS only)
#   just install-app      ...and copy them into /Applications
#
# Every install has an inverse that undoes exactly what it did, and nothing else:
#   just install ↔ just uninstall            the CLIs in PREFIX/bin
#   just install-app ↔ just uninstall-app    the .app bundles in APPS_DIR (also withdraws their
#                                            Launch Services file-type claims)
# Neither uninstall touches PREFIX/bin itself (it predates us and is on your PATH) or an app's
# user data in ~/Library/Application Support — removing a tool is not consent to delete documents,
# settings, or unsaved-work snapshots.
#
# Recipe dependencies run in the order written, one at a time — which is required here, because
# concurrent `dotnet build`/`dotnet test` invocations deadlock on the MSBuild-node locks.
#
# Warnings are errors here (TreatWarningsAsErrors=true), so "build" means "no warnings".

# ---- Solutions ----------------------------------------------------------------
PUFFINOTE_SLN := "app/Puffinote.slnx"
TYPAGE_SLN    := "features/EditMarkdown/Typage.slnx"
CELLESTRA_SLN := "features/EditSpreadsheets/Cellestra.slnx"

# ---- CLI entry projects (run headless in the Linux devcontainer) --------------
PUFFINOTE_CLI := "app/Puffinote.Cli"
TYPAGE_CLI    := "features/EditMarkdown/Typage.Cli"
CELLESTRA_CLI := "features/EditSpreadsheets/Cellestra.Cli"

# ---- Desktop (GUI) entry projects (run on a native host, not the container) ---
PUFFINOTE_DESKTOP := "app/Puffinote.Desktop"
TYPAGE_DESKTOP    := "features/EditMarkdown/Typage.Desktop"
CELLESTRA_DESKTOP := "features/EditSpreadsheets/Cellestra.Desktop"

DOTNET := "dotnet"

# ---- Packaging: where publish writes, and where install puts things -----------
# Override any of these on the command line, e.g.
#   just RID=osx-x64 CONFIG=Debug cellestra-publish
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
restore: puffinote-restore typage-restore cellestra-restore

# Build every area (warnings are errors)
[group('all areas')]
build: puffinote-build typage-build cellestra-build

# Test every area
[group('all areas')]
test: puffinote-test typage-test cellestra-test

# Auto-format every area
[group('all areas')]
format: puffinote-format typage-format cellestra-format

# Verify formatting for every area (no changes)
[group('all areas')]
format-check: puffinote-format-check typage-format-check cellestra-format-check

# Remove build artifacts for every area
[group('all areas')]
clean: puffinote-clean typage-clean cellestra-clean

# Definition of done: formatting verified, all areas build clean and green
[group('all areas')]
check: format-check build test

# Publish every app (CLI + desktop, self-contained) to artifacts/
[group('all areas')]
publish: puffinote-publish typage-publish cellestra-publish

# Install every CLI into PREFIX/bin (default ~/.local/bin)
[group('all areas')]
install: puffinote-install typage-install cellestra-install

# Remove every installed CLI from PREFIX/bin
[group('all areas')]
uninstall: puffinote-uninstall typage-uninstall cellestra-uninstall

# Build every macOS .app bundle (macOS only)
[group('all areas')]
publish-app: puffinote-publish-app typage-publish-app cellestra-publish-app

# Install every macOS .app into APPS_DIR (macOS only)
[group('all areas')]
install-app: puffinote-install-app typage-install-app cellestra-install-app

# Remove every macOS .app from APPS_DIR (the inverse of install-app; macOS only)
[group('all areas')]
uninstall-app: puffinote-uninstall-app typage-uninstall-app cellestra-uninstall-app

# ==============================================================================
# Puffinote — the composite shell (app/)
# ==============================================================================

# Restore Puffinote packages
[group('puffinote')]
puffinote-restore:
    {{ DOTNET }} restore {{ PUFFINOTE_SLN }}

# Build the Puffinote shell
[group('puffinote')]
puffinote-build:
    {{ DOTNET }} build {{ PUFFINOTE_SLN }}

# Test the Puffinote shell
[group('puffinote')]
puffinote-test:
    {{ DOTNET }} test {{ PUFFINOTE_SLN }}

# Auto-format the Puffinote shell
[group('puffinote')]
puffinote-format:
    {{ DOTNET }} format {{ PUFFINOTE_SLN }}

# Verify Puffinote formatting (fails if unformatted)
[group('puffinote')]
puffinote-format-check:
    {{ DOTNET }} format {{ PUFFINOTE_SLN }} --verify-no-changes

# Clean Puffinote build artifacts
[group('puffinote')]
puffinote-clean:
    {{ DOTNET }} clean {{ PUFFINOTE_SLN }}

# Run the Puffinote desktop shell (native host — GUI won't launch in the container)
[group('puffinote')]
puffinote-run:
    {{ DOTNET }} run --project {{ PUFFINOTE_DESKTOP }}

# Run the Puffinote CLI: just puffinote-run-cli info | search "todo"
[group('puffinote')]
puffinote-run-cli *ARGS:
    {{ DOTNET }} run --project {{ PUFFINOTE_CLI }} -- {{ ARGS }}

# Publish the Puffinote CLI + shell to artifacts/puffinote/<rid>/
[group('puffinote')]
puffinote-publish: puffinote-publish-cli puffinote-publish-desktop

# Publish the `puffinote` CLI (single self-contained file)
[group('puffinote')]
puffinote-publish-cli:
    {{ DOTNET }} publish {{ PUFFINOTE_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ PUFFINOTE_OUT }}/cli

# Publish the Puffinote desktop shell (self-contained)
[group('puffinote')]
puffinote-publish-desktop:
    {{ DOTNET }} publish {{ PUFFINOTE_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ PUFFINOTE_OUT }}/desktop

# Build Puffinote.app (macOS only)
[group('puffinote')]
puffinote-publish-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ PUFFINOTE_OUT }} {{ PACKAGE_MAC }} puffinote

# Install the `puffinote` CLI into PREFIX/bin (default ~/.local/bin)
[group('puffinote')]
puffinote-install: puffinote-publish-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ PUFFINOTE_OUT }}/cli/puffinote "{{ BINDIR }}/puffinote"
    @echo "installed {{ BINDIR }}/puffinote — make sure {{ BINDIR }} is on your PATH"

# Install Puffinote.app into APPS_DIR (default /Applications, macOS only)
[group('puffinote')]
puffinote-install-app: puffinote-publish-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Puffinote.app"
    cp -R {{ PUFFINOTE_OUT }}/Puffinote.app "{{ APPS_DIR }}/Puffinote.app"
    @echo "installed {{ APPS_DIR }}/Puffinote.app"

# Remove the installed `puffinote` CLI from PREFIX/bin
[group('puffinote')]
puffinote-uninstall:
    rm -f "{{ BINDIR }}/puffinote"
    @echo "removed {{ BINDIR }}/puffinote"

# Remove Puffinote.app from APPS_DIR (the inverse of puffinote-install-app; macOS only)
[group('puffinote')]
puffinote-uninstall-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Puffinote.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Puffinote.app"
    @echo "removed {{ APPS_DIR }}/Puffinote.app"

# ==============================================================================
# Typage — the Markdown editor (features/EditMarkdown/)
# ==============================================================================

# Restore Typage packages
[group('typage')]
typage-restore:
    {{ DOTNET }} restore {{ TYPAGE_SLN }}

# Build the Typage editor
[group('typage')]
typage-build:
    {{ DOTNET }} build {{ TYPAGE_SLN }}

# Test the Typage editor
[group('typage')]
typage-test:
    {{ DOTNET }} test {{ TYPAGE_SLN }}

# Auto-format the Typage editor
[group('typage')]
typage-format:
    {{ DOTNET }} format {{ TYPAGE_SLN }}

# Verify Typage formatting (fails if unformatted)
[group('typage')]
typage-format-check:
    {{ DOTNET }} format {{ TYPAGE_SLN }} --verify-no-changes

# Clean Typage build artifacts
[group('typage')]
typage-clean:
    {{ DOTNET }} clean {{ TYPAGE_SLN }}

# Run the Typage desktop editor (native host — GUI won't launch in the container)
[group('typage')]
typage-run:
    {{ DOTNET }} run --project {{ TYPAGE_DESKTOP }}

# Run the Typage CLI: just typage-run-cli html notes.md -o notes.html
[group('typage')]
typage-run-cli *ARGS:
    {{ DOTNET }} run --project {{ TYPAGE_CLI }} -- {{ ARGS }}

# Publish the Typage CLI + editor to artifacts/typage/<rid>/
[group('typage')]
typage-publish: typage-publish-cli typage-publish-desktop

# Publish the `typage` CLI (single self-contained file)
[group('typage')]
typage-publish-cli:
    {{ DOTNET }} publish {{ TYPAGE_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ TYPAGE_OUT }}/cli

# Publish the Typage desktop editor (self-contained)
[group('typage')]
typage-publish-desktop:
    {{ DOTNET }} publish {{ TYPAGE_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ TYPAGE_OUT }}/desktop

# Build Typage.app (macOS only)
[group('typage')]
typage-publish-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ TYPAGE_OUT }} {{ PACKAGE_MAC }} typage

# Install the `typage` CLI into PREFIX/bin (default ~/.local/bin)
[group('typage')]
typage-install: typage-publish-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ TYPAGE_OUT }}/cli/typage "{{ BINDIR }}/typage"
    @echo "installed {{ BINDIR }}/typage — make sure {{ BINDIR }} is on your PATH"

# Install Typage.app into APPS_DIR (default /Applications, macOS only)
[group('typage')]
typage-install-app: typage-publish-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Typage.app"
    cp -R {{ TYPAGE_OUT }}/Typage.app "{{ APPS_DIR }}/Typage.app"
    @echo "installed {{ APPS_DIR }}/Typage.app"

# Remove the installed `typage` CLI from PREFIX/bin
[group('typage')]
typage-uninstall:
    rm -f "{{ BINDIR }}/typage"
    @echo "removed {{ BINDIR }}/typage"

# Remove Typage.app from APPS_DIR (the inverse of typage-install-app; macOS only)
[group('typage')]
typage-uninstall-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Typage.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Typage.app"
    @echo "removed {{ APPS_DIR }}/Typage.app"

# ==============================================================================
# Cellestra — the spreadsheet editor (features/EditSpreadsheets/)
# ==============================================================================

# Restore Cellestra packages
[group('cellestra')]
cellestra-restore:
    {{ DOTNET }} restore {{ CELLESTRA_SLN }}

# Build the Cellestra editor
[group('cellestra')]
cellestra-build:
    {{ DOTNET }} build {{ CELLESTRA_SLN }}

# Test the Cellestra editor
[group('cellestra')]
cellestra-test:
    {{ DOTNET }} test {{ CELLESTRA_SLN }}

# Auto-format the Cellestra editor
[group('cellestra')]
cellestra-format:
    {{ DOTNET }} format {{ CELLESTRA_SLN }}

# Verify Cellestra formatting (fails if unformatted)
[group('cellestra')]
cellestra-format-check:
    {{ DOTNET }} format {{ CELLESTRA_SLN }} --verify-no-changes

# Clean Cellestra build artifacts
[group('cellestra')]
cellestra-clean:
    {{ DOTNET }} clean {{ CELLESTRA_SLN }}

# Run the Cellestra desktop editor (native host — GUI won't launch in the container)
[group('cellestra')]
cellestra-run:
    {{ DOTNET }} run --project {{ CELLESTRA_DESKTOP }}

# Run the Cellestra CLI: just cellestra-run-cli get book.ods A1
[group('cellestra')]
cellestra-run-cli *ARGS:
    {{ DOTNET }} run --project {{ CELLESTRA_CLI }} -- {{ ARGS }}

# Publish the Cellestra CLI + editor to artifacts/cellestra/<rid>/
[group('cellestra')]
cellestra-publish: cellestra-publish-cli cellestra-publish-desktop

# Publish the `cellestra` CLI (single self-contained file)
[group('cellestra')]
cellestra-publish-cli:
    {{ DOTNET }} publish {{ CELLESTRA_CLI }} {{ PUBLISH_CLI_FLAGS }} -o {{ CELLESTRA_OUT }}/cli

# Publish the Cellestra desktop editor (self-contained)
[group('cellestra')]
cellestra-publish-desktop:
    {{ DOTNET }} publish {{ CELLESTRA_DESKTOP }} {{ PUBLISH_DESKTOP_FLAGS }} -o {{ CELLESTRA_OUT }}/desktop

# Build Cellestra.app (macOS only)
[group('cellestra')]
cellestra-publish-app:
    CONFIG={{ CONFIG }} RID={{ RID }} OUT={{ CELLESTRA_OUT }} {{ PACKAGE_MAC }} cellestra

# Install the `cellestra` CLI into PREFIX/bin (default ~/.local/bin)
[group('cellestra')]
cellestra-install: cellestra-publish-cli
    @mkdir -p "{{ BINDIR }}"
    install -m 0755 {{ CELLESTRA_OUT }}/cli/cellestra "{{ BINDIR }}/cellestra"
    @echo "installed {{ BINDIR }}/cellestra — make sure {{ BINDIR }} is on your PATH"

# Install Cellestra.app into APPS_DIR (default /Applications, macOS only)
[group('cellestra')]
cellestra-install-app: cellestra-publish-app
    @mkdir -p "{{ APPS_DIR }}"
    rm -rf "{{ APPS_DIR }}/Cellestra.app"
    cp -R {{ CELLESTRA_OUT }}/Cellestra.app "{{ APPS_DIR }}/Cellestra.app"
    @echo "installed {{ APPS_DIR }}/Cellestra.app"

# Remove the installed `cellestra` CLI from PREFIX/bin
[group('cellestra')]
cellestra-uninstall:
    rm -f "{{ BINDIR }}/cellestra"
    @echo "removed {{ BINDIR }}/cellestra"

# Remove Cellestra.app from APPS_DIR (the inverse of cellestra-install-app; macOS only)
[group('cellestra')]
cellestra-uninstall-app:
    @{{ LSREGISTER }} -u "{{ APPS_DIR }}/Cellestra.app" >/dev/null 2>&1 || true
    rm -rf "{{ APPS_DIR }}/Cellestra.app"
    @echo "removed {{ APPS_DIR }}/Cellestra.app"

# ==============================================================================
# Iterating on a single test / subset (prefer this while developing)
# ==============================================================================

# Run a filtered subset: just test-filter SomeTest [puffinote|typage|cellestra]
[group('all areas')]
test-filter FILTER AREA="puffinote":
    {{ DOTNET }} test {{ if AREA == "typage" { TYPAGE_SLN } else if AREA == "cellestra" { CELLESTRA_SLN } else { PUFFINOTE_SLN } }} --filter "FullyQualifiedName~{{ FILTER }}"
