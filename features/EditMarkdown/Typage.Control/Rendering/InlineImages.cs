using Avalonia.Media.Imaging;
using Puffinote.Text.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>The size an inline image is drawn at, in device-independent pixels (see <see cref="InlineImages.Fit"/>).</summary>
internal readonly record struct InlineImage(double Width, double Height);

/// <summary>
/// The rules that turn an <c>![alt](url)</c> reference into a drawn picture: which lines qualify as an image
/// block, where a URL's file actually lives, and how big it is drawn. Pure functions over the token model and
/// the filesystem path — no Avalonia, no cache, no control — so the layout's behaviour around images is
/// testable without loading a bitmap (the headless suite has no decoder worth trusting).
/// </summary>
internal static class InlineImages
{
    /// <summary>
    /// The URL of the image on <paramref name="line"/> when the line holds <b>one image reference and nothing
    /// else</b> (surrounding whitespace aside) — the shape that renders as a picture on its own, the way
    /// Obsidian's live preview does. An image sitting inside a sentence stays inline text: it is part of the
    /// paragraph's flow, and pulling it out would reflow the prose around a hole. Returns null for every other
    /// line.
    /// </summary>
    public static string? ImageLineUrl(MarkdownModel model, TextDocument doc, int line)
    {
        if (line < 0 || line >= doc.LineCount)
        {
            return null;
        }

        var tokens = model.LineTokens(line);
        Token? image = null;
        foreach (var t in tokens)
        {
            if (t.Type != TokenType.ImageUrl)
            {
                continue;
            }

            if (image is not null)
            {
                return null; // two images on the line — that's a row of pictures, not an image block
            }

            image = t;
        }

        if (image is not { Meta: { Length: > 0 } destination } url)
        {
            return null;
        }

        // The reference's full source span is its node's outermost markers ("![" … ")").
        var start = int.MaxValue;
        var end = 0;
        foreach (var t in tokens)
        {
            if (t.Node == url.Node)
            {
                start = Math.Min(start, t.Start);
                end = Math.Max(end, t.End);
            }
        }

        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        if (start < lineStart || end > lineEnd)
        {
            return null;
        }

        var before = doc.GetText(lineStart, start - lineStart);
        var after = doc.GetText(end, lineEnd - end);
        return before.Trim().Length == 0 && after.Trim().Length == 0 ? destination : null;
    }

    /// <summary>
    /// The filesystem path <paramref name="url"/> names, as seen from a document saved at
    /// <paramref name="documentPath"/> — or null when there is nothing local to draw. A relative URL is
    /// anchored against the document's folder (so it needs a saved document); an absolute path or a
    /// <c>file://</c> URL is taken as it stands. A <b>remote</b> URL (<c>http</c>, <c>https</c>, anything with
    /// another scheme) resolves to null on purpose: the editor does not fetch over the network, so a remote
    /// image stays the text of its reference rather than becoming a silent request.
    /// </summary>
    public static string? ResolvePath(string url, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var scheme = SchemeOf(url);
        if (scheme is not null && !scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return null; // remote (or otherwise non-file) — nothing on disk to draw
        }

        string path;
        try
        {
            path = ImageStaging.DecodeUrl(url); // "file://…" → local path; "%20" → " "
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        if (documentPath is null)
        {
            return null; // an unsaved document has no folder to anchor a relative path against
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        return dir is null ? null : Path.GetFullPath(Path.Combine(dir, path));
    }

    /// <summary>The URL scheme of <paramref name="url"/> (<c>"file"</c>, <c>"https"</c>, …), or null when it
    /// carries none. A Windows drive letter (<c>C:\…</c>) is a path, not a one-letter scheme.</summary>
    private static string? SchemeOf(string url)
    {
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 1)
        {
            return null;
        }

        for (var i = 0; i < colon; i++)
        {
            var c = url[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return null;
            }
        }

        return url[..colon];
    }

    /// <summary>
    /// The size an image of <paramref name="naturalWidth"/>×<paramref name="naturalHeight"/> is drawn at inside
    /// a column <paramref name="maxWidth"/> wide: scaled down to fit, never scaled <i>up</i> — a 16px icon
    /// blown across the reading column is not what the author meant by pasting an icon. A non-positive
    /// <paramref name="maxWidth"/> (wrapping off) means "no column to fit", so the natural size stands.
    /// </summary>
    public static InlineImage Fit(double naturalWidth, double naturalHeight, double maxWidth)
    {
        if (naturalWidth <= 0 || naturalHeight <= 0)
        {
            return new InlineImage(0, 0);
        }

        if (maxWidth <= 0 || naturalWidth <= maxWidth)
        {
            return new InlineImage(naturalWidth, naturalHeight);
        }

        var scale = maxWidth / naturalWidth;
        return new InlineImage(maxWidth, Math.Round(naturalHeight * scale));
    }
}

/// <summary>
/// The loaded bitmaps behind the inline images of one open document, keyed by the absolute path they resolve
/// to. Decoding is the expensive half of drawing a picture and the layout asks for an image's size on every
/// pass, so a miss loads once and a failure is remembered too (a missing or unreadable file must not be
/// re-opened on every keystroke). The editor clears it when the document it is anchored against changes.
/// </summary>
internal sealed class InlineImageCache
{
    private readonly Dictionary<string, Bitmap?> _loaded = new(StringComparer.Ordinal);

    /// <summary>The size <paramref name="url"/>'s image draws at in a column <paramref name="maxWidth"/> wide,
    /// or null when it doesn't resolve to a picture this editor can draw.</summary>
    public InlineImage? Resolve(string url, string? documentPath, double maxWidth)
    {
        if (Lookup(url, documentPath) is not { } bitmap)
        {
            return null;
        }

        var size = bitmap.Size;
        var fitted = InlineImages.Fit(size.Width, size.Height, maxWidth);
        return fitted.Height > 0 ? fitted : null;
    }

    /// <summary>The decoded bitmap for <paramref name="url"/>, or null when it is remote, missing, or not an
    /// image. Loaded on first ask and cached — failures included.</summary>
    public Bitmap? Lookup(string url, string? documentPath)
    {
        if (InlineImages.ResolvePath(url, documentPath) is not { } path)
        {
            return null;
        }

        if (_loaded.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new Bitmap(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            bitmap = null; // an unreadable or undecodable file just isn't a picture — the reference stays text
        }

        _loaded[path] = bitmap;
        return bitmap;
    }

    /// <summary>Forget every loaded bitmap (the document moved, or its images changed on disk).</summary>
    public void Clear()
    {
        foreach (var bitmap in _loaded.Values)
        {
            bitmap?.Dispose();
        }

        _loaded.Clear();
    }
}
