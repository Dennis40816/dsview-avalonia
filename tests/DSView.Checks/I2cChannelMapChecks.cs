// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;
using DSView.Infrastructure;

internal static class I2cChannelMapChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(PacksExplicitRolesInWorkerOrder(), nameof(PacksExplicitRolesInWorkerOrder));
        check(SwappingRolesSwapsOnlyInputPlanes(), nameof(SwappingRolesSwapsOnlyInputPlanes));
        check(RejectsUnavailableOrAmbiguousRoles(), nameof(RejectsUnavailableOrAmbiguousRoles));
        check(RejectsGapInsteadOfJoiningSamples(), nameof(RejectsGapInsteadOfJoiningSamples));
        check(CancelledPackingProducesNoInput(), nameof(CancelledPackingProducesNoInput));
        check(RedecodePreservesOtherProtocolsAndProvenance(), nameof(RedecodePreservesOtherProtocolsAndProvenance));
        check(GeneratedPacketIdsDoNotCollideWithOtherProtocols(), nameof(GeneratedPacketIdsDoNotCollideWithOtherProtocols));
        check(RejectsDuplicateOutputFields(), nameof(RejectsDuplicateOutputFields));
        check(RejectsUnknownOutputFields(), nameof(RejectsUnknownOutputFields));
        check(RejectsMissingCompletionEvidence(), nameof(RejectsMissingCompletionEvidence));
    }

    // Thirteen samples: clock high [1,9), data high [0,3) and [12,13).
    // Extra D0 and D1 deliberately do not carry these roles.
    private static Capture Source() => new(1_000_000, 13,
        [new(3, "Data", true, [new(3, false), new(12, true)]),
         new(0, "Unrelated", false, []), new(19, "Clock", false, [new(1, true), new(9, false)]),
         new(1, "Also unrelated", true, [])], []) { SourceDescription = "Imported capture; original provenance." };
    private const string Empty = """
        {"schema":1,"decoder":"1:i2c","addressFormat":"shifted","sampleRate":1000000,"sampleCount":13,"events":[],"completion":"eof","openTransaction":false}
        """;
    private const string Started = """
        {"schema":1,"decoder":"1:i2c","addressFormat":"shifted","sampleRate":1000000,"sampleCount":13,"events":[{"sequence":0,"kind":"START","start":2,"end":2,"spanEvidence":"point","value":null,"bits":[]}],"completion":"eof","openTransaction":true}
        """;
    private static bool PacksExplicitRolesInWorkerOrder()
    {
        var source = Source(); var signals = source.Signals;
        var packed = I2cSampleInput.Pack(source, new(19, 3), default);
        return packed.SequenceEqual(new byte[] { 0xFE, 0x01, 0x07, 0x10 })
            && ReferenceEquals(source.Signals, signals) && signals.Select(s => s.Id).SequenceEqual(new[] { 3, 0, 19, 1 });
    }
    private static bool SwappingRolesSwapsOnlyInputPlanes() =>
        I2cSampleInput.Pack(Source(), new(3, 19), default).SequenceEqual(new byte[] { 0x07, 0x10, 0xFE, 0x01 });
    private static bool RejectsUnavailableOrAmbiguousRoles() =>
        Rejected(() => I2cSampleInput.Pack(Source(), new(19, 19), default))
        && Rejected(() => I2cSampleInput.Pack(Source(), new(19, 31), default))
        && Rejected(() => I2cSampleInput.Pack(Source(), new(-1, 3), default))
        && Rejected(() => I2cSampleInput.Pack(Source() with { Signals = [.. Source().Signals, Source().Signals[0]] }, new(19, 3), default));
    private static bool RejectsGapInsteadOfJoiningSamples() =>
        Rejected(() => I2cSampleInput.Pack(Source() with { Gaps = [new(5, 7)] }, new(19, 3), default));
    private static bool CancelledPackingProducesNoInput()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { I2cSampleInput.Pack(Source(), new(19, 3), cancellation.Token); return false; }
        catch (OperationCanceledException) { return true; }
    }
    private static Packet OtherPacket() => new("i2c-1", "spi", new(1, 4), [0x4B], "", "SPI byte", null, null, []);
    private static bool RedecodePreservesOtherProtocolsAndProvenance()
    {
        var other = OtherPacket(); var source = Source() with { Packets = [other, other with { Id = "old-i2c", Decoder = "i2c" }] };
        var updated = I2cEventAdapter.Apply(source, Empty);
        return ReferenceEquals(updated.Signals, source.Signals) && updated.SourceDescription == source.SourceDescription
            && updated.Packets.Count == 1 && ReferenceEquals(updated.Packets[0], other) && source.Packets.Count == 2;
    }
    private static bool GeneratedPacketIdsDoNotCollideWithOtherProtocols()
    {
        var other = OtherPacket(); var updated = I2cEventAdapter.Apply(Source() with { Packets = [other] }, Started);
        return updated.Packets.Count == 2 && updated.Packets.Select(p => p.Id).Distinct().Count() == 2
            && ReferenceEquals(updated.Packets[0], other) && updated.Packets.Single(p => p.Decoder == "i2c").I2c?.Completion == I2cCompletion.CaptureEnded;
    }
    private static bool RejectsDuplicateOutputFields() =>
        Rejected(() => I2cEventAdapter.Apply(Source(), Empty.Replace("\"schema\":1", "\"schema\":1,\"Schema\":1", StringComparison.Ordinal)))
        && Rejected(() => I2cEventAdapter.Apply(Source(), Started.Replace("\"start\":2", "\"start\":2,\"Start\":2", StringComparison.Ordinal)));
    private static bool RejectsUnknownOutputFields() =>
        Rejected(() => I2cEventAdapter.Apply(Source(), Empty.Replace("\"schema\":1", "\"schema\":1,\"extra\":false", StringComparison.Ordinal)))
        && Rejected(() => I2cEventAdapter.Apply(Source(), Started.Replace("\"start\":2", "\"start\":2,\"extra\":false", StringComparison.Ordinal)));
    private static bool RejectsMissingCompletionEvidence() =>
        Rejected(() => I2cEventAdapter.Apply(Source(), Empty.Replace(",\"openTransaction\":false", "", StringComparison.Ordinal)));
    private static bool Rejected(Action action)
    {
        try { action(); return false; } catch (InvalidDataException) { return true; }
    }

    public static async Task RunNativeAsync(Action<bool, string> check, string? configuration)
    {
        if (configuration == null) { Console.WriteLine("SKIP mapped-channel worker checks: pass --native-i2c <configuration>."); return; }
        var raw = await NativeI2cSource.LoadRawExampleAsync();
        var options = NativeI2cSource.ReadConfiguration(configuration);
        // The installed DSView demo uses this reversed physical mapping.
        // This fixture only validates role handling; it is not hardware evidence.
        var swapped = raw with { Signals = [raw.Signals[1] with { Id = 0 }, raw.Signals[0] with { Id = 1 }], SourceDescription = "Mapped raw fixture; unchanged provenance." };
        var result = await NativeI2cSource.DecodeMappedAsync(swapped, new(1, 0), options);
        check(ReferenceEquals(result.Source, swapped) && result.Channels == new I2cChannelMap(1, 0)
            && result.Packets.Single().Address == "0x2A" && result.Packets.Single().Bytes.SequenceEqual(new byte[] { 0xA5, 0x11, 0x02, 0x35, 0x12, 0 })
            && swapped.Packets.Count == 0 && swapped.SourceDescription == "Mapped raw fixture; unchanged provenance.",
            "Native decoder honors D1/SCL and D0/SDA while retaining source identity and original physical IDs");
        var sparse = raw with { Signals = [raw.Signals[1] with { Id = 3 }, new(0, "Unrelated", false, []), raw.Signals[0] with { Id = 19 }, new(1, "Unrelated", true, [])] };
        var sparseResult = await NativeI2cSource.DecodeMappedAsync(sparse, new(19, 3), options);
        check(sparseResult.Packets.Single().Hex == result.Packets.Single().Hex && ReferenceEquals(sparseResult.Source, sparse)
            && sparseResult.Channels == new I2cChannelMap(19, 3) && sparse.Signals.Select(s => s.Id).SequenceEqual(new[] { 3, 0, 19, 1 }),
            "Native decoder uses sparse mapped channels instead of source order or unrelated D0/D1");
    }
}
