// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class DecoderSourceChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(ApplyPreservesTimingAndRejectsRepeatedSource(), nameof(ApplyPreservesTimingAndRejectsRepeatedSource));
        check(PendingMeasurementPreventsResultReplacement(), nameof(PendingMeasurementPreventsResultReplacement));
        check(RedecodeClearsOldPacketSelectionEvenWhenIdsRepeat(), nameof(RedecodeClearsOldPacketSelectionEvenWhenIdsRepeat));
        check(UnrelatedDiagnosticSelectionUsesCurrentIssueGraph(), nameof(UnrelatedDiagnosticSelectionUsesCurrentIssueGraph));
        check(LocateUsesMappedSourcesWithoutRewritingPriorGroup(), nameof(LocateUsesMappedSourcesWithoutRewritingPriorGroup));
        check(GroupEditorUsesActualRoles(), nameof(GroupEditorUsesActualRoles));
        check(await MappedAnalysisRoundTripsAsync(), nameof(MappedAnalysisRoundTripsAsync));
        check(await LegacyVersionThreeUpgradesWithExplicitDefaultsAsync(), nameof(LegacyVersionThreeUpgradesWithExplicitDefaultsAsync));
        check(await MissingOrNullV4SourcesAreRejectedAsync(), nameof(MissingOrNullV4SourcesAreRejectedAsync));
        check(await DuplicateAndUnknownRoleFieldsAreRejectedAsync(), nameof(DuplicateAndUnknownRoleFieldsAreRejectedAsync));
        check(InvalidRoleSetsCannotBecomeSavedEvidence(), nameof(InvalidRoleSetsCannotBecomeSavedEvidence));
    }
    private static Capture Raw() => new(1_000_000, 200,
        [new(0, "Unrelated", false, []), new(3, "SDA", true, [new(20, false), new(40, true)]),
         new(19, "SCL", false, [new(10, true), new(30, false), new(60, true)])], []) { SourceDescription = "Raw source provenance." };
    private static Packet Packet() => new("mapped-1", "i2c", new(15, 40), [], "Address unknown", "Prefix", null, null, [])
    { I2c = new(false, []) { AddressObserved = false, Completion = I2cCompletion.CaptureEnded }, Annotations = [new("START", new(15, 15), "point")] };
    private static I2cDecodeResult Result(Capture raw) => new(raw, new(19, 3), [Packet()]);
    private static Workbench Model(Capture capture)
    {
        var model = new Workbench(); model.Load(capture); model.ShowAll();
        model.SetMode(2); model.Pick(3, 42, 0); model.Pick(19, 70, 0); model.KeepMeasurement(); return model;
    }
    private static AnalysisDocument Document(bool mapped = true)
    {
        var raw = Raw(); var capture = mapped ? Result(raw).ToCapture() : raw;
        var model = Model(capture);
        return new(AnalysisDocument.FileFormat, 4, "Mapped capture", capture, model.SnapshotState(new(null, 0), 0));
    }
    private static bool ApplyPreservesTimingAndRejectsRepeatedSource()
    {
        var raw = Raw(); var model = Model(raw); model.Search("unmatched");
        var pair = (model.A, model.B); var kept = model.Measurements.Single();
        bool applied = model.ApplyI2cResult(Result(raw)); var accepted = model.Capture;
        return applied && !model.ApplyI2cResult(Result(raw)) && ReferenceEquals(accepted, model.Capture)
            && (model.A, model.B) == pair && model.Measurements.Single() == kept && model.Query == "unmatched"
            && model.Start == 0 && model.End == 200 && model.Decoders.Single().Channels.SequenceEqual(new[] { 19, 3 })
            && model.Capture!.SourceDescription == raw.SourceDescription;
    }
    private static bool PendingMeasurementPreventsResultReplacement()
    {
        var raw = Raw(); var model = Model(raw); model.Arm(0);
        return !model.ApplyI2cResult(Result(raw)) && ReferenceEquals(model.Capture, raw) && model.Armed == 0;
    }
    private static bool LocateUsesMappedSourcesWithoutRewritingPriorGroup()
    {
        var model = Model(Result(Raw()).ToCapture()); var group = model.Groups[0]; model.SetGroup(group);
        var before = group.Channels.ToArray(); var packet = model.Capture!.Packets[0]; model.Select(packet);
        bool outside = !model.SelectionInGroup && model.Decoders.Count == 0;
        int? focus = null; model.SignalFocusRequested += channel => focus = channel; model.Locate();
        return outside && focus == 19 && model.Group.Channels.SequenceEqual(new[] { 19, 3 })
            && group.Channels.SequenceEqual(before) && model.Decoders.Single().Id == "i2c"
            && model.A?.Channel == 3 && model.B?.Channel == 19;
    }
    private static bool RedecodeClearsOldPacketSelectionEvenWhenIdsRepeat()
    {
        var original = Packet(); var raw = Raw() with { Packets = [original] }; var model = Model(raw);
        model.SelectProtocol(original, 0); bool applied = model.ApplyI2cResult(Result(raw));
        return applied && model.Capture!.Packets.Single().Id == original.Id && model.Selected == null
            && model.SelectedProtocol == null && model.SelectedProtocolIndex == null && !model.Details;
    }
    private static bool UnrelatedDiagnosticSelectionUsesCurrentIssueGraph()
    {
        var other = new Packet("spi-kept", "spi", new(5, 9), [0x42], "", "SPI", "Fault", null, [])
        { Diagnostics = [new("spi-fault", "SPI", "fault", "Fixture diagnostic", [new(5, 9)])] };
        var raw = Raw() with { Packets = [other] }; var model = Model(raw); model.SelectIssue(model.Issues.Single());
        var oldIssue = model.SelectedIssue; bool applied = model.ApplyI2cResult(Result(raw));
        return applied && ReferenceEquals(model.Selected, other) && model.Details && model.SelectedIssue != null
            && !ReferenceEquals(oldIssue, model.SelectedIssue) && model.Issues.Any(i => ReferenceEquals(i, model.SelectedIssue));
    }
    private static bool GroupEditorUsesActualRoles()
    {
        var draft = new GroupEditState(Result(Raw()).ToCapture(), null); draft.SetDecoder("i2c", true);
        var restored = GroupEditState.Restore([new("Mapped", [3, 19], ["i2c"])]).Single();
        return draft.Channels.SetEquals([3, 19]) && !draft.Channels.Contains(0) && restored.Channels.SequenceEqual(new[] { 3, 19 });
    }
    private static async Task<bool> MappedAnalysisRoundTripsAsync()
    {
        using var workspace = new TestWorkspace(); string path = Path.Combine(workspace.Root, "mapped.dsview2");
        var document = Document(); await AnalysisFile.WriteAsync(path, document); var loaded = await AnalysisFile.ReadAsync(path);
        var model = new Workbench(); model.RestoreAnalysis(loaded); model.Select(loaded.Capture.Packets[0]); model.Locate();
        return loaded.Version == AnalysisDocument.CurrentVersion && loaded.Capture.ResolvedDecoders.Single(d => d.Id == "i2c").Channels.SequenceEqual(new[] { 19, 3 })
            && loaded.State.A == document.State.A && loaded.State.B == document.State.B && model.Measurements.Count == 1
            && loaded.Capture.Signals.Select(s => s.Id).SequenceEqual(new[] { 0, 3, 19 }) && loaded.Capture.SourceDescription == document.Capture.SourceDescription;
    }
    private static async Task<bool> LegacyVersionThreeUpgradesWithExplicitDefaultsAsync()
    {
        using var workspace = new TestWorkspace(); string path = Path.Combine(workspace.Root, "legacy.dsview2");
        await AnalysisFile.WriteAsync(path, Document(false)); var json = JsonNode.Parse(workspace.Read("legacy.dsview2"))!;
        json["version"] = 3; json["capture"]!.AsObject().Remove("decoderSources"); workspace.Write("legacy.dsview2", json.ToJsonString());
        var old = await AnalysisFile.ReadAsync(path); await AnalysisFile.WriteAsync(path, old); var current = await AnalysisFile.ReadAsync(path);
        return old.Version == 3 && old.Capture.DecoderSources == null && current.Version == AnalysisDocument.CurrentVersion
            && current.Capture.DecoderSources!.Single(s => s.Id == "i2c").Inputs.Single(i => i.Role == "scl").Channel == 0
            && current.State.Measurements!.Length == 1 && current.State.A == old.State.A;
    }
    private static async Task<bool> MissingOrNullV4SourcesAreRejectedAsync()
    {
        using var workspace = new TestWorkspace(); string path = Path.Combine(workspace.Root, "bad.dsview2");
        await AnalysisFile.WriteAsync(path, Document()); var json = JsonNode.Parse(workspace.Read("bad.dsview2"))!;
        foreach (bool missing in new[] { true, false })
        {
            var bad = json.DeepClone(); if (missing) bad["capture"]!.AsObject().Remove("decoderSources"); else bad["capture"]!["decoderSources"] = null;
            workspace.Write("bad.dsview2", bad.ToJsonString());
            if (!await RejectedRead(path)) return false;
        }
        return true;
    }
    private static async Task<bool> DuplicateAndUnknownRoleFieldsAreRejectedAsync()
    {
        using var workspace = new TestWorkspace(); string path = Path.Combine(workspace.Root, "bad.dsview2");
        await AnalysisFile.WriteAsync(path, Document()); string json = workspace.Read("bad.dsview2");
        foreach (string replacement in new[] { "\"role\":\"scl\",\"role\":\"sda\"", "\"role\":\"scl\",\"extra\":0" })
        {
            workspace.Write("bad.dsview2", json.Replace("\"role\":\"scl\"", replacement, StringComparison.Ordinal));
            if (!await RejectedRead(path)) return false;
        }
        return true;
    }
    private static bool InvalidRoleSetsCannotBecomeSavedEvidence()
    {
        var doc = Document();
        foreach (var inputs in new DecoderInput[][] { [new("scl", 19)], [new("scl", 19), new("scl", 3)], [new("scl", 3), new("sda", 3)], [new("scl", 32), new("sda", 3)] })
        {
            try { AnalysisFile.Validate(doc with { Capture = doc.Capture with { DecoderSources = [new("i2c", inputs)] } }); return false; }
            catch (InvalidDataException) { }
        }
        try { AnalysisFile.Validate(doc with { Version = 3 }); return false; } catch (InvalidDataException) { return true; }
    }
    private static async Task<bool> RejectedRead(string path)
    {
        try { await AnalysisFile.ReadAsync(path); return false; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return true; }
    }
}
