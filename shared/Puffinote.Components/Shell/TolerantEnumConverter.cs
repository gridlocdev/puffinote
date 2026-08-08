using System.Text.Json;
using System.Text.Json.Serialization;

namespace Puffinote.Components.Shell;

/// <summary>
/// Reads enums from settings JSON by <b>name</b> (what every app in the family writes) or by number, and
/// answers anything else with the enum's default member instead of throwing.
/// <para>
/// The tolerance is the point. <see cref="SettingsStore.Load{T}"/> is tolerant per <i>file</i>: one
/// unparseable value and the whole file is discarded for defaults. That is far too blunt for an enum, whose
/// values are exactly the settings most likely to go stale — a mode renamed between releases, a name typed
/// by hand into the file — and losing every other setting in the file because one word no longer resolves
/// is not a fallback, it is data loss. Here an unrecognized value costs you that one setting.
/// </para>
/// </summary>
public sealed class TolerantEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String when Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var named):
                    return named;

                case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number):
                    return (TEnum)Enum.ToObject(typeof(TEnum), number);

                default:
                    // Skip is a no-op on a scalar and consumes a whole object/array, so a value of any
                    // shape leaves the reader positioned for the next property.
                    reader.Skip();
                    return default;
            }
        }

        /// <summary>Always writes the name — a settings file is meant to be readable and hand-editable.</summary>
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }
}
