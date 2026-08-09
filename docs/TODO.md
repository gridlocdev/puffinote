- [ ] Pivotask: a few differences from `pivotask-prototype/pivotask-prototype.html` remain (read it and the
  four screenshots beside it — the prototype needs no launching, its whole behaviour is in that one file).
  Closed so far: the due-date and completion rules, single-click-to-open, the card's step/note facts, the
  list rendering the same card as the board, the list's section chrome and finished-tasks fold, rename and
  delete on a group heading, the board losing the collapse caret it should not have had, the empty-column
  sentences, the header strip (search / filter pills / segmented view switch), and the `/` and `V` keys.
  Still different:
    - [ ] **"Add a task" is a permanent text box.** The prototype shows a `+ Add a task` *button* that
      becomes an input when clicked (placeholder "Task name, then Enter"), keeps focus for the next one
      after Enter, cancels on Escape, and commits a non-empty value on blur. Ours is always an open box,
      so every column carries an empty field whether or not you are adding anything.
    - [ ] **A new group is not put straight into rename.** The prototype focuses the new name and selects
      it, so "Add a list" is one gesture rather than two.
    - [ ] **`TaskDialog` has never been audited against the drawer screenshot line by line** — the group
      picker, the `+` add-to-task menu (Labels / Due date / Steps, with an on/off state), the steps
      progress bar and its percentage, "Delete all", and the "Delete task" / "Done" footer. This is the
      one surface where the gap is still unmeasured rather than merely unclosed.
    - [ ] The prototype has a brand block at the far left of its strip (mark + "Pivot" / "same tasks,
      turned 90°"). Deliberately not copied: these apps name the open document in the shared
      `DocumentTitleBar` above, and a second app-name band under it would be the one piece of the
      prototype that is a web page rather than an app. Worth a decision rather than a silent omission.

- [ ] Pivotask: two of the five command-without-an-affordance gaps are still open. Renaming a group,
  deleting a group, and folding completed tasks now have affordances (the group heading, and the list's
  `finished · N` subhead). **`BoardCommands.DeleteTask` and `BoardCommands.AddLabel` still have no caller
  outside their tests** — the prototype's drawer has a "Delete task" button in its footer and creates
  labels from its `+` menu, and `TaskDialog` has neither: it says "This board has no labels yet." with no
  way to make one.
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
