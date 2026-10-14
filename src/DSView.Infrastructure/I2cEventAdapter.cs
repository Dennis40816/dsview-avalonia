// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using DSView.Domain;

namespace DSView.Infrastructure;

// Adapts structured decoder output; it does not parse raw SCL/SDA samples.
public static class I2cEventAdapter
{
    public static Capture Apply(Capture raw, string json)
    {
        var output = JsonSerializer.Deserialize<Envelope>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Decoder returned no result.");
        if (raw.SampleCount is < 10 or > 64_000_000 || raw.SampleRate <= 0 || output.Schema != 1 || output.Decoder != "1:i2c" || output.AddressFormat != "shifted" ||
            output.SampleRate != raw.SampleRate || output.SampleCount != raw.SampleCount || output.Completion != "eof" ||
            output.Events == null || output.Events.Length > 200_000 || raw.Gaps.Count > 0)
            throw new InvalidDataException("Decoder result does not match the requested source or contract.");
        var packets = new List<Packet>();
        Segment? segment = null;
        Event? pendingBits = null;
        long previous = -1;
        foreach (var (item, index) in output.Events.Select((item, index) => (item, index)))
        {
            if (item == null || item.Sequence != index || item.Start < previous || item.Start < 0 || item.Start >= raw.SampleCount ||
                item.End < item.Start || item.End > raw.SampleCount * 2 || item.Bits == null ||
                item.SpanEvidence != (item.Start == item.End ? "point" : "decoder-annotation"))
                throw new InvalidDataException("Invalid decoder event coordinates or evidence.");
            previous = item.Start;
            if (pendingBits != null && item.Kind is not ("ADDRESS READ" or "ADDRESS WRITE" or "DATA READ" or "DATA WRITE"))
                throw new InvalidDataException("Native bit evidence was not followed by its byte.");
            if (item.Kind is "START" or "START REPEAT" or "STOP" && (item.Start != item.End || item.Value != null || item.Bits.Length != 0))
                throw new InvalidDataException("Invalid protocol point.");
            switch (item.Kind)
            {
                case "START":
                    if (segment != null) throw new InvalidDataException("START before the preceding segment ended.");
                    segment = new(item.Start, "START");
                    break;
                case "START REPEAT":
                    if (segment == null) throw new InvalidDataException("Repeated START without a segment.");
                    Finish(I2cCompletion.RepeatedStart, item.Start);
                    segment = new(item.Start, "RESTART");
                    break;
                case "STOP":
                    if (segment == null) throw new InvalidDataException("STOP without a segment.");
                    segment.Annotations.Add(new("STOP", new(item.Start, item.End), "point"));
                    Finish(I2cCompletion.Stop, item.Start);
                    break;
                case "BITS":
                    if (segment == null || item.Value != null || item.Bits.Length != 8 || item.Bits.Any(bit => bit == null || bit.Length != 3 || bit[0] is < 0 or > 1 || bit[1] < item.Start || bit[1] >= raw.SampleCount || bit[2] <= bit[1] || bit[2] > item.End))
                        throw new InvalidDataException("Invalid native bit evidence.");
                    if (item.Bits[0][1] != item.Start || item.Bits[^1][2] != item.End ||
                        item.Bits.Zip(item.Bits.Skip(1)).Any(pair => pair.First[2] != pair.Second[1]))
                        throw new InvalidDataException("Native bit evidence is not in transmission order.");
                    pendingBits = item;
                    break;
                case "ADDRESS READ": case "ADDRESS WRITE": case "DATA READ": case "DATA WRITE":
                    if (segment == null || item.Value is not (>= 0 and <= 255) || item.Bits.Length != 0 || item.End == item.Start)
                        throw new InvalidDataException("Invalid decoded byte.");
                    bool address = item.Kind.StartsWith("ADDRESS", StringComparison.Ordinal);
                    bool read = item.Kind.EndsWith("READ", StringComparison.Ordinal);
                    if (pendingBits == null || pendingBits.Start != item.Start || pendingBits.End != item.End)
                        throw new InvalidDataException("Decoded byte has no matching bit evidence.");
                    int wireByte = pendingBits.Bits.Aggregate(0, (value, bit) => (value << 1) | (int)bit[0]);
                    if (address ? item.Value != (wireByte >> 1) || read != ((wireByte & 1) != 0) : item.Value != wireByte)
                        throw new InvalidDataException("Decoded byte contradicts its native bit evidence.");
                    pendingBits = null;
                    if (address ? segment.Address != null || item.Value > 127 : segment.Address == null || segment.Read != read)
                        throw new InvalidDataException("Invalid address/data sequence.");
                    if (segment.Acknowledged.Count > 0 && segment.Acknowledged[^1] == null)
                        throw new InvalidDataException("Byte arrived without preceding ACK evidence.");
                    if (address) { segment.Address = (byte)item.Value; segment.Read = read; }
                    else segment.Bytes.Add((byte)item.Value);
                    segment.Acknowledged.Add(null);
                    segment.Annotations.Add(new(address ? $"0x{item.Value:X2} {(read ? "R" : "W")}" : $"0x{item.Value:X2}", new(item.Start, item.End), "data"));
                    break;
                case "ACK": case "NACK":
                    if (segment == null || item.Value != null || item.Bits.Length != 0 || segment.Acknowledged.Count == 0 || segment.Acknowledged[^1] != null || item.End == item.Start)
                        throw new InvalidDataException("ACK has no pending byte.");
                    segment.Acknowledged[^1] = item.Kind == "ACK";
                    segment.Annotations.Add(new(item.Kind, new(item.Start, item.End), "ack"));
                    break;
                default: throw new InvalidDataException("Unsupported I²C event.");
            }
        }
        if (pendingBits != null) throw new InvalidDataException("Unconsumed native bit evidence at EOF.");
        if (output.OpenTransaction != (segment != null)) throw new InvalidDataException("Decoder completion contradicts observed events.");
        if (segment != null) Finish(I2cCompletion.CaptureEnded, raw.SampleCount);
        return raw with { Packets = packets.ToArray(), SourceDescription = "Generated SCL/SDA, decoded from these samples. No hardware capture. Protocol intervals are decoder annotations; timing measurements use raw edges." };

        void Finish(I2cCompletion completion, long end)
        {
            var s = segment!;
            string id = $"i2c-{packets.Count + 1}";
            var diagnostics = new List<PacketDiagnostic>();
            if (s.Acknowledged.FirstOrDefault() == false)
            {
                var ack = s.Annotations.First(a => a.Kind == "ack");
                diagnostics.Add(new(id + "/address-nack", "I²C", "address-nack", "Address was not acknowledged.", [ack.Span]));
            }
            if (!s.Read)
            {
                var acknowledgements = s.Annotations.Where(a => a.Kind == "ack").ToArray();
                for (int i = 0; i < s.Bytes.Count; i++)
                    if (s.Acknowledged[i + 1] == false)
                        diagnostics.Add(new(id + $"/data-{i + 1}-nack", "I²C", "data-nack", $"Data byte {i + 1} (0x{s.Bytes[i]:X2}) was not acknowledged.", [acknowledgements[i + 1].Span]));
            }
            if (completion == I2cCompletion.CaptureEnded)
                diagnostics.Add(new(id + "/incomplete", "I²C", "incomplete", "Capture ended before STOP or repeated START. Only observed bytes and ACKs are shown.", [new(Math.Max(s.Start, end - 1), end)]));
            string title = s.Address == null ? "Incomplete address" : s.Read ? "Read transfer" : "Write transfer";
            if (completion == I2cCompletion.CaptureEnded && s.Address != null) title += " · incomplete";
            packets.Add(new(id, "i2c", new(s.Start, end), s.Bytes.ToArray(), s.Address is byte addr ? $"0x{addr:X2}" : "Address unknown", title,
                diagnostics.FirstOrDefault()?.Message, null, [])
            {
                I2c = new(s.Read, s.Acknowledged.ToArray()) { AddressObserved = s.Address != null, Completion = completion },
                Annotations = s.Annotations.ToArray(), Diagnostics = diagnostics.ToArray()
            });
            segment = null;
        }
    }
    private sealed class Segment(long start, string label)
    {
        public long Start { get; } = start;
        public byte? Address { get; set; }
        public bool Read { get; set; }
        public List<byte> Bytes { get; } = [];
        public List<bool?> Acknowledged { get; } = [];
        public List<ProtocolAnnotation> Annotations { get; } = [new(label, new(start, start), "point")];
    }
    private sealed record Envelope(int Schema, string Decoder, string AddressFormat, long SampleRate, long SampleCount, Event[] Events, string Completion, bool OpenTransaction);
    private sealed record Event(int Sequence, string Kind, long Start, long End, string SpanEvidence, int? Value, long[][] Bits);
}
