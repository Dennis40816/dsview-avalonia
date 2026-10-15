// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record DslChannel(int Id, string Name);
public sealed record DslImportMetadata(int Version, string Layout, bool Complete, long SampleRate, long SampleCount,
    long PayloadBytes, long? TriggerPosition, long? TriggerTimeUnixMs, DslChannel[] Channels);
public sealed record DslImportResult(string SourceName, Capture Capture);

public static class DslCrossDataAdapter
{
    public const int MaximumTransitions = 2_000_000;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, MaxDepth = 8
    };
    public static DslImportMetadata ReadMetadata(string json)
    {
        if (json.Length > 32_768) throw new InvalidDataException("Capture metadata is too large.");
        try
        {
            using var parsed = JsonDocument.Parse(json, new() { MaxDepth = 8 });
            void Unique(JsonElement item)
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var p in item.EnumerateObject()) { if (!seen.Add(p.Name)) throw new InvalidDataException("Duplicate capture metadata."); Unique(p.Value); }
                }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) Unique(child);
            }
            Unique(parsed.RootElement);
            var value = JsonSerializer.Deserialize<DslImportMetadata>(json, Json) ?? throw new InvalidDataException("Missing capture metadata.");
            Validate(value); return value;
        }
        catch (JsonException ex) { throw new InvalidDataException("Incomplete or unsupported capture metadata.", ex); }
    }
    public static void Validate(DslImportMetadata value)
    {
        if (value == null || value.Version != 1 || value.Layout != "cross64-le-lsb" || !value.Complete
            || value.SampleRate is <= 0 or > 1_000_000_000 || value.SampleCount is < 10 or > 64_000_000
            || value.Channels is not { Length: > 0 and <= 32 } || value.PayloadBytes is <= 0 or > AnalysisFile.MaximumBytes
            || value.PayloadBytes != (value.SampleCount + 63) / 64 * value.Channels.Length * 8
            || value.TriggerPosition is long trigger && (trigger < 0 || trigger > value.SampleCount))
            throw new InvalidDataException("Incomplete, excessive or unsupported capture metadata.");
        int previous = -1;
        foreach (var channel in value.Channels)
        {
            if (channel == null || channel.Id <= previous || channel.Id is < 0 or > 31 || string.IsNullOrWhiteSpace(channel.Name)
                || channel.Name.Length > 128 || channel.Name.Any(char.IsControl))
                throw new InvalidDataException("Capture channels need ordered physical IDs and readable names up to 128 characters.");
            previous = channel.Id;
        }
    }
    public static async Task<DslImportResult> ReadAsync(Stream payload, DslImportMetadata metadata, string originalName, string sourceSha256,
        CancellationToken cancellation = default)
    {
        Validate(metadata); cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 1024 || originalName.Any(char.IsControl)
            || sourceSha256.Length != 64 || !sourceSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid capture provenance.");
        if (payload.CanSeek && payload.Length - payload.Position != metadata.PayloadBytes)
            throw new InvalidDataException("Capture payload length does not match its metadata.");
        int count = metadata.Channels.Length, stride = count * 8, totalEdges = 0;
        var edges = Enumerable.Range(0, count).Select(_ => new List<Edge>()).ToArray();
        var initial = new bool[count]; var previous = new ulong[count];
        byte[] buffer = new byte[stride * 512]; long sampleBase = 0, remaining = metadata.PayloadBytes;
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            int bytes = (int)Math.Min(buffer.Length, remaining);
            try { await payload.ReadExactlyAsync(buffer.AsMemory(0, bytes), cancellation).ConfigureAwait(false); }
            catch (EndOfStreamException ex) { throw new InvalidDataException("Capture payload ended early.", ex); }
            for (int offset = 0; offset < bytes; offset += stride, sampleBase += 64)
            {
                cancellation.ThrowIfCancellationRequested();
                int validBits = (int)Math.Min(64, metadata.SampleCount - sampleBase);
                for (int channel = 0; channel < count; channel++)
                {
                    ulong word = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + channel * 8, 8));
                    ulong changes = word ^ ((word << 1) | previous[channel]);
                    if (sampleBase == 0) { initial[channel] = (word & 1) != 0; changes &= ~1UL; }
                    if (validBits < 64) changes &= (1UL << validBits) - 1;
                    int added = BitOperations.PopCount(changes);
                    if (added > MaximumTransitions - totalEdges)
                        throw new InvalidDataException("This capture exceeds the current 2,000,000-transition analysis limit.");
                    totalEdges += added;
                    while (changes != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(changes);
                        edges[channel].Add(new(sampleBase + bit, ((word >> bit) & 1) != 0)); changes &= changes - 1;
                    }
                    previous[channel] = (word >> 63) & 1;
                }
            }
            remaining -= bytes;
        }
        if (await payload.ReadAsync(buffer.AsMemory(0, 1), cancellation).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Capture payload contains extra data.");
        cancellation.ThrowIfCancellationRequested();
        string name = originalName;
        if (name.Length > 128) { int length = char.IsHighSurrogate(name[126]) ? 126 : 127; name = name[..length] + "…"; }
        string position = metadata.TriggerPosition?.ToString(CultureInfo.InvariantCulture) ?? "not recorded";
        string timestamp = metadata.TriggerTimeUnixMs?.ToString(CultureInfo.InvariantCulture) ?? "not recorded";
        var capture = new Capture(metadata.SampleRate, metadata.SampleCount,
            metadata.Channels.Select((c, i) => new Signal(c.Id, c.Name, initial[i], edges[i].ToArray())).ToArray(), [])
        {
            SourceDescription = $"Imported DSView logic-v3 capture: {originalName}. Raw digital samples; protocol decoding has not run. " +
                $"Time zero is the first stored sample. Stored trigger position: {position} samples; stored trigger time: {timestamp} Unix milliseconds. " +
                $"Saved device settings and decoder configuration are not applied. Source SHA-256: {sourceSha256.ToLowerInvariant()}."
        };
        return new(name, capture);
    }
}
