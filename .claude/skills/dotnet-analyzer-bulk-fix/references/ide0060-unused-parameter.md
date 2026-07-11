# IDE0060 — Remove unused parameter

## What it is

IDE0060 flags a method/constructor/local-function parameter that is never used in
the body. Category is Style; default severity is Suggestion (so it often hides in
the "Messages" tab until you enforce it). It's configured by
`dotnet_code_quality_unused_parameters`.

`dotnet format` reports `No associated code fix found` for it in batch mode because
the only real fix — deleting the parameter — is a **signature change** that must
update every call site, which isn't a safe Fix-All operation. So you decide,
per parameter, between *remove*, *use*, or *suppress*.

Important built-in escape hatch: **the rule does not flag parameters named with the
discard `_`** (or `_1`, `_2`, …). Microsoft added this specifically to silence
parameters that are *needed for a signature* but legitimately unused — e.g. a method
used as a delegate, a parameter with special attributes, or one accessed implicitly.
That makes `_` the cleanest suppression when the parameter must stay.

## Decision tree (per flagged parameter)

**Step 1 — Is the parameter required by a contract you don't control?** If yes →
**keep it, suppress the warning.** These are the common false-positive cases; do NOT
remove:

- **Interface implementations and `override`/`virtual` members** — the signature is
  fixed by the base/interface. (Roslyn has known cases where it wrongly flags these,
  e.g. an interface implemented via a base method.)
- **Delegate / event-handler signatures** — e.g. `(object sender, EventArgs e)`;
  the handler must match even if it ignores an argument.
- **Framework-mandated signatures** — e.g. MSTest `ClassInitialize(TestContext _)`
  requires the `TestContext` parameter; removing it breaks the test host.
- **DI-, reflection-, or convention-invoked members** — anything called by a
  container, serializer, or `MethodInfo.Invoke` where the signature matters.
- **Shipped public API** — removing a public parameter is a breaking change for
  consumers.

**Step 2 — If not contract-bound, is the parameter genuinely dead, or is its absence
a bug?** IDE0060 sometimes means a caller passes a value the method silently ignores
(the classic `Send(message, encrypt)` that never encrypts). Before deleting, confirm
no caller depends on the effect. If callers *expect* it to do something, the fix is
to **use** the parameter, not remove it.

**Step 3 — Genuinely unused and safe to change → remove it.** Delete the parameter
and update **every call site**. This is a cross-file signature change → **Lane B**
(serialize or isolate; never run concurrently with other signature edits on
overlapping files).

## How to apply each fix

- **Remove (Lane B):** use a semantic "Change signature / remove parameter"
  operation, or find real references via Roslyn (`SymbolFinder.FindReferencesAsync`)
  and drop the argument at the correct positional index — mind named args, optional
  args, and params arrays. Never grep-and-delete call sites. Build after each
  removal.
- **Use:** wire the parameter into the body if that was the original intent (this
  also fixes a latent bug).
- **Suppress — file-local, Lane A**, ranked cleanest-first:
  1. **Rename to discard `_`** (or `_1`, `_2`): the analyzer stops flagging it and it
     documents "intentionally unused." Best when the parameter must stay for a
     signature.
  2. **`#pragma warning disable IDE0060` / `restore`** around the member — for a
     one-off you want to keep named.
  3. **`[SuppressMessage("Style", "IDE0060")]`** attribute on the member.
  4. **`.editorconfig` scope/severity** — for whole categories (see below).

## `.editorconfig` levers (config-vs-code decision)

- `dotnet_code_quality_unused_parameters = all | non_public`. Setting it to
  `non_public` stops the rule flagging public-surface parameters — appropriate when
  public signatures are intentionally stable and you don't want churn there.
- Severity: `dotnet_diagnostic.IDE0060.severity = none | silent | suggestion | warning | error`.

If most hits are framework/contract signatures, scoping the rule to `non_public`
(and/or leaning on the `_` convention) clears the noise far more safely than editing
signatures. Confirm with the user before changing enforcement.

## Verify

`dotnet build` (no new `CS` errors — removals surface here fast if a call site was
missed), re-collect diagnostics (IDE0060 count down by what you changed), and
`dotnet test` (a removed parameter that mattered at runtime shows up here). Removals
are the riskiest fix in this skill; treat green tests as a hard gate.
