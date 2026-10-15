// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using System.Text.Json;
using DSView.Domain;
using static DSView.Infrastructure.SerialEnvelope;

namespace DSView.Infrastructure;

public static class UartEventAdapter
{
    public static SerialDecodeResult Read(Capture raw, DecoderSource source, string json, CancellationToken cancellation = default)
    {
        var signal = SerialDecodeRequest.Resolve(raw, source)[0]!; var options = source.Uart ?? throw new InvalidDataException("UART settings required.");
        using var output = new SerialEnvelope(raw, source, json, cancellation);
        var builder = new SerialPacketBuilder(raw, source);
        SerialEvent? start = null, stop = null, stopFailure = null; SerialWord? data = null; BigInteger value = 0;
        bool invalidStart = false, invalidStop = false, paritySeen = false, parityError = false;
        long previousStart = -1, lastEnd = -1;
        var annotations = new List<ProtocolAnnotation>(); var errors = new List<(string Code, string Message, SourceSpan Span)>();
        foreach (var item in output.Events)
        {
            cancellation.ThrowIfCancellationRequested(); Require(Number(item.First) == 0, "Unexpected UART direction.");
            switch (item.Kind)
            {
                case "STARTBIT": case "INVALID STARTBIT":
                    Require(start == null && item.Span.Start > previousStart, "Nested or unordered UART frame.");
                    bool level = Bit(item.Second); invalidStart = item.Kind == "INVALID STARTBIT";
                    Require(level == invalidStart && Observed(item.Span) == level, "UART start contradicts native/raw evidence.");
                    start = item; previousStart = item.Span.Start; lastEnd = item.Span.End;
                    annotations.Add(new(invalidStart ? "Invalid start" : "Start", item.Span, "data"));
                    if (invalidStart) errors.Add(("invalid-start", "Invalid UART start bit.", item.Span));
                    break;
                case "DATA":
                    // Independent floor/ceil rounding can overlap adjacent annotations by one sample.
                    Require(start != null && !invalidStart && data == null && item.Span.Start >= lastEnd - 1, "UART data has no valid start.");
                    Array(item.Second, 2); value = Value(item.Second[0], options.DataBits); var bits = item.Second[1]; Array(bits, options.DataBits);
                    BigInteger reconstructed = 0; long previousBitEnd = item.Span.Start;
                    for (int i = 0; i < options.DataBits; i++)
                    {
                        Array(bits[i], 3); bool bit = Bit(bits[i][0]); var span = new SourceSpan(Number(bits[i][1]), Number(bits[i][2]));
                        Require(span.Start >= previousBitEnd && span.Start - previousBitEnd <= 1 && (i != 0 || span.Start == item.Span.Start)
                            && span.End > span.Start && span.End <= item.Span.End && Observed(span) == bit, "UART bit contradicts source or interval.");
                        if (bit) reconstructed |= BigInteger.One << (options.BitOrder == "lsb-first" ? i : options.DataBits - 1 - i);
                        previousBitEnd = span.End;
                    }
                    Require(reconstructed == value && item.Span.End - previousBitEnd <= 1, "UART value or extent contradicts its bits.");
                    data = Word(value, options.DataBits, item.Span); lastEnd = item.Span.End; break;
                case "PARITYBIT": case "PARITY ERROR":
                    Require(data != null && options.Parity != "none" && !paritySeen && stop == null && item.Span.Start >= lastEnd - 1, "Unexpected UART parity.");
                    paritySeen = true; parityError = item.Kind == "PARITY ERROR";
                    bool actual = Observed(item.Span); int ones = 0;
                    for (int i = 0; i < options.DataBits; i++) if ((value & (BigInteger.One << i)) != 0) ones++;
                    bool expected = options.Parity switch { "even" => ones % 2 != 0, "odd" => ones % 2 == 0, "one" => true, _ => false };
                    Require(parityError == (actual != expected), "UART parity status contradicts observed bits.");
                    if (parityError)
                    {
                        // Upstream emits placeholder [0,1]; never present it as measured detail.
                        Array(item.Second, 2); Bit(item.Second[0]); Bit(item.Second[1]);
                        errors.Add(("parity", "UART parity error.", item.Span));
                    }
                    else Require(Bit(item.Second) == actual, "UART parity bit contradicts source.");
                    annotations.Add(new(parityError ? "Parity error" : "Parity", item.Span, "data")); lastEnd = item.Span.End; break;
                case "INVALID STOPBIT":
                    Require(CanStop() && !invalidStop && !Bit(item.Second) && !Observed(item.Span), "Unexpected UART stop failure.");
                    invalidStop = true; stopFailure = item; errors.Add(("invalid-stop", "Invalid UART stop bit.", item.Span)); break;
                case "STOPBIT":
                    Require(CanStop() && item.Span.Start >= lastEnd - 1 && (stopFailure == null || stopFailure.Span == item.Span), "Unexpected UART stop.");
                    bool stopLevel = Bit(item.Second);
                    Require(stopLevel == Observed(item.Span) && invalidStop == !stopLevel, "UART stop status contradicts source.");
                    stop = item; lastEnd = item.Span.End; annotations.Add(new(stopLevel ? "Stop" : "Invalid stop", item.Span, "data")); break;
                case "FRAME":
                    Require(start != null && (invalidStart || stop != null) && item.Span.Start <= start!.Span.Start
                        && start.Span.Start - item.Span.Start <= 1 && item.Span.End == lastEnd, "UART frame has no matching boundaries.");
                    var beginning = signal.Nearest(item.Span.Start);
                    Require(beginning?.Sample == item.Span.Start && beginning.High == options.Inverted, "UART frame has no observed start transition.");
                    Array(item.Second, 2); bool valid = Boolean(item.Second[1]);
                    Require(valid == !(invalidStart || invalidStop || parityError), "UART frame validity contradicts its events.");
                    if (!invalidStart) Require(Value(item.Second[0], options.DataBits) == value, "UART frame value contradicts DATA.");
                    else Require(item.Second[0].ValueKind == JsonValueKind.Null || Value(item.Second[0], options.DataBits) >= 0, "Invalid rejected-frame value.");
                    Finish(item.Span, invalidStart ? UartCompletion.RejectedStart : UartCompletion.StopObserved, valid); break;
                case "BREAK":
                    Require(Number(item.Second) == 0 && item.Span.Start < item.Span.End && item.Span.End < raw.SampleCount
                        && (item.Span.End - item.Span.Start) >= Math.Ceiling((1d + options.DataBits + (options.Parity == "none" ? 0 : 1) + options.StopBits) * raw.SampleRate / options.BaudRate), "Invalid UART break interval.");
                    Require(Level(signal, item.Span.Start) == options.Inverted && Level(signal, item.Span.End) != options.Inverted
                        && !signal.Edges.Any(e => e.Sample > item.Span.Start && e.Sample < item.Span.End), "Break is not a continuous asserted interval.");
                    if (start != null) Finish(new(start.Span.Start, item.Span.End), UartCompletion.Incomplete, null);
                    var packet = builder.Create(item.Span, "UART break");
                    builder.Add(packet with { Uart = new(null, UartCompletion.Break, null), Annotations = [new("Break", item.Span, "data")],
                        Diagnostics = [new(packet.Id + "/break", "UART", "break", "UART break condition.", [item.Span])] }); break;
                default: throw new InvalidDataException("Unknown native UART event.");
            }
        }
        if (start != null)
        {
            Require(stop == null && !invalidStart && !invalidStop, "UART completion events are truncated.");
            Finish(new(start.Span.Start, raw.SampleCount), UartCompletion.Incomplete, null);
        }
        return builder.Result();
        bool CanStop() => start != null && !invalidStart && data != null && stop == null && (options.Parity == "none" || paritySeen);
        bool Observed(SourceSpan span)
        {
            long middle = span.Start + (span.End - span.Start) / 2;
            Require(span.Start >= 0 && span.Start < span.End && middle < raw.SampleCount, "UART bit has no sampled center.");
            return Level(signal, middle) ^ options.Inverted;
        }
        void Finish(SourceSpan span, UartCompletion completion, bool? valid)
        {
            var packet = builder.Create(span, "UART frame");
            builder.Add(packet with { Uart = new(data, completion, valid), Annotations = annotations.ToArray(),
                Error = errors.Count == 0 ? null : errors[0].Message,
                Diagnostics = errors.Select((e, i) => new PacketDiagnostic(packet.Id + "/" + i, "UART", e.Code, e.Message, [e.Span])).ToArray() });
            start = stop = stopFailure = null; data = null; annotations.Clear(); errors.Clear(); invalidStart = invalidStop = paritySeen = parityError = false; value = 0;
        }
    }
    private static bool Bit(JsonElement value)
    {
        // The upstream inversion path uses Python `not`, which emits JSON booleans.
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        long bit = Number(value); Require(bit is 0 or 1, "Expected bit."); return bit == 1;
    }
}
