using System.Text.Json;
using System.Text.Json.Serialization;

namespace AwtrixSharpWeb.Apps.Configs
{
    /// <summary>
    /// A JSON converter that supports converting between JSON arrays and ValueMap collections
    /// </summary>
    public class ValueMapJsonConverter : JsonConverter<List<ValueMap>>
    {
        public override List<ValueMap> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected start of array");
            }

            var valueMaps = new List<ValueMap>();
            
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var valueMap = new ValueMap();
                    
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            string propertyName = reader.GetString();
                            reader.Read();
                            
                            // For ValueMatcher (required), we should ensure it exists
                            if (propertyName.Equals("ValueMatcher", StringComparison.OrdinalIgnoreCase))
                            {
                                string value = reader.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    valueMap.ValueMatcher = value;
                                }
                            }
                            else
                            {
                                // For all other properties, add them as key-value pairs
                                if (reader.TokenType == JsonTokenType.String)
                                {
                                    valueMap[propertyName] = reader.GetString();
                                }
                                else if (reader.TokenType == JsonTokenType.Number)
                                {
                                    valueMap[propertyName] = reader.GetInt32().ToString();
                                }
                                else if (reader.TokenType == JsonTokenType.True)
                                {
                                    valueMap[propertyName] = "true";
                                }
                                else if (reader.TokenType == JsonTokenType.False)
                                {
                                    valueMap[propertyName] = "false";
                                }
                                else if (reader.TokenType == JsonTokenType.Null)
                                {
                                    valueMap[propertyName] = string.Empty;
                                }
                            }
                        }
                    }
                    
                    // Only add the ValueMap if it has a ValueMatcher
                    if (!string.IsNullOrEmpty(valueMap.ValueMatcher))
                    {
                        valueMaps.Add(valueMap);
                    }
                }
            }
            
            return valueMaps;
        }

        public override void Write(Utf8JsonWriter writer, List<ValueMap> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            
            foreach (var valueMap in value)
            {
                writer.WriteStartObject();
                
                foreach (var kvp in valueMap)
                {
                    writer.WritePropertyName(kvp.Key);
                    writer.WriteStringValue(kvp.Value);
                }
                
                writer.WriteEndObject();
            }
            
            writer.WriteEndArray();
        }
    }
}