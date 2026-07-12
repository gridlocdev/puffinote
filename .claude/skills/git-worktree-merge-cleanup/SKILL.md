---
name: git-worktree-merge-cleanup
description: >-
  Rebase and land pending branches or agent worktrees onto main with a linear,
  merge-commit-free history, verify them against this repo's build gate, then
  safely delete the merged branches and worktrees. Covers the two failures this
  devcontainer reliably produces: worktrees registered against a dead `/workspace`
  mount (`fatal: not a git repository: /workspace/.git/worktrees/...`) and a racy
  index that makes unchanged files report `error: Your local changes to the
  following files would be overwritten by merge`. Use this whenever someone asks to
  rebase/merge/land pending worktrees or branches, clean up or prune merged
  branches, asks which branches still have unmerged work, or hits either of those
  two git errors.
---

# Landing pending worktrees & branches, then cleaning up

## What this skill is for

Taking the branches left behind by agent worktrees (or ordinary feature branches),
replaying them onto `main` as a **linear history with no merge commits**, proving
they still build and pass tests, and then removing what is genuinely redundant.

The mechanical part is `rebase` + `merge --ff-only`. The part that actually costs
time is this devcontainer's two git pathologies, and the several ways you can fool
yourself into thinking a branch has (or hasn't) unmerged work. Read the
**Environment faults** and **Traps** sections before you touch anything — both
failures look like data loss and are not.

## Golden rules

1. **Prove there is no uncommitted work before any destructive step.** Not
   `git status` alone — see the audit below.
2. **Record the original branch tips** so every step is recoverable.
3. **`rebase` then `merge --ff-only`.** Never plain `git merge`; `--ff-only` is what
   guarantees "no merge commits", and it *fails loudly* instead of silently
   creating one.
4. **Verify before you delete.** The build gate is the point of no return, not the
   merge.
5. **Delete with `git branch -d`, never `-D`.** The lowercase form refuses anything
   not fully merged; that refusal is a feature and your last safety net.
6. **Re-check state immediately before destructive ops.** Something else may be
   driving this repo concurrently (see Traps).

## Step 1 — Survey

```bash
git worktree list
git --no-pager branch -vv
git status --short
```

For every candidate branch, get ahead/behind and the file overlap:

```bash
for b in <branches>; do
  printf '%-40s %s ahead, %s behind\n' "$b" \
    "$(git rev-list --count main..$b)" "$(git rev-list --count $b..main)"
  git diff --name-only "$(git merge-base main $b)" "$b"   # overlap → conflict risk
done
```

**A branch that is `0 ahead` has nothing to land.** Its tip is already an ancestor
of `main` and it can be deleted outright. Don't rebase it, don't "merge" it.

**Order the merges by file overlap.** Branches touching disjoint trees (e.g. `app/`
vs `features/EditMarkdown/`) land cleanly in any order. Two branches touching the
same files *will* conflict on the second one — land them adjacently so the conflict
is fresh in your head, and expect the second to need a real resolution.

## Step 2 — Safety audit (before anything destructive)

`git status` being clean is **not** sufficient here, because the racy-index fault
below makes status and checkout disagree. Prove content equality against `HEAD`:

```bash
git -C <worktree> diff HEAD --stat     # MUST be empty — this is the real check
git -C <worktree> status --short       # untracked files show up here as ??
git stash list                         # stashes are repo-global; check once
```

Record the tips so any mistake is a one-line recovery:

```bash
for b in <branches> main; do echo "  $b = $(git rev-parse $b)"; done
```

If a file *is* genuinely modified, stop and surface it. Everything downstream
assumes the worktrees are clean.

## Step 3 — Environment faults (fix these first, they will bite)

### Fault A — worktrees registered against a dead mount

Symptom:

```
fatal: not a git repository: /workspace/.git/worktrees/<name>
```

`git worktree list` prints paths under `/workspace/...` that don't exist, while the
real directories sit in `/workspaces/Puffinote/.claude/worktrees/<name>`. The
worktrees were created when the repo was mounted at a different path. Both the
worktree's `.git` file and the repo's `.git/worktrees/<name>/gitdir` hold stale
absolute paths.

Note these worktrees are usually **locked**, which is why `git worktree prune`
leaves them alone and they linger.

Fix — rewrites only the pointers, never file content:

```bash
git worktree repair .claude/worktrees/<a> .claude/worktrees/<b> ...
git worktree list        # paths should now be under /workspaces/Puffinote
```

### Fault B — the racy index ("local changes" on unmodified files)

Symptom: a rebase or checkout aborts with

```
error: Your local changes to the following files would be overwritten by merge:
        <file>
```

...while `git status` says clean and `git diff HEAD` is empty.

**This is not data loss.** Confirm the file is byte-identical on all three sides —
if these three hashes match, there are no local changes, full stop:

```bash
git hash-object <file>          # working tree
git rev-parse HEAD:<file>       # HEAD
git ls-files -s <file>          # index (2nd field)
```

Cause: git's stat cache is *racily clean*. This git build compares mtimes at
one-second granularity, and a checkout writes the files and the index within the
same second, so the entries look potentially-dirty and `unpack_trees` refuses to
overwrite them.

What does **not** work (don't waste the cycles):

- `git reset --hard` — rewrites files *and* index in the same instant, recreating
  the race.
- Backdating mtimes (`git ls-files -z | xargs -0 touch -d '1 hour ago'`) then
  refreshing — the rebase's own internal checkout of `onto` immediately rewrites
  them with fresh mtimes, so the race returns mid-rebase.

**What works — sidestep the worktree entirely.** Detach each worktree's HEAD, which
frees the branch ref without changing any file (it's a no-op checkout at the same
commit, so the race can't trigger), then do the rebase in the **main repo**, whose
index is healthy:

```bash
git -C .claude/worktrees/<name> checkout --detach   # no files change; frees the branch
```

Secondary trick: if a rebase does stall on this mid-flight, `git rebase --continue`
frequently just succeeds, because by then the mtimes have settled into an earlier
second. A stalled rebase reschedules the same `pick`, so the status output looks
alarming ("1 command done" + the same commit still pending) — it isn't.

Always `git rebase --abort` before retrying; a leftover `rebase-merge` directory
makes the next attempt refuse with *"I am stopping in case you still have something
valuable there"*, and leaves HEAD detached at the `onto` commit (so `git log` looks
like your branch was reset — it wasn't; the branch ref is intact).

## Step 4 — Rebase and land, one branch at a time

With the branch refs freed (Step 3B), work in the main repo:

```bash
git rebase main <branch>          # replay onto main
git diff <original-tip> <branch> --stat   # sanity: only main's own changes should differ
git checkout main
git merge --ff-only <branch>      # fast-forward — no merge commit
```

Repeat per branch, **re-basing each one on the new `main`** so the history stays
linear. After each merge, assert the invariant:

```bash
git log --merges <old-main>..main | wc -l   # MUST be 0
```

### Resolving the conflict you will actually hit

The realistic conflict is **branch A renamed a symbol; branch B was written before
that rename and still calls the old name, while also adding new behavior.**

During a *rebase*, the sides are inverted from what you may expect:

- `<<<<<<< HEAD` = the **new base** (`main`, already containing branch A)
- `>>>>>>> <sha>` = the **commit being replayed** (branch B)

Resolve by **keeping the new base's naming and layering the replayed commit's new
behavior on top** — never by taking one side wholesale. Then confirm the rename is
truly dead before continuing:

```bash
grep -rn "^<<<<<<<\|^=======$\|^>>>>>>>" app/ features/ shared/   # no markers left
grep -rn "<OldName>" app/ features/ shared/                       # no stale refs
```

Ignore hits under `obj/` and `bin/` — those are gitignored build artifacts and are
routinely stale.

## Step 5 — Verify (the real gate)

Per `AGENTS.md`, done means **format-check + build + test** for every area touched.
**Warnings are errors** (`TreatWarningsAsErrors=true`), so "builds" means zero
warnings. A hand-resolved conflict makes this mandatory, not optional.

`make` is frequently **not on PATH** in this container. The make targets are just
`dotnet <verb> <area>.slnx`, so run them directly:

| Area touched | Solution |
|---|---|
| `app/` (shell/CLI) | `app/Puffinote.slnx` |
| `features/EditMarkdown/` (Typage) | `features/EditMarkdown/Typage.slnx` |
| `features/EditSpreadsheets/` (Cellestra) | `features/EditSpreadsheets/Cellestra.slnx` |
| `shared/` | **all three** — it compiles into every solution |

```bash
dotnet build <area>.slnx                          # expect: 0 Warning(s), 0 Error(s)
dotnet test  <area>.slnx
dotnet format <area>.slnx --verify-no-changes     # exit 0 == formatted
```

**Never run two `dotnet build`/`dotnet test` invocations at once** — concurrent runs
deadlock on the MSBuild-node lock and hang forever. Run them serially, one area at a
time. (If it happens: kill `dotnet`/`testhost`/`MSBuild`, `dotnet build-server
shutdown`, retry.)

The GUI cannot launch in this headless container; verify UI behavior through the
headless UI tests, not by running the app.

## Step 6 — Clean up

Only now, and only after re-confirming state:

```bash
git worktree unlock .claude/worktrees/<name>   # they're usually locked
git worktree remove .claude/worktrees/<name>   # NO --force: let it refuse if dirty
git branch -d <branch> ...                     # NO -D: let it refuse if unmerged
git worktree list && git --no-pager branch     # confirm
```

Both commands are chosen because they **fail rather than destroy**. If
`git worktree remove` demands `--force`, or `git branch -d` refuses, that is the
tool telling you your safety audit was wrong — stop and re-examine, don't escalate
the flag.

Removing a worktree does **not** delete its branch; they're separate steps.

## Traps

- **Never filter `git branch` output through `grep -v '^\*'`.** It strips whichever
  branch is currently checked out and invents a phantom "unmerged" branch that is
  actually merged. Use `git --no-pager branch --merged main` unfiltered, or the
  authoritative check: `git rev-list --count main..<branch>` (`0` == nothing to
  land). This exact trap produced a wrong "these branches are unmerged" claim.
- **`--merged`/`--contains` disagreeing with `rev-list` means you filtered
  something.** Trust `rev-list --count` and `git merge-base --is-ancestor`.
- **Something else may be driving this repo.** HEAD was observed hopping across
  branches, and `main` was pushed to `origin`, with no such command issued from this
  session (`git reflog` and `git reflog show origin/main` reveal both). So:
  re-verify HEAD and the working tree immediately before any destructive step, and
  don't trust a snapshot taken several commands ago. If history has already been
  pushed, it is public — do not rewrite it.
- **Don't push unless asked.** Landing on local `main` is the task; publishing is a
  separate decision.
- **A stale `obj/`/`bin/` will echo old symbol names** back at your post-conflict
  greps. Scope searches to source, or ignore artifact paths.
