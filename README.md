# Puffinote

A notes app for a folder of Markdown files. You open a folder, and Puffinote shows it as a tree, opens notes
in tabs, and formats them as you type. Your notes stay plain `.md` files that any other editor, sync tool or
git can use.

![Puffinote with a notebook open. The sidebar on the left shows the folders Journal, Projects and Reading and
the notes Inbox and Welcome, with "Garden plan" selected under Projects. Two tabs are open, Welcome and Garden
plan. The editor shows the Garden plan note formatted in place: a large heading, a paragraph with bold, italic
and highlighted words, a task list with one done (checked and struck through), one in progress and two open
tasks, a numbered list, a blockquote with a bar, a link, inline code, and a table. The status bar reads "Ln 3,
Col 119", "127 words", and the view switcher Rich, Formatted and Raw.](docs/images/notebook.webp)

It runs in the browser, on your NAS, or as a desktop app, all from the same code.

## What it does

- **Formats as you type.** Headings, emphasis, lists, quotes, tables and pictures show formatted. The
  Markdown markers stay hidden until the caret reaches them. Switch to **Formatted** to see every marker, or
  **Raw** for the plain source.
- **Tasks with three states.** `[ ]` open, `[/]` in progress, `[x]` done. A click on the box completes it,
  and ticked tasks move below the open ones. `⌥↑` and `⌥↓` move a list item, `⌥⌘↑` and `⌥⌘↓` to the top or
  bottom of its list.
- **Pictures in your notes.** Paste or drop a picture and Puffinote saves it next to your notes and links it.
  Click a picture to see it full size, zoom and pan; right-click it to rename, copy or delete it.
- **Finds things fast.** ⌘O opens any file by name, the sidebar searches every note, filters the tree by name,
  and outlines the open note. ⌘K runs any command.
- **Keeps up with the folder.** Changes made by other apps show up, and a note you are editing is never
  overwritten without asking. Notes save themselves a second after you stop typing.
- **Each notebook keeps its own settings.** Puffinote stores its state in a `.puffinote` folder inside the
  notebook, so the settings travel with the folder.
- **Works from the keyboard.** Every command has a menu entry, and the file tree works with the arrow keys.
- **A command-line tool.** `puffinote` prints a notebook's tree, searches it, and turns a note into HTML.

## Download

Get the desktop app from the [Releases page](https://github.com/gridlocdev/Puffinote/releases/latest):

| System                | File                        |
| --------------------- | --------------------------- |
| macOS (Apple silicon) | `Puffinote-…-mac-arm64.dmg` |
| macOS (Intel)         | `Puffinote-…-mac-x64.dmg`   |
| Windows               | `Puffinote-…-win-x64.exe`   |
| Linux                 | `Puffinote-….AppImage`      |

The apps aren't signed by Apple or Microsoft yet, so the first launch needs one extra step:

- **macOS** says Puffinote "can't be opened" or "is damaged". Open **System Settings > Privacy & Security**,
  scroll down, and click **Open Anyway** next to Puffinote. Or run this once after moving the app to
  Applications:

  ```bash
  xattr -dr com.apple.quarantine /Applications/Puffinote.app
  ```

- **Windows** shows "Windows protected your PC". Click **More info**, then **Run anyway**.
- **Linux**: make the AppImage executable (`chmod +x Puffinote-*.AppImage`), then run it.

The desktop app checks for a new version once a day and tells you when one is out; **Download** opens the
release page. It never installs anything by itself. You can turn the check off in the dialog, or run it from
**Check for Updates…** in the app menu (Help on Windows and Linux).

## Run it on a NAS (Docker)

```bash
docker run -d -p 8790:8790 -v /volume1/notes:/data ghcr.io/gridlocdev/puffinote:latest
```

Then open `http://<nas>:8790`. To expose several folders, add a password, or run as your NAS user, see
[compose.example.yaml](compose.example.yaml).

| Variable           | Default       | Meaning                                                           |
| ------------------ | ------------- | ----------------------------------------------------------------- |
| `PUFFINOTE_ROOTS`  | `Notes=/data` | The folders to show, as `Name=/path;Other=/path2`                 |
| `PUFFINOTE_TOKEN`  | unset         | If set, the browser asks for this token before it shows any files |
| `PUFFINOTE_HOSTS`  | unset         | Without a token, the extra host names to answer (comma-separated) |
| `PORT`             | `8790`        | The port to listen on                                             |
| `PUFFINOTE_MAX_MB` | `64`          | The largest file you can save, in MB                              |

Without `PUFFINOTE_TOKEN`, the server only answers requests addressed to an IP address or `localhost`. If you
open it by name (for example `http://nas.lan:8790`), list that name in `PUFFINOTE_HOSTS=nas.lan`. Other names
get `421 Misdirected Request`, which stops a hostile web page from reaching your notes through DNS rebinding.

If someone else saved a note after you opened it, Puffinote warns you before overwriting their changes.

The image runs on `amd64` and `arm64` NAS boxes. To build it yourself instead, run
`docker build -t puffinote .` in a clone of this repository.

## Build it from source

You need Node 22.18 or later, pnpm (`corepack enable`) and [just](https://github.com/casey/just):

```bash
just setup            # install the dependencies
just run-desktop      # run it in dev mode
just package-desktop  # build a .dmg / AppImage / installer in apps/desktop/release
just install-desktop  # macOS: package and install into /Applications
```

## Keyboard shortcuts

|                              | macOS              | Windows and Linux               |
| ---------------------------- | ------------------ | ------------------------------- |
| Command menu                 | `⌘K`               | `Ctrl+K`                        |
| Go to file                   | `⌘O`               | `Ctrl+O`                        |
| New note / new folder        | `⌘N` / `⇧⌘N`       | `Ctrl+N` / `Ctrl+Shift+N`       |
| Save                         | `⌘S`               | `Ctrl+S`                        |
| Close tab                    | `⌘W`               | `Ctrl+W`                        |
| Next / previous tab          | `⇧⌘]` / `⇧⌘[`      | `Ctrl+Shift+]` / `Ctrl+Shift+[` |
| Show or hide the sidebar     | `⌘\`               | `Ctrl+\`                        |
| Focus the file tree / editor | `⌥⌘S` / `⌥⌘E`      | `Ctrl+Alt+S` / `Ctrl+Alt+E`     |
| Settings                     | `⌘,`               | `Ctrl+,`                        |
| Bold / italic / inline code  | `⌘B` / `⌘I` / `⌘E` | `Ctrl+B` / `Ctrl+I` / `Ctrl+E`  |
| Heading 1–6 / paragraph      | `⌘1`–`⌘6` / `⌘0`   | `Ctrl+1`–`Ctrl+6` / `Ctrl+0`    |
| Toggle a checkbox            | `⌘L`               | `Ctrl+L`                        |
| Move a line or list item     | `⌥↑` / `⌥↓`        | `Alt+↑` / `Alt+↓`               |
| Move to top / bottom of list | `⌥⌘↑` / `⌥⌘↓`      | `Ctrl+Alt+↑` / `Ctrl+Alt+↓`     |
| Open a link                  | `⌘`-click          | `Ctrl`-click                    |
| Preview a picture            | click it           | click it                        |

In the picture preview, pinch or `⌘`-scroll zooms, scrolling or dragging pans, `0` fits the picture to the
window, `1` shows it at actual size, and `Esc` closes it. ⌘K lists every other command with its shortcut.

## Documentation

**[Read the product spec](docs/PRD.md)**, or jump to a question:

- [Where does Puffinote keep its own files, and what happens when a note changes outside the app?](docs/PRD.md#5-the-notebook)
- [How does the live preview decide what to hide?](docs/PRD.md#102-live-preview)
- [Which settings are there, and how do notebook settings work?](docs/PRD.md#9-settings)
- [What can the `puffinote` command do?](docs/PRD.md#12-the-cli)

## For developers

The [developer guide](docs/Developer-Guide.md) covers setup, every `just` recipe, the checks, the CLI and how
to package the apps. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains how the code is laid out.

## License

[MIT](LICENSE)
