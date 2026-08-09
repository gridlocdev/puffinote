- [ ] Pivotask: the UI still drifts from `pivotask-prototype/pivotask-prototype.html` (read it and the four
  screenshots beside it — the prototype needs no launching, its whole behaviour is in that one file).
  Landed so far: the due-date and completion rules, single-click-to-open, and the card's step/note facts.
  What is still different, verified against the prototype source and screenshots:
    - [ ] **List rows are a different, thinner control than board cards.** The prototype renders *the same*
      `taskHTML` in both views, so a list row shows labels, the due pill, the step count and the note
      marker. `TaskListView.BuildRow` shows only checkbox + title + star + due, so the same task reads as
      two different tasks depending on the view. Fixing this by using `TaskCard` in both is also the
      rule-of-three answer, since the two row builders already duplicate each other.
    - [ ] **The list has no per-group chrome.** The prototype gives each section a collapse caret, an
      editable name, a dotted leader, a count, a delete button, an "add a task" foot, and — the one with
      real behaviour — a `finished · N` subhead that folds the completed tasks. Puffinote's list has a bold
      group label and nothing else, and it *skips* empty groups where the prototype shows them with a
      message.
    - [ ] **Column headers cannot rename or delete a group.** The prototype makes the name
      `contenteditable` and puts an × beside the count; deleting a non-empty group confirms first, and
      deleting the last one seeds a replacement rather than leaving no groups. This is the same gap the
      "BoardCommands has five commands with no caller" item below names.
    - [ ] **The board column has a collapse caret the prototype does not have.** Collapse is a list-view
      affordance there (`.sect-head` has the caret, `.col-head` does not).
    - [ ] **"Add a task" is always an open text box.** The prototype shows a `+ Add a task` button that
      becomes an input on click (placeholder "Task name, then Enter"), keeps focus for the next one after
      Enter, cancels on Escape, and commits a non-empty value on blur.
    - [ ] **An empty column says nothing.** The prototype writes "Empty. Add the first task below." or
      "Nothing here matches the filter." — two different sentences, because they are two different
      situations.
    - [ ] **The header strip is not the prototype's.** It has the brand block (mark + "Pivot" / "same
      tasks, turned 90°"), a search field with a magnifier and the placeholder "Search tasks", filters as
      pills with the active one filled, and a segmented BOARD | LIST switch with icons. Ours has
      underline-style filter buttons, one "Board / List" toggle, and a bare "Search" box on the right.
    - [ ] **No `/` or `V` keys.** `/` focuses the search box and selects it; `V` toggles board/list. Both
      are suppressed while typing or while the drawer is open.
    - [ ] **The trailing button is worded "+ Group".** The prototype says "Add a list" on the board and
      "Add a group" in the list, and focuses the new group's name for renaming straight away.
    - [ ] Compare `TaskDialog` against the drawer screenshot in detail — group picker, the `+` add-to-task
      menu (Labels / Due date / Steps with an on/off state), the steps progress bar and percentage, "Delete
      all", "Delete task" — this one has not been audited line by line yet.

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
- [ ] Puffinote: decide whether the shell's title bar should flash "— Saved" the way Cellestra's now does. The editor raises a `Saved` event the shell could subscribe to; whether a notebook tab wants that acknowledgement is a design call, not an oversight.
