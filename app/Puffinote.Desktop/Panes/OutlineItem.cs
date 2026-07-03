namespace Puffinote.Desktop.Panes;

/// <summary>
/// One entry in a document's outline (PRD §7.2.3): a Markdown heading for a note, or a sheet/chart for a
/// workbook. <see cref="Level"/> is the nesting depth (heading level; 0 for a flat sheet list) used to
/// indent the row, and <see cref="Reveal"/> navigates the editor to it (scroll + focus). The pane builds
/// these from the editor it hosts, so the outline panel stays editor-agnostic.
/// </summary>
/// <param name="Title">The row label (heading text, or sheet name).</param>
/// <param name="Level">Indent depth: 1..6 for headings, 0 for a sheet.</param>
/// <param name="Reveal">Navigates the editor to this entry.</param>
internal sealed record OutlineItem(string Title, int Level, Action Reveal);
