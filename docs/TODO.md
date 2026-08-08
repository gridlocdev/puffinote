- [x] Puffinote: UI bug where clicking a tab in the tab bar at the top double highlights content in the sidebar. The sidebar should always simply match the highlight of the single file that corresponds to the active tab on the top, if there is a tab open.
- [x] Cellestra: active tab should have a colored underline underneath it in addition to the bold
- [x] Cellestra: auto resize columns to fit contents while in a table
- [x] Cellestra: header column format should also copy when adding new line after
- [x] Cellestra: editing text in a column that has word wrapped should word wrap as well (textbox should contain the contents and the cursor behavior should be expected)
- [x] Cellestra: double clicking the title bar to expand the column should widen it to the widest non-word-wrapped column, instead of all column lengths
- [x] Cellestra: while editing a value with white text in the light theme (table header) the text remains white, it should be black so you can see it against the background
- [x] Cellestra: pre-fill previous commands done in the current session at the top of the command bar so you can quickly do them again
- [x] Cellestra: change "?" keyboard shortcut to cmd+? since I do actually type "?" into cells from the main navigation view
- [x] Cellestra: if text is autocompleted while entering, if I use a different case than the autocomplete does it should stop autocompleting and type the correct capitalization on the letter
- [x] Cellestra: saving the document should append "- Saved" to the top title bar for a brief period
- [x] Cellestra: shift+return to enter newline in editing textbox
- [x] Cellestra: when editing cell, keep the current behavior of the cell remaining on screen, but ensure it continues being edited without the odd delay it has currently for new characters
- [x] Cellestra: when scrolling and a table's contents are on screen, visually show table's column titles in the top column headers (and remember to also do the opposite, if scrolling past the table end it should revert back to not include the titles at the top headers)
- [x] Typage: bug fix for unexpected behavior that adds extra newlines unexpectedly
- [x] Plaintain: Create a new plaintext editor (use the plan file)
- [x] Pivotask: Create a new todos editor (use the plan file)


- [x] Across all apps, add a settings menu to be able to configure the options present in the configuration file.
    - [x] Cellestra
    - [x] Puffinote
    - [x] Typage
    - [x] Plaintain
    - [x] Pivotask

- [x] Across all apps, Center-align the document name in the title bar
    - [x] Cellestra
    - [x] Puffinote
    - [x] Typage
    - [x] Plaintain
    - [x] Pivotask

    One shared control, `shared/Puffinote.Components/Shell/DocumentTitleBar.cs`. There was nothing to
    align before this: every app left its title to the native title bar, which macOS centres and the
    other platforms left-align, and which showed whatever `Window.Title` said — suffixes and all. The
    bar is the app's own, so it can show the document's name alone, centred, with the "edited" dot and
    Cellestra's transient "Saved" badge. `DocumentTitleBar.Extend` merges it with the window on macOS
    (extending the client area keeps the traffic lights and drops only the system's title text), which
    is Cellestra PRD §6.1's "unified with the window". `Window.Title` is unchanged — it is what the
    Window menu and app switcher show. **Not verified on a real Mac**: the container is headless, so
    the extended-client-area path (traffic-light inset, drag, double-click zoom) is reasoned about, not
    seen. Windows and Linux keep their native title bar with this one below it; taking those over means
    owning the caption buttons too, which is its own piece of work.

- [x] If not present, implement zooming in/out across all apps (puffinote and typage)(cmd + zooms in, cmd - zooms out)
    - [x] Cellestra
    - [x] Puffinote
    - [x] Typage
    - [x] Plaintain
    - [x] Pivotask
    - Zoom In / Zoom Out / Actual Size are defined once in `Puffinote.Components` (`ZoomCommands` over the
      shared `ZoomScale` ladder, 50%–400%) and spliced into each editor's own catalog, so all five apps carry
      one id, one title and one chord. ⌘= in, ⌘− out, **⌥⌘0** reset — reset avoids plain ⌘0 because that is
      already the last rung of Typage's heading ladder (⌘1–⌘6 apply a level, ⌘0 removes it), and one chord
      meaning two things depending on the open Puffinote tab is the thing this repo keeps saying not to do.
      Puffinote itself has no zoom code: it binds the active pane's catalog.

- [x] Plaintain: pinch-to-zoom — and with it Typage, Cellestra and Pivotask, since all four share `ZoomScale`.
    - Wired once, in `Puffinote.Components` (`PinchZoom`, over the `PinchZoomGesture` accumulator), against
      the same read/write pair each editor already hands `ZoomCommands`. Each view attaches it in one line.
    - The gesture is watched in both places it can be observed. macOS reports a trackpad pinch as its own
      gesture (`PointerTouchPadGestureMagnify`, carrying an NSEvent magnification); Windows and X11 have no
      such event and their drivers report it as **ctrl+wheel** — which is also what a mouse user presses to
      zoom, so the two arrive as one case. The wheel route has to tunnel: the surfaces underneath (Plaintain's
      editor, Cellestra's grid) handle the wheel themselves and mark it handled, so a bubbling handler would
      scroll the document instead of zooming it.
    - A pinch lands on the same stops as ⌘= / ⌘−, rather than zooming continuously. One ladder for the family
      is the point: a reader who pinches to "137%" and then presses ⌘= has to be told what the next size even
      is, and every intermediate value would re-lay out a whole sheet on the way past. About 0.15 of
      magnification (or one wheel notch) buys a stop, so a full-hand pinch covers roughly 100% → 300%; the
      residue is dropped after 250ms of quiet, so one pinch can never finish the one before it.
    - Not done: a touchscreen two-finger pinch (Avalonia's `PinchGestureRecognizer`). Same word, different
      device — and adding that recognizer changes how touch scrolling competes on all four surfaces, which is
      a decision for whenever a touch device is actually a target.

- [ ] Cellestra: editing text in a column that has word wrapped should word wrap the popup text editor as well (the textbox popup should contain similar contents and the cursor behavior should be expected)
- [ ] Cellestra: If text is autocompleted while entering, It should also not autocomplete the title of a table, only the distinct value of one of its preceding rows in that column.

- [ ] Cellestra: shift+return to enter newline in editing textbox has unexpected UI behavior in the top formula textbox, it should only extend the height of the specific textbox editor my cursor is in. There are two textbox editors, one inline, and one in the formula bar. 


- [ ] Pivotask: The UI significantly drifts from the prototype. Launch the prototype and compare its UI to the current implementation. The prototype is a reference for the intended design, and the current implementation should be updated to match it.

- [x] Plaintain: The cursor is slightly offset from the text in the editor.

    The surface was not drawing in a monospace font at all. `FontFamily`/`FontSize` were adopted from
    `TextBlock` via `AddOwner`, and `TextElement`'s text properties **inherit** — so the ambient UI font
    of whatever hosted the editor (the themed window, or a Puffinote tab) beat the monospace default
    underneath it, and nothing ever sets a family explicitly. The surface then measured one advance from
    `"0"` and stepped every column by it while the renderer laid out proportional glyphs, so the error
    accumulated along the row: measured against Inter at 14pt, a 16-character line drew 130.3px wide
    where the column arithmetic placed the caret at 140.0px — a caret sitting more than a full character
    past the text by the end of a short line, and further out on a longer one. The caret was only the
    visible symptom; selection, the find highlights, click-to-position and the wrap column were all
    reading the same wrong width. Both properties are now the editor's own non-inherited registrations,
    so a host can still set a font deliberately but can no longer leak one in. `CaretGeometryTests`
    covers the inheritance and the invariant the caret is placed by — that `n` columns of text occupy
    exactly `n * CharWidth`.
- [ ] Plaintain: ⌥⌘W (Word Wrap) collides with Puffinote's ⌥⌘W (Close All Tabs) — inside a Plaintain tab one of them wins and the other silently doesn't. Move one; the shell's is the older claim.
- [ ] Pivotask: the board has no way to rename a group, delete a group, delete a task, fold completed tasks, or create a label. `BoardCommands` has all five, tested, with no caller outside the tests — the commands are done and the affordances aren't.
- [ ] Puffinote: an external edit (a `git pull`, an edit in another app) refreshes the file tree and the search index but never reloads an open tab, so the pane keeps showing stale content and can overwrite the change on save. Proposal 0004 Phase 4 asks for this and it applies to every pane type, not just boards.
- [ ] Pivotask: verify the `.pivotask.json` compound-extension claim on a real Mac. The plist ships the exported `com.pivotask.board` UTI; if Launch Services won't honour a two-part extension tag, proposal 0004 §10.3 says strip the document-type claim entirely rather than fall back to claiming bare `json`.
- [ ] Pivotask: drag-and-drop is the simple press-and-release version. Phase 5's refinement is still open — a drop indicator, precise placement between cards, and a way to cancel a drag in flight.
- [ ] Pivotask: no keyboard path around the board — move between cards and columns, and move a task, without the mouse. The Move command exists; nothing binds it to a chord.
- [ ] Plaintain: measure the 50 MB load path before optimising it (proposal 0003 §9.5). The geometry was written against that number but never tested against it, and there is no benchmarks project in the area.
- [ ] Plaintain: converting a file's encoding on save is not implemented — only confirming the guessed one. An explicit "save this as UTF-8" is what actually rescues a Latin-1 file, and it must stay an explicit action, never implicit.
- [ ] Plaintain: detect mixed line endings and offer to normalise them. Offer, not force — a file with both is usually a merge artefact, but sometimes it is deliberate.
- [ ] Plaintain: no crash-recovery snapshot and no external-change detection, both of which Typage has (its PRD §7.1). A plaintext editor holding an unsaved 50 MB log is exactly where losing the buffer hurts most.
- [ ] Shared: the rule-of-three check both proposals defer to Phase 5, now that the duplication is real and observable — Plaintain's `FindBar` against Typage's, and Pivotask's label chip and relative-date pill against anything similar. Promote to `Puffinote.Components` only where the duplication genuinely is.
- [ ] Puffinote: `docs/puffinote/ARCHITECTURE.md` §13's folder tree still lists the pane files as they were two panes ago (it names an `IOutline.cs` and an `IEditorPaneFactory.cs` that don't exist). Refresh it now that `PlainTextPane` and `TaskBoardPane` are there.
- [ ] Puffinote: zoom is per-pane and per-session. The shell binds each editor's own zoom commands, so ⌘= works in every tab, but nothing persists the factor — every tab reopens at 100%, while the four standalone apps each remember theirs. Decide whether the shell should carry it in `SessionStore` (per tab, restored with the tab) or in `PuffinoteSettings` (one override for the window); the two answers behave differently for a notebook of mixed file types.
- [ ] Puffinote: decide whether the shell's title bar should flash "— Saved" the way Cellestra's now does. The editor raises a `Saved` event the shell could subscribe to; whether a notebook tab wants that acknowledgement is a design call, not an oversight.
