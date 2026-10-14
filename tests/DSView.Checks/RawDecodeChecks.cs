// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class RawDecodeChecks
{
    private sealed record Event(int Sequence, string Kind, long Start, long End, string SpanEvidence, int? Value, long[][] Bits);
    private static Event E(string kind, long start, long end, int? value = null) => new(0, kind, start, end, start == end ? "point" : "decoder-annotation", value, []);
    private static Event Bits(Event item)
    {
        int wire = item.Kind.StartsWith("ADDRESS", StringComparison.Ordinal) ? (item.Value!.Value << 1) | (item.Kind.EndsWith("READ", StringComparison.Ordinal) ? 1 : 0) : item.Value!.Value;
        long width = (item.End - item.Start) / 8;
        return E("BITS", item.Start, item.End) with { Bits = Enumerable.Range(0, 8).Select(i => new long[] { (wire >> (7 - i)) & 1, item.Start + i * width, item.Start + (i + 1) * width }).ToArray() };
    }
    private static string Json(Capture raw, bool open, params Event[] events) => Serialize(raw, open,
        events.SelectMany(e => e.Kind.StartsWith("ADDRESS", StringComparison.Ordinal) || e.Kind.StartsWith("DATA", StringComparison.Ordinal) ? new[] { Bits(e), e } : [e]).ToArray());
    private static string Serialize(Capture raw, bool open, params Event[] events) => JsonSerializer.Serialize(new
    {
        Schema = 1, Decoder = "1:i2c", AddressFormat = "shifted", raw.SampleRate, raw.SampleCount,
        Completion = "eof", OpenTransaction = open, Events = events.Select((e, i) => e with { Sequence = i }).ToArray()
    });
    public static async Task Run(Action<bool, string> check, string? configuration)
    {
        var raw = await NativeI2cSource.LoadRawExampleAsync();
        check(raw.SampleCount == 804 && raw.Signals.Select(s => s.Id).SequenceEqual(new[] { 0, 1 }) && raw.Packets.Count == 0,
            "Raw I2C fixture contains two physical IDs and exact samples, with no precomputed records");
        var model = new Workbench(); model.Load(raw); model.Pick(8, 50, 10); model.StepEdge(8, 1);
        check(model.Signals.Count == 2 && model.A == null && model.Start == 0, "Absent D8 does not throw or invent a measurement in a two-signal capture");
        model.SetGroup(model.Groups[1]); model.Load(raw);
        check(model.Signals.Count == 2 && model.Decoders.Select(d => d.Id).SequenceEqual(new[] { "i2c" }), "Source switching replaces an incompatible Group with visible source signals and available decoders");
        model.SetGroup(model.Groups[1]);
        check(model.Signals.Count == 0 && model.Decoders.Count == 0, "Explicitly selecting an unavailable Group never invents decoder rows");
        var savedGroups = model.Groups.ToArray(); model.Colors[0] = "#FFFF00";
        model.Load(raw); model.Load(raw);
        check(model.Groups.SequenceEqual(savedGroups) && model.Colors[0] == "#FFFF00", "Source fallback preserves Group definitions and colors without appending Groups");
        var narrow = new ObservationGroup("SCL only", [0], []); model.Groups.Insert(0, narrow); model.SetGroup(narrow); model.Load(raw);
        check(ReferenceEquals(model.Group, narrow) && model.Signals.Single().Id == 0, "Source loading retains a compatible narrow Group");
        var complete = I2cEventAdapter.Apply(raw, Json(raw, false, E("START", 16, 16), E("ADDRESS READ", 28, 124, 0x51),
            E("ACK", 124, 136), E("DATA READ", 136, 232, 0x96), E("NACK", 232, 244), E("STOP", 248, 248)));
        var read = complete.Packets.Single();
        check(read.Address == "0x51" && read.Bytes.SequenceEqual(new byte[] { 0x96 }) && read.I2c!.Completion == I2cCompletion.Stop && read.Diagnostics.Count == 0,
            "Observed final read NACK is preserved without a fabricated bus error or application fields");
        check(complete.ProtocolItems(read).Select(i => i.Label).SequenceEqual(new[] { "START", "0x51 R", "ACK", "0x96", "NACK", "STOP" }) && read.WireCells.Count == 0 && read.Fields.Count == 0,
            "Native protocol annotations render only their explicit point/byte/ACK evidence");
        var writeNack = I2cEventAdapter.Apply(raw, Json(raw, false, E("START", 16, 16), E("ADDRESS WRITE", 28, 124, 0x51),
            E("ACK", 124, 136), E("DATA WRITE", 136, 232, 0x96), E("NACK", 232, 244), E("STOP", 248, 248)));
        check(writeNack.Issues.Single().Diagnostic is { Layer: "I²C", Code: "data-nack" } nack && nack.Spans.Single() == new SourceSpan(232, 244),
            "Write data NACK is discoverable at its observed evidence without treating normal read NACK as an error");
        var repeated = I2cEventAdapter.Apply(raw, Json(raw, false, E("START", 16, 16), E("ADDRESS WRITE", 28, 124, 0x51), E("ACK", 124, 136),
            E("START REPEAT", 140, 140), E("ADDRESS READ", 152, 248, 0x51), E("ACK", 248, 260), E("STOP", 264, 264)));
        check(repeated.Packets.Count == 2 && repeated.Packets[0].I2c!.Completion == I2cCompletion.RepeatedStart &&
            !repeated.ProtocolItems(repeated.Packets[0]).Any(i => i.Label == "STOP") && repeated.ProtocolItems(repeated.Packets[1])[0].Label == "RESTART",
            "Repeated START ends one addressed segment and starts another without inventing STOP");
        var prefix = I2cEventAdapter.Apply(raw, Json(raw, true, E("START", 16, 16)));
        check(prefix.Packets.Single() is { Direction: "?", Address: "Address unknown" } && prefix.Packets.Single().Bytes.Length == 0 &&
            prefix.ProtocolItems(prefix.Packets.Single()).Count == 1 && TimingText.I2cStatus(prefix.Packets.Single()).Contains("Incomplete"),
            "EOF before address preserves unknown direction and no fabricated byte, ACK or STOP");
        var pending = I2cEventAdapter.Apply(raw, Json(raw, true, E("START", 16, 16), E("ADDRESS WRITE", 28, 124, 0x2A)));
        check(pending.Packets.Single().I2c!.Acknowledged.SequenceEqual(new bool?[] { null }) && pending.Packets.Single().I2c!.Completion == I2cCompletion.CaptureEnded,
            "EOF before ACK keeps ACK unknown even when a complete address byte was observed");
        var unknownEnd = read with { I2c = read.I2c! with { Completion = I2cCompletion.Unknown } };
        check(TimingText.I2cStatus(unknownEnd) == "I²C · Final observed data byte: NACK", "A final observed NACK alone does not prove transaction termination");
        Reject(() => I2cEventAdapter.Apply(raw, Json(raw, false, E("START", 16, 16))), "Contradictory native completion is rejected");
        Reject(() => I2cEventAdapter.Apply(raw, Json(raw, true, E("START", 16, 16), E("ACK", 20, 24))), "Orphan ACK output is rejected");
        Reject(() => I2cEventAdapter.Apply(raw with { SampleRate = 1 }, Json(raw, false)), "Output sample clock must match its source");
        var addressEvent = E("ADDRESS WRITE", 28, 124, 0x2A);
        var bitEvent = Bits(addressEvent);
        Reject(() => I2cEventAdapter.Apply(raw, Serialize(raw, true, E("START", 16, 16), addressEvent)), "Native byte without BITS is rejected");
        Reject(() => I2cEventAdapter.Apply(raw, Serialize(raw, true, E("START", 16, 16), bitEvent, addressEvent with { Value = 0x2B })), "Native byte contradicting BITS is rejected");
        Reject(() => I2cEventAdapter.Apply(raw, Serialize(raw, true, E("START", 16, 16), bitEvent, bitEvent, addressEvent)), "Duplicate native BITS is rejected");
        Reject(() => I2cEventAdapter.Apply(raw, Serialize(raw, true, E("START", 16, 16), bitEvent)), "Unconsumed native BITS at EOF is rejected");
        Reject(() => I2cEventAdapter.Apply(raw, Serialize(raw, true, E("START", 16, 16), bitEvent with { Bits = bitEvent.Bits.Reverse().ToArray() }, addressEvent)), "Reordered native bit positions are rejected");
        var veryShort = raw with { SampleCount = 112 };
        Reject(() => I2cEventAdapter.Apply(veryShort, Serialize(veryShort, true, E("START", 16, 16), bitEvent, addressEvent)), "A bit sampling position at EOF is outside observed data");
        if (configuration == null) { Console.WriteLine("SKIP optional native process checks: pass --native-i2c <configuration>."); return; }
        var options = NativeI2cSource.ReadConfiguration(configuration);
        var decoded = await NativeI2cSource.DecodeAsync(raw, options);
        check(decoded.Packets.Single().Bytes.SequenceEqual(new byte[] { 0xA5,0x11,0x02,0x35,0x12,0 }) && decoded.Packets.Single().Address == "0x2A" &&
            decoded.ProtocolItems(decoded.Packets.Single()).Count == 16 && ReferenceEquals(raw.Signals, decoded.Signals),
            "Actual native worker decodes the exact displayed samples into address/bytes/ACKs");
        var sda = raw.Signals.Single(s => s.Id == 1);
        var values = Enumerable.Range(0, (int)raw.SampleCount).Select(i => sda.Edges.LastOrDefault(e => e.Sample <= i)?.High ?? sda.Initial).ToArray();
        for (int i = 132; i < 144; i++) values[i] = !values[i];
        var changed = sda with { Initial = values[0], Edges = Enumerable.Range(1, values.Length - 1).Where(i => values[i] != values[i - 1]).Select(i => new Edge(i, values[i])).ToArray() };
        var mutation = await NativeI2cSource.DecodeAsync(raw with { Signals = [raw.Signals[0], changed] }, options);
        check(mutation.Packets.Single().Bytes[0] == 0x25 && decoded.Packets.Single().Bytes[0] == 0xA5, "Mutating raw SDA changes native decoded byte while preserving prior capture");
        var shortRaw = raw with { SampleCount = 124, Signals = raw.Signals.Select(s => s with { Edges = s.Edges.Where(e => e.Sample < 124).ToArray() }).ToArray() };
        var truncated = await NativeI2cSource.DecodeAsync(shortRaw, options);
        check(truncated.Packets.Single().I2c is { Completion: I2cCompletion.CaptureEnded } bus && bus.Acknowledged.Single() == null &&
            !truncated.ProtocolItems(truncated.Packets.Single()).Any(i => i.Label == "STOP"), "Actual EOF before native ACK stays incomplete without STOP");
        var lastBitRaw = raw with { SampleCount = 113, Signals = raw.Signals.Select(s => s with { Edges = s.Edges.Where(e => e.Sample < 113).ToArray() }).ToArray() };
        var lastBit = await NativeI2cSource.DecodeAsync(lastBitRaw, options);
        check(lastBit.Packets.Single().Address == "0x2A" && lastBit.Packets.Single().I2c!.Acknowledged.Single() == null &&
            lastBit.ProtocolItems(lastBit.Packets.Single()).Single(i => i.Kind == "data").Span.End > lastBitRaw.SampleCount,
            "Observed final bit survives EOF while its predicted annotation end remains distinct from capture coverage");
        try { await NativeI2cSource.DecodeAsync(raw with { Gaps = [new(20, 30)] }, options); throw new Exception("Gap accepted"); }
        catch (InvalidDataException) { check(true, "Native source rejects missing samples before starting a continuous decode"); }
        try { await NativeI2cSource.DecodeAsync(raw, options with { WorkerSha256 = new('0', 64) }); throw new Exception("Changed worker accepted"); }
        catch (InvalidDataException) { check(true, "Changed native executable identity is rejected before execution"); }
        try { await NativeI2cSource.DecodeAsync(raw, options with { InitializerSha256 = new('0', 64) }); throw new Exception("Changed initializer accepted"); }
        catch (InvalidDataException) { check(true, "Changed executable Python initializer identity is rejected before execution"); }
        // Observe this invocation's actual worker before cancelling, rather than racing a short timer.
        var prior = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(options.Worker)).Select(p => { int id = p.Id; p.Dispose(); return id; }).ToHashSet();
        using var cancel = new CancellationTokenSource();
        var large = new Capture(50_000_000, 64_000_000, [new(0,"SCL",false,[]),new(1,"SDA",false,[])], []);
        var job = NativeI2cSource.DecodeAsync(large, options, cancel.Token);
        Process? worker = null;
        for (int attempt = 0; attempt < 500 && worker == null && !job.IsCompleted; attempt++)
        {
            foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(options.Worker)))
            {
                if (!prior.Contains(candidate.Id) && string.Equals(candidate.MainModule?.FileName, options.Worker, StringComparison.OrdinalIgnoreCase)) { worker = candidate; break; }
                candidate.Dispose();
            }
            if (worker == null) await Task.Delay(10);
        }
        if (worker == null) { cancel.Cancel(); try { await job; } catch (OperationCanceledException) { } throw new Exception("Cancellation check did not observe a live worker."); }
        using (worker)
        {
            cancel.Cancel();
            try { await job; throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { worker.Refresh(); check(worker.HasExited, "Cancelling an observed live native worker terminates it before source cleanup"); }
        }
        var again = await NativeI2cSource.DecodeAsync(raw, options);
        check(again.Packets.Single().Bytes.SequenceEqual(decoded.Packets.Single().Bytes), "Native decode succeeds after active cancellation without inherited decoder state");
        void Reject(Action action, string description)
        {
            try { action(); throw new Exception(description); }
            catch (InvalidDataException) { check(true, description); }
        }
    }
}
