# Changelog

## 0.1.0

The first release. Puffinote is a notes app for a folder of Markdown files, on the desktop (macOS, Windows
and Linux), in the browser, and on a NAS.

- **A live-preview editor.** Headings, emphasis, lists, quotes, tables, links and pictures show formatted as
  you type, and the Markdown markers appear when the caret reaches them. **Formatted** and **Raw** views show
  more of the source.
- **Tasks with three states**: open, in progress and done. Ticked tasks move below the open ones. List items
  move with `⌥↑` and `⌥↓`, and to the top or bottom of their list with `⌥⌘↑` and `⌥⌘↓`.
- **Pictures.** Paste or drop a picture to save it in the notebook and link it. Click one to preview it with
  zoom and pan. Its menu renames, copies, opens or deletes it, and Puffinote offers to delete pictures no
  note uses any more.
- **Two layouts.** An icon rail with tabs, or a library of folders beside a list of notes.
- **Finding things.** Quick Open (`⌘O`), search across every note, a tree filter, an outline of the open
  note, and a command menu (`⌘K`) that lists every command with its shortcut.
- **Safe with your files.** Notes save a second after you stop typing, writes are atomic, and changes made by
  other apps show up without overwriting your edits. Quitting with unsaved changes asks first.
- **Settings per notebook**, kept in the notebook's `.puffinote` folder so they travel with it.
- **Export to HTML**, and a `puffinote` command-line tool that prints a notebook's tree, searches it, and
  turns a note into HTML.
- **A NAS server** in a Docker image (`ghcr.io/gridlocdev/puffinote`), with an optional access token and
  protection against DNS rebinding.
- **Update check.** The desktop app looks for a new release once a day and links to it. It never installs
  anything by itself.
