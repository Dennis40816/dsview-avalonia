// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using DSView.Domain;

namespace DSView.Infrastructure;

public static class DemoCaptureSource
{
    public static Task<Capture> LoadAsync(CancellationToken cancellation = default) => Task.Run(() =>
    {
        using var stream = typeof(DemoCaptureSource).Assembly.GetManifestResourceStream("DSView.Infrastructure.Fixtures.demo-capture.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var signals = new List<Signal>();
        foreach (var channel in root.GetProperty("channels").EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            bool initial = channel.GetProperty("initial").GetInt32() != 0, previous = initial;
            var edges = new List<Edge>();
            foreach (var e in channel.GetProperty("events").EnumerateArray())
            {
                bool high = e.GetProperty("v").GetInt32() != 0;
                if (high != previous) edges.Add(new(Sample(e.GetProperty("t")), high));
                previous = high;
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
            I2cTransfer? wire = r.TryGetProperty("wire", out var w) ? new(Text(w, "direction") == "Read", w.GetProperty("acknowledged").EnumerateArray().Select(a => a.ValueKind == JsonValueKind.Null ? (bool?)null : a.GetBoolean()).ToArray()) : null;
            packets.Add(new Packet(Text(r, "id"), Text(r, "group"), new(Sample(r.GetProperty("start")), Sample(r.GetProperty("end"))), Bytes(r, "bytes"), Text(r, "address"), Text(r, "title"), r.GetProperty("error").GetString(), value, fields) { Diagnostics = diagnostics, I2c = wire });
        }
        return new Capture(50_000_000, 50_000, signals.OrderBy(s => s.Id).ToArray(), packets.OrderBy(p => p.Span.Start).ToArray());
    }, cancellation);
    private static long Sample(JsonElement value) => (long)Math.Round(value.GetDouble() * 50);
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    private static byte[] Bytes(JsonElement value, string key) => value.GetProperty(key).EnumerateArray().Select(b => b.GetByte()).ToArray();
}
