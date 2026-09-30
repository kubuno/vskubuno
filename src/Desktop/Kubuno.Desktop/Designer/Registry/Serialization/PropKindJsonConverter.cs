using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubuno.Desktop.Designer.Registry.Serialization
{
    /// <summary>
    /// Reads/writes <see cref="PropKind"/>'s polymorphic shape (docs/DESIGNER.md §5): the three scalar
    /// variants are bare JSON strings ("Bool"/"F32"/"String" - <c>serde</c>'s default, unrenamed
    /// unit-variant encoding), and <c>Enum</c> alone carries data, so it is the one-key object
    /// <c>{"Enum": ["A", "B", ...]}</c> - again <c>serde</c>'s default "externally tagged" encoding for a
    /// tuple variant, not something invented for this bridge.
    /// </summary>
    public sealed class PropKindJsonConverter : JsonConverter<PropKind>
    {
        public override PropKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var tag = reader.GetString();
                return tag switch
                {
                    "Bool" => PropKind.Bool,
                    "F32" => PropKind.F32,
                    "String" => PropKind.String,
                    _ => throw new JsonException($"Unknown scalar PropKind tag '{tag}'."),
                };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "Enum")
                {
                    throw new JsonException("Expected a single \"Enum\" property on an object-shaped PropKind.");
                }

                reader.Read();
                var variants = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? new List<string>();

                reader.Read();
                if (reader.TokenType != JsonTokenType.EndObject)
                {
                    throw new JsonException("Expected the object-shaped PropKind to close after its \"Enum\" property.");
                }

                return PropKind.CreateEnum(variants);
            }

            throw new JsonException($"Unexpected token {reader.TokenType} for PropKind.");
        }

        public override void Write(Utf8JsonWriter writer, PropKind value, JsonSerializerOptions options)
        {
            if (value.Tag == PropKindTag.Enum)
            {
                writer.WriteStartObject();
                writer.WriteStartArray("Enum");
                foreach (var variant in value.EnumVariants)
                {
                    writer.WriteStringValue(variant);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStringValue(value.Tag.ToString());
            }
        }
    }
}
