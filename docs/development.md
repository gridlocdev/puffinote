# Development

Puffinote is TypeScript in a pnpm workspace: Preact and `@preact/signals` for the UI, Vite for the web build, Electron for the desktop app, and a dependency-free Node server and CLI that run straight from source with Node's type stripping. The rules in `AGENTS.md` at the repository root apply to everyone working on it, and the code layout and its layer rules are in [architecture.md](architecture.md).

## Getting set up

You need Node 22.18 or later, pnpm (through `corepack enable`) and [just](https://github.com/casey/just). The devcontainer in `.devcontainer/` has all three.

```bash
just setup                # corepack enable, then pnpm install
just run-web              # the web app as gridloc.dev serves it, on http://localhost:5173 (no server)
just run-nas              # the web app on a NAS: the client on :5173 plus the NAS server on :8790
just run-server           # the NAS server only, serving ./.nas-data, restarting on changes
just run-desktop          # the desktop app in dev mode (builds the web client first)
just preview-web          # the built client as static files, as on gridloc.dev (:4173)
just preview-nas          # the built client served by the NAS server, as on a NAS (:8790)
just run-editor-demo      # the editor alone, with no shell, on http://localhost:5199
just cli tree ~/Notes     # the puffinote CLI from source (any arguments)
```

- The web client runs in three modes, chosen at runtime: in the desktop app, served with a NAS server (`run-nas`, `preview-nas`), or as a plain web page like gridloc.dev (`run-web`, `preview-web`), which keeps notes in the browser's private storage.
- `just run-nas`, `just preview-nas` and `just run-server` serve `.nas-data/` in the repository as the root `Sample`. The folder is gitignored.
- `just cli` runs from the folder you are in, so relative paths work as they would with an installed `puffinote`.

## Checking your work

```bash
just check            # everything: Prettier, ESLint, tsc -b, Vitest, then the build
just test             # the tests once; narrow with a file or -t "name"
just test-watch       # the tests in watch mode
just lint             # ESLint
just typecheck        # tsc -b over every package
just fmt              # format every file
just fmt-check        # check the formatting
just check-arch       # the architecture rules alone (part of just check)
just fuzz 5000        # the random-operation tests for 5000 seeds instead of 20
```

`just check` must pass before every commit. It includes `test/architecture.test.ts`, which checks that imports point down the layers and go through each package's entry point, that the engine stays pure, that only the store adapters fetch and store data, that keys go through the command catalog and timers through `packages/ui/src/schedule.ts`, that no error is dropped silently and that no source file passes 600 lines. Each failure says what to do instead. The violations that predate the rules are counted in `test/architecture-baseline.json`, which only shrinks: lower or delete an entry when you fix one, and never add to it. Deliberate exceptions (an `eslint-disable`, a `@ts-expect-error`, an `ignoreError(promise, 'why')`) are listed in `test/architecture-exceptions.json`, and a new one needs the owner's agreement. ESLint also runs typed rules on source files: no floating or misused promises (a promise nobody awaits ends in `.catch` with something that shows or logs the failure, such as `nb.report`, never `void`), and every switch over a union names each case. When you change behaviour, add or update a test in that package's `test/` folder. A change to how a notebook store reads, writes, moves or removes files must keep `packages/ui/test/storeContract.test.ts` passing; it runs the same cases against the memory, browser, NAS and desktop stores (the desktop one through a stand-in for the Electron bridge, `fakeNativeBridge.ts`). Keep that stand-in in step with the desktop main process when the bridge changes.

Random-operation tests (each named "random …") run seeded sequences of edits, undo, redo, saves and reloads and check the invariants after every step: the text buffer against a plain string (`packages/text/test/randomEdits.test.ts`), the incremental Markdown model against a full rebuild (`packages/markdown/test/randomModel.test.ts`), and saving open notes with autosave, refreshes, outside changes and slow or failing disk calls let through in any order (`packages/ui/test/saveRandom.test.ts`), checking that no save is lost and no tab goes back to an older file. `just check` runs the same 20 seeds every time, and `just fuzz` tries more. A failure prints `FUZZ_SEED=<n>` and the last steps; `FUZZ_SEED=<n> just test -t random` replays it with every step. Before blaming the code, check that the test's own model isn't wrong. Fix the bug and keep it fixed with a plain test that fails without the fix.

Check UI changes headlessly: run `just run-web` or `just run-nas` (or a `preview-` recipe) and drive it in a browser that isn't in your way. The desktop app renders offscreen with `PUFFINOTE_HEADLESS=1`.

## The spec and its references

The product spec is [plans/prd.md](plans/prd.md). The code cites it as `PRD §n` in comments next to the behaviour a section describes. When you change behaviour, change the spec in the same commit. If you renumber a section, find its references with `grep -rn 'PRD §' packages apps`.

Some questions the spec answers:

- [Where does Puffinote keep its own files, and what happens when a note changes outside the app?](plans/prd.md#5-the-notebook)
- [How does the live preview decide what to hide?](plans/prd.md#102-live-preview)
- [Which settings are there, and how do notebook settings work?](plans/prd.md#9-settings)
- [Where do pasted pictures go, and what happens when a note stops using one?](plans/prd.md#106-pasted-and-dropped-pictures)

## The puffinote CLI

`puffinote` reads notebooks and notes with the same engine as the app, for scripts and CI. It runs from source:

```bash
just cli --help                # every command and option
just cli tree ~/Notes          # the notebook's file tree, folders first
just cli search puffin ~/Notes # search every note; prints path, hit count and a snippet
just cli info ~/Notes          # file counts and total size
just cli html Note.md          # render a note to self-contained HTML
just cli cat Note.md           # the Markdown source, decoded the way the editor reads it
just cli stats Note.md         # word, character and line counts, and reading time
just cli outline Note.md       # the note's headings as an indented outline
just cli tokens Note.md        # the editor's token stream, for debugging the tokenizer
just cli ~/Notes               # open the folder in the desktop app
```

`just cli` runs from the folder you are in, so relative paths work as they would with an installed `puffinote`. The commands are specified in PRD §12.

## Adding a command

1. Add it to the catalog: a shell command in `packages/ui/src/shell/shellModel.ts`, or an editor command in `packages/ui/src/editor/editorCommands.ts`. Give it an id (`shell.`, `view.`, `edit.`, `format.` or `insert.`), a title, a group, and a shortcut if it needs one.
2. If the shortcut belongs to the shell, check it against the reserved set (PRD §8.2).
3. The command menu, the shortcut and the desktop menu bar pick it up from the catalog. Nothing else needs registering.
4. Add a test for its behaviour.

## Adding a Markdown construct

1. Add the token types to `packages/markdown/src/markdown/token.ts`.
2. Recognize it in `lineTokenizer.ts` (a block or line prefix) or `inlineTokenizer.ts` (inline). If it spans lines, add its state to `blockState.ts`.
3. Render it: styling in `packages/ui/src/editor/lineRender.ts` and `styles.css`, reveal rules in `reveal.ts`, and HTML in `packages/markdown/src/html/htmlRenderer.ts`.
4. Add tests next to the existing ones in `packages/markdown/test/`, and the construct to PRD §11.3.

## The NAS server

```bash
just build-docker     # build the image (puffinote:latest)
just run-docker       # run it against ./.nas-data on http://localhost:8790
```

The server is configured only through environment variables, listed at the top of `apps/server/src/main.ts` and in [architecture.md](architecture.md#server-appsserver). `compose.example.yaml` is the starting point for a NAS.

## Building and packaging the desktop app

```bash
just build                # the web client and the desktop main process
just build-desktop        # the desktop app only, into apps/desktop/out
just package-desktop      # a .dmg and .zip (macOS), AppImage (Linux) or installer (Windows)
just package-desktop-dir  # an unpacked app only, faster for local testing
just install-desktop      # macOS: package and install into /Applications
```

- The package is signed ad hoc so it runs on Apple silicon. Swap the identity in `apps/desktop/electron-builder.yml` for a Developer ID to distribute it.
- `just install-desktop` moves an existing copy to the Trash first, and refuses while the app is running. It registers the installed copy with Launch Services, so `puffinote <folder>` opens it.

### The app icon

The icon is drawn by hand in `apps/desktop/assets/icon.svg`: a 1024 canvas on Apple's grid, with the 824-unit tile at (100, 100). After editing it, render the platform files and commit them, so builds don't need Inkscape:

```bash
just icons  # icon.icns (macOS), icon.ico (Windows) and icon.png (Linux window) in apps/desktop/build
```

It needs Inkscape (`brew install --cask inkscape`) and, for the `.icns`, macOS. If the Dock still shows the old icon after a rebuild, that is the macOS icon cache: move the app, or log out and back in.

## Releasing

Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`. It packages the desktop app on macOS (Apple silicon and Intel), Windows and Linux, and publishes them on a GitHub release. The NAS image isn't published: people build it from the repository ([Getting started](getting-started.md#run-it-on-a-nas-docker)).

1. Set the same version in every `package.json` (the root, `apps/*` and `packages/*`). The workflow stops if the tag doesn't match `apps/desktop/package.json`.
2. Add a `## X.Y.Z` section to `CHANGELOG.md`. It becomes the release notes.
3. Run `just check`, commit, then tag and push:

   ```bash
   git tag -a vX.Y.Z -m "Puffinote X.Y.Z"
   git push origin main vX.Y.Z
   ```

- The apps aren't notarized or code-signed, so [Getting started](getting-started.md#download) tells people how to open them the first time. The desktop app's update check (PRD §3.1) reads the latest release from GitHub's API, so a published release reaches existing installs within a day.
- `.github/workflows/check.yml` runs `pnpm check` on Linux, macOS and Windows for every pull request. It does not run on push: run it by hand from the repository's **Actions** tab, or with `gh workflow run check.yml`. `.github/workflows/docker.yml` builds the Docker image and checks that it starts; it also runs only by hand (`gh workflow run docker.yml`).

## Cleaning up

```bash
just clean        # remove build output
just clean-all    # also remove every node_modules (run just setup after)
```
