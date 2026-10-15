// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

internal static class SerialEvidenceValidation
{
    public static void Validate(Packet packet, DecoderSource source, long sampleCount, int version)
    {
        if (!packet.IsSerial) return;
        if (version < 5 || packet.Spi != null && packet.Uart != null || packet.I2c != null
            || packet.WireCells.Count != 0 || packet.Bytes.Length != 0 || packet.Value != null)
            throw new InvalidDataException("Serial words require version 5 and cannot mix with byte or I2C payloads.");
        void Word(SerialWord? word, int width)
        {
            if (word == null || !word.IsValid || word.BitWidth != width || word.Span == null
                || word.Span.Start < packet.Span.Start || word.Span.Start >= packet.Span.End || word.Span.Start >= sampleCount
                || word.Span.Start >= word.Span.End || word.Span.End > sampleCount * 2)
                throw new InvalidDataException("Invalid lossless word or source interval.");
        }
        if (packet.Spi is { } spi)
        {
            if (source.EffectiveProtocol != "spi" || source.Spi == null || spi.Words == null || spi.Words.Length > 8192 || !Enum.IsDefined(spi.Completion))
                throw new InvalidDataException("SPI word evidence requires matching decoder options.");
            bool mosi = source.Inputs.Any(i => i.Role == "mosi"), miso = source.Inputs.Any(i => i.Role == "miso"), cs = source.Inputs.Any(i => i.Role == "cs");
            if ((!cs && spi.Completion != SpiCompletion.WithoutChipSelect) || (cs && spi.Completion == SpiCompletion.WithoutChipSelect))
                throw new InvalidDataException("SPI completion contradicts chip-select presence.");
            long previous = -1;
            foreach (var pair in spi.Words)
            {
                if (pair == null || (pair.Mosi != null) != mosi || (pair.Miso != null) != miso)
                    throw new InvalidDataException("SPI data directions contradict captured roles.");
                if (mosi) Word(pair.Mosi, source.Spi.WordSize);
                if (miso) Word(pair.Miso, source.Spi.WordSize);
                if (mosi && miso && pair.Mosi!.Span != pair.Miso!.Span)
                    throw new InvalidDataException("Simultaneous SPI words must share their sampled interval.");
                var span = (pair.Mosi ?? pair.Miso)!.Span;
                // Native ends are estimates and may overlap when clock period changes.
                if (span.Start <= previous) throw new InvalidDataException("SPI word starts must be strictly ordered.");
                previous = span.Start;
            }
        }
        if (packet.Uart is { } uart)
        {
            if (source.EffectiveProtocol != "uart" || source.Uart == null || !Enum.IsDefined(uart.Completion)
                || !(uart.Completion switch { UartCompletion.Incomplete => uart.Valid == null,
                    UartCompletion.StopObserved => uart.Valid != null && uart.Data != null,
                    UartCompletion.RejectedStart => uart.Valid == false && uart.Data == null,
                    UartCompletion.Break => uart.Valid == null && uart.Data == null, _ => false }))
                throw new InvalidDataException("UART completion, validity or options are inconsistent.");
            if (uart.Data != null) Word(uart.Data, source.Uart.DataBits);
        }
    }
}
