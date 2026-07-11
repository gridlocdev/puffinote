# Proposal 0003 — Making agent worktrees visible in GitHub Desktop on the host

- **Status:** Proposed — nothing implemented. Phase 0 (the spike) is the next action.
- **Author:** (you) · drafted with Claude
- **Affects:** `.devcontainer/` and the local dev workflow only. **No product code, no build, no tests.**

---

## 1. The ask

Claude Code creates its worktrees inside the container at `.claude/worktrees/<name>/`. We want to open
those worktrees in **GitHub Desktop on the macOS host** — to review an agent's diff, stage, and commit
with a real GUI instead of reading `git diff` in a terminal.

---

## 2. What is actually true today (measured, not assumed)

**The files are already on the host.** `/workspace` in the container is a bind mount of the host repo
(`/run/host_mark/Users` → Docker Desktop's macOS file sharing). So a worktree at
`/workspace/.claude/worktrees/foo` already exists on the host at `<host-repo>/.claude/worktrees/foo`.
**Nothing needs to be copied, exported, or synced.** GitHub Desktop can already *see* the files.

**The blocker is one thing only: git's worktree metadata records absolute container paths.** A linked
worktree is not self-contained — it's two pointer files that reference each other by absolute path.
Probing a real worktree in this container gives:

| File | Content today |
|---|---|
| `.claude/worktrees/foo/.git` | `gitdir: /workspace/.git/worktrees/foo` |
| `.git/worktrees/foo/gitdir` | `/workspace/.claude/worktrees/foo/.git` |
| `.git/worktrees/foo/commondir` | `../..` ← already relative, not a problem |

`/workspace` **does not exist on the host**. When GitHub Desktop opens
`<host-repo>/.claude/worktrees/foo`, it reads the `.git` file, follows `gitdir:` to `/workspace/...`,
finds nothing, and concludes the folder is not a git repository. Every fix below is a way of making
those two pointers resolve on *both* sides of the container boundary.

**Supporting facts:**

- Container git is **2.47.3**. The `worktree.useRelativePaths` config — the built-in fix for exactly
  this problem — landed in **git 2.48**. It is not available in the image as it stands.
- GitHub Desktop ships its **own bundled git**, whose version we don't control and can't assume is ≥ 2.48.
- `.claude/worktrees/` is gitignored (twice — top of `.gitignore` and again at the bottom). Keep it that
  way; none of this should ever be committed.
- Claude Code hardcodes `.claude/worktrees/` as the worktree location for git repos. Relocating them
  elsewhere is not a supported knob, so every option below keeps them where they are.

---

## 3. Options

### Option A — Path parity (**recommended**)

Make the container's workspace path **identical to the host's**: mount the repo at
`/Users/<you>/…/Puffinote` inside the container instead of `/workspace`.

Then every absolute path git writes (`gitdir: /Users/<you>/…/Puffinote/.git/worktrees/foo`) is valid in
*both* worlds, because the string names the same directory on both sides. Worktrees created by the agent
are openable by GitHub Desktop with no repair step, forever, and a `git worktree repair` run from either
side is a no-op rather than a fight.

- **Cost:** one devcontainer change + a rebuild; anything that hardcodes `/workspace` must be updated.
- **Dependencies:** none. No git upgrade, no GitHub Desktop version requirement.
- **Why it wins:** it removes the *class* of bug (two names for one directory) rather than translating
  between the two names on every worktree creation.

### Option B — Relative worktree pointers (git ≥ 2.48 on both sides)

Upgrade git in the image to ≥ 2.48, set `git config worktree.useRelativePaths true`, and `git worktree
repair` existing worktrees. The pointers become `gitdir: ../../../.git/worktrees/foo` and
`../../../.claude/worktrees/foo/.git`, which resolve identically from any mount point.

- **Cost:** the Dockerfile's Ubuntu base doesn't carry 2.48 in apt — needs `ppa:git-core/ppa` or a
  source build, i.e. a new image dependency to maintain.
- **Risk:** GitHub Desktop's bundled git may be older than 2.48. It will still *read* a relative
  `gitdir:` file (git has resolved those relative to the file's own directory for years), but a
  host-side `repair`/`prune` from an older git can rewrite the pointers back to **host** absolute paths
   — which then breaks the container. Two version dependencies we don't control.
- **Verdict:** the "correct" fix in the abstract, but strictly more fragile here than Option A.

### Option C — Dual mount + host-side repair

Keep `/workspace`, and *additionally* bind-mount the repo at its host path inside the container. Then run
`git worktree repair` on the host after each worktree is created: it rewrites the pointers to host paths,
which — thanks to the second mount — are also valid inside the container.

- Works, but it's Option A with extra steps plus a manual repair per worktree. Only worth it if something
  turns out to hard-depend on `/workspace` and can't be changed.

### Option D — Don't open worktrees at all (**fallback**)

The agent already pushes its branch and opens a draft PR. In GitHub Desktop, just fetch and check the
branch out **in the main clone** — or keep a second full clone on the host purely for browsing.

- Zero container changes, zero risk. This is the answer if **Phase 0** shows GitHub Desktop won't open a
  linked worktree at all (see below). It costs you the ability to see the agent's *uncommitted* work.

---

## 4. Phase 0 — the spike (do this first, ~20 minutes, no changes to anything)

**Unverified assumption:** that GitHub Desktop will open a linked worktree *even with correct paths*. A
worktree's `.git` is a **file**, not a directory, and GitHub Desktop has historically been shaky about
worktrees. Prove this before touching the devcontainer — if it fails, Options A/B/C are all wasted work
and the answer is Option D.

Cheapest possible test, entirely on the host, no container involved:

```bash
# in the host repo, in a terminal (not the container)
git worktree add ../puffinote-spike -b spike/gh-desktop
```

Then in GitHub Desktop: **File → Add Local Repository…** → pick `../puffinote-spike`.

- **It opens and shows the branch + diffs** → GitHub Desktop supports worktrees. Proceed to Option A.
- **It refuses** ("not a git repository", or the repo opens empty/broken) → stop. Take **Option D**.

Clean up either way:

```bash
git worktree remove ../puffinote-spike && git branch -D spike/gh-desktop
```

---

## 5. Plan of record (Option A), step by step

1. **Find where the `/workspace` mount is actually declared.** `.devcontainer/devcontainer.json` does
   *not* set `workspaceMount`/`workspaceFolder`, yet the container's cwd is `/workspace` — the Dev
   Containers default would be `/workspaces/<repo>`. So the mount is coming from whatever launches the
   container (a `docker run -v …:/workspace`, a compose file, or the Claude Code container integration).
   **The change must go where that mount is defined**; putting it only in `devcontainer.json` will do
   nothing if `devcontainer.json` isn't what starts the container.

2. **If it *is* Dev Containers**, add to `.devcontainer/devcontainer.json`:

   ```jsonc
   "workspaceMount": "source=${localWorkspaceFolder},target=${localWorkspaceFolder},type=bind,consistency=cached",
   "workspaceFolder": "${localWorkspaceFolder}",
   ```

   `${localWorkspaceFolder}` is the host path, so mount target == host path == container cwd. If it's a
   plain `docker run`, the equivalent is `-v "$PWD:$PWD" -w "$PWD"`.

3. **Optional compat mount** — only if something genuinely hardcodes `/workspace`:

   ```jsonc
   "mounts": ["source=${localWorkspaceFolder},target=/workspace,type=bind,consistency=cached"]
   ```

   Treat `/workspace` as read-only-ish legacy: **never create worktrees while cd'd into it**, or you
   reintroduce the exact bug this proposal fixes.

4. **Sweep for hardcoded `/workspace`.** Known: `/root/.gitconfig` has `safe.directory /workspace`
   (harmless — `/etc/gitconfig` already sets `safe.directory *`), and the Claude memory notes under
   `/root/.claude/projects/-workspace/`. The repo's own config is clean: `postCreateCommand` and the
   VS Code `dotnet.defaultSolution` are both repo-relative, and `.claude/settings.local.json` has no
   path dependencies.

5. **Rebuild the container** and confirm `pwd` is the host path.

6. **Repair worktrees created under the old path** (one-time, from inside the container):

   ```bash
   git worktree repair .claude/worktrees/*
   ```

   Or simply let the old ones go — they're transient by design.

7. **Add a worktree in GitHub Desktop:** File → Add Local Repository… → `<repo>/.claude/worktrees/<name>`.
   Finder hides dot-folders: press **⌘⇧G** in the picker and type the path, or **⌘⇧.** to reveal them.
   Each worktree shows up as its own repository entry in the sidebar, named after its folder.

---

## 6. Verification

Done means all of these hold:

- [ ] Container: `git worktree list` shows the host-shaped path for a new worktree.
- [ ] Container: `cat .claude/worktrees/<n>/.git` → `gitdir: /Users/…` (no `/workspace`).
- [ ] Host: `git -C .claude/worktrees/<n> status` works in a plain terminal.
- [ ] GitHub Desktop opens the worktree, shows the right branch, and shows the agent's uncommitted diff.
- [ ] The agent still works: `make check` builds/tests green from the new workspace path.
- [ ] A file edited in GitHub Desktop shows up immediately in the container, and vice versa.

**Rollback:** revert the devcontainer change, rebuild, `git worktree prune`. Nothing here touches product
code or history.

---

## 7. Risks and things to know

- **Host paths with spaces** (`/Users/you/My Code/Puffinote`) will break the mount string and a lot of
  tooling besides. If the repo lives in such a path, move it before doing any of this.
- **Concurrent edits.** An agent session actively working in a worktree and you committing from GitHub
  Desktop in that same worktree will race. Treat a live worktree as **read-only in GitHub Desktop** — look
  at the diff, commit after the session is done.
- **Stale entries.** Claude Code removes a worktree on session exit if it's unchanged, and worktrees are
  transient by design. GitHub Desktop will keep showing the dead entry; remove it from the sidebar and run
  `git worktree prune` occasionally.
- **Docker Desktop file sharing** must include the repo's path (`/Users` is shared by default — fine here).
- **Don't commit any of this.** `.claude/worktrees/` stays gitignored.

---

## 8. Recommendation

Run **Phase 0** first — it's 20 minutes and it decides whether the rest of the document is worth reading.
If GitHub Desktop opens a worktree, do **Option A**: it's a single mount change, it needs no version
upgrades on either side of the boundary, and it makes container and host agree on what to call the repo
once and for all. If GitHub Desktop refuses worktrees outright, take **Option D** and review the agent's
work from its branch/PR in the main clone.
