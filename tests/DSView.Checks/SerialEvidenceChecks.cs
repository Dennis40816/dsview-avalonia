// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class SerialEvidenceChecks
{
    private const string Wide = "FEDCBA98765432100123456789ABCDEF";
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(await WideDuplexAndUartSurviveSaveAsync(), nameof(WideDuplexAndUartSurviveSaveAsync));
        check(DuplexLanesSelectDistinctGlobalItems(), nameof(DuplexLanesSelectDistinctGlobalItems));
        check(OptionalSpiRolesDoNotInventChannels(), nameof(OptionalSpiRolesDoNotInventChannels));
        check(InstanceGroupsLocateTheirActualSources(), nameof(InstanceGroupsLocateTheirActualSources));
        check(WordInspectorIsBoundedAndRejectsStaleSelection(), nameof(WordInspectorIsBoundedAndRejectsStaleSelection));
        check(WideWordsRejectTruncationAndUnusedHighBits(), nameof(WideWordsRejectTruncationAndUnusedHighBits));
        check(SerialEvidenceRejectsContradictoryDirectionsAndSpans(), nameof(SerialEvidenceRejectsContradictoryDirectionsAndSpans));
        check(UartDistinguishesIncompleteInvalidAndValidFrames(), nameof(UartDistinguishesIncompleteInvalidAndValidFrames));
        check(UnsupportedOptionsCannotBecomeEvidence(), nameof(UnsupportedOptionsCannotBecomeEvidence));
        check(await LegacyV4KeepsUnknownOptionsAsync(), nameof(LegacyV4KeepsUnknownOptionsAsync));
        check(await SerialJsonRejectsDuplicateUnknownAndMissingFieldsAsync(), nameof(SerialJsonRejectsDuplicateUnknownAndMissingFieldsAsync));
        check(await NewMetadataCannotBeSmuggledIntoV4Async(), nameof(NewMetadataCannotBeSmuggledIntoV4Async));
        check(await AbsentInstanceMembershipSurvivesSaveWithoutInventingTracksAsync(), nameof(AbsentInstanceMembershipSurvivesSaveWithoutInventingTracksAsync));
        check(await IndependentI2cInstanceKeepsByteIdentityAsync(), nameof(IndependentI2cInstanceKeepsByteIdentityAsync));
    }
    private static Capture Capture()
    {
        var spi = new Packet("transfer-1", "sensor", new(5, 180), [], "", "Transfer", null, null, [])
        {
            Spi = new([new(new(128, Wide, new(10, 138)), new(128, "00000000000000000000000000000001", new(10, 138)))], SpiCompletion.ChipSelectReleased),
            Annotations = [new("CS asserted", new(5, 5), "point"), new("CS released", new(180, 180), "point")]
        };
        var uart = new Packet("frame-1", "console", new(240, 280), [], "", "Frame", null, null, [])
        { Uart = new(new(9, "1A5", new(250, 268)), UartCompletion.StopObserved, true) };
        return new(1_000_000, 1000, [new(0, "CLK", false, []), new(3, "MOSI", false, []),
            new(7, "MISO", false, []), new(8, "CS", true, []), new(9, "RX", true, [])], [spi, uart])
        {
            SourceDescription = "Controlled decoded snapshot fixture; not a native decode or UI test.",
            DecoderSources = [new("sensor", [new("clk", 0), new("mosi", 3), new("miso", 7), new("cs", 8)])
                { Protocol = "spi", Spi = new(3, "lsb-first", 128, "active-low") },
                new("console", [new("data", 9)]) { Protocol = "uart", Uart = new(115200, 9, "even", 1, "lsb-first", false) }]
        };
    }
    private static AnalysisDocument Document(Capture? capture = null)
    {
        capture ??= Capture(); var model = new Workbench(); model.Load(capture); model.ShowAll();
        model.SetMode(2); model.Pick(3, 42, 0); model.Pick(9, 278, 0);
        return new(AnalysisDocument.FileFormat, 5, "Serial fixture", capture, model.SnapshotState(new(3, 7), 3));
    }
    private static bool Rejected(AnalysisDocument document)
    {
        try { AnalysisFile.Validate(document); return false; } catch (InvalidDataException) { return true; }
    }
    private static async Task<bool> RejectedRead(string path)
    {
        try { await AnalysisFile.ReadAsync(path); return false; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return true; }
    }
    private static async Task<bool> WideDuplexAndUartSurviveSaveAsync()
    {
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "serial.dsview2");
        var doc = Document(); var model = new Workbench(); model.RestoreAnalysis(doc);
        model.SelectProtocol(doc.Capture.Packets[0], 2); doc = doc with { State = model.SnapshotState(new(3, 7), 3) };
        await AnalysisFile.WriteAsync(path, doc); var loaded = await AnalysisFile.ReadAsync(path); model.RestoreAnalysis(loaded);
        return loaded.Version == 5 && loaded.Capture.Packets[0].Spi!.Words[0].Mosi!.Hex == Wide
            && loaded.Capture.Packets[0].Spi!.Words[0].Miso!.Hex == "00000000000000000000000000000001"
            && loaded.Capture.Packets[1].Uart!.Data!.Hex == "1A5" && loaded.Capture.DecoderSources![0].Spi == doc.Capture.DecoderSources![0].Spi
            && loaded.Capture.DecoderSources[1].Uart == doc.Capture.DecoderSources[1].Uart
            && model.SelectedProtocol?.Serial?.Direction == "MISO" && model.A == doc.State.A && model.B == doc.State.B;
    }
    private static bool DuplexLanesSelectDistinctGlobalItems()
    {
        var capture = Capture(); var model = new Workbench(); model.Load(capture); model.ShowAll();
        var lanes = model.DecoderLanes.Where(l => l.Decoder.Id == "sensor").ToArray();
        var tx = lanes[0].Items(capture, capture.Packets[0]); var rx = lanes[1].Items(capture, capture.Packets[0]);
        model.SelectProtocol(capture.Packets[0], rx.Single().Index);
        return lanes.Select(l => l.Direction).SequenceEqual(new[] { "MOSI", "MISO" })
            && tx.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 3 }) && rx.Single().Index == 2
            && model.SelectedProtocol!.Serial!.Word.Hex.EndsWith("01", StringComparison.Ordinal)
            && tx.Single(x => x.Item.Serial != null).Item.Span == rx.Single().Item.Span;
    }
    private static bool OptionalSpiRolesDoNotInventChannels()
    {
        var capture = Capture(); var source = capture.DecoderSources![0]; var pair = capture.Packets[0].Spi!.Words[0];
        foreach (string role in new[] { "mosi", "miso" })
        {
            var single = source with { Inputs = source.Inputs.Where(i => i.Role == "clk" || i.Role == role).ToArray() };
            var packet = capture.Packets[0] with { Spi = new([role == "mosi" ? pair with { Miso = null } : pair with { Mosi = null }], SpiCompletion.WithoutChipSelect), Annotations = [] };
            var limited = capture with { DecoderSources = [single], Packets = [packet] };
            AnalysisFile.Validate(Document(limited));
            var model = new Workbench(); model.Load(limited); model.ShowAll();
            if (model.Decoders.Single().Channels.Length != 2 || model.DecoderLanes.Single().Direction != role.ToUpperInvariant()) return false;
        }
        return true;
    }
    private static bool InstanceGroupsLocateTheirActualSources()
    {
        var capture = Capture(); var model = new Workbench(); model.Load(capture); model.Select(capture.Packets[1]); model.Locate();
        var restored = GroupEditState.Restore([new("Console", [9], ["console"])]).Single();
        return model.Signals.Single().Id == 9 && model.Decoders.Single().Id == "console"
            && restored.Decoders.Single() == "console" && restored.Channels.Single() == 9;
    }
    private static bool WordInspectorIsBoundedAndRejectsStaleSelection()
    {
        var capture = Capture(); var packet = capture.Packets[0] with
        {
            Span = new(0, 900), Annotations = [],
            Spi = new(Enumerable.Range(0, 80).Select(i => new SpiWord(new(128, Wide, new(i * 10, i * 10 + 5)), new(128, "00000000000000000000000000000001", new(i * 10, i * 10 + 5)))).ToArray(), SpiCompletion.ChipSelectReleased)
        };
        capture = capture with { Packets = [packet] }; var model = new Workbench(); model.Load(capture); model.SelectProtocol(packet, 99);
        var view = new SerialWordsViewModel(model, packet);
        bool selected = view.Rows.Count == 32 && view.Rows[3].Selected && view.Rows[3].Value == "0x00000000000000000000000000000001";
        view.NextCommand.Execute(null); view.Rows[1].SelectCommand.Execute(null);
        bool next = model.SelectedProtocolIndex == 129 && view.CopyText.Contains("MOSI · 0x" + Wide, StringComparison.Ordinal);
        model.Load(Capture()); view.Rows[0].SelectCommand.Execute(null);
        return selected && next && model.SelectedProtocol == null && !view.IsCurrent;
    }
    private static bool WideWordsRejectTruncationAndUnusedHighBits()
    {
        var doc = Document(); var packet = doc.Capture.Packets[1];
        foreach (var word in new[] { new SerialWord(9, "A5", new(250, 268)), new(9, "3A5", new(250, 268)), new(9, "1a5", new(250, 268)), new(8, "A5", new(250, 268)), new(9, "1A5", new(280, 285)) })
            if (!Rejected(doc with { Capture = doc.Capture with { Packets = [doc.Capture.Packets[0], packet with { Uart = packet.Uart! with { Data = word } }] } })) return false;
        return true;
    }
    private static bool SerialEvidenceRejectsContradictoryDirectionsAndSpans()
    {
        var doc = Document(); var packet = doc.Capture.Packets[0]; var pair = packet.Spi!.Words[0];
        var invalid = new[] { packet with { Bytes = [0x12] }, packet with { Spi = packet.Spi with { Completion = SpiCompletion.WithoutChipSelect } },
            packet with { Spi = packet.Spi with { Words = [pair with { Miso = null }] } },
            packet with { Spi = packet.Spi with { Words = [pair with { Miso = pair.Miso! with { Span = new(11, 138) } }] } },
            packet with { Spi = packet.Spi with { Words = [pair, pair] } }, packet with { Uart = doc.Capture.Packets[1].Uart } };
        return invalid.All(p => Rejected(doc with { Capture = doc.Capture with { Packets = [p] } }));
    }
    private static bool UartDistinguishesIncompleteInvalidAndValidFrames()
    {
        var doc = Document(); var packet = doc.Capture.Packets[1];
        foreach (var frame in new[] { new UartFrame(null, UartCompletion.Incomplete, null), packet.Uart! with { Completion = UartCompletion.Incomplete, Valid = null }, packet.Uart! with { Valid = false }, packet.Uart! })
            AnalysisFile.Validate(doc with { Capture = doc.Capture with { Packets = [packet with { Uart = frame }] } });
        return new[] { packet.Uart! with { Completion = UartCompletion.Incomplete }, packet.Uart! with { Valid = null }, packet.Uart! with { Data = null } }
            .All(frame => Rejected(doc with { Capture = doc.Capture with { Packets = [packet with { Uart = frame }] } }));
    }
    private static bool UnsupportedOptionsCannotBecomeEvidence()
    {
        var doc = Document(); var uart = doc.Capture.DecoderSources![1]; var spi = doc.Capture.DecoderSources[0];
        return new[] { uart with { Uart = uart.Uart! with { StopBits = 2 } }, uart with { Uart = null }, uart with { Uart = uart.Uart! with { BaudRate = 0 } }, uart with { Protocol = "other" } }
            .All(source => Rejected(doc with { Capture = doc.Capture with { DecoderSources = [spi, source] } }))
            && Rejected(doc with { Capture = doc.Capture with { DecoderSources = [spi with { Spi = spi.Spi! with { Mode = 4 } }, uart] } });
    }
    private static async Task<bool> LegacyV4KeepsUnknownOptionsAsync()
    {
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "old.dsview2");
        var capture = Capture() with { DecoderSources = DecoderSource.LegacyDefaults(), Packets = [] };
        await AnalysisFile.WriteAsync(path, Document(capture)); var json = JsonNode.Parse(files.Read("old.dsview2"))!;
        json["version"] = 4;
        foreach (var source in json["capture"]!["decoderSources"]!.AsArray())
            foreach (string field in new[] { "protocol", "spi", "uart" }) source!.AsObject().Remove(field);
        files.Write("old.dsview2", json.ToJsonString()); var old = await AnalysisFile.ReadAsync(path);
        await AnalysisFile.WriteAsync(path, old); var loaded = await AnalysisFile.ReadAsync(path);
        return old.Version == 4 && loaded.Version == 5 && loaded.Capture.DecoderSources!.All(s => s.Protocol != null && s.Spi == null && s.Uart == null);
    }
    private static async Task<bool> SerialJsonRejectsDuplicateUnknownAndMissingFieldsAsync()
    {
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "bad.dsview2");
        await AnalysisFile.WriteAsync(path, Document()); string json = files.Read("bad.dsview2");
        foreach (var (original, replacement) in new[] { ("\"version\":5", "\"version\":5,\"version\":5"), ("\"mode\":3", "\"mode\":3,\"mode\":0"),
            ("\"bitWidth\":128", "\"bitWidth\":128,\"bitWidth\":8"), ("\"mode\":3", "\"mode\":3,\"extra\":1"), ("\"mode\":3,", "") })
        {
            if (!json.Contains(original, StringComparison.Ordinal)) throw new Exception("Fixture mutation did not match");
            files.Write("bad.dsview2", json.Replace(original, replacement, StringComparison.Ordinal)); if (!await RejectedRead(path)) return false;
        }
        var node = JsonNode.Parse(json)!; node["capture"]!["decoderSources"]![0] = null;
        files.Write("bad.dsview2", node.ToJsonString()); return await RejectedRead(path);
    }
    private static async Task<bool> NewMetadataCannotBeSmuggledIntoV4Async()
    {
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "bad.dsview2");
        await AnalysisFile.WriteAsync(path, Document()); var json = JsonNode.Parse(files.Read("bad.dsview2"))!; json["version"] = 4;
        files.Write("bad.dsview2", json.ToJsonString()); return await RejectedRead(path);
    }
    private static async Task<bool> AbsentInstanceMembershipSurvivesSaveWithoutInventingTracksAsync()
    {
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "group.dsview2");
        var model = new Workbench(); model.Load(Capture() with { Packets = [], DecoderSources = [] });
        var group = GroupEditState.Restore([new("Saved bus", [0, 3, 7, 8], ["sensor"])]).Single();
        model.Groups.Add(group); model.SetGroup(group);
        var doc = new AnalysisDocument(AnalysisDocument.FileFormat, 5, "New raw capture", model.Capture!, model.SnapshotState(new(null, 0), null));
        await AnalysisFile.WriteAsync(path, doc); model.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
        return model.Group.Decoders.Single() == "sensor" && model.Decoders.Count == 0 && model.DecoderLanes.Count == 0
            && !model.Packets.Any() && model.Signals.Count == 4;
    }
    private static async Task<bool> IndependentI2cInstanceKeepsByteIdentityAsync()
    {
        var board = await DemoCaptureSource.LoadAsync(); var packet = board.Packets.Single(p => p.Id == "ctl-2") with { Decoder = "aux-i2c" };
        var capture = board with { Packets = [packet], DecoderSources = [new("aux-i2c", [new("scl", 0), new("sda", 1)]) { Protocol = "i2c" }] };
        var model = new Workbench(); model.Load(capture); model.ShowAll();
        var items = capture.ProtocolItems(packet); int index = items.ToList().FindIndex(i => i.DataIndex == 3);
        model.SelectProtocol(packet, index);
        AnalysisFile.Validate(new(AnalysisDocument.FileFormat, 5, "Second I2C instance", capture, model.SnapshotState(new(null, 0), null)));
        return model.SelectedProtocol?.DataIndex == 3 && model.SelectedProtocol.Label == "0x35" && model.Decoders.Single().Id == "aux-i2c";
    }
}
