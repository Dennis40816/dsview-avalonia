// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

public sealed record DecoderLane(Decoder Decoder, string? Direction, bool ShowAnnotations)
{
    public string Name => Direction == null ? Decoder.Name : Decoder.Name + " · " + Direction;
    public static DecoderLane[] Create(Capture capture, IEnumerable<Decoder> decoders) => decoders.SelectMany(decoder =>
    {
        var source = capture.EffectiveDecoderSources.First(s => s.Id == decoder.Id);
        if (!capture.Packets.Any(p => p.Decoder == decoder.Id && p.Spi != null)) return new[] { new DecoderLane(decoder, null, true) };
        return new[] { "MOSI", "MISO" }.Where(d => source.Inputs.Any(i => i.Role == d.ToLowerInvariant()))
            .Select((direction, index) => new DecoderLane(decoder, direction, index == 0)).ToArray();
    }).ToArray();
    public (ProtocolItem Item, int Index)[] Items(Capture capture, Packet packet) => capture.ProtocolItems(packet)
        .Select((item, index) => (Item: item, Index: index))
        .Where(p => Direction == null || (p.Item.Serial == null ? ShowAnnotations : p.Item.Serial.Direction == Direction)).ToArray();
}
