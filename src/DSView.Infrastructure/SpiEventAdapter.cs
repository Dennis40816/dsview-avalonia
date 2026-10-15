// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using System.Text.Json;
using DSView.Domain;
using static DSView.Infrastructure.SerialEnvelope;

namespace DSView.Infrastructure;

// Interprets native Python records, never decodes raw SPI into words itself.
public static class SpiEventAdapter
{
    public static SerialDecodeResult Read(Capture raw, DecoderSource source, string json, CancellationToken cancellation = default)
    {
        var signals = SerialDecodeRequest.Resolve(raw, source); var options = source.Spi ?? throw new InvalidDataException("SPI settings required.");
        using var output = new SerialEnvelope(raw, source, json, cancellation);
        var builder = new SerialPacketBuilder(raw, source); var words = new List<SpiWord>(); var annotations = new List<ProtocolAnnotation>();
        SerialEvent? pending = null; long? begin = null, released = null; long previousWord = -1, lastCs = -1; bool? cs = null;
        bool haveCs = signals[3] != null; bool active = options.ChipSelectPolarity == "active-high";
        Require(output.Events.Count > 0 && output.Events[0].Kind == "CS-CHANGE", "Missing SPI source state.");
        foreach (var item in output.Events)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pending != null) Require(item.Kind == "DATA", "SPI bits have no following data word.");
            if (released != null) Require(item.Kind == "TRANSFER", "SPI release has no transfer summary.");
            switch (item.Kind)
            {
                case "CS-CHANGE":
                    Require(item.Span.Start == item.Span.End && item.Span.Start > lastCs, "Invalid SPI CS event.");
                    lastCs = item.Span.Start;
                    if (!haveCs)
                    {
                        Require(item.Sequence == 0 && item.Span.Start == 0 && item.First.ValueKind == JsonValueKind.Null && item.Second.ValueKind == JsonValueKind.Null, "Unexpected absent CS evidence.");
                        begin = 0; break;
                    }
                    bool next = Bit(item.Second);
                    Require(next == Level(signals[3]!, item.Span.Start), "CS state contradicts raw samples.");
                    if (item.Sequence == 0) Require(item.Span.Start == 0 && item.First.ValueKind == JsonValueKind.Null, "Invalid initial CS state.");
                    else Require(cs != null && Bit(item.First) == cs && next != cs && signals[3]!.Edges.Any(e => e.Sample == item.Span.Start && e.High == next), "Invalid CS transition.");
                    cs = next;
                    if (next == active)
                    {
                        Require(begin == null, "Nested SPI transfer."); begin = item.Span.Start;
                        annotations.Add(new(item.Sequence == 0 ? "CS active at capture start" : "CS asserted", item.Span, "point"));
                    }
                    else if (begin != null) { released = item.Span.Start; annotations.Add(new("CS released", item.Span, "point")); }
                    break;
                case "BITS":
                    Require(begin != null && (!haveCs || cs == active) && item.Span.Start >= begin && item.Span.Start > previousWord, "SPI data outside an active transfer.");
                    pending = item; break;
                case "DATA":
                    Require(pending != null && pending.Span == item.Span, "SPI data lacks matching native bits.");
                    var pair = new SpiWord(ReadWord(item.First, pending!.First, signals[2], item.Span), ReadWord(item.Second, pending.Second, signals[1], item.Span));
                    Require(words.Count < 8192, "SPI transfer exceeds the word limit."); words.Add(pair);
                    previousWord = item.Span.Start; pending = null; break;
                case "TRANSFER":
                    Require(haveCs && begin != null && released != null && item.Span == new SourceSpan(begin.Value, released.Value), "Unexpected SPI transfer summary.");
                    VerifyTransfer(item.First, words.Select(w => w.Mosi)); VerifyTransfer(item.Second, words.Select(w => w.Miso));
                    Finish(released!.Value, SpiCompletion.ChipSelectReleased); break;
                default: throw new InvalidDataException("Unknown native SPI event.");
            }
        }
        Require(pending == null && released == null, "Truncated SPI event stream.");
        if (begin != null && (haveCs || words.Count > 0)) Finish(raw.SampleCount, haveCs ? SpiCompletion.CaptureEnded : SpiCompletion.WithoutChipSelect);
        return builder.Result();

        SerialWord? ReadWord(JsonElement value, JsonElement bits, Signal? signal, SourceSpan span)
        {
            if (signal == null) { Require(value.ValueKind == JsonValueKind.Null && bits.ValueKind == JsonValueKind.Null, "Data on an absent SPI role."); return null; }
            Array(bits, options.WordSize); var native = bits.EnumerateArray().Reverse().ToArray(); BigInteger observed = 0; long previous = -1;
            for (int i = 0; i < native.Length; i++)
            {
                Array(native[i], 3); bool bit = Bit(native[i][0]); long start = Number(native[i][1]), end = Number(native[i][2]);
                Require(start >= span.Start && start < raw.SampleCount && end > start && end <= span.End && (i == 0 ? start == span.Start : start == previous)
                    && Level(signal, start) == bit, "SPI bit evidence contradicts source or interval.");
                bool rising = options.Mode is 0 or 3; var edge = signals[0]!.Nearest(start);
                Require(edge?.Sample == start && edge.High == rising && (!haveCs || Level(signals[3]!, start) == active), "SPI bit was not sampled at its selected edge.");
                if (bit) observed |= BigInteger.One << (options.BitOrder == "lsb-first" ? i : native.Length - 1 - i);
                previous = end;
            }
            Require(previous == span.End && observed == Value(value, options.WordSize), "SPI value contradicts bit evidence.");
            return Word(observed, options.WordSize, span);
        }
        void Finish(long end, SpiCompletion completion)
        {
            var packet = builder.Create(new(begin!.Value, end), haveCs ? "SPI transfer" : "SPI stream");
            builder.Add(packet with { Spi = new(words.ToArray(), completion), Annotations = annotations.ToArray() });
            words.Clear(); annotations.Clear(); begin = released = null; previousWord = -1;
        }
    }
    private static bool Bit(JsonElement value) { long bit = Number(value); Require(bit is 0 or 1, "Expected bit."); return bit == 1; }
    private static void VerifyTransfer(JsonElement summary, IEnumerable<SerialWord?> direction)
    {
        var words = direction.OfType<SerialWord>().ToArray(); Array(summary, words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            Array(summary[i], 3); var word = words[i];
            Require(Number(summary[i][0]) == word.Span.Start && Number(summary[i][1]) == word.Span.End
                && Word(Value(summary[i][2], word.BitWidth), word.BitWidth, word.Span).Hex == word.Hex, "SPI transfer contradicts its words.");
        }
    }
}
