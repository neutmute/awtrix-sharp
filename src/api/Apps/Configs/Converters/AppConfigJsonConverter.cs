//using System.Text.Json;
//using System.Text.Json.Serialization;

//namespace AwtrixSharpWeb.Apps.Configs
//{
//    /// <summary>
//    /// Custom JSON converter for AppConfig to properly handle deserialization of complex properties
//    /// </summary>
//    public class AppConfigJsonConverter : JsonConverter<AppConfig>
//    {
//        private readonly AppConfigKeysJsonConverter _configKeysConverter;

//        public AppConfigJsonConverter()
//        {
//            _configKeysConverter = new AppConfigKeysJsonConverter();
//        }

//        public override AppConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//        {
//            if (reader.TokenType != JsonTokenType.StartObject)
//            {
//                throw new JsonException("Expected start of object");
//            }

//            var appConfig = new AppConfig();

//            while (reader.Read())
//            {
//                if (reader.TokenType == JsonTokenType.EndObject)
//                {
//                    return appConfig;
//                }

//                if (reader.TokenType != JsonTokenType.PropertyName)
//                {
//                    throw new JsonException("Expected property name");
//                }

//                var propertyName = reader.GetString();
//                reader.Read();

//                switch (propertyName)
//                {
//                    case "Type":
//                        if (reader.TokenType == JsonTokenType.String)
//                        {
//                            appConfig.Type = reader.GetString();
//                        }
//                        break;

//                    case "Environment":
//                        if (reader.TokenType == JsonTokenType.String)
//                        {
//                            appConfig.Environment = reader.GetString();
//                        }
//                        break;

//                    case "Config":
//                        // Use the dedicated AppConfigKeysJsonConverter for the Config property
//                        if (reader.TokenType == JsonTokenType.StartObject)
//                        {
//                            appConfig.Config = _configKeysConverter.Read(ref reader, typeof(AppConfigKeys), options);
//                        }
//                        break;

//                    case "ValueMaps":
//                        if (reader.TokenType == JsonTokenType.StartArray)
//                        {
//                            appConfig.ValueMaps = ReadValueMaps(ref reader, options);
//                        }
//                        break;

//                    default:
//                        // For any other property, process it based on token type
//                        ProcessPropertyValue(ref reader, appConfig, propertyName);
//                        break;
//                }
//            }

//            throw new JsonException("Expected end of object");
//        }

//        private void ProcessPropertyValue(ref Utf8JsonReader reader, AppConfig appConfig, string propertyName)
//        {
//            switch (reader.TokenType)
//            {
//                case JsonTokenType.String:
//                    appConfig.SetConfig(propertyName, reader.GetString());
//                    break;

//                case JsonTokenType.Number:
//                    if (reader.TryGetInt32(out int intValue))
//                    {
//                        appConfig.SetConfig(propertyName, intValue.ToString());
//                    }
//                    else if (reader.TryGetInt64(out long longValue))
//                    {
//                        appConfig.SetConfig(propertyName, longValue.ToString());
//                    }
//                    else if (reader.TryGetDouble(out double doubleValue))
//                    {
//                        appConfig.SetConfig(propertyName, doubleValue.ToString());
//                    }
//                    else
//                    {
//                        appConfig.SetConfig(propertyName, reader.GetDecimal().ToString());
//                    }
//                    break;

//                case JsonTokenType.True:
//                    appConfig.SetConfig(propertyName, "true");
//                    break;

//                case JsonTokenType.False:
//                    appConfig.SetConfig(propertyName, "false");
//                    break;

//                case JsonTokenType.Null:
//                    // Skip null values or set as empty string based on your preference
//                    // appConfig.SetConfig(propertyName, string.Empty);
//                    break;

//                case JsonTokenType.StartObject:
//                case JsonTokenType.StartArray:
//                    {
//                        // Capture complex objects and arrays as JSON strings
//                        using var doc = JsonDocument.ParseValue(ref reader);
//                        var jsonString = doc.RootElement.ToString();
//                        appConfig.SetConfig(propertyName, jsonString);
//                    }
//                    break;

//                default:
//                    // Skip anything else
//                    reader.Skip();
//                    break;
//            }
//        }

//        private List<ValueMap> ReadValueMaps(ref Utf8JsonReader reader, JsonSerializerOptions options)
//        {
//            // Try to use the existing ValueMapJsonConverter if available
//            var valueMapsConverter = options.Converters.FirstOrDefault(c => c is ValueMapJsonConverter) as ValueMapJsonConverter;
//            if (valueMapsConverter != null)
//            {
//                return valueMapsConverter.Read(ref reader, typeof(List<ValueMap>), options);
//            }

//            // Fallback to simple parsing if ValueMapJsonConverter is not available
//            var valueMaps = new List<ValueMap>();
            
//            while (reader.Read())
//            {
//                if (reader.TokenType == JsonTokenType.EndArray)
//                {
//                    return valueMaps;
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
                            
//                            // Process the property value based on its token type
//                            switch (reader.TokenType)
//                            {
//                                case JsonTokenType.String:
//                                    string value = reader.GetString();
//                                    if (propertyName.Equals("ValueMatcher", StringComparison.OrdinalIgnoreCase))
//                                    {
//                                        valueMap.ValueMatcher = value;
//                                    }
//                                    else
//                                    {
//                                        valueMap[propertyName] = value;
//                                    }
//                                    break;

//                                case JsonTokenType.Number:
//                                    if (reader.TryGetInt32(out int intValue))
//                                    {
//                                        valueMap[propertyName] = intValue.ToString();
//                                    }
//                                    else if (reader.TryGetDouble(out double doubleValue))
//                                    {
//                                        valueMap[propertyName] = doubleValue.ToString();
//                                    }
//                                    else
//                                    {
//                                        valueMap[propertyName] = reader.GetDecimal().ToString();
//                                    }
//                                    break;

//                                case JsonTokenType.True:
//                                    valueMap[propertyName] = "true";
//                                    break;

//                                case JsonTokenType.False:
//                                    valueMap[propertyName] = "false";
//                                    break;

//                                case JsonTokenType.Null:
//                                    valueMap[propertyName] = string.Empty;
//                                    break;

//                                case JsonTokenType.StartObject:
//                                case JsonTokenType.StartArray:
//                                    {
//                                        // Capture nested objects and arrays as JSON strings
//                                        using var doc = JsonDocument.ParseValue(ref reader);
//                                        var jsonString = doc.RootElement.ToString();
//                                        valueMap[propertyName] = jsonString;
//                                    }
//                                    break;

//                                default:
//                                    // Skip anything we don't understand
//                                    reader.Skip();
//                                    break;
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
//                    // Skip anything that's not an object inside the array
//                    reader.Skip();
//                }
//            }
            
//            throw new JsonException("Expected end of ValueMaps array");
//        }

//        public override void Write(Utf8JsonWriter writer, AppConfig value, JsonSerializerOptions options)
//        {
//            writer.WriteStartObject();

//            // Write simple properties
//            writer.WriteString("Type", value.Type);
            
//            if (!string.IsNullOrEmpty(value.Environment))
//            {
//                writer.WriteString("Environment", value.Environment);
//            }

//            // Write Config dictionary using the AppConfigKeysJsonConverter
//            if (value.Config != null && value.Config.Count > 0)
//            {
//                writer.WritePropertyName("Config");
//                _configKeysConverter.Write(writer, value.Config, options);
//            }

//            // Write ValueMaps
//            if (value.ValueMaps != null && value.ValueMaps.Count > 0)
//            {
//                writer.WritePropertyName("ValueMaps");
//                writer.WriteStartArray();

//                foreach (var valueMap in value.ValueMaps)
//                {
//                    writer.WriteStartObject();
                    
//                    // Always write ValueMatcher first
//                    writer.WriteString("ValueMatcher", valueMap.ValueMatcher);
                    
//                    foreach (var kvp in valueMap.Where(k => k.Key != "ValueMatcher"))
//                    {
//                        // Try to determine if the value should be written as a number, boolean, or object/array
//                        writer.WritePropertyName(kvp.Key);
                        
//                        if (string.IsNullOrEmpty(kvp.Value))
//                        {
//                            writer.WriteNullValue();
//                        }
//                        else if (bool.TryParse(kvp.Value, out bool boolValue))
//                        {
//                            writer.WriteBooleanValue(boolValue);
//                        }
//                        else if (int.TryParse(kvp.Value, out int intValue))
//                        {
//                            writer.WriteNumberValue(intValue);
//                        }
//                        else if (double.TryParse(kvp.Value, out double doubleValue))
//                        {
//                            writer.WriteNumberValue(doubleValue);
//                        }
//                        else if ((kvp.Value.StartsWith("{") && kvp.Value.EndsWith("}")) || 
//                                 (kvp.Value.StartsWith("[") && kvp.Value.EndsWith("]")))
//                        {
//                            // This looks like a JSON object or array - try to preserve it
//                            try
//                            {
//                                using var doc = JsonDocument.Parse(kvp.Value);
//                                doc.RootElement.WriteTo(writer);
//                            }
//                            catch
//                            {
//                                // If parsing fails, fall back to string
//                                writer.WriteStringValue(kvp.Value);
//                            }
//                        }
//                        else
//                        {
//                            writer.WriteStringValue(kvp.Value);
//                        }
//                    }
                    
//                    writer.WriteEndObject();
//                }

//                writer.WriteEndArray();
//            }

//            writer.WriteEndObject();
//        }
//    }
//}