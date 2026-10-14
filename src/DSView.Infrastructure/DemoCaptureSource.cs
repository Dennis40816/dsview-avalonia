// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record DemoExample(string Id, string Name, string Description)
{
    public override string ToString() => Name;
}

public static class DemoCaptureSource
{
    private static readonly IReadOnlyList<DemoExample> StandardExamples = [
        new("demo-capture", "Board bring-up", "I²C, SPI and UART with application and framing diagnostics."),
        new("timing-gaps", "Timing + gaps", "Different timing and bytes, I²C read termination, address NACK and a missing-data interval."),
        new("dense-signals", "Dense signals", "32 synthetic raw signals, 1,048,576 transitions over 120 ms. No decoded packets.")];
    public static IReadOnlyList<DemoExample> Examples => NativeI2cSource.IsConfigured
        ? [.. StandardExamples, new("i2c-raw", "I²C from samples", "Two generated signals. Address, Hex bytes and ACKs are decoded from these samples.")]
        : StandardExamples;
    public static Task<Capture> LoadAsync(CancellationToken cancellation = default) => LoadAsync(Examples[0].Id, cancellation);
    public static Task<Capture> LoadAsync(string example, CancellationToken cancellation = default) => example == "i2c-raw"
        ? NativeI2cSource.LoadAsync(cancellation) : LoadFixtureAsync(example, cancellation);
    internal static Task<Capture> LoadFixtureAsync(string example, CancellationToken cancellation = default) => Task.Run(() =>
    {
        if (example != "i2c-raw" && !StandardExamples.Any(e => e.Id == example)) throw new ArgumentException("Unknown offline example.", nameof(example));
        if (example == "dense-signals") return DenseSignals(cancellation);
        using var stream = typeof(DemoCaptureSource).Assembly.GetManifestResourceStream($"DSView.Infrastructure.Fixtures.{example}.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        long rate = root.GetProperty("sampleRate").GetInt64(), count = root.GetProperty("sampleCount").GetInt64();
        long Sample(JsonElement value) => (long)Math.Round(value.GetDouble() * rate / 1_000_000d);
        SourceSpan Span(JsonElement value) => new(Sample(value[0]), Sample(value[1]));
        var gaps = root.GetProperty("gaps").EnumerateArray().Select(Span).ToArray();
        var signals = new List<Signal>();
        foreach (var channel in root.GetProperty("channels").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            bool initial = channel.GetProperty("initial").GetInt32() != 0;
            bool? previous = initial; long lastObserved = 0;
            var edges = new List<Edge>();
            foreach (var e in channel.GetProperty("events").EnumerateArray())
            {
                long sample = e.TryGetProperty("sample", out var exact) ? exact.GetInt64() : Sample(e.GetProperty("t"));
                if (gaps.Any(g => g.Start <= sample && g.End > sample)) continue;
                if (gaps.Any(g => g.Start <= sample && g.End > lastObserved)) previous = null;
                bool high = e.GetProperty("v").GetInt32() != 0;
                // Fixture events are explicit transitions, not first-resumed level snapshots.
                if (high != previous) edges.Add(new(sample, high));
                previous = high; lastObserved = sample;
            }
            signals.Add(new(channel.GetProperty("id").GetInt32(), Text(channel, "name"), initial, edges));
        }
        var packets = new List<Packet>();
        foreach (var r in root.GetProperty("records").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var fields = new List<PacketField>();
            if (r.TryGetProperty("fields", out var list)) foreach (var f in list.EnumerateArray())
                fields.Add(new(Text(f, "name"), Bytes(f, "raw"), f.GetProperty("spans").EnumerateArray().Select(s => new SourceSpan(Sample(s[0]), Sample(s[1]))).ToArray()));
            int? value = r.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number ? val.GetInt32() : null;
            var diagnostics = r.TryGetProperty("diagnostics", out var ds) ? ds.EnumerateArray().Select(d => new PacketDiagnostic(
                Text(d, "id"), Text(d, "layer"), Text(d, "code"), Text(d, "message"), d.GetProperty("spans").EnumerateArray().Select(s => new SourceSpan(Sample(s[0]), Sample(s[1]))).ToArray(), d.TryGetProperty("field", out var field) ? field.GetString() : null)).ToArray() : [];
            I2cTransfer? wire = r.TryGetProperty("wire", out var w) ? new(Text(w, "direction") == "Read", w.GetProperty("acknowledged").EnumerateArray().Select(a => a.ValueKind == JsonValueKind.Null ? (bool?)null : a.GetBoolean()).ToArray()) { Completion = I2cCompletion.Stop } : null;
            var cells = wire != null && w.TryGetProperty("cells", out var cs) ? cs.EnumerateArray().Select(c => new I2cCell(Span(c.GetProperty("data")), Span(c.GetProperty("ack")))).ToArray() : [];
            packets.Add(new Packet(Text(r, "id"), Text(r, "group"), new(Sample(r.GetProperty("start")), Sample(r.GetProperty("end"))), Bytes(r, "bytes"), Text(r, "address"), Text(r, "title"), r.GetProperty("error").GetString(), value, fields) { Diagnostics = diagnostics, I2c = wire, WireCells = cells });
        }
        return new Capture(rate, count, signals.OrderBy(s => s.Id).ToArray(), packets.OrderBy(p => p.Span.Start).ToArray()) { Gaps = gaps };
    }, cancellation);
    private static Capture DenseSignals(CancellationToken cancellation)
    {
        var signals = new Signal[32];
        for (int channel = 0; channel < signals.Length; channel++)
        {
            var edges = new Edge[32768];
            long step = 64 + channel % 16 * 4, offset = channel / 16 * 1_000_000 + channel * 127;
            for (int i = 0; i < edges.Length; i++)
            {
                if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                edges[i] = new(offset + (i + 1) * step, (i & 1) == 0);
            }
            signals[channel] = new(channel, channel == 0 ? "CLOCK" : $"SIGNAL_{channel:00}", false, edges);
        }
        return new(50_000_000, 6_000_000, signals, []) { SourceDescription = "Generated raw signals with no decoded packets. No hardware capture." };
    }
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    private static byte[] Bytes(JsonElement value, string key) => value.GetProperty(key).EnumerateArray().Select(b => b.GetByte()).ToArray();
}
