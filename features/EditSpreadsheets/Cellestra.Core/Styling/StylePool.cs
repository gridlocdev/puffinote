namespace Cellestra.Core.Styling;

/// <summary>
/// Stores each distinct <see cref="CellStyle"/> once and hands out a stable integer id
/// (ARCHITECTURE §5). Cells reference styles by id, so a million cells sharing one style
/// cost one style object. Index 0 is always <see cref="CellStyle.Default"/>.
/// </summary>
public sealed class StylePool
{
    private readonly List<CellStyle> _styles = new();
    private readonly Dictionary<CellStyle, int> _index = new();

    public StylePool()
    {
        // Index 0 is reserved for the default style.
        Intern(CellStyle.Default);
    }

    public int Count => _styles.Count;

    public const int DefaultId = 0;

    /// <summary>Return the id for a style, adding it if new. Equal styles share an id (value equality).</summary>
    public int Intern(CellStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (_index.TryGetValue(style, out int existing))
        {
            return existing;
        }

        int id = _styles.Count;
        _styles.Add(style);
        _index[style] = id;
        return id;
    }

    public CellStyle Get(int id)
    {
        if (id < 0 || id >= _styles.Count)
        {
            return CellStyle.Default;
        }

        return _styles[id];
    }

    /// <summary>Derive a new style from an existing one and intern the result, returning its id.</summary>
    public int Mutate(int baseId, Func<CellStyle, CellStyle> change) => Intern(change(Get(baseId)));

    public IReadOnlyList<CellStyle> Styles => _styles;
}
