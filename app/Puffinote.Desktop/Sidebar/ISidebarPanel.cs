using Avalonia.Controls;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// One switchable panel in the sidebar (PRD §7.2.3): the file tree, notebook search, the document outline, or
/// recent files. The <see cref="SidebarView"/> shows one panel at a time under a keyboard-focusable
/// switcher; each panel is a thin, self-contained control so later sub-phases add a panel by implementing
/// this and registering it — no switcher rewrite.
/// </summary>
internal interface ISidebarPanel
{
    /// <summary>Stable id used by focus/activate commands (e.g. "files", "recent").</summary>
    string Id { get; }

    /// <summary>
    /// The switcher button's label. The switcher draws <see cref="Icon"/> rather than this text, so it is
    /// what the button is <i>named</i> — its tooltip and its accessible name — not what it prints.
    /// </summary>
    string Title { get; }

    /// <summary>The vendored Lucide glyph key the switcher draws for this panel (see <c>LucideIcons</c>).</summary>
    string Icon { get; }

    /// <summary>The panel body mounted when this panel is active.</summary>
    Control View { get; }

    /// <summary>
    /// Whether showing this panel should also put the keyboard in it.
    ///
    /// <para>True for the two panels whose first act is <em>typing</em> — Search and Filter. Their box is
    /// the panel; arriving at one and then having to click the field is a click with no other purpose, and
    /// the panel is useless until it happens.</para>
    ///
    /// <para>False for the panels you arrive at to <em>look</em> at something. Files especially: the shell
    /// activates it on the user's behalf when it reveals the active document, and taking focus out of the
    /// editor for that would land the next keystroke in the file tree instead of in the sentence being
    /// written.</para>
    /// </summary>
    bool FocusOnActivate { get; }

    /// <summary>Move keyboard focus into the panel (called when the panel is activated by keyboard).</summary>
    void Focus();
}
