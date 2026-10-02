using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Shared.DevAssistant.Logic.Tools
{
    /// <summary>
    /// Validates a tool input against its JSON schema before anything runs (eager input streaming hands over inputs the
    /// API did not validate - docs/AI-ASSISTANT.md section 2.1). Covers the subset the assistant's schemas use: object,
    /// required, properties with string/integer/number/boolean/array/object types, enum, items, additionalProperties false.
    /// </summary>
    public static class ToolInputValidator
    {
        /// <summary>The problems found (empty when valid), phrased for the model.</summary>
        public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement input)
        {
            var errors = new List<string>();
            Check(schema, input, "input", errors);
            return errors;
        }

        private static void Check(JsonElement schema, JsonElement value, string path, List<string> errors)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && !Matches(type.GetString()!, value))
            {
                errors.Add($"{path}: expected {type.GetString()}, got {value.ValueKind.ToString().ToLowerInvariant()}");
                return;
            }

            if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array &&
                !choices.EnumerateArray().Any(c => c.GetRawText() == value.GetRawText()))
            {
                errors.Add($"{path}: must be one of {choices.GetRawText()}");
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                var properties = schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object ? props : default;
                if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                {
                    foreach (var name in required.EnumerateArray().Select(r => r.GetString()).Where(n => n is not null))
                    {
                        if (!value.TryGetProperty(name!, out _))
                        {
                            errors.Add($"{path}.{name}: required");
                        }
                    }
                }

                bool closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
                foreach (var property in value.EnumerateObject())
                {
                    if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
                    {
                        Check(propertySchema, property.Value, path + "." + property.Name, errors);
                    }
                    else if (closed)
                    {
                        errors.Add($"{path}.{property.Name}: unknown property");
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
            {
                int index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Check(items, item, $"{path}[{index++}]", errors);
                }
            }
        }

        private static bool Matches(string type, JsonElement value) => type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        };
    }
}
