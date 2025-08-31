//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace AwtrixSharpWeb.Apps.Configs
//{
//    /// <summary>
//    /// Custom JSON converter for AppConfigKeys to properly handle deserialization of configuration dictionary
//    /// </summary>
//    public class AppConfigKeysJsonConverter : JsonConverter<AppConfigKeys>
//    {
//        public override AppConfigKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//        {
//            if (reader.TokenType != JsonTokenType.StartObject)
//            {
//                throw new JsonException("Expected start of object for AppConfigKeys");
//            }

//            var configKeys = new AppConfigKeys();
            
//            while (reader.Read())
//            {
//                if (reader.TokenType == JsonTokenType.EndObject)
//                {
//                    return configKeys;
//                }

//                if (reader.TokenType != JsonTokenType.PropertyName)
//                {
//                    throw new JsonException("Expected property name in AppConfigKeys object");
//                }

//                string key = reader.GetString();
//                reader.Read();

//                // Handle different value types and convert to string
//                switch (reader.TokenType)
//                {
//                    case JsonTokenType.String:
//                        configKeys[key] = reader.GetString();
//                        break;
                    
//                    case JsonTokenType.Number:
//                        if (reader.TryGetInt32(out int intValue))
//                        {
//                            configKeys[key] = intValue.ToString();
//                        }
//                        else if (reader.TryGetInt64(out long longValue))
//                        {
//                            configKeys[key] = longValue.ToString();
//                        }
//                        else if (reader.TryGetDouble(out double doubleValue))
//                        {
//                            configKeys[key] = doubleValue.ToString();
//                        }
//                        else
//                        {
//                            configKeys[key] = reader.GetDecimal().ToString();
//                        }
//                        break;
                    
//                    case JsonTokenType.True:
//                        configKeys[key] = "true";
//                        break;
                    
//                    case JsonTokenType.False:
//                        configKeys[key] = "false";
//                        break;
                    
//                    case JsonTokenType.Null:
//                        configKeys[key] = string.Empty;
//                        break;
                    
//                    case JsonTokenType.StartObject:
//                    case JsonTokenType.StartArray:
//                        {
//                            // Capture nested objects and arrays as JSON strings
//                            var originalPosition = reader.CurrentState;
//                            using var doc = JsonDocument.ParseValue(ref reader);
//                            var jsonString = doc.RootElement.ToString();
//                            configKeys[key] = jsonString;
//                        }
//                        break;
                    
//                    default:
//                        throw new JsonException($"Unexpected token type {reader.TokenType} for property {key}");
//                }
//            }
            
//            throw new JsonException("Unexpected end of JSON while reading AppConfigKeys");
//        }

//        public override void Write(Utf8JsonWriter writer, AppConfigKeys value, JsonSerializerOptions options)
//        {
//            writer.WriteStartObject();
            
//            foreach (var kvp in value)
//            {
//                writer.WritePropertyName(kvp.Key);
                
//                // Try to determine if the value should be written as a number, boolean, or object/array
//                if (string.IsNullOrEmpty(kvp.Value))
//                {
//                    writer.WriteNullValue();
//                }
//                else if (bool.TryParse(kvp.Value, out bool boolValue))
//                {
//                    writer.WriteBooleanValue(boolValue);
//                }
//                else if (int.TryParse(kvp.Value, out int intValue))
//                {
//                    writer.WriteNumberValue(intValue);
//                }
//                else if (double.TryParse(kvp.Value, out double doubleValue))
//                {
//                    writer.WriteNumberValue(doubleValue);
//                }
//                else if ((kvp.Value.StartsWith("{") && kvp.Value.EndsWith("}")) || 
//                         (kvp.Value.StartsWith("[") && kvp.Value.EndsWith("]")))
//                {
//                    // This looks like a JSON object or array - try to preserve it
//                    try
//                    {
//                        using var doc = JsonDocument.Parse(kvp.Value);
//                        doc.RootElement.WriteTo(writer);
//                    }
//                    catch
//                    {
//                        // If parsing fails, fall back to string
//                        writer.WriteStringValue(kvp.Value);
//                    }
//                }
//                else
//                {
//                    writer.WriteStringValue(kvp.Value);
//                }
//            }
            
//            writer.WriteEndObject();
//        }
//    }
//}