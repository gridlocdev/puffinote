# Icons

These SVG glyphs are vendored from [Lucide](https://lucide.dev) (ISC/MIT licensed,
© Lucide Contributors), kept local per Golden Rule #6 — icons are never fetched at runtime.

File names are stable internal keys (e.g. `align-left.svg`); a few correspond to a renamed
Lucide source glyph (`align-left` ← `text-align-start`, `filter` ← `funnel`). They are loaded and
stroked by `Controls/Icons/LucideIcons.cs`. To add one, drop the 24×24 `fill="none"` SVG here and
reference its file name as a command's `Icon` key in `AppCommands`.

`move-decimal-left` / `move-decimal-right` are first-party glyphs drawn in the same 24×24,
`fill="none"`, stroke-2 Lucide style (Lucide has no decimal-shift icon); they back the
Increase/Decrease Decimal commands.
