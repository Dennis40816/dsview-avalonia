// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

internal static class SerialDecodeRequest
{
    public static Signal?[] Resolve(Capture capture, DecoderSource source)
    {
        DecoderSource.Validate([source]);
        if (capture.SampleCount is < 10 or > 64_000_000 || capture.SampleRate <= 0 || capture.Gaps.Count != 0)
            throw new InvalidDataException("Serial decoding requires a finite continuous capture of 10–64,000,000 samples.");
        if (source.EffectiveProtocol == "spi" ? source.Spi == null : source.EffectiveProtocol != "uart" || source.Uart == null)
            throw new InvalidDataException("Explicit SPI or UART settings are required.");
        if (source.Uart is { } uart && capture.SampleRate < 2L * uart.BaudRate)
            throw new InvalidDataException("UART decoding requires at least two samples per bit.");
        var existing = capture.EffectiveDecoderSources.FirstOrDefault(s => s.Id == source.Id);
        if (existing != null && existing.EffectiveProtocol != source.EffectiveProtocol)
            throw new InvalidDataException("This decoder identity belongs to a different protocol.");
        Signal? ResolveRole(string role)
        {
            var input = source.Inputs.SingleOrDefault(i => i.Role == role);
            if (input == null) return null;
            var signals = capture.Signals.Where(s => s.Id == input.Channel).ToArray();
            if (signals.Length != 1) throw new InvalidDataException($"Captured channel D{input.Channel} is absent or duplicated.");
            return signals[0];
        }
        return (source.Spi != null ? new[] { "clk", "miso", "mosi", "cs" } : ["data"]).Select(ResolveRole).ToArray();
    }
    public static int SpiMask(DecoderSource source) => (source.Inputs.Any(i => i.Role == "miso") ? 1 : 0)
        | (source.Inputs.Any(i => i.Role == "mosi") ? 2 : 0) | (source.Inputs.Any(i => i.Role == "cs") ? 4 : 0);
}

public sealed record SerialDecodeResult(Capture Source, DecoderSource Decoder, IReadOnlyList<Packet> Packets) : IDecoderResult
{
    public DecoderSource Binding => Decoder;
    public Capture ToCapture()
    {
        SerialDecodeRequest.Resolve(Source, Decoder);
        var sources = Source.EffectiveDecoderSources.Where(s => s.Id != Decoder.Id).Append(Decoder.WithExplicitProtocol()).ToArray();
        DecoderSource.Validate(sources);
        if (Packets.Any(p => p.Decoder != Decoder.Id)) throw new InvalidDataException("Result contains a different decoder instance.");
        return Source with { DecoderSources = sources,
            Packets = Source.Packets.Where(p => p.Decoder != Decoder.Id).Concat(Packets).OrderBy(p => p.Span.Start).ToArray() };
    }
}
