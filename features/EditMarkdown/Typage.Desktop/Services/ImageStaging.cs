using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Desktop.Services;

/// <summary>
/// Staging for images inserted into an <b>unsaved</b> document. A new document has no folder to anchor a
/// relative image path against, so a picked image is copied into a per-user scratch directory and the
/// document references that absolute path. When the document is finally saved, <see cref="MainWindow"/>
/// offers to move the staged files into the document's permanent home (an <c>images/</c> folder beside it)
/// and rewrites the Markdown paths to point there relatively — avoiding orphaned uploads while keeping the
/// in-progress draft openable. A saved document skips all of this and references images by relative path.
/// </summary>
/// <remarks>
/// The scratch root lives under the platform's local-application-data folder (resolved via
/// <see cref="System.Environment.SpecialFolder.LocalApplicationData"/> so it is correct on macOS, Linux,
/// and Windows alike) rather than a hand-rolled XDG path.
/// </remarks>
public static class ImageStaging
{
    /// <summary>A staged file name is <c>{id}__{original-name}</c> so the original name survives the round trip.</summary>
    private const string NameSeparator = "__";

    /// <summary>The per-user scratch directory holding images staged for not-yet-saved documents.</summary>
    public static string StagingRoot { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Typage",
        "staged-images");

    /// <summary>The image file types offered in the insert-image picker (PRD §8.3 image set + common aliases).</summary>
    public static readonly string[] ImagePatterns =
    {
        "*.png", "*.jpg", "*.jpeg", "*.jfif", "*.pjpeg", "*.gif", "*.webp", "*.svg",
    };

    /// <summary>
    /// Copy <paramref name="sourcePath"/> into the staging root under a collision-proof name and return the
    /// absolute staged path (to embed as the image URL while the document is unsaved). The original file name
    /// is preserved after the id so it can be restored when the image is later moved to its permanent home.
    /// </summary>
    public static string Stage(string sourcePath, string uniqueId)
    {
        Directory.CreateDirectory(StagingRoot);
        var name = $"{uniqueId}{NameSeparator}{Path.GetFileName(sourcePath)}";
        var dest = Path.Combine(StagingRoot, name);
        File.Copy(sourcePath, dest, overwrite: true);
        return dest;
    }

    /// <summary>
    /// Percent-encode the characters that would break a Markdown link destination — space (critical: the
    /// scratch root lives under "…/Application Support/…" on macOS), parentheses, and <c>%</c> itself — so the
    /// path survives as a single <c>![alt](url)</c> destination. Path separators are left intact.
    /// </summary>
    public static string EncodeUrl(string url) =>
        url.Replace("%", "%25").Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");

    /// <summary>Reverse <see cref="EncodeUrl"/> (and any other percent-escapes) back to a filesystem path/URL.</summary>
    public static string DecodeUrl(string url) => System.Uri.UnescapeDataString(url);

    /// <summary>True when <paramref name="url"/> is an absolute path inside the staging root.</summary>
    public static bool IsStaged(string url)
    {
        if (string.IsNullOrEmpty(url) || !Path.IsPathFullyQualified(url))
        {
            return false;
        }

        var full = Path.GetFullPath(url);
        var root = Path.GetFullPath(StagingRoot);
        return full.StartsWith(root + Path.DirectorySeparatorChar, System.StringComparison.Ordinal)
            || full.StartsWith(root + Path.AltDirectorySeparatorChar, System.StringComparison.Ordinal);
    }

    /// <summary>The original file name of a staged path (the part after the id), or the file name as-is.</summary>
    public static string OriginalName(string stagedPath)
    {
        var name = Path.GetFileName(stagedPath);
        var sep = name.IndexOf(NameSeparator, System.StringComparison.Ordinal);
        return sep >= 0 ? name[(sep + NameSeparator.Length)..] : name;
    }

    /// <summary>
    /// Every image reference in <paramref name="documentText"/> whose URL is a staged path, with the URL's
    /// source span so it can be rewritten. Returned in document order; the engine tokenizer finds them, so
    /// only genuine <c>![alt](url)</c> URLs match (not look-alike text).
    /// </summary>
    public static IReadOnlyList<StagedRef> StagedReferences(string documentText)
    {
        var model = MarkdownModel.Build(TextDocument.FromString(documentText));
        return model.AllTokens()
            .Where(t => t.Type == TokenType.ImageUrl && t.Meta is not null)
            .Select(t => new StagedRef(t.Start, t.Length, DecodeUrl(t.Meta!))) // decode "%20" etc. to the real path
            .Where(r => IsStaged(r.Url))
            .OrderBy(r => r.Start)
            .ToList();
    }

    /// <summary>The relative, forward-slashed URL for an image at <paramref name="targetFullPath"/> as seen
    /// from the saved document at <paramref name="documentPath"/> (e.g. <c>images/photo.png</c>).</summary>
    public static string RelativeUrl(string documentPath, string targetFullPath)
    {
        var docDir = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
        var rel = Path.GetRelativePath(docDir, Path.GetFullPath(targetFullPath));
        return rel.Replace('\\', '/');
    }

    /// <summary>
    /// Apply URL rewrites to <paramref name="documentText"/>. Each edit replaces the span <c>[Start, Start+Length)</c>
    /// with a new URL; edits are applied right-to-left so earlier offsets stay valid. Used to repoint staged
    /// URLs at their permanent relative locations after the files have been moved.
    /// </summary>
    public static string RewriteUrls(string documentText, IEnumerable<(int Start, int Length, string NewUrl)> edits)
    {
        var text = documentText;
        foreach (var e in edits.OrderByDescending(e => e.Start))
        {
            text = text[..e.Start] + e.NewUrl + text[(e.Start + e.Length)..];
        }

        return text;
    }

    /// <summary>
    /// Pick a non-colliding destination path for <paramref name="originalName"/> inside <paramref name="assetsDir"/>:
    /// the bare name if free, otherwise <c>name-1.ext</c>, <c>name-2.ext</c>, … This is a pure name computation;
    /// the caller decides existence via <paramref name="exists"/> so it can be unit-tested without the disk.
    /// </summary>
    public static string UniqueDestination(string assetsDir, string originalName, System.Func<string, bool> exists)
    {
        var stem = Path.GetFileNameWithoutExtension(originalName);
        var ext = Path.GetExtension(originalName);
        var candidate = Path.Combine(assetsDir, originalName);
        var n = 1;
        while (exists(candidate))
        {
            candidate = Path.Combine(assetsDir, $"{stem}-{n}{ext}");
            n++;
        }

        return candidate;
    }

    /// <summary>
    /// Copy each staged image referenced in <paramref name="documentText"/> into an <c>images/</c> folder
    /// beside the document being saved to <paramref name="targetPath"/> (created if missing), and return the
    /// document text with those URLs repointed at their new relative, encoded locations. The files are
    /// physically copied out of the scratch directory. A per-file IO failure is reported via the returned
    /// error while the successful copies are still applied, so disk and document stay consistent.
    /// </summary>
    public static (string NewText, string? Error) Reconcile(string documentText, string targetPath, IReadOnlyList<StagedRef> staged)
    {
        if (staged.Count == 0)
        {
            return (documentText, null);
        }

        var imagesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".", "images");
        try
        {
            Directory.CreateDirectory(imagesDir);
        }
        catch (System.Exception ex) when (ex is IOException or System.UnauthorizedAccessException)
        {
            return (documentText, ex.Message);
        }

        var edits = new List<(int Start, int Length, string NewUrl)>();
        string? error = null;
        foreach (var s in staged)
        {
            try
            {
                var dest = UniqueDestination(imagesDir, OriginalName(s.Url), File.Exists);
                File.Copy(s.Url, dest);
                edits.Add((s.Start, s.Length, EncodeUrl(RelativeUrl(targetPath, dest))));
            }
            catch (System.Exception ex) when (ex is IOException or System.UnauthorizedAccessException)
            {
                error = ex.Message;
            }
        }

        var text = edits.Count > 0 ? RewriteUrls(documentText, edits) : documentText;
        return (text, error);
    }

    /// <summary>One staged image reference: the URL's source span and its decoded (real) filesystem path.</summary>
    public readonly record struct StagedRef(int Start, int Length, string Url);
}
