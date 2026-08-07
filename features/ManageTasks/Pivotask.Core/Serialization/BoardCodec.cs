using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pivotask.Core.Model;

namespace Pivotask.Core.Serialization;

/// <summary>Thrown when a file isn't a board this build can open. Carries a message meant for a person.</summary>
public sealed class BoardFormatException(string message) : Exception(message);

/// <summary>
/// Reads and writes <c>.pivotask.json</c>. The serialization rules <i>are</i> the format's design, and
/// they exist because this file lives in git (proposal 0004 §4):
///
/// <list type="bullet">
/// <item><b>Deterministic output.</b> Stable key order, two-space indent, LF endings, trailing newline.
/// A save that changed nothing produces a byte-identical file, so it never appears in a diff — which is
/// what makes "did anything actually change?" answerable by looking.</item>
/// <item><b>Unknown fields are preserved.</b> A file written by a newer Pivotask, opened and saved by an
/// older one, must not lose data. Every object round-trips the properties it didn't recognise.</item>
/// <item><b>A version ahead of ours is refused, not mangled.</b> Silently reading half of a format we
/// don't know and writing our half back is how a file gets quietly destroyed.</item>
/// </list>
///
/// <para><see cref="System.Text.Json"/> only — BCL, so no new dependency and no allowlist change. It is
/// hand-written rather than attribute-driven because the unknown-field round-trip and the fixed key
/// order are the requirements, and neither is something a serializer does for you.</para>
/// </summary>
public static class BoardCodec
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        // The board's own strings are user text, and escaping is the default because output goes to a
        // file that other tools read. Relaxed encoding would put raw UTF-8 in, which is nicer to read
        // but changes what "byte-identical" means across .NET versions.
    };

    // Keys we model. Anything else on an object is unknown and travels through untouched.
    private static class Keys
    {
        public const string Version = "version";
        public const string Labels = "labels";
        public const string Groups = "groups";
        public const string Id = "id";
        public const string Name = "name";
        public const string Color = "color";
        public const string Collapsed = "collapsed";
        public const string FoldDone = "foldDone";
        public const string Tasks = "tasks";
        public const string Title = "title";
        public const string Notes = "notes";
        public const string Due = "due";
        public const string Important = "important";
        public const string Done = "done";
        public const string Steps = "steps";
        public const string Text = "text";
    }

    // ---- Reading ----------------------------------------------------------

    public static Board Read(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BoardFormatException($"This isn't valid JSON: {ex.Message}");
        }

        if (node is not JsonObject root)
        {
            throw new BoardFormatException("A board file's root must be an object.");
        }

        int version = root[Keys.Version]?.GetValue<int>() ?? Board.CurrentVersion;
        if (version > Board.CurrentVersion)
        {
            throw new BoardFormatException(
                $"This board was written by a newer Pivotask (format version {version}; this build reads {Board.CurrentVersion}). " +
                "Opening it would mean guessing at fields that didn't exist yet, so it hasn't been opened.");
        }

        var board = new Board();
        foreach (var label in Array(root, Keys.Labels))
        {
            board.Labels.Add(new Label
            {
                Id = String(label, Keys.Id) ?? Ids.New(),
                Name = String(label, Keys.Name) ?? "label",
                Color = String(label, Keys.Color) ?? "#64748b",
                Unknown = Rest(label, Keys.Id, Keys.Name, Keys.Color),
            });
        }

        foreach (var group in Array(root, Keys.Groups))
        {
            var target = new TaskGroup
            {
                Id = String(group, Keys.Id) ?? Ids.New(),
                Name = String(group, Keys.Name) ?? "Untitled",
                Collapsed = Bool(group, Keys.Collapsed),
                FoldDone = Bool(group, Keys.FoldDone),
                Unknown = Rest(group, Keys.Id, Keys.Name, Keys.Collapsed, Keys.FoldDone, Keys.Tasks),
            };

            foreach (var task in Array(group, Keys.Tasks))
            {
                var item = new TaskItem
                {
                    Id = String(task, Keys.Id) ?? Ids.New(),
                    Title = String(task, Keys.Title) ?? string.Empty,
                    Notes = String(task, Keys.Notes) ?? string.Empty,
                    Due = Ids.ParseDate(String(task, Keys.Due)),
                    Important = Bool(task, Keys.Important),
                    Done = Bool(task, Keys.Done),
                    Unknown = Rest(task, Keys.Id, Keys.Title, Keys.Notes, Keys.Due, Keys.Important, Keys.Done, Keys.Steps, Keys.Labels),
                };

                foreach (var step in Array(task, Keys.Steps))
                {
                    item.Steps.Add(new TaskStep
                    {
                        Id = String(step, Keys.Id) ?? Ids.New(),
                        Text = String(step, Keys.Text) ?? string.Empty,
                        Done = Bool(step, Keys.Done),
                        Unknown = Rest(step, Keys.Id, Keys.Text, Keys.Done),
                    });
                }

                if (task[Keys.Labels] is JsonArray labels)
                {
                    foreach (var label in labels)
                    {
                        if (label?.GetValue<string>() is { } id)
                        {
                            item.Labels.Add(id);
                        }
                    }
                }

                target.Tasks.Add(item);
            }

            board.Groups.Add(target);
        }

        foreach (var (key, value) in Rest(root, Keys.Version, Keys.Labels, Keys.Groups))
        {
            board.Unknown[key] = value;
        }

        return board;
    }

    // ---- Writing ----------------------------------------------------------

    /// <summary>
    /// Serialize <paramref name="board"/>. LF endings and a trailing newline regardless of platform:
    /// this file is committed, and a CRLF board on one machine and an LF board on another is a diff
    /// nobody wrote.
    /// </summary>
    public static string Write(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber(Keys.Version, Board.CurrentVersion);

            writer.WriteStartArray(Keys.Labels);
            foreach (var label in board.Labels)
            {
                writer.WriteStartObject();
                writer.WriteString(Keys.Id, label.Id);
                writer.WriteString(Keys.Name, label.Name);
                writer.WriteString(Keys.Color, label.Color);
                WriteUnknown(writer, label.Unknown);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray(Keys.Groups);
            foreach (var group in board.Groups)
            {
                writer.WriteStartObject();
                writer.WriteString(Keys.Id, group.Id);
                writer.WriteString(Keys.Name, group.Name);
                writer.WriteBoolean(Keys.Collapsed, group.Collapsed);
                writer.WriteBoolean(Keys.FoldDone, group.FoldDone);

                writer.WriteStartArray(Keys.Tasks);
                foreach (var task in group.Tasks)
                {
                    writer.WriteStartObject();
                    writer.WriteString(Keys.Id, task.Id);
                    writer.WriteString(Keys.Title, task.Title);
                    writer.WriteString(Keys.Notes, task.Notes);
                    if (task.Due is { } due)
                    {
                        writer.WriteString(Keys.Due, Ids.Format(due));
                    }
                    else
                    {
                        writer.WriteNull(Keys.Due);
                    }

                    writer.WriteBoolean(Keys.Important, task.Important);
                    writer.WriteBoolean(Keys.Done, task.Done);

                    writer.WriteStartArray(Keys.Labels);
                    foreach (string id in task.Labels)
                    {
                        writer.WriteStringValue(id);
                    }

                    writer.WriteEndArray();

                    writer.WriteStartArray(Keys.Steps);
                    foreach (var step in task.Steps)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(Keys.Id, step.Id);
                        writer.WriteString(Keys.Text, step.Text);
                        writer.WriteBoolean(Keys.Done, step.Done);
                        WriteUnknown(writer, step.Unknown);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    WriteUnknown(writer, task.Unknown);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WriteUnknown(writer, group.Unknown);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteUnknown(writer, board.Unknown);
            writer.WriteEndObject();
        }

        // Utf8JsonWriter emits the platform's newline; normalise, and end with one.
        string text = Encoding.UTF8.GetString(buffer.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return text.EndsWith('\n') ? text : text + "\n";
    }

    /// <summary>The bytes to write to disk: UTF-8, no BOM.</summary>
    public static byte[] Encode(Board board) => new UTF8Encoding(false).GetBytes(Write(board));

    private static void WriteUnknown(Utf8JsonWriter writer, Dictionary<string, JsonElement> unknown)
    {
        // Ordered, so an unknown field lands in the same place on every save. Unordered here would be
        // the one thing that made an otherwise-deterministic file churn in git.
        foreach (var (key, value) in unknown.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
    }

    // ---- Reading helpers --------------------------------------------------

    private static IEnumerable<JsonObject> Array(JsonObject parent, string key) =>
        parent[key] is JsonArray array ? array.OfType<JsonObject>() : [];

    private static string? String(JsonObject o, string key) =>
        o[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private static Dictionary<string, JsonElement> Rest(JsonObject o, params string[] known)
    {
        var rest = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in o)
        {
            if (!known.Contains(key, StringComparer.Ordinal) && value is not null)
            {
                rest[key] = JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
            }
        }

        return rest;
    }
}
