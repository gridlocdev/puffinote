# IDE1006 — Naming rule violation

## What it is

IDE1006 fires when an identifier violates a naming convention configured in
`.editorconfig` (via `dotnet_naming_rule` / `dotnet_naming_symbols` /
`dotnet_naming_style`). Typical rules: private fields must be `_camelCase`,
interfaces must start with `I`, type/method/property names must be `PascalCase`,
constants `PascalCase`, async methods end in `Async`, etc. All naming options share
the single ID **IDE1006** with title "Naming rule violation".

The only real fix is to **rename the symbol** to satisfy the rule. There is no
"Fix All in Solution" for this — `NamingStyleCodeFixProvider` deliberately opts out,
because a rename is a semantic, cross-cutting operation Roslyn won't apply in bulk
without per-symbol confirmation. That is the source of the
`doesn't support Fix All in Solution` message.

## Decision procedure (per flagged symbol)

1. **Read what the rule actually wants.** The message names the required style
   ("must begin with upper case characters", "must begin with '_'", etc.). Derive
   the target name from the rule, not from a guess.
2. **Determine the symbol's visibility / reach** — this decides the lane:
   - **Private/file-local, no references outside the file** → Lane A. Safe to rename
     in place with a semantic rename limited to that file's scope.
   - **Anything visible beyond the file** (internal/public members, types,
     interfaces, cross-project references) → Lane B. Rename must update all
     references solution-wide; serialize or isolate.
3. **Check the do-not-rename cases** (below). If it's one of those, don't rename —
   suppress or adjust config instead.
4. **Pick the new name and check for collisions.** Renaming `foo`→`_foo` fails if
   `_foo` already exists in scope. Renaming to `PascalCase` can collide with a
   type/property of the same name. Resolve collisions before applying.

## How to rename — semantically, never by text replace

A correct rename updates **every reference** to the symbol and **nothing else** (not
comments, not strings, not lookalike substrings, not a different symbol that happens
to share the name). Options, best first:

- **IDE / language-server rename.** In VS, VS Code (C# Dev Kit), or Rider this is
  the "Rename" refactoring (F2). It's reference-aware and solution-wide. If you're
  driving an editor, this is the safest tool.
- **Roslyn `Renamer` API (scriptable, best for bulk/parallel).** Load the solution
  with `MSBuildWorkspace`, locate the symbol, and call
  `Renamer.RenameSymbolAsync(solution, symbol, newName, options)`; apply the
  returned solution. This is reference-correct across projects and is the way to
  script many renames without an editor. Because it returns a whole new solution,
  **apply renames one symbol at a time** (or on isolated worktrees) so they don't
  clobber — this is why solution-visible renames are Lane B.
- **Scoped textual edit — only for truly file-local private symbols.** If and only
  if the symbol is private and provably referenced within a single file, an edit
  confined to that file is acceptable. Even then, prefer matching identifier tokens,
  not raw substrings, and skip comments/strings.

If a parameter or local is flagged and is genuinely unused, renaming it to the
discard `_` is also a valid way to satisfy naming (and it interacts with IDE0060 —
see that reference).

## Do-not-rename / suppress-instead cases

- **Shipped public API.** Renaming a public member is a binary-breaking change for
  consumers. If the assembly is published, suppress or keep the name unless the
  user confirms a breaking change is acceptable.
- **Names dictated by an external contract** — P/Invoke / interop names, serialized
  member names bound by position or by a serializer that keys off the identifier,
  data-binding paths, names referenced by reflection or string, and generated code.
  Renaming silently breaks these and the build stays green, so tests are your only
  safety net.
- **Overrides / interface implementations** must match the base/interface name; fix
  the declaration there (or suppress) rather than diverging.

## `.editorconfig` levers (for the config-vs-code decision)

- Severity: `dotnet_diagnostic.IDE1006.severity = none | silent | suggestion | warning | error`.
- The convention itself lives in the `dotnet_naming_*` triplet. If a large batch of
  violations reflects a convention the team doesn't actually want enforced (or wants
  scoped to certain symbol kinds), adjusting the rule/symbols/style entries clears
  them at the source. Get user sign-off — this changes what the linter enforces
  going forward, not just today's count.

## Verify

After renames: `dotnet build` (no new `CS` errors), re-collect diagnostics (IDE1006
count dropped by what you renamed), and `dotnet test`. Pay special attention to
serialization, config binding, reflection, and DI registration — a rename that
compiles can still break these at runtime.
