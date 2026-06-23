using Avalonia.Controls;
using Typage.Engine.Markdown;

namespace Typage.Control;

/// <summary>
/// The control-side seam that turns an engine-detected embed <i>reference</i> into an Avalonia
/// renderable (PRD §10.1). Reference <i>detection</i> is engine-side (the tokenizer emits an
/// <see cref="TokenType.Embed"/> with the target string); <i>resolution</i> to a control is here,
/// so the standalone app can leave embeds unresolved while the future notes-app shell injects a
/// resolver that renders images, tables, or hosted controls.
/// </summary>
public interface IEmbedResolver
{
    /// <summary>
    /// Resolve an embed target (e.g. <c>image.png</c> from <c>![[image.png]]</c>) to a control,
    /// or return null to fall back to showing the raw source. Off by default in the standalone app.
    /// </summary>
    Avalonia.Controls.Control? Resolve(string target);
}

/// <summary>The default resolver: resolves nothing (embeds show as source). Used by the standalone shell.</summary>
public sealed class NullEmbedResolver : IEmbedResolver
{
    public static readonly NullEmbedResolver Instance = new();

    public Avalonia.Controls.Control? Resolve(string target) => null;
}
