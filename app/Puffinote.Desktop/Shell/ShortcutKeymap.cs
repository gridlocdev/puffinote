using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Puffinote.Components.Commands;
using Puffinote.Components.Shell;

namespace Puffinote.Desktop.Shell;

/// <summary>
/// The user's custom keyboard bindings for Puffinote's <em>shell</em> commands (PRD §7.6), persisted at the
/// per-user scope as id → <see cref="Shortcut"/>. The shortcuts editor writes these, and
/// <c>BuildShellCommands</c> consults them so a rebound command's menu item, key binding, and palette entry
/// all pick up the new key. Reserved global keys are fixed and never overridden here (they're marked in the
/// editor). Tolerant, best-effort I/O — a missing/corrupt file just yields the defaults.
/// </summary>
internal sealed class ShortcutKeymap
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }, // readable Key/modifier names in keymap.json
    };

    private readonly string _path;
    private readonly Dictionary<string, Shortcut> _bindings;

    private ShortcutKeymap(string path, Dictionary<string, Shortcut> bindings)
    {
        _path = path;
        _bindings = bindings;
    }

    private static string DefaultPath => SettingsStore.AppConfigPath("Puffinote", "keymap.json");

    /// <summary>Load the keymap (tolerant). <paramref name="path"/> defaults to the per-user keymap file.</summary>
    public static ShortcutKeymap Load(string? path = null)
    {
        var resolved = path ?? DefaultPath;
        return new ShortcutKeymap(resolved, SettingsStore.Load(resolved, static () => new Dictionary<string, Shortcut>(), Options));
    }

    /// <summary>The user's custom binding for <paramref name="id"/>, or null when they haven't rebound it.</summary>
    public Shortcut? For(string id) => _bindings.TryGetValue(id, out var shortcut) ? shortcut : null;

    /// <summary>The effective shortcut for <paramref name="id"/>: the user's binding if any, else <paramref name="fallback"/>.</summary>
    public Shortcut? Resolve(string id, Shortcut? fallback) => For(id) ?? fallback;

    /// <summary>The ids that currently carry a custom binding (for the editor's "reset" affordance).</summary>
    public IReadOnlyCollection<string> CustomizedIds => _bindings.Keys.ToList();

    /// <summary>Rebind <paramref name="id"/> to <paramref name="shortcut"/> (null clears it), then persist.</summary>
    public void Set(string id, Shortcut? shortcut)
    {
        if (shortcut is { } value)
        {
            _bindings[id] = value;
        }
        else
        {
            _bindings.Remove(id);
        }

        SettingsStore.Save(_path, _bindings, Options);
    }
}
