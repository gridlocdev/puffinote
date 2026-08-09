- [ ] Puffinote, Pivotask, and Plaintain: copy the way that Cellestra does its menu bar being native on macos. (For example, the app name, an about screen, the separate sections i.e file, edit, etc.)

- [ ] Cellestra: in the radial menu display the name of the action immediately in the middle circle, and move the original hover behavior of the additional tooltip to show up if the user continues hovering a specific option for a couple seconds

- [ ] Cellestra: in the radial menu settings menu change "Select..." with an icon button with a pencil that when hovered says "Edit". Also add draggable indicator to the left of each of the items, and add clicking and dragging to reorder them. Also, just show the name of the command, you don't need to say "Segment <number>" over and over again, it wastes much-needed space.

- [ ] Cellestra: research online to see how other spreadsheet apps like LibreOffice and Excel are architected to handle "Cmd+A" for select all (do they render all cells or just some), currently it literally selects all cells, so I think cellestra renders something incorrectly

- [ ] Plaintain: Add multiple selection with cmd+d like how VSCode does it.

- [ ] Plaintain: Mute the "current line highlight" to a dimmer color, so that it still highlights but isn't visually distracting.

- [ ] Puffinote: an external edit (a `git pull`, an edit in another app) refreshes the file tree and the search index but never reloads an open tab, so the pane keeps showing stale content and can overwrite the change on save. Proposal 0004 Phase 4 asks for this and it applies to every pane type, not just boards.

- [ ] Pivotask: verify the `.pivotask.json` compound-extension claim on a real Mac. The plist ships the exported `com.pivotask.board` UTI; if Launch Services won't honour a two-part extension tag, proposal 0004 §10.3 says strip the document-type claim entirely rather than fall back to claiming bare `json`.

- [ ] Pivotask: drag-and-drop is the simple press-and-release version. Phase 5's refinement is still open — a drop indicator, precise placement between cards, and a way to cancel a drag in flight.

- [ ] Pivotask: no keyboard path around the board — move between cards and columns, and move a task, without the mouse. The Move command exists; nothing binds it to a chord.

- [ ] Plaintain: measure the 50 MB load path before optimising it (proposal 0003 §9.5). The geometry was written against that number but never tested against it, and there is no benchmarks project in the area.

- [ ] Plaintain: converting a file's encoding on save is not implemented — only confirming the guessed one. An explicit "save this as UTF-8" is what actually rescues a Latin-1 file, and it must stay an explicit action, never implicit.

- [ ] Plaintain: detect mixed line endings and offer to normalise them. Offer, not force — a file with both is usually a merge artefact, but sometimes it is deliberate.

- [ ] Plaintain: no crash-recovery snapshot and no external-change detection, both of which Typage has (its PRD §7.1). A plaintext editor holding an unsaved 50 MB log is exactly where losing the buffer hurts most.

- [ ] Shared: the rule-of-three check both proposals defer to Phase 5, now that the duplication is real and observable — Plaintain's `FindBar` against Typage's, and Pivotask's label chip and relative-date pill against anything similar. Promote to `Puffinote.Components` only where the duplication genuinely is.

- [ ] Puffinote: decide whether the shell's title bar should flash "— Saved" the way Cellestra's now does. The editor raises a `Saved` event the shell could subscribe to; whether a notebook tab wants that acknowledgement is a design call, not an oversight.
