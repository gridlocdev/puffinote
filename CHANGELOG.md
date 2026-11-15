# Changelog

## Unreleased

### What's new

- **Pictures embedded as `![[name.png]]`** (as other Markdown apps paste them) are drawn as pictures, found by name wherever they are in the notebook. `![[name.png|300]]` draws one 300 px wide.

### Bugs fixed

- Pasting or dropping a picture with the caret at the very end of a note saves the picture and links it. Before, the file was saved but no link went into the note.

## 0.2.3

### Improvements

- **Headings inside quotes look like headings.** A line such as `> ## Note` is drawn at heading size in the editor, and the export drops its closing `#` marks as it does for other headings.
- **Moves in the desktop app are written to disk** before Puffinote reports them done, so a power cut right after a move doesn't lose it.
- **Right-click menus wait for open dialogs.** Choosing a menu item while a dialog is open no longer acts behind it.

### Bugs fixed

- **Edit > Replace** in the desktop menu turns on as soon as the Replace row opens, and the menu updates when you pin or unpin a tab.
- List items indented with tabs nest at the right depth, with each tab counting to the next tab stop.
- In the desktop app, a save checks again just before it writes that the note is still inside a folder you opened.

## 0.2.2

### Improvements

- **Typing is faster in long notes.** Near the top of a 20,000-line note, a keystroke takes about half a millisecond instead of six.
- **The desktop app picks up changes made on other machines.** It reads the notebook again when its window comes back to the front, so edits to a network folder from another computer show up.
- **Git no longer makes Puffinote re-read everything.** Changes inside `.git/` are ignored, and an open note is read again only when its file changed.
- **The Library layout fills in smoothly with thousands of notes.** Each card redraws only when its own details arrive, and pictures are no longer downloaded in full just to get their size.

### Bugs fixed

- Notes with Windows line endings keep them when you use **Code Block**, **Horizontal Rule**, **Delete Line**, move list items, or drop a picture.
- Shift+Delete cuts the selection on Windows and Linux, so it reaches the clipboard instead of being deleted.
- The Library filter shows every matching note, not just the first 500.
- A rename or move no longer gets undone by a slower refresh that finished after it.
- On the NAS server, a save that lost a conflict no longer brings back a folder someone deleted, and a save checks again that it stays inside the notebook folder just before it writes.

## 0.2.1

### Bugs fixed

- A pasted or dropped picture goes where you pasted it, even if you select something else while it saves, and a picture that can't be saved is said in the status bar.
- **Delete Picture** and **Edit Description…** act on the note that shows the picture. The picture preview closes when another note comes to the front.
- The desktop app no longer writes over a file another app saved in the same second.
- After you switch notebooks and choose **Don't Save**, nothing more is written to the old notebook.
- Dropping a note onto a folder that opened while you hovered over it moves the note into that folder.
- In the Library layout, commands act on the note in front.
- The **Settings** dialog saves only the settings you changed, so it doesn't undo changes made elsewhere while it was open.

## 0.2.0

### Before you update

- **The NAS server needs a token to listen on your network.** It used to accept connections from the whole network without one, so anyone on your LAN could read and change your notes. It now refuses to start that way and says how to fix it. Set `PUFFINOTE_TOKEN`, or set `PUFFINOTE_ALLOW_NO_TOKEN=1` if a reverse proxy in front of it already checks who you are.
- **The browser version no longer opens a folder on disk.** That only worked in Chrome and Edge. In every browser, Puffinote now keeps its notebook in the browser's own storage. To work on a folder on disk, use the desktop app.

### What's new

- **Find and Replace.** **Find in Note…** (`⌘F`) marks every match, counts them and steps through them with `⌘G` and `⇧⌘G`. `⌥⌘F` adds **Replace** and **Replace All**, and `⇧⌘F` searches every note.
- **Print or save as PDF** (`⌘P`). The desktop app saves the note as a PDF where you choose; a browser opens its print dialog. **Export as HTML** saves the note in front as a standalone page.
- **A Trash on the NAS and in the browser.** Deleted notes and folders go to the notebook's Trash, with **Undo**, **Show Trash** to restore them, and **Empty Trash**.
- **Recovery of unsaved edits.** While a note has unsaved changes, Puffinote keeps a copy of the text. If the app or the page crashes, a banner offers to restore it the next time the note opens.
- **Download Notebook** saves the browser notebook as a .zip, and **Import Notes…** brings in Markdown files or a whole folder. You can also drop files from the desktop onto the window.
- **Open a linked note with ⌘-click.**
- **The desktop app reopens its window** at the size and place you left it.
- **Starting is quicker.** The browser opens straight into its notebook, and a NAS server with one folder opens that folder.

### Improvements

- **Lists behave like a notes app.** Enter writes one line, and Enter on an empty nested item moves it out one level. Backspace removes a whole marker. Nested numbered lists count a, b, c and then i, ii, iii, and nested bullets change shape by depth.
- **More ways to move the caret:** by visual row, by screen, and with the macOS Emacs keys (`⌃A`, `⌃E`, `⌃F`, `⌃B`, `⌃N`, `⌃P`).
- **Better with screen readers.** Task checkboxes have names, the caret's line is announced, and Escape then Tab leaves the editor.
- **Faster typing in long notes.** The word count, the Outline and find marks update less often and do less work.
- **Larger folders list faster** on the NAS server.
- **The browser asks to keep its notebook**, so it isn't cleared when the disk runs low.

### Bugs fixed

- Press-and-hold accents on macOS replace the held letter instead of adding a second one.
- Keys typed during an IME composition, or with AltGr, no longer run commands, and shortcuts with punctuation match the character your keyboard layout types.
- Undo takes back a run of Backspace or Delete, or a held key, in one step.
- Cut that joins list lines renumbers the list, and indenting list items inside a quote works.
- Enter and paste keep the note's own line endings.
- `⌃Tab` and `⌃⇧Tab` switch tabs on macOS, and `Ctrl+W` closes a tab on Windows and Linux.
- A click lands between whole characters, and a drag-selection ends when you let go of the button.
- Saving never writes over a file another app changed, never empties a hard-linked file, and refuses to replace a read-only one.
- A save and a rename or move of the same note no longer get in each other's way, and closing or quitting says why a note that changed on disk wasn't saved.
- Deleting a note or folder says what it loses before asking.
- Puffinote never calls a picture unused when it's shown with a wiki embed or an `<img>` tag, or when a note couldn't be checked.
- HTML export keeps table alignment, nested list items, indented code, quoted headings and paragraphs, and no longer hangs on lines of only spaces.
- Headings with closing `#`s, setext headings in the Outline, and bare URLs with parentheses show correctly.
- A file whose bytes don't match its byte-order mark opens read-only instead of being garbled.
- The desktop app reloads its window if the page crashes, and **Check for Updates…** answers while the daily check runs.
- Search shows when it fails, and doesn't show results from an earlier search.
- A NAS folder with links that loop back up no longer lists forever.

## 0.1.0

The first release. Puffinote is a notes app for a folder of Markdown files, on the desktop (macOS, Windows and Linux), in the browser, and on a NAS.

- **A live-preview editor.** Headings, emphasis, lists, quotes, tables, links and pictures show formatted as you type, and the Markdown markers appear when the caret reaches them. **Formatted** and **Raw** views show more of the source.
- **Tasks with three states**: open, in progress and done. Ticked tasks move below the open ones. List items move with `⌥↑` and `⌥↓`, and to the top or bottom of their list with `⌥⌘↑` and `⌥⌘↓`.
- **Pictures.** Paste or drop a picture to save it in the notebook and link it. Click one to preview it with zoom and pan. Its menu renames, copies, opens or deletes it, and Puffinote offers to delete pictures no note uses any more.
- **Two layouts.** An icon rail with tabs, or a library of folders beside a list of notes.
- **Finding things.** Quick Open (`⌘O`), search across every note, a tree filter, an outline of the open note, and a command menu (`⌘K`) that lists every command with its shortcut.
- **Safe with your files.** Notes save a second after you stop typing, writes are atomic, and changes made by other apps show up without overwriting your edits. Quitting with unsaved changes asks first.
- **Settings per notebook**, kept in the notebook's `.puffinote` folder so they travel with it.
- **Export to HTML**, and a `puffinote` command-line tool that prints a notebook's tree, searches it, and turns a note into HTML.
- **A NAS server** in a Docker image you build from the repository, with an optional access token and protection against DNS rebinding.
- **Update check.** The desktop app looks for a new release once a day and links to it. It never installs anything by itself.
