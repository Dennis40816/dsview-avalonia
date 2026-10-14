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
public sealed record PacketDiagnostic(string Id, string Layer, string Code, string Message, IReadOnlyList<SourceSpan> Spans, string? Field = null);
public sealed record CaptureIssue(Packet Packet, PacketDiagnostic Diagnostic);
public sealed record I2cTransfer(bool Read, IReadOnlyList<bool?> Acknowledged);
public sealed record I2cCell(SourceSpan Data, SourceSpan Acknowledge);
public sealed record SignalActivity(int Edges, bool Complete, bool? ConstantLevel);
public sealed record Packet(string Id, string Decoder, SourceSpan Span, byte[] Bytes, string Address,
    string Title, string? Error, int? Value, IReadOnlyList<PacketField> Fields)
{
    public IReadOnlyList<PacketDiagnostic> Diagnostics { get; init; } = [];
    public I2cTransfer? I2c { get; init; }
    public IReadOnlyList<I2cCell> WireCells { get; init; } = [];
    public string Direction => I2c == null ? "?" : I2c.Read ? "R" : "W";
    public string Hex => string.Join(" ", Bytes.Select(b => b.ToString("X2")));
    public override string ToString() => $"{Id} · {Decoder.ToUpperInvariant()} · {Hex} · {Error ?? Title}";
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
    public bool HasSample(long sample) => sample >= 0 && sample < SampleCount && !Gaps.Any(g => g.Start <= sample && g.End > sample);
    public Edge? NearestObservedEdge(Signal signal, long sample)
    {
        int right = signal.LowerBound(sample), left = right - 1;
        while (left >= 0 && !HasSample(signal.Edges[left].Sample)) left--;
        while (right < signal.Edges.Count && !HasSample(signal.Edges[right].Sample)) right++;
        if (left < 0) return right < signal.Edges.Count ? signal.Edges[right] : null;
        if (right == signal.Edges.Count) return signal.Edges[left];
        return sample - signal.Edges[left].Sample <= signal.Edges[right].Sample - sample ? signal.Edges[left] : signal.Edges[right];
    }
    public SignalActivity Activity(int channel, long start, long end)
    {
        var signal = Signals.FirstOrDefault(s => s.Id == channel);
        if (signal == null || start < 0 || end > SampleCount || start >= end) return new(0, false, null);
        int first = signal.LowerBound(start), last = signal.LowerBound(end);
        int count = Gaps.Count == 0 ? last - first : signal.Edges.Skip(first).Take(last - first).Count(e => HasSample(e.Sample));
        bool complete = !Gaps.Any(g => g.Start < end && g.End > start);
        long evidence = first > 0 ? signal.Edges[first - 1].Sample : 0;
        bool knownLevel = complete && !Gaps.Any(g => g.Start <= start && g.End > evidence);
        return new(count, complete, count == 0 && knownLevel ? first > 0 ? signal.Edges[first - 1].High : signal.Initial : null);
    }
    // A gap invalidates the level until a subsequent observed transition establishes it again.
    public IEnumerable<SourceSpan> UnknownLevelSpans(Signal signal)
    {
        foreach (var gap in Gaps)
        {
            int next = signal.LowerBound(gap.End);
            while (next < signal.Edges.Count && !HasSample(signal.Edges[next].Sample)) next++;
            yield return new(Math.Max(0, gap.Start), next < signal.Edges.Count ? signal.Edges[next].Sample : SampleCount);
        }
    }
    public bool[] ActivityBins(IEnumerable<Signal> signals, int count)
    {
        if (count < 1 || SampleCount <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var bins = new bool[count];
        foreach (var edge in signals.SelectMany(s => s.Edges).Where(e => HasSample(e.Sample)))
            bins[Math.Clamp((int)(edge.Sample / (double)SampleCount * count), 0, count - 1)] = true;
        return bins;
    }
    public IReadOnlyList<CaptureIssue> Issues => Packets.SelectMany(p =>
        (p.Diagnostics.Count > 0 ? p.Diagnostics : p.Error is { } error ? [new PacketDiagnostic(p.Id + "/unclassified", "Unclassified", "unknown", error, [p.Span])] : Array.Empty<PacketDiagnostic>())
        .Select(d => new CaptureIssue(p, d))).OrderBy(i => i.Diagnostic.Spans.Count > 0 ? i.Diagnostic.Spans.Min(s => s.Start) : i.Packet.Span.Start).ThenBy(i => i.Diagnostic.Id, StringComparer.Ordinal).ToArray();
    public LocalMeasurement MeasureAt(int channel, long sample)
    {
        var signal = Signals.FirstOrDefault(s => s.Id == channel);
        if (signal == null || sample < 0 || sample >= SampleCount)
            return new(channel, false, null, null, null, "Outside capture");
        if (!HasSample(sample)) return new(channel, false, null, null, null, "Missing samples at this position");
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
        if (packet.Decoder != "i2c" || packet.WireCells.Count != packet.Bytes.Length + 1)
            return [new(packet, packet.Decoder == "i2c" ? packet.Address + " " + packet.Direction + " · " + packet.Hex : packet.Hex, packet.Span, "data")];
        // Explicit synthetic annotations, not a live protocol decoder. Never infer bit time from sample rate.
        var result = new List<ProtocolItem> { new(packet, "START", new(packet.Span.Start, packet.Span.Start), "point") };
        string Ack(int index) => packet.I2c?.Acknowledged.ElementAtOrDefault(index) switch { true => "ACK", false => "NACK", _ => "?" };
        for (int i = 0; i < packet.WireCells.Count; i++)
        {
            var cell = packet.WireCells[i];
            result.Add(new(packet, i == 0 ? packet.Address + " " + packet.Direction : $"0x{packet.Bytes[i - 1]:X2}", cell.Data, "data"));
            result.Add(new(packet, Ack(i), cell.Acknowledge, "ack"));
        }
        result.Add(new(packet, "STOP", new(packet.Span.End, packet.Span.End), "point"));
        return result;
    }
}
