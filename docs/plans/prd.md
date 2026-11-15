# PRD — Puffinote

A Markdown notes app. A notebook is a folder of plain `.md` files. Puffinote shows the folder as a tree, opens notes in tabs, and edits them in a live-preview editor. The files stay ordinary text that any other tool can read, diff and sync.

The code cites this document as `PRD §n`. If you renumber a section, update those references too (`grep -rn 'PRD §' packages apps`).

---

## 1. Summary

- **One notebook, one window,** in one of two layouts (§6): an **icon rail** with a floating panel (Files, Filter, Search, Outline and Recent) and tabbed notes, or a **library** of folders beside a list of notes, one note at a time. Both have the note in front and a status bar.
- **The editor is built for Markdown.** It is a live preview: formatting shows in place, and the Markdown markers stay hidden until the caret reaches them. Saving writes exactly the text in the buffer.
- **Three hosts, one client:**
  - **Web:** a static page that opens a folder from disk, or the browser's private storage.
  - **NAS:** the same page, served by a small Node server that exposes named folders.
  - **Desktop:** an Electron app that opens folders from disk.
- **A CLI**, `puffinote`, reads notebooks and notes for scripts and CI (§12).

## 2. Goals and non-goals

### 2.1 Goals

- **The files are the source of truth.** The app keeps no database. A note is a file, a folder is a folder, and the app's own state lives in `.puffinote/` inside the notebook (§5).
- **Keyboard first.** Every command is in the command menu (⌘K) and in the native menu bar on the desktop. Files can be reached with Quick Open (⌘O), and the whole tree works from the keyboard (§6.1).
- **The same behaviour in every host.** One UI package runs everywhere. A host only supplies a `NotebookStore` (§3).
- **Few dependencies.** Preact and `@preact/signals` at runtime. Vite, Electron and electron-builder to build. Nothing else without the owner's approval.

### 2.2 Non-goals

- Editors for other file types. A non-Markdown file shows in the tree and opens in its default app (or downloads). A picture opens in a tab as a read-only view.
- Rewriting links when a file moves, backlinks, graph views, and plug-ins.
- Real-time collaboration and automatic merging. A conflict is shown, never merged (§5).
- Full CommonMark/GFM fidelity (§11.6).

## 3. Hosts

| Host | Where notebooks come from | Change detection |
| --- | --- | --- |
| Web | The browser's origin-private folder (every browser; no folders from disk) | Re-read the tree when the window gets focus |
| NAS | Folders named in `PUFFINOTE_ROOTS` on the server, browsed from the welcome screen | ETags; re-read the tree on focus |
| Desktop | Any folder on disk: the open dialog, the Dock, `puffinote <folder>`, or Recent | A folder watcher in the main process; re-read the tree on focus too |

- **The desktop window** opens where it was when it last closed: its size, place and whether it was maximized, in `window-state.json` in the app's user-data folder. If that place is no longer on any screen (a display was unplugged), the window opens centred on the screen the pointer is on; one partly off its screen is moved fully onto it and shrunk to fit. The first time, it fills the usable part of the screen the pointer is on. A headless run (`PUFFINOTE_HEADLESS=1`) neither reads nor writes it.
- **One build serves all three.** The page detects at runtime whether it is in Electron, served by the NAS server, or a plain page. There is no build flag.
- **Start-up opens a notebook, not a welcome screen.** The last notebook reopens. Otherwise a browser opens its browser notebook, and a NAS server with one root opens that root. **A browser never opens a folder from disk**, in any browser: Chromium's File System Access API was dropped (Chrome and Edge aren't tested here), and `<input webkitdirectory>` gives only a read-only copy. A folder on disk is the desktop app's job. Open Notebook… (⇧⌘O) brings up the folder dialog in the desktop app and the server's folders on a NAS; a plain browser has only its own notebook, so the command is hidden there. **The welcome screen** shows only where there's a choice: the desktop app with nothing to reopen, a NAS server with several roots (and Open Notebook on a NAS), or when the browser can't keep a notebook. While start-up opens a notebook, the page shows only "Opening …".
- **The browser notebook asks to be kept.** When it opens, Puffinote asks the browser for persistent storage (`navigator.storage.persist()`), so the notes aren't evicted when the disk runs low. It doesn't wait for the answer: Firefox may ask the user, and Chrome and Safari decide on their own. A refusal, or a browser without the API, changes nothing. Safari still deletes a site's storage after seven days without a visit unless the site was added to the Home Screen or Dock; that is outside Puffinote’s control, so download a notebook that matters now and then (Download Notebook, §5).
- **The server is optional to deploy and safe by default:** paths are confined to their root, writes are atomic, a bearer token (`PUFFINOTE_TOKEN`) protects it, and a Host check blocks DNS rebinding (architecture.md). Without a token it refuses to start on any address but loopback (`127.0.0.1`, `::1`, `localhost`), unless `PUFFINOTE_ALLOW_NO_TOKEN=1` says a reverse proxy in front does the authentication.

### 3.1 Desktop updates

The desktop app checks GitHub for a newer release. It never downloads or installs anything itself: the builds aren't signed for automatic updates, and a notes app shouldn't replace itself unasked.

- **Once a day, on start**, a packaged app asks GitHub's API for the latest release (not a draft or pre-release). If it's newer than the running version and the user hasn't skipped it, a dialog says so with **Download** (opens the release page in the browser), **Later**, and **Skip This Version**.
- **Check for Updates…** (the app menu on macOS, Help elsewhere) checks at once (or, while the automatic check is running, right after it) and always answers: a newer version, "up to date", or why it couldn't check. It shows a skipped version too.
- **Check for updates automatically** is a checkbox in the dialog. The choice, the skipped version and the time of the last check live in `updates.json` in the app's user data folder, not in the notebook settings.
- A development build (`just desktop`) and a headless one never check on their own; `PUFFINOTE_NO_UPDATE_CHECK=1` turns the automatic check off too.

## 4. Principles

1. **Never lose text.** A save is atomic. A failed save keeps the buffer. A hard-linked file can't be renamed over without splitting the link, so it is written in place: the growth past its old end first (a full disk then leaves the old file whole), then the rest from the start, then the old tail is cut. It is never emptied first. A read-only file is never replaced: the save fails with a "read-only" error (403 on the NAS), in the app and in `puffinote html -o`. Closing, switching notebooks and quitting all ask about unsaved tabs.
2. **Never rewrite bytes the user didn't touch.** The editor edits the source minimally (§11.7).
3. **Dim, don't hide.** A command that can't run right now stays in its menu, dimmed, so the menus don't change shape under the user.
4. **No surprise network traffic.** Remote pictures and links load only on explicit action. The desktop app and the plain web page never contact a server.

## 5. The notebook

- **A notebook is one root folder.** One notebook is open at a time. Open Notebook… (⇧⌘O) asks about unsaved tabs, then switches. Reading the new notebook can be slow (the NAS): if a tab has unsaved changes by the time it is read, Puffinote asks again just before switching, and Cancel stays in the old notebook. When a second notebook is opened before the first has been read, the second wins. Switching closes whatever was open over the old notebook (a dialog, a menu, the picture preview, the find bar); a question still up is answered as cancelled, and the old notebook changes no files after it closes. The one write as it closes is its own window state (`session.json`), so the next open restores its tabs.
- **The notebook keeps its own state in `.puffinote/`:**
  - `session.json`: the open tabs, the active tab, the sidebar (width, panel, open folders), the recent files, the pinned notes, and the library's folder and sort order.
  - `settings.json`: this notebook's settings overrides (§9).
  - `.gitignore`: ignores `session.json` and keeps `settings.json`, so a notebook under git shares its settings but not its window state.
- **What opens where:**
  - A Markdown file (`.md`, `.markdown`, `.mdown`, `.mkd`, `.txt`) opens in the editor.
  - A picture opens in a tab as a read-only view.
  - Anything else opens in its default app (desktop) or downloads (web, NAS). On the desktop Puffinote asks first, with **Open** focused so Enter opens it, unless Ask before opening other files (§9) is off.
- **Encoding.** A file is read as UTF-8, with or without a BOM. If it is not valid UTF-8, the encoding is guessed and the tab is read-only, so that a save cannot write the guess back. A byte-order mark is trusted only while the bytes agree with it: invalid UTF-8 after a UTF-8 BOM, or an odd byte count after a UTF-16 BOM, also opens read-only, because a save would change bytes the user never touched.
- **External changes.** The tree is re-read when the folder changes (desktop) and whenever the window regains focus (every host). The desktop looks on focus too because its folder watcher never hears of edits made on another machine to a network folder (SMB, NFS); Puffinote can't reliably tell a network folder from a local one, so it looks for every folder. When reads of the tree overlap (a slow refresh and a rename, say), the one started last wins, so a slow read never brings back an old name. Changes inside `.git/` (a git client or an IDE touching its index) don't count as a folder change. Every open tab is then reconciled with the disk, reading again only the notes whose size or modified time in the new listing differs from when they were last read (a store that gives no times reads them all; a same-size edit within the disk's time resolution waits for the next change, and a save still catches it as a conflict):
  - A tab without unsaved edits reloads.
  - A tab with unsaved edits is left alone and flagged. Auto-save stops for it. Saving it asks whether to overwrite the file on disk or take the disk's version.
  - Taking the disk's version is **one undoable step**, so ⌘Z gives back what the tab held.
  - A file deleted on disk under a dirty tab is flagged the same way. Saving recreates it. Auto-save never recreates it: a save that finds the file gone flags the tab instead. If the file comes back with the text the tab last saved, the flag clears.
  - Where the disk can't give a version to check against (a NAS server that sends no ETag), Puffinote reads the file again just before saving and saves only if it still holds what the tab last read or saved; otherwise the tab is flagged as changed or deleted, as above. The same goes for picture-link updates in closed notes and for the settings file. This narrows, but can't close, the gap a versioned save closes: a change landing between that read and the write is still written over.
- **Download Notebook (.zip)…** (command menu, every host) saves the notes with unsaved changes, then hands the whole notebook to the browser as a download: one zip named after the notebook (`Browser notebook.zip`), holding a folder of that name with every file and folder in it, hidden ones and pictures included. It leaves out `.git/`, the notebook's Trash (`.puffinote/trash/`) and `.puffinote/session.json` (this machine's window state); `.puffinote/settings.json` and `.puffinote/.gitignore` go with it, so the notebook's settings travel. Files are stored uncompressed (notes are small and pictures are already compressed), by Puffinote's own zip writer. If a note can't be saved, nothing downloads and the status bar says why. An archive holds up to 65,535 entries and 4 GiB.
- **Import Notes…** (command menu, in a browser and on a NAS) copies files from this computer into the notebook. It asks first: **Choose Folder…** (the default) or **Choose Files…**, then shows the browser's file dialog. The browser hands over a read-only copy of what was picked; nothing on the disk changes. The files go into the selected folder (the folder that holds the selected file), or the top level when nothing is selected, as New Note does.
  - A picked folder arrives as a folder of the same name with everything in it; empty folders inside it don't come along (the browser doesn't list them).
  - **Nothing is overwritten.** A file or folder whose name is taken where it lands gets a number, as New Note's names do (`Trip 2`, `a 2.md`), comparing without case. A folder is never merged into one that's there.
  - **Skipped:** `.DS_Store`, `._` files, `__MACOSX`, `Thumbs.db`, `desktop.ini`, `.Spotlight-V100`, `.Trashes`, `.fseventsd` and `.git` folders. Characters a name can't hold in a notebook (`\ : * ? " < > |`) become `-`, and spaces at either end go.
  - Files are copied one at a time. One that can't be read or written is counted and the rest carry on. The status bar then says what happened ("Imported 12 files into “Trip”.", plus how many couldn't be copied), the folder that received them opens, and a single imported folder or file is selected.
  - The desktop app has no Import Notes…: its menu bar can't open a page's file dialog, and Finder, or a drop on the tree (§6.1), does the job.
- **Saving.** ⌘S saves the active tab; Save All saves every dirty tab. With auto-save on (the default), a note saves a second after the last edit. Revert to Saved discards the edits.
  - A tab without unsaved edits isn't written (except Save on a tab flagged deleted, which recreates it).
  - A tab's saves happen one at a time. A save asked for while one is in flight writes after it, with the text the tab has by then.
- **Recovery of unsaved edits.** While a tab has unsaved edits, Puffinote keeps a recovery copy of its text outside the notebook, so a crash, a killed app or a closed browser tab doesn't lose it. The note's file is never touched.
  - The copy is taken a second after the last edit, and at least every five seconds while typing goes on, and again as the page goes away. It lives in the page's local storage, keyed by notebook and note path. Every host has that storage: the browser, the NAS client (per browser, so a copy is offered only in the browser that made it) and the desktop app (whose page storage lives in the app's user-data folder).
  - A save, Revert to Saved, closing the tab with Don't Save, or Don't Save when switching notebooks or quitting drops the copy. A rename or move takes it along.
  - A note longer than a million characters gets no copy. When the storage is full, the oldest copies of other notes make room.
  - **Offering it back.** When a note opens and its copy differs from the file, a banner says “Recovered unsaved changes to “name” from (time)” with **Restore** and **Discard**. A copy that matches the file is dropped quietly. Notes with a copy that the session didn't reopen get a tab when the notebook opens, so the banner is seen. Restore loads the copy into the tab as one undoable step, unsaved: nothing is written until the tab saves as usual. Discard drops the copy. Until the user answers, nothing is saved over the file, the copy is kept (no newer copy is taken for that tab), and closing the tab keeps it for next time. A copy whose note is gone stays until the note comes back.

## 6. The layouts

The Layout setting (§9) picks one. Both share the note, the status bar, the commands and the settings. The accent colour (§9) tints selections, active icons and highlights in both, light and dark.

- **Icon rail (the default).** A narrow rail on the left: the notebook's initials (a click shows the notebook's folder in Finder or Explorer; on the web, it opens another notebook), a button per panel (§6.2), Hide Sidebar and Settings. The chosen panel floats beside it in a card with its title and, for Files and Filter, New Note, New Folder and a ⋯ menu (expand or collapse all folders, refresh). Clicking the shown panel's button hides it. The open notes are tabs in a second card (§7). The panel's width is dragged at its right edge.
- **Library.** Three columns:
  - **The library:** All notes, Pinned and Recent, then the notebook's folders (the notebook's own folder first; a folder with folders in it opens with its chevron). The chosen one is the note list's scope. New Folder and Settings sit at the bottom. A folder's context menu is the tree's (§6.1).
  - **The note list:** the scope's name and counts ("7 notes · 2 folders"), a Sort menu (Last Edited, Title, Created) and New Note. A card per file: a note shows its name, when it was last edited (or created, when sorted by that) as "Now", "5m", "2h", "Yesterday", a weekday or a date, and its first lines with the Markdown taken out (an open note's follow its unsaved text). A picture shows its format and size in pixels and a thumbnail; any other file says so. Where the page loads pictures from a URL (the NAS, the desktop), the size comes from the thumbnail once it has loaded (an SVG's from its own attributes), so a picture isn't downloaded twice; until then, and for a thumbnail scrolled out of reach, the card shows the format alone. Elsewhere it is read from the file. Pinned notes (Pin to Top in a card's context menu) come first, in every scope. The **Filter** box narrows the list to names or text that contain what's typed, ignoring case; every note in the scope is looked at, with no cap on how many match (Search's limit doesn't apply). ↑/↓ in the list show the next note, keeping the keyboard in the list.
  - **The note,** under a path bar: the folders leading to it (a click shows that folder's notes), the note's name, and Outline and History buttons that open those lists as popovers.
  - One note at a time: opening a note closes the one before it, unless it has unsaved edits. Tabs (§7) and Toggle Tab Bar don't apply. The Files, Filter and Search commands go to the note list and its box; Outline and Recent open the popovers. With no tree to select in, Rename… and Delete… act on the note in front, and New Note… (⌘N) and New Folder… create in the folder the note list shows (the top level for All notes, Pinned and Recent), as its New Note button does.
- ⌘\ hides and shows the panel (icon rail) or the library and note list (library). The state persists per notebook.

### 6.1 The file tree

- **Files** shows every file and folder in the notebook, folders first, sorted case-insensitively with numbers by value (`note2` before `note10`). Dotfiles are hidden unless Show Hidden Files is on. Extensions are hidden unless Show File Extensions is on. Symlinks are followed while they stay inside the notebook (the NAS root), but a folder link back to the folder it is in, or to any folder above it, is left out, so a link loop can't stop the notebook from opening.
- **Tree verbs:** new note, new folder, rename (F2), move to another folder, delete (confirmed), reveal the active file, refresh, and expand or collapse all. They are in each row's context menu and in the command menu.
  - Delete… asks first. Its tabs close without saving, so the question says how many notes inside have unsaved changes that will be lost, and lists the notes outside that show a picture being deleted (as Delete Picture does, §10.6).
- **The Trash.** Nothing Puffinote deletes is gone for good until the user says so. Delete…, Delete Picture, the no-longer-used offer and Find Unused Pictures all move to a Trash, and every question and button says which one ("moved to the Trash" or "moved to the notebook's Trash"); the button is Move to Trash.
  - The desktop app uses the system Trash, from which Finder or Explorer restores. Show Trash… and Empty Trash… are dimmed there.
  - The browser and the NAS keep **the notebook's Trash** inside the notebook, in `.puffinote/trash/`: a deleted item moves to `trash/<id>/<its path>`, and `trash/<id>.json` records where it was and when. The client does this through the store's ordinary move, so the NAS server has no trash of its own and the same folder works from any client. `.puffinote` is never listed, so the Trash stays out of the tree, Quick Open, search and Find Unused Pictures. `trash/.gitignore` keeps it out of git.
  - After a delete into the notebook's Trash the status bar says what moved, with **Undo**, which restores it.
  - **Show Trash…** (command menu) lists what the notebook's Trash holds, newest first, with each item's folder and time, and a Restore button for each. **Restore never overwrites:** an item goes back where it was, recreating its folders, or beside it as `name 2.md` when the name is taken, and the status bar says so.
  - **Empty Trash…** asks first, with Cancel as the default, and is the only delete that can't be undone. Nothing empties the Trash on its own.
  - Move To… opens a searchable list of the folders the item can move into, the notebook's top level first. Folders where the move would change nothing, or would put a folder inside itself, are left out.
  - New Note adds `.md` to the name unless it already ends in a note extension (§5). A dot elsewhere is part of the name: "Meeting 2024.11" becomes `Meeting 2024.11.md`.
  - A rename that changes only the case of a name works on disks that ignore case.
- **Drag to move.** The tree draws the drag itself: a ghost under the pointer, an outline on the folder it would land in, and a closed folder that opens after the pointer rests on it for a second. Holding the pointer near the tree's top or bottom edge for half a second scrolls the tree that way, faster the closer the pointer is to the edge.
  - A drop on a folder means that folder. A drop on a file means the folder that holds it. A drop on the empty space means the top level.
  - A move that changes nothing, or a folder into itself, shows no indicator. A name that is already taken is refused. Escape cancels.
  - A move is a plain move. Links to the file are not rewritten, except links to pictures (§10.6). Open tabs and the recent list follow it. A save already under way on a moved note finishes before the move starts; a save asked for during it (auto-save included) waits and writes to the new place.

- **Drop files from the desktop.** Files and folders dragged in from Finder, Explorer or the desktop and dropped on the tree are copied into the notebook, in every host. They land as Drag to move's drops do: in the folder dropped on, the folder that holds the file dropped on, or the top level on empty space; the outline shows where, and a closed folder held under the pointer for a second opens. In the Library layout a drop on a folder in the library goes into that folder, and a drop on the note list into the folder it shows (the top level for All notes, Pinned and Recent). The copy follows Import Notes…'s rules (§5): nothing is overwritten, a taken name gets a number, a dropped folder comes with everything in it, clutter is skipped, and the status bar says how many files came in. The originals are only read. Files let go anywhere else (other than pictures on a note, §10.6) are ignored, so the browser never opens one in place of the app.

### 6.2 Keyboard

- ⌥⌘S focuses the tree (on the open note when nothing is selected); ⌥⌘E focuses the editor.
- The keyboard goes where the click was meant for. A click on a toolbar button (the rail, the panel's header buttons, tabs, the view switch) leaves it where it was, as on macOS, so the caret stays in the note and a cancelled dialog gives it back there. Tab still reaches those buttons.
- A new note takes the caret. A new folder is selected with the keyboard on it in the tree, so Enter renames it. The tree getting the keyboard back (after a dialog, say) never selects a row on its own.
- In the tree: ↑/↓ move, ←/→ collapse or expand (or go to the parent or first child), Home/End jump, typing jumps to a matching name, Enter opens a file or toggles a folder, F2 renames, Delete deletes. Escape selects the folder that holds the selected row, and from the top level selects nothing.
- On macOS, as in Finder, Enter renames the selected row and ⌘↓ opens it (Space also opens or toggles).
- A click on a file opens and selects it and puts the keyboard in the editor; a click on a folder opens or closes it without selecting it, so the selection stays where it was. ⌥-click selects a row without opening it and puts the keyboard in the tree, so Enter then renames it.
- With **Open notes and folders with** set to a double click (§9), one click selects a row as ⌥-click does, and a double click opens the file or opens or closes the folder. A folder's arrow still opens or closes it with one click.
- The selected row has an accent background: a tint, and the solid accent while the tree has the keyboard.
- A click on the tree's empty space, or on the panel's header away from its buttons, selects nothing and puts the keyboard in the tree, so New Note and New Folder add to the top level.
- The tree is one ARIA `tree` with a visible focus ring.

### 6.3 Panels

The rail's buttons change the panel. Filter and Search put the caret in their box; Files, Outline and Recent leave the focus where it was.

- **Filter** narrows the tree to entries whose whole name (extension included) contains the text, case-insensitively. It keeps the folders above a match and everything inside a matching folder, opens the folders that lead to matches, and counts the matches (or says "No matches"). It is the same tree, with the same keys, menu and drag.
- **Search** (⇧⌘F) finds note names and the text in every note, including the unsaved text of open tabs. Notes whose name matches come first, led by the name with the match marked. Each text hit shows its line with the match marked. Enter or a click opens the note, at the hit for a text hit. When the query changes, the old hits go at once and the panel says Searching… until the new answer; a search that fails says "Search failed" with the reason.
- **Outline** lists the headings of the active note, indented by level, and catches up with edits once typing pauses for a third of a second. A click moves the caret to the heading.
- **Recent** lists this notebook's recently opened files.

### 6.4 Quick Open (⌘O)

Type part of a file name to open it. Ranking: names that start with the query, then names that contain it, then paths that contain it, then names and paths that contain its letters in order. Ties go to the more recently opened file. With an empty query, recent files come first. The command menu is for commands only; files are opened here.

## 7. Tabs

- One tab per open file. A click shows it, a middle-click or × closes it, and the context menu has Pin Tab, Close, Close Others and Close All. ⇧⌘] and ⇧⌘[ step through tabs.
- A file opened again while it is still being read (a double click, a slow NAS) gets the same tab. When several opens are out at once, the file asked for last stays in front, unless a tab was picked after it.
- **Pinned tabs** sit at the start of the strip in their own group, which stays in view while the other tabs scroll. A pinned tab shows a pin in place of its ×; clicking the pin unpins it. Close Others and Close All leave pinned tabs open; ⌘W, a middle-click or Close Tab still closes one. Pins are remembered with the open tabs.
- A dirty tab shows a dot. Closing it asks Save, Don't Save or Cancel. When the note changed on disk since it was opened, the question says so and Save becomes Keep My Version, which writes the tab over the disk. When closing several tabs, switching notebooks or quitting, Save All saves the rest but leaves a note that changed on disk; then nothing closes, that tab comes to the front and the status bar says why, so the user can pick Reload from Disk or Keep My Version and try again.
- After a save, the status bar says "Saved" for a moment.
- Each tab keeps its scroll position, caret, undo history and view (§10).
- Show Tab Bar (§9) hides the strip. The tabs stay open.
- Tabs belong to the icon rail layout. The library shows one note at a time (§6).

## 8. Commands and shortcuts

### 8.1 The command catalog and the command menu

- **One catalog.** Every command has an id, a title, a group, an optional shortcut, and `enabled`/`active` predicates. The command menu, the shortcuts, the context menus and the Electron menu bar all read it.
- **The command menu (⌘K)** filters the catalog as you type. Recent commands come first while the query is empty. A disabled command still shows, dimmed, and does nothing.
- Ids start with `shell.`, `view.`, `edit.`, `format.` or `insert.`.

### 8.2 Shortcuts

- Shortcuts are written once as `mod+shift+k` and shown in each platform's style (⌘⇧K, Ctrl+Shift+K).
- **Keyboard layouts.** A shortcut matches the character the layout types, not the key's position: Ctrl+- is Zoom Out on French AZERTY (where `-` is on the 6 key) and ⌘- is Zoom Out on a German Mac. Punctuation or a digit that needs Shift on the layout matches without it (Dvorak ⌘+ zooms in; AZERTY Ctrl+Shift+6 is Heading 6). Only a key that types no ASCII character (an ⌥ layer, a Cyrillic layout) matches by its physical key, so ⌥⌘L and Ctrl+Я (Ctrl+Z) still work. An exact match anywhere in the catalog wins over a looser one.
- **AltGr.** On Windows and Linux a character typed with AltGr (which Windows reports as Ctrl+Alt) never runs a shortcut, so Polish AltGr+S types ś instead of focusing the file tree.
- **Input methods.** While an IME composition is open (Japanese, Chinese, Korean), its keys belong to it: Enter that commits a conversion, Escape that cancels it and arrows that pick a candidate run no shortcut, in the note or in the find bar.
- **Keys in the command menu, Go to File and Move To.** ↓ and Tab move the highlight down, ↑ and Shift+Tab up, stopping at the ends; Page Down and Page Up move eight. Enter picks, Escape closes. Focus stays in the search field.
- **Resolution order:** an open overlay (a dialog, the command menu) gets keys first. Then the shell's reserved set: ⌘K, ⌘O, ⌘N, ⇧⌘N, ⌘S, ⌘W, ⌥⌘W, ⇧⌘], ⇧⌘[, ⌥⌘S, ⌥⌘E, ⌘\, ⌘, (Settings), ⇧⌘O, ⌘F, ⌥⌘F (Ctrl+H elsewhere), ⇧⌘F, ⌘G, ⇧⌘G and ⌘P (Export as PDF). Then the editor.
- The desktop menu bar's built-in items (clipboard, Window) never take a catalog shortcut. On Windows and Linux the Window menu has Minimize only, since Electron's own Close would take Ctrl+W from Close Tab.
- While a question is up (Save changes?, a name to type), menu-bar commands do nothing. Quitting or opening another folder still works: its own question replaces the open one, which is answered Cancel.
- In a text field other than the note (a dialog, the filter box), editing shortcuts keep the field's own behaviour. For example, ⌘A selects the field's text.

## 9. Settings

Settings resolve in layers, lowest first: the defaults, the user's preferences (every notebook), then the notebook's overrides in `.puffinote/settings.json`. The Settings dialog (⌘,) has two scopes over the same fields: **Preferences** and **This notebook**. A notebook field can be reset to inherit.

| Setting | Values | Default |
| --- | --- | --- |
| Appearance | Match system, Light, Dark | Match system |
| Layout | Icon rail, Library and note list | Icon rail |
| Accent colour | Blue, Amber, Teal, Purple | Blue |
| Notes open in | Rich view, Formatted view, Raw text | Rich view |
| Save automatically | on/off | on |
| Move ticked tasks to the bottom | on/off | on |
| Indent with | 4 spaces, 2 spaces, Tab | 4 spaces |
| Open notes and folders with | A single click (⌥-click selects), A double click (a click selects) | A single click |
| Show file extensions | on/off | off |
| Show hidden files | on/off | off |
| Show tab bar | on/off | on |
| Ask before opening other files | on/off | on |
| Save pasted pictures in | The notebook's picture folder, A folder beside each note | The notebook's picture folder |
| Picture folder | a folder name | attachments |
| When a picture is no longer used | Offer to delete it, Keep it | Offer to delete it |

A setting Puffinote doesn't recognise, or a value it can't use, is ignored when it reads the settings but kept when it saves them. A notebook opened in an older version keeps the settings a newer one wrote. `.puffinote/settings.json` may change under Puffinote (a sync, git, another window): a refresh takes its changes, and a change in the dialog is written over what the file holds by then, touching only the settings changed in the dialog, and is tried again if the file changes between the read and the write. A setting that changed while the dialog was open (a refresh, another window) is kept unless the dialog changed it too; Preferences work the same way.

## 10. The editor

A custom editing surface, not a `contenteditable`. Lines render as DOM. A hidden `<textarea>` takes keyboard, IME and clipboard input. The caret and selection are drawn from an offset ↔ visual map. There are three views, switched from the status bar or the View commands:

- **Rich view** (`live`): the live preview. Markers are hidden until the caret reaches them (§10.2).
- **Formatted view** (`source`): the same styling with every marker shown.
- **Raw text** (`raw`): the literal source in one monospace style.

### 10.1 Editing

- **Caret and selection** move by grapheme cluster, and a click lands on a grapheme boundary (never inside an emoji or between a letter and its accent). IME composition works in place.
- **Accents and input methods.** Holding a letter on macOS shows the accent menu, and the accent chosen replaces the held letter (`e` becomes `é`), as one undo step. To make this work, the hidden input holds the letter just before the caret (only a letter, never punctuation, so the OS's smart dashes and quotes have nothing to rewrite); any change the OS makes to that letter is made to the note. Input never only deletes text: deleting is the keys' job. IME composition (Japanese, Chinese, Korean) and dead keys (⌥E then e) insert at the caret as before, and the text being composed draws at the caret.
- **Context menu.** A right-click (a two-finger click, or Control-click on macOS) opens Cut, Copy, Paste and Select All, in every view. A click inside the selection keeps it; anywhere else moves the caret there first. Cut and Copy are off with nothing selected. ⌘X, ⌘C and ⌘V stay the platform's own keys. On Windows and Linux, Shift+Delete is Cut as well, as it is across those platforms: it puts the selection on the clipboard, then deletes it; with nothing selected it deletes as Delete does. A right-click on a picture opens the picture's own menu instead (§10.6).
- **Undo and redo.** Typing coalesces into runs. A run of Backspace undoes as one step, and so does a run of Delete, wherever it deletes (a list marker, an empty pair, a line break). Any key held down (auto-repeat) undoes as one step once it is released: held Enter, held Tab, a held character. Typing, another key, a caret move or a command ends a run. Deleting a selection, structural edits, commands and a paste are single steps. Revert resets the history. Taking an external change is one step (§5).
- **Enter.** Enter writes one newline, in every view, so one press makes one visible line and each line in the editor is one line in the file. A blank line (Enter twice) starts a new paragraph. Shift+Enter writes one newline without continuing a list or quote. A newline the editor writes (Enter, Shift+Enter, a paste, a pasted or dropped picture, a code block, a horizontal rule, a line command) uses the note's line ending, the one its first line break uses, so a CRLF note stays CRLF. Deleting a line takes its whole line break, both halves of a CRLF, and the editor never splits one.
- **Lists and quotes.** Enter continues a list item, task (always as `[ ]`), or quote. Enter on an empty top-level item leaves the block and lands on a visible line. An empty nested item goes up one level instead, as Shift+Tab would: it keeps its marker and takes the next number of the list it joins. Enter with the caret at the start of an item's text (or inside its marker) pushes the whole item down, keeping its marker and tick, and keeps the caret at the start of its text. It opens one empty item above, like the pushed item (a task as `[ ]`), in every list type and at every level, the first item of a list included; a numbered item above takes the pushed item's number and the rest renumber. Ordered lists renumber as items are added, moved or re-indented, counting past the lines nested under their items and past blank lines between items (a list split by blank lines is still one list, as the export renders it). Tab on a numbered item that becomes the first child of the item above starts that nested list at 1, and Shift+Tab on a nested list's first item restarts the items it leaves behind at 1.
- **Backspace in lists.** Backspace at the start of a list item's text (in every view but Raw text) takes off the whole marker in one press: the bullet or number and any checkbox, keeping the indent and quote, so the item becomes a plain line. A numbered list after it starts again at 1, as in Apple Notes. Backspace pressed again at the start of that line joins it to the line above, as anywhere else.
- **⌘⌫ (macOS)** deletes back to the start of the caret's visual row, so in a wrapped paragraph it takes only that row. On a list item it stops at the start of the item's text in Rich view, keeping the bullet, number or checkbox. With nothing before the caret on the row, it deletes as Backspace does.
- **Deleting across lines renumbers.** When Backspace, Delete, Cut or deleting a selection joins lines, the numbered lists at the caret renumber in the same step, so removing an item closes the gap.
- **Tab and Shift+Tab** indent and outdent list items, by the Indent with setting. In a quote, the indent goes after the `>` and its space (`> - b` becomes `>     - b`), so the item nests inside the quote; Enter on an empty item nested that way goes up one level as elsewhere. Elsewhere, Tab inserts the indent. Right after Escape, Tab and Shift+Tab move the focus out of the note instead, so the keyboard never gets stuck in it.
- **Auto-pairing:** `*`, `_`, `` ` ``, `[` and `(` wrap a selection, or insert a pair. `*`, `_` and `` ` `` don't pair at the start of a line (a list item, a rule, a code fence), right after the same character, or after a letter or digit (`2*3`, `snake_case`). A space typed right after a lone opener removes its closer (`2 * 3`). Typing the closer steps over it. Backspace right after the editor inserts a pair removes both halves; once you type inside the pair or move the caret, Backspace deletes one character, and a pair you typed yourself is never deleted whole.
- **Line commands:** move line up/down (⌥↑/⌥↓), duplicate (⌘D), delete (⇧⌘K).
- **Move to the top or bottom of a list** (⌥⌘↑/⌥⌘↓): the list item or task the caret is in, or the items a selection spans, goes to the top or bottom of its list, with its nested lines. It works in bulleted, numbered and task lists, at any nesting level (within that level's list). A numbered list keeps counting from where it started. Outside a list item, or with the item already there, nothing happens.
- **Formatting:** bold ⌘B, italic ⌘I, inline code ⌘E, strikethrough ⇧⌘X, highlight ⇧⌘H, headings ⌘1–⌘6, paragraph ⌘0, bullet list ⌘., numbered list ⌘/, blockquote, link ⌥⌘K, code block ⇧⌘E, horizontal rule.
- **Tasks:** ⌘L toggles a checkbox (and adds one where there is none), ⌥⌘L steps the status, ⇧⌘L removes it. With Move ticked tasks to the bottom on (§9), ticking a task done moves it below the open items of its bulleted list, as Apple Notes does, and unticking moves it back to the end of the open ones. The item takes its nested lines along, the rest keep their order, the caret moves with it, and one undo reverses it. Numbered lists aren't reordered.
- **Clipboard:** copy gives the Markdown source. Paste inserts plain text with the note's line endings, and drops a duplicate task marker when pasting a task into a task.

### 10.2 Live preview

**What "reveal" means depends on the construct:**

- **Inline marks** (emphasis, strikethrough, highlight, inline code, links, autolinks): the markers show when the caret or selection touches that node. Otherwise they are hidden and the styled text shows.
- **Line-prefix marks** (heading `#`, `>`, list and task markers): they show when the caret is on the line. List items and tasks are the exception: their `-`, `1.` or `- [ ]` shows only when the caret or selection touches the marker's own characters (from the line start through the bullet, number or `]`), so editing the item's text keeps the bullet, number or checkbox, and the text doesn't move. A selection counts only when one of its ends is inside the marker, so dragging across an item, or to the left of its checkbox, keeps the checkbox drawn. When hidden, they are drawn as decorations: bullets, numbers, checkboxes, a quote bar. A heading inside a quote (`> ## Note`) draws as a heading in the quote, its `#` hidden with the `>`. List markers are drawn by how deeply the item is nested (every list type counts toward the depth): a numbered item counts 1, 2, 3 at the top level, a, b, c one level in, i, ii, iii two levels in; a bullet is a disc (•), then a circle (◦), then a square (▪); both go round again after three levels. Checkboxes look the same at every level. The file keeps the digits, and Export to HTML numbers nested lists the same way.
- **List items hang.** A list item's marker (indent, bullet or number, checkbox) sits in a column of its own and the text in the next, so wrapped rows line up with the text above, as in Apple Notes, not under the bullet. The column stays when the marker shows (in Rich and Formatted views; not in Raw text). A click under the bullet or checkbox lands at the start of that row's text.
- **Pictures** stay drawn wherever the caret or selection is, so a pasted picture shows straight away. Their `![](…)` shows only after View Link Text (§10.6), and only until the caret leaves it.
- **Blocks** (fenced code, tables, front matter): the raw source shows when the selection touches the block.
- A selection reveals every node it touches.

**The caret sits where typing would put text.** Beside a checkbox or bullet it draws past the decoration's margin, at the text's start, as tall as the text. At a soft wrap it draws at the start of the row below, so ↓ from the end of a wrapped row moves to the next row. ↑ and ↓ onto a list item at its left edge land at the start of its text, not before the bullet or checkbox. In Rich view the hidden marker is one step for ← and →: ← from the start of an item's text goes to the end of the line above, and → from the end of a line goes to the start of the next item's text. Home and End (⌘← and ⌘→ on macOS) go to the start and end of the caret's visual row, so a wrapped line is one row at a time; at a soft wrap End stops before the space the line wrapped at. On a line's first row, Home and ⌘↑ go to the start of an item's text first, and to the line start (revealing a task's `- [ ]`) when pressed again there. Page Up and Page Down scroll a screen and move the caret by the same height, keeping its column, and go to the start or end of the note past the first or last row. On macOS the Emacs keys work as in other Cocoa text: ⌃A and ⌃E go to the line's start and end (⌃A as Home on the first row), ⌃F, ⌃B, ⌃N and ⌃P move by character and row, ⌃D and ⌃H delete forward and back, and ⌃K deletes to the end of the line, or the line break at its end. Shift extends all of these.

**Concealed markers are atomic.** Arrow keys skip over a hidden marker run. Once revealed, its characters are ordinary text. The offset ↔ visual map keeps clicks, arrows and selection on the right document offsets.

**Unfinished syntax is the common case while typing.** An unclosed marker is literal text until it closes. An unterminated fence runs to the end of the file as code. Nothing flickers.

**Pictures.** A line that holds only `![alt](url)` shows the picture, scaled down to the text column (never up). The caret sits before or after it, never inside. Relative paths resolve against the note's folder. A picture that changes on disk is shown again as it is now, once Puffinote sees the change (a folder change, or the window coming back on the web and NAS). One that can't be read yet shows nothing, and is tried again the next time the note is drawn.

### 10.3 Status bar and links

- **Status bar:** the latest notice (an error, "Saved"), Ln/Col, the word count (counted again once typing pauses for a third of a second, so a long note doesn't recount on every keystroke), the zoom when it isn't 100% (a click resets it), and the view switcher.
- **Zoom.** Zoom In (⌘=), Zoom Out (⌘-) and Actual Size scale the note's text and spacing from 50% to 300% in 10% steps. The zoom is remembered per browser or desktop app, not per notebook.
- **Links.** Hovering shows the destination (`[[name]]` for a wiki link). ⌘-click (Ctrl-click elsewhere) opens it. A plain click puts the caret in the link, so it stays editable. Of links with a scheme, only `http`, `https` and `mailto` open, in the browser or the OS. Other schemes, including `file:`, `javascript:` and `data:`, are never handed to the OS, and neither is a `//host` link.
- **Links between notes.** ⌘-click on a relative link (`[x](other.md)`, `[x](sub/other%20note.md)`, `<sub/other note.md>`) or a wiki link (`[[name]]`, `[[name|label]]`, `[[folder/name]]`) opens that file in a tab, as opening it from the tree does (a note or picture in a tab; another file in its default app; the library layout swaps the note in front).
  - A relative link is URL-decoded and resolved against the note's folder; a leading `/` means the notebook's top level. Where no file has that exact path, a file whose path differs only in case or Unicode form is taken.
  - A wiki link is tried as a path from the note's folder, then from the top level, with `.md` added unless it names an extension; exactly first, then ignoring case. Failing that, it takes the one file anywhere in the notebook whose name (or, for `[[folder/name]]`, whose trailing folders and name) matches, ignoring case, with or without `.md`.
  - A `#fragment` (`other.md#next-steps`, `[[name#Next steps]]`) puts the caret on the heading it names in the opened note: the heading whose text matches, ignoring case, as written or as a GitHub-style anchor. A heading nobody matches is ignored. `#heading` alone, or `[[#heading]]`, goes to the heading in the same note.
  - A link to a file that isn't there says "There's no “name” in this notebook." in the status bar, and one that matches more than one file says "More than one file is called “name”." Nothing is created.
- **No spell check.** The editor draws each line itself and the text under it isn't editable text, so the browser's checker has nothing to underline.

### 10.4 The heading type scale

Sizes are ratios of a 16px body. Sizes step down by about 1.25× to H4. Below that, weight and colour carry the level, so H5 and H6 do not drop below the body size.

| Level | Size | Space before | Space after | Weight   |
| ----- | ---- | ------------ | ----------- | -------- |
| H1    | 30   | 48           | 12          | Bold     |
| H2    | 24   | 38           | 10          | Bold     |
| H3    | 20   | 32           | 8           | SemiBold |
| H4    | 17   | 27           | 7           | SemiBold |
| H5    | 15   | 24           | 6           | Bold     |
| H6    | 13   | 21           | 5           | Bold     |
| Body  | 16   | 0            | 12          | Normal   |

Headings bond downward: a large space before and a small space after. Adjacent margins collapse, and the first block takes no space before. Nothing uppercases or letter-spaces a heading, because both would break the character-exact offset map.

### 10.5 Export to HTML

The renderer walks the same tokens as the editor and writes one self-contained HTML document (styles included) or a fragment. Every line break inside a paragraph becomes a `<br>`, so the export keeps the lines as the editor shows them. Inside a blockquote the same holds: consecutive quoted lines at one depth are one paragraph, a bare `>` line ends it, and a heading or list item after the `>` exports as a heading or list inside the `<blockquote>`. A list item keeps the blocks indented to its text (at least to the column just past its marker), past blank lines too: a paragraph or indented code under an item exports inside that item's `<li>`, so the list isn't split. A line indented less ends the list. Table columns keep the alignment their delimiter row sets (`:--` left, `:-:` center, `--:` right). It has no dependencies. The CLI's `html` command uses it (§12).

Exported HTML must be safe to open. Every attribute value is escaped, including a fence's info string. A link or autolink keeps its `href` only when the URL has no scheme (a relative path, a `#fragment` or `//host`) or its scheme is `http`, `https` or `mailto`; an email autolink gets `mailto:`. An image `src` may also be a `data:image/` URL of a raster type (png, jpeg, gif, webp, avif or bmp; never SVG). Control characters and whitespace are ignored when the scheme is read. Any other URL (`javascript:`, `vbscript:`, `file:`, other `data:` and so on) is dropped: the link renders as its text, the image as its alt text.

**Export as HTML…** (File menu and command menu, for the note in front) saves the note as one standalone `.html` file named after the note (`Trip.md` → `Trip.html`, title `Trip`), from the editor's text, unsaved changes included. It uses this renderer and stylesheet, which follow the system's light or dark appearance (`prefers-color-scheme`). Every picture the note shows from the notebook (`![](…)`, read through the notebook's store like any file) is embedded as a `data:` URL, so the file works on its own. A picture stays a link to its relative path, and a notice names it, when it is missing or unreadable, isn't a raster type the renderer accepts (PNG, JPEG, GIF, WebP, AVIF, BMP; an SVG stays a link), is larger than 10 MB, or would take the embedded pictures past 50 MB. Web pictures (`https:`) stay links. `![[…]]` embeds export as their text, as before. The file is handed over as a download: the browser saves it where it saves downloads, and the desktop app asks where to save it.

**Export as PDF…** (File menu and command menu, ⌘P) prints the same standalone HTML, pictures embedded the same way. In the browser and the NAS client it is printed from a hidden frame (no scripts run in it) and the browser's print dialog opens, where Save as PDF is one choice; only the note prints, none of the app. The desktop app asks where to save the PDF (suggesting the note's name with `.pdf`), lays the HTML out in a hidden window with scripts off that loads nothing but the document and its embedded pictures, and writes the PDF atomically. Web pictures don't print. The stylesheet's print rules: always light, 18 mm top and bottom and 16 mm side margins, the full page width, 11 pt text, long code lines wrapped, no page break inside a code block, picture, table row, quote or task, and none straight after a heading.

### 10.6 Pasted and dropped pictures

- **Paste or drop a picture** (PNG, JPEG, GIF, WebP, SVG, AVIF or BMP) into a note to save it in the notebook and insert `![](path)` at the caret, or after the line it is dropped on. Each picture gets a line of its own, so the rich view shows it. It goes where it was pasted or dropped even if the caret moved while it saved, following that spot through edits made meanwhile (if the text around it is deleted, it goes where the deletion was), and a selection made meanwhile is kept, not replaced. A picture that can't be saved is said in the status bar. Files that aren't pictures are ignored.
- **Where it's saved** follows Save Pasted Pictures In:
  - **The notebook's picture folder:** one folder for the whole notebook, named by Picture Folder (`attachments` by default), from the notebook's top level. It may be nested (`assets/images`).
  - **A folder beside each note:** `Meeting.assets/` next to `Meeting.md`.
- **Names.** A dropped or copied file keeps its name. A screenshot or a picture copied from a page (the clipboard calls those `image.png`) is named after the time: `Pasted image 2026-11-10 14.03.22.png`. A name that is taken gets a number (`Holiday 2.png`), comparing without case. A picture whose bytes match one already in that folder (Copy Picture from another note, then paste) links to that file instead of saving a copy.
- **The link** is relative to the note, with spaces and brackets percent-encoded (`../attachments/Pasted%20image….png`).
- **Moving keeps links working.** Pictures never move with a note, since other notes may show them.
  - Moving or renaming a note rewrites its own links to pictures so they still reach the same files.
  - Moving or renaming a picture, or a folder with pictures in it, rewrites the links to them in every note.
  - A link keeps its style (`<…>` or encoded) and any `#fragment`. Links that still resolve, and links to anything other than a picture, are left as written.
  - An open note is changed in its tab, as an undoable step, and saved straight away if it had no unsaved edits. A closed note is changed on disk. The status bar says how many notes changed.
- **Unused pictures.** When a save removes the last link to a picture in the notebook, or a deleted note was the last to show one, and When a Picture Is No Longer Used is Offer to Delete It, the status bar says so with a Move to Trash button (the system Trash on the desktop, the notebook's Trash elsewhere, §6.1). The offer stays up for twelve seconds; a save that shows one of its pictures again takes it down. The button checks again before it deletes: a picture a note shows by then (after an Undo, say) is kept, and the status bar says so. Set to Keep It, nothing is said.
- **Picture preview.** A click on a picture in a note shows it alone over the window, fitted to it (never enlarged), with its file name and zoom buttons above it.
  - **Zoom** from fitted up to 800%: pinch or ⌘-scroll about the pointer, the − and + buttons or keys, `0` to fit, `1` for 100%. A double-click, or a click on the percentage, switches between fitted and 100% (or 2× the fit for a picture smaller than the window).
  - **Pan** a picture larger than the window by dragging it, scrolling, or with the arrow keys (⇧ for bigger steps). Its edges never come in past the window's.
  - **Close** with Escape, the × button, or a click outside the picture. It also closes when another note comes to the front, and a picture command from a menu left open does nothing once the note no longer has that picture at that spot.
  - A right-click opens the picture menu, as in the note.
- **Picture menu.** A right-click on a picture in a note, or in the preview, opens:
  - **Preview Picture** (in the note only): the preview above.
  - **Open in Tab:** the file in a tab of its own. **Open in Default App** and **Reveal in Finder** (Show in Explorer, Show in Folder) where the notebook is a folder on this computer. These are off for a web picture or a missing file.
  - **Copy Picture:** the picture on the clipboard, as a PNG. **Copy Markdown:** its `![](…)` as text.
  - **Edit Description…:** changes the alt text (the words between `[` and `]`, read out in the picture's place), as an undoable step.
  - **Rename Picture…:** renames the file; links to it in every note follow (see Moving keeps links working).
  - **View Link Text:** closes the preview, and selects the picture's `![](…)` and shows it as text until the caret leaves it.
  - **Delete Picture:** takes the picture out of the note (its line too, when it was alone on one), as an undoable step, and moves its file to the Trash (§6.1). When another note, or another spot in this note, also shows the file, it asks first and lists those notes: Keep File (the default) takes the picture out of this note only, Delete File also deletes the file, Cancel does nothing. Undo puts the picture and its file back; Redo takes them out again, but keeps the file when a note shows it by then (other than the notes listed when Delete File was chosen).
- **Find Unused Pictures…** (command menu) lists every picture no note links to, with a thumbnail and its folder, all ticked. Move to Trash moves the ticked ones that still aren't used when it is clicked to the Trash (§6.1); in the notebook's Trash the status bar offers Undo. A picture counts as used when a note links to it with `![](…)` or `[…](…)` or embeds it with `![[name.png]]` (outside code), or shows it with an HTML `<img src="…">` (anywhere, code included). A wiki embed has no folder of its own, so it counts for the picture it names from the note's folder or the top level, and for every picture whose path ends with what it names: `![[a.png]]` keeps every `a.png` in the notebook. The same rule decides the no-longer-used offer and which other notes Delete Picture lists. Names are compared in one Unicode form (NFC), and without case where the notebook's disk ignores case (the desktop on macOS and Windows) or Puffinote can't tell (the NAS, a folder picked in the browser). Unsaved text in open tabs counts, and so do notes in hidden folders, whatever Show Hidden Files says. If a note can't be read (a NAS gone offline, say), it stops with an error instead of listing anything; the no-longer-used offer and Delete Picture's question hold back the same way.

### 10.7 Find in Note

- **Find in Note… (⌘F)** opens a bar over the note's top right corner with the caret in its box. It starts with the selected text when that is on one line, and otherwise with the text it had when it was last closed.
- Matching ignores case, and the text is matched as typed (no patterns). Every match in the note is marked, and the current one more strongly; the marks are drawn for the rows in view and a screen either side, and follow scrolling. The bar counts them ("3 of 12", or "No results").
- Typing selects the first match from the selection onwards. Enter or ⌘G selects the next match, and ⇧Enter or ⇧⌘G the previous one, wrapping around the note. Matches follow edits while the bar is open.
- Escape or the close button closes the bar and puts the caret back in the note, with the match selected.
- **Find and Replace… (⌥⌘F; Ctrl+H elsewhere)** opens the same bar with a Replace row under it, starting from the selection as ⌘F does. The caret goes to the Replace box when there is text to find, else to the Find box. ⌘F leaves the row as it is; closing the bar hides it. The Replace box keeps its text between openings.
  - The replacement is taken as typed (no patterns or `$1`), and matching is the same as Find's: ignoring case.
  - **Replace** (Enter in the Replace box) replaces the match the selection is on, as one undo step, and selects the next match after it (wrapping). With the selection on no match, it only selects the next one.
  - **Replace All** replaces every match in the note as one undo step, with no limit on the count, and leaves the caret after the last replacement. The status bar says how many ("Replaced 3 matches", or "Nothing to replace"). It rewrites only the span from the first match to the end of the last.
  - In a note Puffinote won't save (an encoding it had to guess, §5), Replace and Replace All are off.
- Searching every note is the sidebar's Search panel (§6.3), opened with ⇧⌘F.

## 11. Markdown processing

An incremental, line-based tokenizer with block state and a checkpoint cache. There is no document AST. Inline tokenization runs within each line. The engine (`@puffinote/markdown`) emits semantic tokens only — types, source ranges and roles (`content` or `marker`) — and knows nothing about styling or concealment.

### 11.1 Block state and checkpoints

The tokenizer carries block state from line to line: normal, fenced code (fence character and length), indented code, blockquote, list, table, and front matter. Each line stores the state it starts in, so any line can be re-tokenized without starting from the top.

The list part of the state is the content column of each open list item (just past its marker: column 2 for `- a`). Indentation inside an item counts from that column, so after a blank line `    - b` or a tab and `- b` under `- a` is a nested item, not indented code; indented code inside an item needs four columns past the item's text; and a fence indented under an item opens and closes there. A blank line keeps the items open. A non-blank line left of an item's text closes it, unless it is prose straight after a non-blank line (a lazy continuation).

### 11.2 Incremental re-tokenization

An edit re-tokenizes from the first changed line and stops once the block state matches the previous run again. A common edit re-runs a short tail. The worst case is O(n): an opening fence at the top turns the rest of the file into code.

### 11.3 Supported constructs

| Construct        | Syntax                      | Rendering                         |
| ---------------- | --------------------------- | --------------------------------- |
| ATX heading      | `#` … `######`              | Type scale (§10.4)                |
| Setext heading   | text over `===` / `---`     | Same as `#` / `##`                |
| Bold, italic     | `**x**` `__x__` `*x*` `_x_` | Bold, italic                      |
| Bold italic      | `***x***`                   | Bold italic                       |
| Strikethrough    | `~~x~~`                     | Struck                            |
| Highlight        | `==x==`                     | Highlighted                       |
| Inline code      | `` `x` ``                   | Monospace on a tint               |
| Fenced code      | ` ```lang `                 | Monospace band                    |
| Indented code    | four spaces                 | Monospace band                    |
| Blockquote       | `>` (nested `>>`)           | One bar per level                 |
| Lists            | `-` `*` `+` `1.`            | Bullet or number                  |
| Task             | `- [ ]` `- [/]` `- [x]`     | Checkbox, three states (§11.4)    |
| Link             | `[text](url)`               | Styled; ⌘-click opens (§10.3)     |
| Autolink         | `<https://…>`, bare URLs    | Styled                            |
| Image            | `![alt](url)`               | The picture, on a line of its own |
| Horizontal rule  | `---` `***`                 | A rule                            |
| Table            | GFM pipe tables             | Styled cells                      |
| Hard break       | trailing `\` or two spaces  | Line break                        |
| Escape           | `\*`, `\_`, …               | The literal character             |
| Front matter     | leading `---` … `---`       | A dimmed block                    |
| Wiki link, embed | `[[target]]`, `![[target]]` | Styled; ⌘-click opens (§10.3)     |

An ATX heading's optional closing sequence (`# Done #`: blanks, then a run of `#` at the end of the line) is heading markup too: the editor hides it with the opening `#` and the export leaves it out. A setext heading carries a zero-length heading marker that holds its level, so consumers read the level from the line's first token whichever spelling made it. A lone `---` with no paragraph above it is a rule. A list item or task after a blockquote prefix (`> - [x] done`, `> 1. one`) is a list item inside the quote: it gets its bullet, number or checkbox in the editor, and the export renders it as a list inside the `<blockquote>`.

Nothing flickers while a list item is typed under a line (§10.2). A `-` setext underline needs at least two dashes: a lone `-` under a paragraph is the start of a list item and stays text. A table needs a delimiter row with at least one `|`, cells of `-` with an optional `:` at either end, and as many cells as its header row, so a `-` typed under `name | price` doesn't make a table.

A bare URL starts with `http://` or `https://`, in any case, at a word boundary, and runs to whitespace or `<`. Trailing punctuation (`.` `,` `;` `:` `!` `?`) is left out. A trailing `)` stays only while it closes a `(` inside the URL (`…/wiki/Foo_(bar)`), as in GFM, so a URL in parentheses ends before the sentence's `)`.

### 11.4 Task states

One character between the brackets, three states:

| State       | Source | Drawn as                  |
| ----------- | ------ | ------------------------- |
| Not started | `[ ]`  | An empty box              |
| In progress | `[/]`  | An accent dash            |
| Done        | `[x]`  | A check on an accent fill |

- `[X]` also reads as done. Any other character (`[-]`, `[?]`) is literal text.
- The three states differ by shape first. Colour is never the only signal. A done item's text is dimmed and struck through.
- **A click on the box completes it**, and a click on a done box clears it. A click never stops at in progress on the way. To mark a task in progress, use ⌥⌘L (Next Task Status).
- A state change rewrites the one character between the brackets and leaves the caret where it was, unless Move ticked tasks to the bottom moves the item (§10.1).
- The HTML export draws the same three boxes as inline SVG.

### 11.5 Links

See §10.3 for how links behave in the editor. A relative link is resolved against the note's folder, for pictures and for ⌘-click, which opens the notebook file it names (§10.3). It is never handed to the OS as a URL.

### 11.6 Out of scope

Full CommonMark/GFM edge cases (lazy continuation, complete emphasis precedence, link reference definitions, nested HTML blocks); raw HTML rendering; footnotes, definition lists, math, and diagrams.

### 11.7 Determinism and round trip

Editing never rewrites untouched bytes. The document is the source of truth and rendering is derived from it. Saving writes exactly the buffer, with its line endings kept. Formatting commands and checkbox changes edit the source minimally and predictably.

## 12. The CLI

`puffinote` runs from source with Node (`just cli …` in the repo). Every command only reads, and a `-` file argument reads stdin.

| Command                       | Output                                                                 |
| ----------------------------- | ---------------------------------------------------------------------- |
| `puffinote [<path>]`          | Opens the folder (or a file's folder) in the desktop app (macOS)       |
| `tree [<notebook>]`           | The file tree, folders first, dotfiles hidden                          |
| `search <query> [<notebook>]` | `path\thits\tsnippet`, ranked by hits; `-n/--max` (default 200)        |
| `info [<notebook>]`           | Notebook path, file, note and other counts, total bytes                |
| `html <file>`                 | A self-contained HTML document; `--fragment`, `-o/--output`, `--force` |
| `cat <file>`                  | The text, decoded the way the editor reads it                          |
| `tokens <file>`               | The token stream (for debugging the tokenizer)                         |
| `stats <file>`                | Words, characters, lines, reading minutes                              |
| `outline <file>`              | The headings, indented by level                                        |

`html -o` writes only a name ending in `.html` or `.htm` unless `--force` is given, and never the note it renders (the same file under any path, symlink or hard link, `--force` or not); either refusal is an error. A notebook argument defaults to the current folder. Errors print `puffinote: <message>` on stderr and exit 1. A usage error prints the message and then the command's help.

## 13. Non-functional requirements

- **Responsiveness.** Typing re-tokenizes only the changed tail (§11.2). Lines below an edit keep their tokens as they are, since a line's tokens count from its own start, so typing near the top of a long note isn't slower than typing near the bottom. The editor renders only the lines it needs.
- **Accessibility.** The tree, tabs, menus and dialogs carry ARIA roles and labels and a visible focus ring. Concealment never removes text from the accessibility tree. Everything works from the keyboard. A dialog takes focus when it opens, Tab and Shift+Tab cycle through its controls without leaving it, and focus goes back where it was when it closes. In the note, a task's checkbox is named after the task and its state ("Buy milk, not done"), and when the caret settles on another line a polite live region reads that line's number and source text. A full screen-reader mirror of the note (reading by word and character, announcing formatting) is not built yet.
- **Themes.** Light and dark, following the system by default. Colours are tokens; dark uses neutral greys, as Cellestra does. On macOS the desktop window hides its title bar and draws its own 28px one in the chrome colour, showing the window title. The desktop window's own background matches the theme, and a closing window hides before its page unloads, so neither a resize nor quitting flashes white.
- **Security.** The desktop renderer is sandboxed with context isolation, and its bridge only touches folders the user granted. The NAS server confines paths to their roots. Notes cannot run script: the built client has a strict CSP.
- **Portability.** The engine packages (`@puffinote/text`, `@puffinote/markdown`) use no DOM or Node APIs (ESLint enforces this). They run in a browser, a worker and Node.
