// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using DSView.Domain;

namespace DSView.Infrastructure;

// Admission for the new v4 metadata. Reject duplicate keys before materializing
// roles; ordinary deserialization otherwise retains only the last key's value.
internal sealed class DecoderSourceJsonConverter : JsonConverter<DecoderSource>
{
    public override DecoderSource Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var source = document.RootElement; RequireMembers(source, "id", "inputs");
        var inputs = source.GetProperty("inputs");
        if (inputs.ValueKind != JsonValueKind.Array || inputs.GetArrayLength() > 4) throw new JsonException("Invalid decoder inputs.");
        var roles = new List<DecoderInput>();
        foreach (var input in inputs.EnumerateArray())
        {
            RequireMembers(input, "role", "channel");
            if (input.GetProperty("role").ValueKind != JsonValueKind.String || input.GetProperty("channel").ValueKind != JsonValueKind.Number
                || !input.GetProperty("channel").TryGetInt32(out int channel))
                throw new JsonException("Invalid decoder input types.");
            roles.Add(new(input.GetProperty("role").GetString()!, channel));
        }
        if (source.GetProperty("id").ValueKind != JsonValueKind.String) throw new JsonException("Invalid decoder identity.");
        return new(source.GetProperty("id").GetString()!, roles.ToArray());
    }
    private static void RequireMembers(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected decoder object.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!fields.Add(item.Name) || !expected.Contains(item.Name)) throw new JsonException("Duplicate or unknown decoder field.");
        if (!fields.SetEquals(expected)) throw new JsonException("Missing decoder field.");
    }
    public override void Write(Utf8JsonWriter writer, DecoderSource source, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("id", source.Id); writer.WriteStartArray("inputs");
        foreach (var input in source.Inputs)
        {
            writer.WriteStartObject(); writer.WriteString("role", input.Role); writer.WriteNumber("channel", input.Channel); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
}
