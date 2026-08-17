using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Puffinote.Components.Shell;

/// <summary>
/// The second way a document reaches one of the family's apps — and on macOS, the only way that works.
///
/// <para>A launch argument (<c>desktop.Args</c>) covers <c>typage &lt;file&gt;</c> and the shells that
/// start an app with its document on the command line. macOS does neither: Finder's "Open With", a
/// double-click on a claimed extension, and <c>open -a Typage &lt;file&gt;</c> all send a <em>bundled</em>
/// app an Apple Event, and argv never sees the file. Avalonia surfaces that event as
/// <see cref="IActivatableLifetime.Activated"/> carrying <see cref="FileActivatedEventArgs"/>. Claiming a
/// type in <c>Info.plist</c> without handling it is what opened every association onto an empty
/// "Untitled" document with the file dropped on the floor.</para>
///
/// <para>It is also the only route for a document opened while the app is already running: argv is read
/// once, at launch, and a second "Open With" on a live process has nowhere else to arrive.</para>
/// </summary>
public static class DocumentActivation
{
    /// <summary>
    /// Route every document the desktop hands <paramref name="app"/> to <paramref name="open"/>, which
    /// receives the candidates' local paths in the order the shell sent them. A no-op on platforms whose
    /// lifetime has no activation feature — there, argv is the whole story.
    /// </summary>
    public static void Install(Application app, Action<IReadOnlyList<string?>> open)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(open);

        if (app.TryGetFeature<IActivatableLifetime>() is not { } activatable)
        {
            return;
        }

        activatable.Activated += (_, e) =>
        {
            // Only a file activation is ours to answer; the same event carries reactivation and URL opens.
            if (e is FileActivatedEventArgs activation)
            {
                open([.. activation.Files.Select(file => file.TryGetLocalPath())]);
            }
        };
    }

    /// <summary>
    /// The document to open, or null to stay on whatever is already there. Only the first candidate
    /// counts — one window, one document — and only when it names an existing file: a shell can pass
    /// options or a stale path, and a file reachable only through a sandbox has no local path at all.
    /// </summary>
    public static string? FirstExistingFile(IReadOnlyList<string?>? candidates) =>
        candidates is [{ } first, ..] && File.Exists(first) ? first : null;

    /// <summary>
    /// As <see cref="FirstExistingFile"/>, but a folder counts too — for the shell, where a notebook is a
    /// directory and a document is a file, and both are things someone can hand it.
    /// </summary>
    public static string? FirstExistingPath(IReadOnlyList<string?>? candidates) =>
        candidates is [{ } first, ..] && (File.Exists(first) || Directory.Exists(first)) ? first : null;
}
