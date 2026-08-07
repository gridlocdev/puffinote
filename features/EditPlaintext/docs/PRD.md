# Plaintain — Product Requirements

**Plaintain** (**plain** + **plantain**) is a Notepad-class plaintext editor for the Puffinote family:
the tool you reach for when a file is "just text" and a Markdown editor would only get in the way.

Design rationale — why it is a separate app, why the surface is not shared with Typage, what the phases
were — lives in [proposal 0003](../../../docs/proposals/0003-plaintain.md). This document is what it
does.

---

## 1. Users and the problem

Anyone who opens a `.log`, a `.conf`, an extensionless `LICENSE`, or a file whose extension nothing
claims. Today those land in an editor that either refuses them, reformats them, or opens them in a
prose surface built for something else. Plaintain opens any decodable file, shows it exactly as it is,
and writes it back byte for byte.

## 2. Non-goals

- **Syntax highlighting.** The one feature that would make this a genuinely different product, and so
  the one that needs a decision rather than a drift.
- **Format-aware rendering of any kind** — no CSV tables, no JSON folding, no folding at all. A
  delimited file is Cellestra's question; structured config is still text.
- No plugin system, no collaboration, no npm, no new NuGet above the trusted base.

## 3. What it is

| Trait | Decision |
|---|---|
| Font | **Monospace**, uniform row height. Not a preference — the whole layout is arithmetic over a fixed advance (§6). |
| Word wrap | **Off by default**, toggleable and remembered. The Notepad expectation for a log, where wrapping destroys the column structure that makes it readable. |
| Line numbers | **Yes**, in a gutter, toggleable. Typage has none and shouldn't; this is one of the two visible differences. |
| Tabs | Rendered to 4-column tab stops. Preserved in the file exactly as typed. |
| Large files | **50 MB** is the held target. The load path is written against that number rather than retrofitted. |
| Encoding | Detected on open, round-tripped byte-faithfully, shown in the status bar; undecodable input opens **read-only** (§4). |
| Line endings | LF / CRLF preserved exactly. An edit writes the ending the document already uses. |

## 4. Files

- **Open** via the CLI (`pltn <file>`), Finder association / "Open With", the in-app dialog, or a
  Puffinote tab. Opening while dirty prompts.
- **Encoding decision tree** (implemented in `shared/Puffinote.Text`'s `TextFileCodec`):
  BOM present → believe it. No BOM → **validate** as UTF-8 with a throwing decoder, not assume it.
  Invalid UTF-8 → read through **Latin-1**, which is *total* (every byte maps and maps back), open
  **read-only**, and say so — in a dialog once, and continuously in a dimmed gutter and the status bar.
  Saving is held until a person confirms the encoding. A file that looks wrong can be fixed; a file that
  has been *saved* wrong cannot.
- **Save** is atomic: a temp file in the target's own directory, then a rename. Same directory because a
  rename across filesystems is a copy, and a copy is not atomic.
- **Recent files**, MRU, persisted.

## 5. Editing

Caret and selection operate on grapheme clusters (shared with Typage, so they cannot diverge). Undo and
redo are session-only. Home is "smart": the first non-whitespace character, then the row's true start.
Word-wise movement and deletion, page up/down, document ends. Tab indents to the next tab stop; with
more than one line selected, Tab indents every line it touches and Shift+Tab outdents them.

**Find** is a strip over the surface: substring or regex, optional case sensitivity, a live match count,
and stepping that wraps both ways. It starts from the first match **at or after the caret**, so opening
find where you are reading doesn't throw you back to line 1 of a 50,000-line log.

**Go to Line** jumps by number and centres the line.

## 6. Why the geometry is arithmetic

Every row is `RowHeight` tall and every character `CharWidth` wide, so:

- the row a pixel is in is a division, and the pixel a row starts at is a multiplication;
- a line's wrapped row count is `ceil(columns / columnsPerRow)` — no measurement, no text layout;
- with wrap **off**, a line *is* a row, so a million-line file's height is known in O(1) and nothing is
  measured at all.

The control implements `IScrollable`, so a host `ScrollViewer` hands it an offset rather than arranging
the document. Only rows in view are ever built or drawn.

This is the specific thing Typage's surface cannot do — its geometry is a pixel prefix sum over
variable-height blocks with collapsing margins — and it is why the two surfaces are separate while their
semantics are shared.

## 7. Commands

Every command lives in `PlainTextEditingCommands` (`Plaintain.Control`), so the standalone window and a
Puffinote tab offer the same set under the same chords.

| Action | Shortcut |
|---|---|
| Undo / Redo | ⌘Z / ⇧⌘Z |
| Cut / Copy / Paste / Select All | ⌘X / ⌘C / ⌘V / ⌘A |
| Find | ⌘F |
| Go to Line | ⌘G |
| Word Wrap | ⌥⌘W |
| Line Numbers | ⌥⌘L |

⌘K is **not** Plaintain's: it opens the host's command palette, as in the sibling editors.

## 8. File-type claims

`txt`, `text`, `log` and the `public.plain-text` UTI at **Owner** rank — the extensions no one else
wants. The long tail (`conf`, `ini`, `json`, `yaml`, `sh`, `csv`, …) at **Alternate**, which puts
Plaintain in "Open With" without taking anyone's default away. `json` and `yaml` are deliberately not
claimed as Owner: that is a fight with every editor on the machine, and one users would resent losing.

Inside Puffinote extensions matter less — a decode probe on the first few KB is the real gate.

## 9. The `pltn` CLI

A **launcher plus exactly one read-only verb**. `pltn <file>` opens the file in the app; `pltn info
<file>` prints encoding, line endings, and line/word/character/byte counts, exiting non-zero when the
encoding had to be guessed so a script can branch on it. The verb exists so a headless container has
something to assert against — it is not the start of a verb surface.
