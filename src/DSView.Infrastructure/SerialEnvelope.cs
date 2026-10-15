// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DSView.Domain;

namespace DSView.Infrastructure;

internal sealed record SerialEvent(int Sequence, SourceSpan Span, string Kind, JsonElement First, JsonElement Second);

internal sealed class SerialEnvelope : IDisposable
{
    private readonly JsonDocument document;
    public IReadOnlyList<SerialEvent> Events { get; }
    public SerialEnvelope(Capture raw, DecoderSource source, string json, CancellationToken cancellation)
    {
        SerialDecodeRequest.Resolve(raw, source);
        if (json.Length > 4_000_000) throw new InvalidDataException("Serial output exceeds its limit.");
        document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 16 });
        try
        {
            var root = document.RootElement;
            Fields(root, "schema", "decoder", "sampleRate", "sampleCount", "options", "completion", "events");
            Require(Number(root.GetProperty("schema")) == 1 && Text(root.GetProperty("decoder")) == "1:" + source.EffectiveProtocol
                && Number(root.GetProperty("sampleRate")) == raw.SampleRate && Number(root.GetProperty("sampleCount")) == raw.SampleCount
                && Text(root.GetProperty("completion")) == "eof", "Serial output does not match its input.");
            var options = root.GetProperty("options");
            if (source.Spi is { } spi)
            {
                Fields(options, "cpol", "cpha", "bitOrder", "wordSize", "csPolarity", "channelMask");
                Require(Number(options.GetProperty("cpol")) == spi.Mode / 2 && Number(options.GetProperty("cpha")) == spi.Mode % 2
                    && Text(options.GetProperty("bitOrder")) == spi.BitOrder && Number(options.GetProperty("wordSize")) == spi.WordSize
                    && Text(options.GetProperty("csPolarity")) == spi.ChipSelectPolarity && Number(options.GetProperty("channelMask")) == SerialDecodeRequest.SpiMask(source), "SPI options changed in transit.");
            }
            else
            {
                var uart = source.Uart!;
                Fields(options, "baudRate", "dataBits", "parity", "stopBits", "bitOrder", "invert");
                Require(Number(options.GetProperty("baudRate")) == uart.BaudRate && Number(options.GetProperty("dataBits")) == uart.DataBits
                    && Text(options.GetProperty("parity")) == uart.Parity && Number(options.GetProperty("stopBits")) == uart.StopBits
                    && Text(options.GetProperty("bitOrder")) == uart.BitOrder && Boolean(options.GetProperty("invert")) == uart.Inverted, "UART options changed in transit.");
            }
            var events = root.GetProperty("events");
            Require(events.ValueKind == JsonValueKind.Array && events.GetArrayLength() <= 200_000, "Invalid serial event count.");
            var result = new List<SerialEvent>();
            foreach (var item in events.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                Fields(item, "sequence", "start", "end", "record");
                var span = new SourceSpan(Number(item.GetProperty("start")), Number(item.GetProperty("end")));
                Require(Number(item.GetProperty("sequence")) == result.Count && span.Start >= 0 && span.Start < raw.SampleCount
                    && span.Start <= span.End && span.End <= raw.SampleCount * 2, "Invalid serial event identity or interval.");
                var record = item.GetProperty("record"); Array(record, 3);
                result.Add(new(result.Count, span, Text(record[0]), record[1], record[2]));
            }
            Events = result;
        }
        catch { document.Dispose(); throw; }
    }
    public void Dispose() => document.Dispose();
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static void Fields(JsonElement item, params string[] names) => Require(item.ValueKind == JsonValueKind.Object
        && item.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names), "Unknown or missing serial fields.");
    public static void Array(JsonElement item, int count) => Require(item.ValueKind == JsonValueKind.Array && item.GetArrayLength() == count, "Invalid native record shape.");
    public static long Number(JsonElement item) { Require(item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out _), "Expected integer."); return item.GetInt64(); }
    public static string Text(JsonElement item) { Require(item.ValueKind == JsonValueKind.String, "Expected text."); return item.GetString()!; }
    public static bool Boolean(JsonElement item) { Require(item.ValueKind is JsonValueKind.True or JsonValueKind.False, "Expected boolean."); return item.GetBoolean(); }
    public static BigInteger Value(JsonElement item, int bits)
    {
        string value = item.GetRawText();
        Require(item.ValueKind == JsonValueKind.Number && value.Length <= 39 && value.All(char.IsAsciiDigit)
            && BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _), "Expected unsigned word.");
        var parsed = BigInteger.Parse(value, CultureInfo.InvariantCulture);
        Require(parsed >= 0 && parsed < (BigInteger.One << bits), "Word exceeds its declared width."); return parsed;
    }
    public static SerialWord Word(BigInteger value, int width, SourceSpan span) => new(width,
        Convert.ToHexString(value.ToByteArray(isUnsigned: true, isBigEndian: true)).TrimStart('0').PadLeft((width + 3) / 4, '0'), span);
    public static bool Level(Signal signal, long sample)
    { int index = signal.LowerBound(sample + 1); return index == 0 ? signal.Initial : signal.Edges[index - 1].High; }
}
