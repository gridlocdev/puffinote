---
name: dotnet-analyzer-bulk-fix
description: >-
  Systematically clear .NET / C# Roslyn analyzer diagnostics that `dotnet format`
  refuses to auto-fix — the ones that print "Code fix ... doesn't support Fix All
  in Solution" or "No associated code fix found". Covers IDE1006 (naming rule
  violations) and IDE0060 (remove unused parameter) in depth, plus a general
  triage-and-fix workflow that safely partitions the work across parallel
  subagents. Use this whenever someone runs `dotnet format`, `dotnet build`, or an
  analyzer pass on a C#/.NET solution and hits diagnostics that won't auto-fix,
  mentions IDE1006 / IDE0060 / naming violations / unused parameters / "Unable to
  fix" / "doesn't support Fix All", or asks how to clear analyzer warnings across a
  large solution at scale. Use it even if they only paste analyzer output and ask
  you to fix it.
---

# Clearing non-auto-fixable Roslyn analyzer diagnostics at scale

## What this skill is for

`dotnet format` can auto-fix most style/analyzer diagnostics, but a handful refuse
because their code fix is a **cross-cutting semantic change** that Roslyn will not
apply blindly across a whole solution. When that happens you see one of:

- `Unable to fix IDE1006. Code fix NamingStyleCodeFixProvider doesn't support Fix All in Solution.`
- `Unable to fix IDE0060. No associated code fix found.`

This skill gives you a repeatable way to clear these. It goes deep on the two most
common offenders — **IDE1006** (naming) and **IDE0060** (unused parameter) — and
gives a general workflow that applies to any diagnostic in this "won't Fix-All"
class (e.g. other rename-based or signature-changing rules).

The two rules behave differently, so read the matching reference file before
touching code:

- Naming violations → `${CLAUDE_SKILL_DIR}/references/ide1006-naming.md`
- Unused parameters → `${CLAUDE_SKILL_DIR}/references/ide0060-unused-parameter.md`

`${CLAUDE_SKILL_DIR}` is set by Claude Code to this skill's own directory and
resolves the same whether the skill is installed at personal (`~/.claude/skills/`),
project (`.claude/skills/`), or plugin scope — and, crucially, when a subagent runs
from a different working directory. Always use it for the bundled script and
reference files instead of a relative or hardcoded path.

## Why these can't be batch-fixed (understand this before parallelizing)

Both fixes edit code **beyond the flagged line**:

- **IDE1006** is fixed by *renaming a symbol*. A rename must update every reference
  to that symbol, which can span many files and even other projects. Two renames
  applied concurrently can touch the same file and clobber each other.
- **IDE0060** is fixed by *removing a parameter* (or deciding the parameter must
  stay). Removing it changes the method signature, so every call site across the
  solution must be updated too.

The consequence for parallel work: **you cannot naively hand each subagent a slice
of files and let them all edit at once.** A rename or a signature change is not
file-local. Partition by *independence*, not by file count. The orchestration model
below is built around that constraint.

## The workflow

### 1. Collect the authoritative diagnostic list first

Never work off the scrolled-past console output. Regenerate a complete, structured
list so you know exactly how many violations exist, of which rule, in which
projects. Run the bundled collector from the solution root:

```bash
python3 "${CLAUDE_SKILL_DIR}/scripts/collect_diagnostics.py" --run format --out diagnostics.json
# or, if you already captured a build/format log:
python3 "${CLAUDE_SKILL_DIR}/scripts/collect_diagnostics.py" --log build.log --out diagnostics.json
```

It parses MSBuild- and `dotnet format`-style diagnostic lines, buckets them by rule
ID and by project/file, and prints a summary plus a suggested subagent partition.
See the top of the script for prerequisites (notably `EnforceCodeStyleInBuild` —
IDE-prefixed rules only surface at build time when that is on).

### 2. Decide config-vs-code before churning thousands of files

If a rule fires hundreds or thousands of times and most hits are *intentional*
(e.g. naming that matches an interop convention, or parameters required by a
framework signature), the right fix is often to **scope or downgrade the rule in
`.editorconfig`**, not to rewrite the code. That is a judgment call with real
consequences — surface the counts to the user and get explicit sign-off before
either mass-editing code or changing rule severity. Each reference file lists the
relevant `.editorconfig` knobs.

### 3. Classify the work into two lanes

This is the key step for safe parallelism. Sort every diagnostic into:

- **Lane A — file-local & safe to parallelize.** Edits that stay inside one file
  and don't change any public signature. Examples: renaming a `private` field to
  `_camelCase` when it has no cross-file references; adding `#pragma`/discard `_`
  suppressions for IDE0060; *analysis and planning* for anything (deciding the new
  name or whether a parameter is removable). Fan these out to subagents freely.
- **Lane B — cross-cutting, must be serialized or isolated.** Semantic renames of
  solution-visible symbols, and parameter *removals* that update call sites. Two of
  these running at once can edit the same file. Handle Lane B by either (a) applying
  them **serially** from the orchestrator, or (b) giving each subagent its **own git
  worktree/branch** for a disjoint set of symbols and merging afterward.

When unsure which lane a symbol is in, treat it as Lane B. Misclassifying a
cross-cutting change as safe is how you get half-renamed code and broken builds.

### 4. Execute

**Lane A (parallel):** Spawn subagents, each owning a disjoint set of files or
symbols. Give each the same instructions: read the relevant reference file, apply
only file-local fixes, build the affected project, and report back a diff plus any
symbols it could NOT fix locally (those get promoted to Lane B).

**Lane B (serialized or worktree-isolated):** For each symbol/parameter, perform a
**semantic** change — never a text find/replace (see Pitfalls). Apply one, build,
then the next; or dispatch disjoint symbols to isolated worktrees and merge. After
each change, the reference count for that diagnostic should drop by exactly what you
touched, with no new `CS` compiler errors.

### 5. Verify — every worker and the final merge

After each unit of work and once more at the end:

```bash
dotnet build   # 0 new CS errors; target diagnostics reduced
python3 "${CLAUDE_SKILL_DIR}/scripts/collect_diagnostics.py" --run build --out after.json
dotnet test    # behavior preserved (critical after Lane B removals)
```

The pass condition is: target diagnostic counts dropped as intended, **no new
compiler errors**, and tests still green. A rename that compiles but breaks
serialization, reflection, or a config binding won't show up as a `CS` error — which
is exactly why tests matter here.

## Subagent orchestration, concretely

Give each subagent a self-contained brief. A good Lane A brief looks like:

```
You are fixing <RULE> in a C#/.NET solution. Scope: ONLY these files: <list>.
1. Read ${CLAUDE_SKILL_DIR}/references/<rule>.md for the decision procedure.
2. Apply ONLY file-local fixes (no public signature changes, no cross-file renames).
3. If a symbol needs a solution-wide rename or a param removal that touches other
   files, DO NOT fix it — list it under "promote-to-lane-B" and move on.
4. Build the affected project(s). Report: unified diff, counts fixed, promotions.
```

Rules of thumb:
- Parallelize **analysis and planning** aggressively — it never conflicts. You can
  have every subagent classify its slice at once, then merge the plan.
- Parallelize **application** only within Lane A, on disjoint file sets.
- Keep Lane B narrow and mostly serial; its correctness matters more than its speed.
- Re-collect diagnostics after each wave so the remaining work list stays accurate.

## Pitfalls

- **Text find/replace for renames is a bug factory.** It hits comments, strings,
  substrings of unrelated identifiers, and misses references in other projects. Use
  a semantic rename (IDE rename / Roslyn `Renamer`), described in the IDE1006
  reference. Same for parameter removal: find real references, don't grep.
- **Concurrent cross-cutting edits clobber each other.** Two subagents renaming
  different symbols can both edit the same file. That's the whole reason for the
  Lane A/Lane B split.
- **Discarding or removing a parameter can hide a real bug.** IDE0060 sometimes
  means a caller passes a value that the method silently ignores. Confirm the
  parameter is genuinely dead before removing/suppressing — see the IDE0060 ref.
- **Some IDE0060/IDE1006 hits must not be "fixed" at all** (interface
  implementations, overrides, delegate/event-handler signatures, DI- or
  reflection-invoked members, framework test signatures, shipped public API). The
  reference files enumerate these; for them the answer is suppress/scope, not edit.
- **Don't trust stale console output.** Re-run the collector; counts change as you
  fix things and as fixes reveal or resolve other diagnostics.
