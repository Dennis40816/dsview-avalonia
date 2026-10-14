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
        return first >= 0 && first < Edges.Count - 1 ? (Edges[first], Edges[first + 1]) : null;
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
public sealed record LocalMeasurement(int Channel, bool High, SourceSpan? Pulse, SourceSpan? Cycle, long? HighSamples, string? Reason)
{
    public long? WidthSamples => Pulse is { } p ? p.End - p.Start : null;
    public long? PeriodSamples => Cycle is { } p ? p.End - p.Start : null;
}
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
    // Half-open intervals where samples are absent. Silence alone is not a gap.
    public IReadOnlyList<SourceSpan> Gaps { get; init; } = [];
    public LocalMeasurement MeasureAt(int channel, long sample)
    {
        var signal = Signals.FirstOrDefault(s => s.Id == channel);
        if (signal == null || sample < 0 || sample >= SampleCount)
            return new(channel, false, null, null, null, "Outside capture");
        var pair = signal.Surrounding(sample);
        if (pair is not { } p) return new(channel, false, null, null, null, "Incomplete pulse at capture boundary");
        bool Missing(long start, long end) => Gaps.Any(g => g.Start <= end && g.End > start);
        if (p.A.Sample < 0 || p.B.Sample >= SampleCount || p.A.Sample >= p.B.Sample || p.A.High == p.B.High)
            return new(channel, p.A.High, null, null, null, "Incomplete transition evidence");
        if (Missing(p.A.Sample, p.B.Sample)) return new(channel, p.A.High, null, null, null, "Missing samples in pulse");
        var pulse = new SourceSpan(p.A.Sample, p.B.Sample);
        int next = signal.LowerBound(p.B.Sample) + 1;
        if (next >= signal.Edges.Count)
            return new(channel, p.A.High, pulse, null, null, "No complete local cycle");
        var last = signal.Edges[next];
        if (last.Sample >= SampleCount || last.Sample <= p.B.Sample || last.High != p.A.High)
            return new(channel, p.A.High, pulse, null, null, "No complete local cycle");
        if (Missing(p.A.Sample, last.Sample)) return new(channel, p.A.High, pulse, null, null, "Missing samples in cycle");
        return new(channel, p.A.High, pulse, new(p.A.Sample, last.Sample),
            p.A.High ? p.B.Sample - p.A.Sample : last.Sample - p.B.Sample, null);
    }
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
