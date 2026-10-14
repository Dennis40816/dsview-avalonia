// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

public sealed record Edge(long Sample, bool High);
public sealed record Signal(int Id, string Name, bool Initial, IReadOnlyList<Edge> Edges)
{
    public int LowerBound(long sample)
    {
        int low = 0, high = Edges.Count;
        while (low < high) { int mid = (low + high) / 2; if (Edges[mid].Sample < sample) low = mid + 1; else high = mid; }
        return low;
    }
    public (Edge A, Edge B)? Surrounding(long sample)
    {
        if (Edges.Count < 2) return null;
        var right = LowerBound(sample);
        int first = right < Edges.Count && Edges[right].Sample == sample ? right : right - 1;
        first = Math.Clamp(first, 0, Edges.Count - 2);
        return (Edges[first], Edges[first + 1]);
    }
    public Edge? Nearest(long sample)
    {
        if (Edges.Count == 0) return null;
        int index = LowerBound(sample);
        if (index == 0) return Edges[0];
        if (index == Edges.Count) return Edges[^1];
        return sample - Edges[index - 1].Sample <= Edges[index].Sample - sample ? Edges[index - 1] : Edges[index];
    }
}
public sealed record SourceSpan(long Start, long End);
public sealed record PacketField(string Name, byte[] Raw, IReadOnlyList<SourceSpan> Spans);
public sealed record Packet(string Id, string Decoder, SourceSpan Span, byte[] Bytes, string Address,
    string Title, string? Error, int? Value, IReadOnlyList<PacketField> Fields)
{
    public string Hex => string.Join(" ", Bytes.Select(b => b.ToString("X2")));
    public override string ToString() => $"{Id,-8} {Decoder.ToUpperInvariant(),-5}   {Span.Start / 50d,8:0.##} µs   {Hex,-22}   {Error ?? Title}";
}
public sealed record ProtocolItem(Packet Packet, string Label, SourceSpan Span, string Kind);
public sealed record Decoder(string Id, string Name, int[] Channels);
public sealed record ObservationGroup(string Name, int[] Channels, string[] Decoders)
{
    public override string ToString() => Name;
}
public sealed record Endpoint(int Channel, long Sample, bool IsEdge);
public sealed record Capture(long SampleRate, long SampleCount, IReadOnlyList<Signal> Signals, IReadOnlyList<Packet> Packets)
{
    public double ToMicroseconds(long sample) => sample * 1_000_000d / SampleRate;
    public long ToSample(double microseconds) => Math.Clamp((long)Math.Round(microseconds * SampleRate / 1_000_000d), 0, SampleCount - 1);
    public static readonly Decoder[] Decoders = [new("i2c", "I²C", [0, 1]), new("spi", "SPI", [2, 3, 4, 5]), new("ua", "UART TX", [6]), new("ub", "UART RX", [7])];
    public IReadOnlyList<ProtocolItem> ProtocolItems(Packet packet)
    {
        if (packet.Decoder != "i2c") return [new(packet, packet.Hex, packet.Span, "data")];
        // Synthetic fixture metadata, not a live protocol decoder. Coordinates are sample indices.
        var result = new List<ProtocolItem> { new(packet, "START", new(packet.Span.Start, packet.Span.Start), "point") };
        long first = packet.Span.Start + 125;
        result.Add(new(packet, packet.Address + " W", new(first, first + 1000), "data"));
        result.Add(new(packet, "ACK", new(first + 1000, first + 1125), "ack"));
        for (int i = 0; i < packet.Bytes.Length; i++)
        {
            long start = first + (i + 1) * 1125;
            result.Add(new(packet, $"0x{packet.Bytes[i]:X2}", new(start, start + 1000), "data"));
            result.Add(new(packet, "ACK", new(start + 1000, start + 1125), "ack"));
        }
        result.Add(new(packet, "STOP", new(packet.Span.End, packet.Span.End), "point"));
        return result;
    }
}
