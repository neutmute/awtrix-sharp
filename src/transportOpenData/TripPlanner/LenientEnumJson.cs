using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TransportOpenData.TripPlanner
{
    /// <summary>
    /// CR-38: the generated model puts a strict JsonStringEnumConverter attribute on every enum property, so one value
    /// TfNSW adds later (e.g. "gisPoint" on a leg stop) fails the whole response. A property-level [JsonConverter]
    /// beats options.Converters, so this uses a resolver modifier, which runs after attributes are read and can
    /// replace the per-property converter.
    /// </summary>
    public static class LenientEnumJson
    {
        public static void Apply(JsonSerializerOptions settings)
        {
            var resolver = settings.TypeInfoResolver as DefaultJsonTypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(UseLenientEnumConverters);
            settings.TypeInfoResolver = resolver;
        }

        private static void UseLenientEnumConverters(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                var enumType = Nullable.GetUnderlyingType(property.PropertyType);
                if (enumType is { IsEnum: true })
                {
                    property.CustomConverter = (JsonConverter)Activator.CreateInstance(
                        typeof(LenientNullableEnumConverter<>).MakeGenericType(enumType))!;
                }
            }
        }
    }

    /// <summary>
    /// Reads a nullable enum from its [EnumMember] wire name, member name (ignoring case) or defined number.
    /// Anything else becomes null instead of failing the response.
    /// </summary>
    public sealed class LenientNullableEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ByWireName = BuildLookup();

        private static readonly Dictionary<TEnum, string> WireNames = ByWireName
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);

        private static Dictionary<string, TEnum> BuildLookup()
        {
            var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (TEnum)field.GetValue(null)!;
                var wireName = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
                if (!string.IsNullOrEmpty(wireName))
                {
                    map.TryAdd(wireName, value); // first, so Write prefers the wire name
                }

                map.TryAdd(field.Name, value);
            }

            return map;
        }

        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String when ByWireName.TryGetValue(reader.GetString()!, out var named):
                    return named;
                case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number):
                    return (TEnum)Enum.ToObject(typeof(TEnum), number);
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    // TrySkip, not Skip: Skip throws on a non-final buffer during stream deserialisation
                    reader.TrySkip();
                    return null;
                default:
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(WireNames.TryGetValue(value.Value, out var wireName) ? wireName : value.Value.ToString());
        }
    }
}
