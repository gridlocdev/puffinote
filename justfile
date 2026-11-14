# Puffinote — run `just` to list recipes.
set shell := ["bash", "-cu"]

app_name := "Puffinote"
nas_data := justfile_directory() / ".nas-data"
release := "apps/desktop/release"

default:
    @just --list --unsorted

# ── Run (dev) ─────────────────────────────────────────────────────────────

# The web app as gridloc.dev serves it: no server, notes in a picked folder or the browser (http://localhost:5173).
[group('run')]
run-web:
    pnpm --filter @puffinote/web dev

# The web app on a NAS: the dev client (http://localhost:5173) plus the NAS server serving ./.nas-data as
# "Sample". Ctrl-C stops both.
[group('run')]
run-nas:
    mkdir -p "{{ nas_data }}"
    pnpm --parallel --filter @puffinote/server --filter @puffinote/web run dev

# NAS server only (http://localhost:8790), serving ./.nas-data; restarts when its source changes.
[group('run')]
run-server:
    mkdir -p "{{ nas_data }}"
    pnpm --filter @puffinote/server dev

# Desktop app in dev mode (builds the web client first).
[group('run')]
run-desktop: build-web
    pnpm --filter @puffinote/desktop start

# The built web app as gridloc.dev serves it: static files, no server (http://localhost:4173).
[group('run')]
preview-web: build-web
    pnpm --filter @puffinote/web exec vite preview --strictPort

# The built web app served by the NAS server, as it runs on a NAS (http://localhost:8790).
[group('run')]
preview-nas: build-web
    mkdir -p "{{ nas_data }}"
    PUFFINOTE_HOST=127.0.0.1 PUFFINOTE_ROOTS="Sample={{ nas_data }}" PUFFINOTE_STATIC="{{ justfile_directory() }}/apps/web/dist" node apps/server/src/main.ts

# The live-preview editor on its own, with no shell around it (http://localhost:5199).
[group('run')]
run-editor-demo:
    pnpm --filter @puffinote/ui demo

# Run the puffinote CLI from source (paths resolve from where you run it; quoting is kept).
[group('run')]
[no-cd]
[positional-arguments]
cli *args:
    node "{{ justfile_directory() }}/apps/cli/src/main.ts" "$@"

# ── Build and package ─────────────────────────────────────────────────────

# Build everything: the web client and the desktop app.
[group('build')]
build:
    pnpm build

# Build the web client into apps/web/dist (one build serves the browser, the NAS and the desktop).
[group('build')]
build-web:
    pnpm --filter @puffinote/web build

# Build the desktop app's main process and copy the web client into apps/desktop/out.
[group('build')]
build-desktop: build-web
    pnpm --filter @puffinote/desktop build

# Package the desktop app for this OS into apps/desktop/release (.dmg and .zip on macOS).
[group('build')]
package-desktop: build-web
    pnpm --filter @puffinote/desktop package

# Package an unpacked app only (faster; for local testing).
[group('build')]
package-desktop-dir: build-web
    pnpm --filter @puffinote/desktop package --dir

# Package and install into /Applications (macOS). The old copy goes to the Trash.
[group('build')]
[macos]
install-desktop: package-desktop-dir
    #!/usr/bin/env bash
    set -euo pipefail
    target="/Applications/{{ app_name }}.app"
    built=$(ls -d "{{ release }}"/mac*/"{{ app_name }}.app" | head -1)
    # Anchored, so a shell command that merely mentions the path does not count.
    if pgrep -f "^$target/Contents/MacOS/" >/dev/null; then
      echo "{{ app_name }} is running — quit it first, then run this again." >&2
      exit 1
    fi
    trashed=""
    if [ -d "$target" ]; then
      trashed="$HOME/.Trash/{{ app_name }} $(date '+%Y-%m-%d %H.%M.%S').app"
      mv "$target" "$trashed"
      echo "Moved the old app to the Trash."
    fi
    ditto "$built" "$target"
    lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
    # Only the installed copy should answer `open -a Puffinote` (what the CLI runs).
    "$lsregister" -u "$PWD/$built" 2>/dev/null || true
    if [ -n "$trashed" ]; then "$lsregister" -u "$trashed" 2>/dev/null || true; fi
    "$lsregister" -f "$target"
    echo "Installed $target"

# Render the app icon (apps/desktop/assets/icon.svg) into apps/desktop/build. Needs Inkscape; commit the results.
[group('build')]
icons:
    apps/desktop/scripts/make-icons.sh

# Build the NAS Docker image.
[group('build')]
build-docker tag="puffinote:latest":
    docker build -t {{ tag }} .

# Run the NAS Docker image against ./.nas-data (http://localhost:8790, this machine only, no token).
[group('build')]
run-docker tag="puffinote:latest":
    mkdir -p "{{ nas_data }}"
    docker run --rm -p 127.0.0.1:8790:8790 -e PUFFINOTE_ALLOW_NO_TOKEN=1 -v "{{ nas_data }}:/data" {{ tag }}

# ── Quality ───────────────────────────────────────────────────────────────

# Everything CI checks: format, lint, typecheck, tests, build.
[group('quality')]
check:
    pnpm check

# Run the tests once (pass a file or -t "name" to narrow them).
[group('quality')]
test *args:
    pnpm vitest run {{ args }}

# Run the tests in watch mode.
[group('quality')]
test-watch *args:
    pnpm vitest {{ args }}

# Lint with ESLint.
[group('quality')]
lint:
    pnpm lint

# Typecheck every package.
[group('quality')]
typecheck:
    pnpm typecheck

# Format every file.
[group('quality')]
fmt:
    pnpm format

# Check formatting without changing anything.
[group('quality')]
fmt-check:
    pnpm format:check

# ── Housekeeping ──────────────────────────────────────────────────────────

# Install dependencies (pnpm via corepack).
[group('housekeeping')]
setup:
    corepack enable
    pnpm install

# Remove build output.
[group('housekeeping')]
clean:
    rm -rf packages/*/dist apps/web/dist apps/desktop/out "{{ release }}" apps/desktop/node_modules/.cache/puffinote-dev
    find . -name '*.tsbuildinfo' -not -path '*/node_modules/*' -delete

# Remove build output and every node_modules (run `just setup` after).
[group('housekeeping')]
clean-all: clean
    find . -name node_modules -type d -prune -exec rm -rf {} +

alias web := run-web
alias nas := run-nas
alias server := run-server
alias desktop := run-desktop
alias package := package-desktop
alias docker := build-docker
