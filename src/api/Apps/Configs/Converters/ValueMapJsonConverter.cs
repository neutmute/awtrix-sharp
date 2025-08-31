//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace AwtrixSharpWeb.Apps.Configs
//{
//    /// <summary>
//    /// A JSON converter that supports converting between JSON arrays and ValueMap collections
//    /// </summary>
//    public class ValueMapJsonConverter : JsonConverter<List<ValueMap>>
//    {
//        public override List<ValueMap> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//        {
//            if (reader.TokenType != JsonTokenType.StartArray)
//            {
//                throw new JsonException("Expected start of array");
//            }

//            var valueMaps = new List<ValueMap>();
            
//            while (reader.Read())
//            {
//                if (reader.TokenType == JsonTokenType.EndArray)
//                {
//                    break;
//                }

//                if (reader.TokenType == JsonTokenType.StartObject)
//                {
//                    var valueMap = new ValueMap();
                    
//                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
//                    {
//                        if (reader.TokenType == JsonTokenType.PropertyName)
//                        {
//                            string propertyName = reader.GetString();
//                            reader.Read();
                            
//                            // For ValueMatcher (required), we should ensure it exists
//                            if (propertyName.Equals("ValueMatcher", StringComparison.OrdinalIgnoreCase))
//                            {
//                                if (reader.TokenType == JsonTokenType.String)
//                                {
//                                    string value = reader.GetString();
//                                    if (!string.IsNullOrEmpty(value))
//                                    {
//                                        valueMap.ValueMatcher = value;
//                                    }
//                                }
//                                else
//                                {
//                                    // Skip non-string ValueMatcher values
//                                    reader.Skip();
//                                }
//                            }
//                            else
//                            {
//                                // For all other properties, add them as key-value pairs
//                                switch (reader.TokenType)
//                                {
//                                    case JsonTokenType.String:
//                                        valueMap[propertyName] = reader.GetString();
//                                        break;
                                        
//                                    case JsonTokenType.Number:
//                                        if (reader.TryGetInt32(out int intValue))
//                                        {
//                                            valueMap[propertyName] = intValue.ToString();
//                                        }
//                                        else if (reader.TryGetDouble(out double doubleValue))
//                                        {
//                                            valueMap[propertyName] = doubleValue.ToString();
//                                        }
//                                        else
//                                        {
//                                            valueMap[propertyName] = reader.GetDecimal().ToString();
//                                        }
//                                        break;
                                        
//                                    case JsonTokenType.True:
//                                        valueMap[propertyName] = "true";
//                                        break;
                                        
//                                    case JsonTokenType.False:
//                                        valueMap[propertyName] = "false";
//                                        break;
                                        
//                                    case JsonTokenType.Null:
//                                        valueMap[propertyName] = string.Empty;
//                                        break;
                                        
//                                    case JsonTokenType.StartObject:
//                                    case JsonTokenType.StartArray:
//                                        {
//                                            // Capture nested objects and arrays as JSON strings
//                                            using var doc = JsonDocument.ParseValue(ref reader);
//                                            var jsonString = doc.RootElement.ToString();
//                                            valueMap[propertyName] = jsonString;
//                                        }
//                                        break;
                                        
//                                    default:
//                                        // Skip anything else
//                                        reader.Skip();
//                                        break;
//                                }
//                            }
//                        }
//                    }
                    
//                    // Only add the ValueMap if it has a ValueMatcher
//                    if (!string.IsNullOrEmpty(valueMap.ValueMatcher))
//                    {
//                        valueMaps.Add(valueMap);
//                    }
//                }
//                else
//                {
//                    // Skip anything that's not an object in the array
//                    reader.Skip();
//                }
//            }
            
//            return valueMaps;
//        }

//        public override void Write(Utf8JsonWriter writer, List<ValueMap> value, JsonSerializerOptions options)
//        {
//            writer.WriteStartArray();
            
//            foreach (var valueMap in value)
//            {
//                writer.WriteStartObject();
                
//                // Always write ValueMatcher first
//                writer.WriteString("ValueMatcher", valueMap.ValueMatcher);
                
//                foreach (var kvp in valueMap.Where(k => k.Key != "ValueMatcher"))
//                {
//                    writer.WritePropertyName(kvp.Key);
                    
//                    // Try to determine if the value should be written as a number, boolean, or object/array
//                    if (string.IsNullOrEmpty(kvp.Value))
//                    {
//                        writer.WriteNullValue();
//                    }
//                    else if (bool.TryParse(kvp.Value, out bool boolValue))
//                    {
//                        writer.WriteBooleanValue(boolValue);
//                    }
//                    else if (int.TryParse(kvp.Value, out int intValue))
//                    {
//                        writer.WriteNumberValue(intValue);
//                    }
//                    else if (double.TryParse(kvp.Value, out double doubleValue))
//                    {
//                        writer.WriteNumberValue(doubleValue);
//                    }
//                    else if ((kvp.Value.StartsWith("{") && kvp.Value.EndsWith("}")) || 
//                             (kvp.Value.StartsWith("[") && kvp.Value.EndsWith("]")))
//                    {
//                        // This looks like a JSON object or array - try to preserve it
//                        try
//                        {
//                            using var doc = JsonDocument.Parse(kvp.Value);
//                            doc.RootElement.WriteTo(writer);
//                        }
//                        catch
//                        {
//                            // If parsing fails, fall back to string
//                            writer.WriteStringValue(kvp.Value);
//                        }
//                    }
//                    else
//                    {
//                        writer.WriteStringValue(kvp.Value);
//                    }
//                }
                
//                writer.WriteEndObject();
//            }
            
//            writer.WriteEndArray();
//        }
//    }
//}