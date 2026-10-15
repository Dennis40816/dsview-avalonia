// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Nodes;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class ProtocolSelectionChecks
{
    public static async Task RunAsync(Action<bool, string> check, Capture board)
    {
        var packet = board.Packets.Single(p => p.Id == "ctl-2"); var items = board.ProtocolItems(packet);
        int index = items.Select((p, i) => (p, i)).Single(x => x.p.DataIndex == 3).i;
        var model = new Workbench(); model.Load(board); model.SetRange(24_000, 40_000);
        model.SetMode(1); model.Pick(0, 25_000, 1); model.Pick(8, 38_000, 1);
        var a = model.A; var b = model.B;
        model.SelectProtocol(packet, index);
        check(model.SelectedProtocol is { DataIndex: 3, Label: "0x35" } selected && selected.Span == packet.WireCells[4].Data
            && model.Start == 24_000 && model.End == 40_000 && model.A == a && model.B == b,
            "Byte selection owns its ordinal and exact annotation without changing viewport or cross-channel A/B");
        model.Locate();
        check(model.Start < items[index].Span.Start && model.End > items[index].Span.End && model.End - model.Start < packet.Span.End - packet.Span.Start
            && model.A == a && model.B == b, "Locate selected frames byte evidence and retains timing endpoints");
        model.Previous();
        check(model.Start == 24_000 && model.End == 40_000 && model.SelectedProtocolIndex == index, "Previous restores the earlier view without losing selected byte identity");
        model.SelectProtocol(packet, index + 1);
        check(model.SelectedProtocol is { Kind: "ack", DataIndex: null, Label: "ACK" } ack && ack.Span == packet.WireCells[4].Acknowledge,
            "ACK has its own span and never masquerades as its preceding data byte");
        model.SelectProtocol(packet, 0); check(model.SelectedProtocol is { Kind: "point", Label: "START" } point && point.Span.Start == point.Span.End,
            "START remains a point event, not an invented one-bit duration");
        model.SelectProtocol(packet, 1); check(model.SelectedProtocol is { IsAddress: true, DataIndex: null }, "Address is distinct from data byte 1");
        model.SelectProtocol(packet, index); model.SetGroup(model.Groups.Single(g => g.Name == "UART + SPI"));
        check(model.SelectedProtocolIndex == index && !model.Details && model.PacketSelectionContext.Contains("outside this Group"), "Hidden selected byte retains identity and reports its scope");
        model.Locate(); model.SelectIssue(model.Issues.First(i => i.Packet.Id == packet.Id));
        check(model.SelectedProtocol == null && model.SelectedProtocolIndex == null && model.SelectedIssue != null, "Issue selection clears competing byte selection");

        var repeated = packet with { Bytes = Enumerable.Repeat((byte)0x35, packet.Bytes.Length).ToArray() };
        var repeatedItems = board.ProtocolItems(repeated);
        var observed = repeated with { WireCells = [], Annotations = repeatedItems.Select(i => new ProtocolAnnotation(i.Label, i.Span, i.Kind)).ToArray() };
        var mapped = board.ProtocolItems(observed).Where(i => i.DataIndex != null).ToArray();
        check(mapped.Select(i => i.DataIndex).SequenceEqual(Enumerable.Range(0, repeated.Bytes.Length).Select(i => (int?)i))
            && mapped[0].Span != mapped[3].Span && mapped.All(i => i.Label == "0x35"), "Identical byte values map by ordered evidence rather than first text match");
        var ambiguous = observed with { Annotations = observed.Annotations!.Where((_, i) => i != 3).ToArray() };
        check(board.ProtocolItems(ambiguous).All(i => i.DataIndex == null && !i.IsAddress), "An incomplete annotation mapping never fabricates individual Hex sources");
        var reordered = observed with { Annotations = observed.Annotations!.Reverse().ToArray() };
        check(board.ProtocolItems(reordered).All(i => i.DataIndex == null) && board.ProtocolItems(repeated with { WireCells = repeated.WireCells.Reverse().ToArray() }).All(i => i.DataIndex == null),
            "Reordered equal-byte annotations and wire cells cannot assign misleading byte ordinals");
        var unspanned = packet with { WireCells = [], Annotations = null };
        var unknownModel = new Workbench(); unknownModel.Load(board with { Packets = [unspanned] }); unknownModel.Select(unspanned); unknownModel.SelectProtocol(unspanned, 0);
        check(unknownModel.SelectedProtocol == null && board.ProtocolItems(unspanned).Single().DataIndex == null, "Whole-packet summaries cannot be selected as exact byte evidence");

        string path = Path.Combine(Path.GetTempPath(), "dsview2-protocol-" + Guid.NewGuid().ToString("N") + ".dsview2");
        try
        {
            model.SelectProtocol(packet, index);
            var document = new AnalysisDocument(AnalysisDocument.FileFormat, AnalysisDocument.CurrentVersion, "Board bring-up", board, model.SnapshotState(new(null, 0), 0));
            await AnalysisFile.WriteAsync(path, document); var restored = new Workbench(); restored.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
            check(restored.SelectedProtocol is { DataIndex: 3, Label: "0x35" } saved && saved.Span == items[index].Span && restored.A == a && restored.B == b,
                "Current version reopens selected byte from the saved graph with unchanged A/B");
            var legacy = JsonNode.Parse(await File.ReadAllTextAsync(path))!; legacy["version"] = 1; legacy["state"]!.AsObject().Remove("selectedProtocolIndex");
            legacy["capture"]!.AsObject().Remove("decoderSources");
            await File.WriteAllTextAsync(path, legacy.ToJsonString()); var old = await AnalysisFile.ReadAsync(path); restored.RestoreAnalysis(old);
            check(restored.Selected?.Id == packet.Id && restored.SelectedProtocol == null && restored.A == a, "Version 1 files without element selection reopen at packet level");
            await AnalysisFile.WriteAsync(path, old);
            check((await AnalysisFile.ReadAsync(path)).Version == AnalysisDocument.CurrentVersion, "Saving a legacy document writes current version without re-decoding evidence");
            bool rejected = false;
            try { restored.RestoreAnalysis(document with { State = document.State with { SelectedProtocolIndex = int.MaxValue } }); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && restored.SelectedProtocol == null, "An invalid saved element index is rejected before replacing current state");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
