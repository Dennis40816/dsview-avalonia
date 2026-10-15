// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

internal sealed class SerialPacketBuilder(Capture raw, DecoderSource source)
{
    private readonly HashSet<string> used = raw.Packets.Where(p => p.Decoder != source.Id).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
    private int ordinal;
    public List<Packet> Packets { get; } = [];
    public Packet Create(SourceSpan span, string title)
    {
        string id; do { id = source.Id + "/native-" + ++ordinal; } while (!used.Add(id));
        return new(id, source.Id, new(span.Start, Math.Min(raw.SampleCount, Math.Max(span.Start + 1, span.End))), [], "", title, null, null, []);
    }
    public void Add(Packet packet)
    {
        SerialEnvelope.Require(Packets.Count < 100_000, "Too many serial packets.");
        SerialEvidenceValidation.Validate(packet, source, raw.SampleCount, 5); Packets.Add(packet);
    }
    public SerialDecodeResult Result() => new(raw, source.WithExplicitProtocol(), Packets.OrderBy(p => p.Span.Start).ToArray());
}
