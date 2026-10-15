// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using DSView.Domain;

namespace DSView.Infrastructure;

public static class AnalysisFile
{
    public const long MaximumBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 32,
        TypeInfoResolver = CreateResolver(), Converters = { new DecoderSourceJsonConverter() }
    };
    private static IJsonTypeInfoResolver CreateResolver()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].Set == null) info.Properties.RemoveAt(i); // Also excludes read-only collections such as Capture.Issues.
                else info.Properties[i].IsRequired = !(info.Type == typeof(AnalysisState) && info.Properties[i].Name is "selectedProtocolIndex" or "measurements")
                    && !(info.Type == typeof(Capture) && info.Properties[i].Name == "decoderSources"); // Version-specific evidence requirements are validated below.
        });
        return resolver;
    }
    public static string StateKey(AnalysisState state) => JsonSerializer.Serialize(state, Json);

    public static Task<AnalysisDocument> ReadAsync(string path, CancellationToken cancellation = default) => Task.Run(async () =>
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (file.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Analysis file must be between 1 byte and 128 MiB.");
        var document = await JsonSerializer.DeserializeAsync<AnalysisDocument>(file, Json, cancellation)
            ?? throw new InvalidDataException("Analysis document is missing.");
        if (document.Version >= 4 && document.Capture?.DecoderSources == null)
            throw new InvalidDataException("Saved decoder sources are missing.");
        Validate(document, cancellation); return Normalize(document);
    }, cancellation);

    public static Task WriteAsync(string path, AnalysisDocument document, CancellationToken cancellation = default) => Task.Run(async () =>
    {
        Validate(document, cancellation);
        document = Normalize(document) with { Version = AnalysisDocument.CurrentVersion,
            Capture = document.Capture with { DecoderSources = document.Capture.EffectiveDecoderSources.ToArray() } };
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".dsview2-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                using var bounded = new BoundedOutput(file);
                await JsonSerializer.SerializeAsync(bounded, document, Json, cancellation);
                if (file.Length > MaximumBytes) throw new InvalidDataException("This analysis exceeds the current 128 MiB file limit.");
                await file.FlushAsync(cancellation); file.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, cancellation);

    private static AnalysisDocument Normalize(AnalysisDocument document) => document with
    { State = document.State with { Measurements = document.State.Measurements ?? [] } };

    public static void ValidateTimingResult(Capture capture, TimingResult result)
    {
        if (result == null || !Guid.TryParseExact(result.Id, "N", out _) || !TimingResult.ValidMetadata(result.Name, result.Note)
            || result.A == null || result.B == null) throw new InvalidDataException("Invalid timing result identity, name, note or endpoints.");
        ValidateMeasurement(capture, result.A, result.B, result.LocalPulse ? 0 : 2, result.LocalPulse);
    }
    private static void ValidateMeasurement(Capture capture, Endpoint? a, Endpoint? b, int mode, bool localPulse)
    {
        foreach (var endpoint in new[] { a, b }.OfType<Endpoint>())
        {
            var signal = capture.Signals.FirstOrDefault(s => s.Id == endpoint.Channel);
            if (signal == null || endpoint.Sample < 0 || endpoint.Sample >= capture.SampleCount)
                throw new InvalidDataException("Measurement source is missing or outside capture.");
            if (endpoint.IsEdge && (!capture.HasSample(endpoint.Sample) || signal.Nearest(endpoint.Sample)?.Sample != endpoint.Sample))
                throw new InvalidDataException("Saved edge does not match an observed raw transition.");
        }
        if (mode is < 0 or > 2) throw new InvalidDataException("Invalid measurement mode.");
        if (localPulse)
        {
            var local = a == null ? null : capture.MeasureAt(a.Channel, a.Sample);
            if (mode != 0 || a is not { IsEdge: true } || b is not { IsEdge: true } || a.Channel != b.Channel
                || local?.Pulse is not { } pulse || pulse.Start != a.Sample || pulse.End != b.Sample)
                throw new InvalidDataException("Saved local measurement does not match raw pulse evidence.");
        }
        else if (mode == 0 && (a != null || b != null)) throw new InvalidDataException("Local measurement identity is missing.");
    }

    private sealed class BoundedOutput(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        private void Check(int count) { if (count > MaximumBytes - inner.Position) throw new InvalidDataException("This analysis exceeds the current 128 MiB file limit."); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); inner.Write(buffer, offset, count); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation = default) { Check(buffer.Length); return inner.WriteAsync(buffer, cancellation); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellation) => inner.FlushAsync(cancellation);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public static void Validate(AnalysisDocument document, CancellationToken cancellation = default)
    {
        static void Require([DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        static bool Text(string? value, int maximum = 4096) => value != null && value.Length <= maximum && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
        static bool Name(string? value, int maximum = 128) => Text(value, maximum) && !string.IsNullOrWhiteSpace(value);
        Require(document != null && document.Format == AnalysisDocument.FileFormat && document.Version is >= 1 and <= AnalysisDocument.CurrentVersion, "Unsupported analysis format or version.");
        Require(Name(document!.SourceName), "Invalid source name.");
        var capture = document.Capture; var state = document.State;
        Require(capture != null && state != null, "Capture or analysis state is missing.");
        Require(capture!.SampleRate > 0 && capture.SampleCount is >= 10 and <= 4_503_599_627_370_496, "Invalid sample rate or count.");
        Require(capture.Signals != null && capture.Signals.Count <= 32 && capture.Packets != null && capture.Packets.Count <= 100_000 && capture.Gaps != null && capture.Gaps.Count <= 10_000 && Text(capture.SourceDescription), "Invalid or excessive capture contents.");
        Require(document.Version >= 4 || capture.DecoderSources == null, "Explicit decoder sources require analysis version 4.");
        var decoderSources = capture.EffectiveDecoderSources;
        DecoderSource.Validate(decoderSources);
        bool Span(SourceSpan? span, long maximum) => span != null && span.Start >= 0 && span.Start <= span.End && span.End <= maximum;
        Require(capture.Gaps!.All(g => Span(g, capture.SampleCount) && g.Start < g.End), "Invalid missing-data interval.");
        // Normalize only the validation lookup; preserve the exact saved gap list.
        var gapEnds = new List<SourceSpan>();
        foreach (var gap in capture.Gaps.OrderBy(g => g.Start))
            if (gapEnds.Count > 0 && gap.Start <= gapEnds[^1].End) gapEnds[^1] = gapEnds[^1] with { End = Math.Max(gap.End, gapEnds[^1].End) };
            else gapEnds.Add(gap);
        var ids = new HashSet<int>(); long edges = 0; int previousId = -1;
        foreach (var signal in capture.Signals!)
        {
            cancellation.ThrowIfCancellationRequested();
            Require(signal != null && signal.Id is >= 0 and < 32 && signal.Id > previousId && ids.Add(signal.Id) && Name(signal.Name) && signal.Edges != null, "Signals must have unique ordered physical IDs and valid names.");
            previousId = signal!.Id; edges += signal.Edges!.Count;
            Require(edges <= 2_000_000, "This version supports at most 2,000,000 stored transitions per analysis.");
            long last = -1; bool level = signal.Initial; int gapIndex = 0;
            foreach (var edge in signal.Edges)
            {
                if ((last & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                Require(edge != null && edge.Sample > last && edge.Sample < capture.SampleCount, "Raw transitions must be ordered and inside capture coverage.");
                while (gapIndex < gapEnds.Count && gapEnds[gapIndex].End <= Math.Max(0, last)) gapIndex++;
                bool crossedGap = gapIndex < gapEnds.Count && gapEnds[gapIndex].Start <= edge!.Sample;
                Require(edge!.High != level || crossedGap, "Repeated signal level without missing coverage is not a transition.");
                // Transitions stored in a gap are not observed; the existing capture model masks them.
                level = edge.High; last = edge.Sample;
            }
        }
        var packetIds = new HashSet<string>(StringComparer.Ordinal); var issueIds = new HashSet<string>(StringComparer.Ordinal);
        long packetStart = -1;
        foreach (var packet in capture.Packets!)
        {
            cancellation.ThrowIfCancellationRequested();
            Require(packet != null && Name(packet.Id) && packetIds.Add(packet.Id) && decoderSources.Any(d => d.Id == packet.Decoder)
                && Span(packet.Span, capture.SampleCount) && packet.Span.Start >= packetStart && packet.Bytes is { Length: <= 1_000_000 }
                && Text(packet.Address) && Text(packet.Title) && (packet.Error == null || Text(packet.Error)) && packet.Fields != null
                && packet.Fields.Count <= 1024 && packet.Diagnostics != null && packet.Diagnostics.Count <= 1024 && packet.WireCells != null, "Invalid decoded packet snapshot.");
            packetStart = packet!.Span.Start;
            // Decoder annotation extents may extend beyond EOF; raw capture count remains authoritative.
            long annotationLimit = capture.SampleCount * 2;
            foreach (var field in packet.Fields!) Require(field != null && Name(field.Name) && field.Raw is { Length: <= 1_000_000 } && field.Spans != null && field.Spans.Count <= 1024 && field.Spans.All(s => Span(s, annotationLimit)), "Invalid decoded field.");
            foreach (var issue in packet.Diagnostics!) Require(issue != null && Name(issue.Id) && issueIds.Add(packet.Id + "\0" + issue.Id) && Name(issue.Layer) && Name(issue.Code) && Text(issue.Message) && (issue.Field == null || Name(issue.Field)) && issue.Spans != null && issue.Spans.Count <= 1024 && issue.Spans.All(s => Span(s, annotationLimit)), "Invalid diagnostic snapshot.");
            Require(packet.WireCells!.Count <= 1_000_001 && packet.WireCells.All(c => c != null && Span(c.Data, annotationLimit) && Span(c.Acknowledge, annotationLimit)), "Invalid protocol source cells.");
            if (packet.I2c is { } wire) Require(Enum.IsDefined(wire.Completion) && wire.Acknowledged != null && wire.Acknowledged.Count == (wire.AddressObserved ? packet.Bytes.Length + 1 : 0), "Invalid I2C completion or acknowledgement snapshot.");
            if (packet.Annotations is { } annotations) Require(annotations.Count <= 2_000_003 && annotations.All(a => a != null && Text(a.Label) && a.Kind is "point" or "data" or "ack" && Span(a.Span, annotationLimit)), "Invalid protocol annotations.");
        }
        Require(state!.Groups != null && state.Groups.Length <= 1024 && Name(state.ActiveGroup, 40) && state.Colors != null && state.Colors.Count <= 32, "Invalid saved observation settings.");
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All signals" };
        foreach (var group in state.Groups!)
        {
            Require(group != null && Name(group.Name, 40) && groups.Add(group.Name) && group.Channels is { Length: > 0 and <= 32 } && group.Channels.Distinct().Count() == group.Channels.Length && group.Channels.All(c => c is >= 0 and < 32) && group.Decoders is { Length: <= 4 }, "Invalid or duplicate saved Group.");
            Require(group!.Decoders.Distinct().Count() == group.Decoders.Length && group.Decoders.All(id => Capture.Decoders.Any(d => d.Id == id
                && (document.Version >= 4 || d.Channels.All(group.Channels.Contains)))), "Saved Group has invalid decoder sources.");
        }
        Require(state.ActiveGroup == "All signals" || state.Groups.Any(g => g.Name == state.ActiveGroup), "Active Group is missing.");
        Require(state.Colors!.All(c => c.Key is >= 0 and < 32 && c.Value != null && c.Value.Length is 7 or 9 && c.Value[0] == '#' && c.Value.Skip(1).All(Uri.IsHexDigit)), "Invalid waveform colors.");
        Require(double.IsFinite(state.Start) && double.IsFinite(state.End) && state.Start >= 0 && state.End <= capture.SampleCount && state.End - state.Start >= 10, "Invalid time viewport.");
        ValidateMeasurement(capture, state.A, state.B, state.Mode, state.LocalPulse);
        Require(Text(state.Query, 4096), "Invalid search.");
        Require(document.Version < 3 || state.Measurements != null, "Saved timing results are missing.");
        Require(state.Measurements == null || state.Measurements.Length <= TimingResult.MaximumCount, "Too many saved timing results.");
        Require(document.Version >= 3 || state.Measurements is null or { Length: 0 }, "Timing results require analysis version 3.");
        var resultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in state.Measurements ?? [])
        {
            ValidateTimingResult(capture, result);
            Require(resultIds.Add(result.Id), "Duplicate timing result identity.");
        }
        Require(state.SelectedPacket == null || packetIds.Contains(state.SelectedPacket), "Selected packet is missing.");
        Require(!state.Details || state.SelectedPacket != null, "Packet detail has no selection.");
        Require(state.SelectedIssue == null || capture.Issues.Any(i => i.Packet.Id == state.SelectedPacket && i.Diagnostic.Id == state.SelectedIssue), "Selected diagnostic is missing.");
        if (state.SelectedProtocolIndex is int selectedIndex)
        {
            var packet = capture.Packets.FirstOrDefault(p => p.Id == state.SelectedPacket);
            Require(document.Version >= 2 && state.SelectedIssue == null && packet != null &&
                (packet.Annotations != null || packet.HasI2cCells)
                && selectedIndex >= 0 && selectedIndex < capture.ProtocolItems(packet).Count, "Selected protocol element is missing or ambiguous.");
        }
        var channels = state.ActiveGroup == "All signals" ? ids : state.Groups.First(g => g.Name == state.ActiveGroup).Channels.Where(ids.Contains).ToHashSet();
        Require(state.EdgeChannel == null || channels.Contains(state.EdgeChannel.Value), "Edge navigation source is outside the saved Group.");
        Require(state.Vertical != null && double.IsFinite(state.Vertical.Offset) && state.Vertical.Offset >= 0 && (state.Vertical.Channel == null ? state.Vertical.Offset <= 200 : channels.Contains(state.Vertical.Channel.Value) && state.Vertical.Offset < 52), "Invalid vertical source anchor.");
    }
}
