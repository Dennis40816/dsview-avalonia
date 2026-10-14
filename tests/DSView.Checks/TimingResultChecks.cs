// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Nodes;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class TimingResultChecks
{
    public static async Task RunAsync(Action<bool, string> check, Capture board)
    {
        var model = new Workbench(); model.Load(board);
        check(model.KeepMeasurement() == null && model.Measurements.Count == 0, "An incomplete working pair cannot create a timing result");
        model.Pick(0, 25_025, 1); var pulse = model.KeepMeasurement()!;
        check(pulse.LocalPulse && pulse.A is { Sample: 25_000, IsEdge: true } && pulse.B.Sample == 25_063 && model.PinnedMeasurement?.PeriodSamples == 125,
            "Keep copies exact local pulse sources; period remains derived from the observed cycle");
        var snapshot = model.SnapshotState(new(null, 0), 0);
        model.EditMeasurement(pulse.Id, "Clock low", "At the command boundary\nExpected clock");
        check(snapshot.Measurements![0].Name != "Clock low" && model.Measurements[0].Name == "Clock low" && model.Measurements[0].Id == pulse.Id,
            "Renaming and notes replace immutable metadata without changing an in-flight snapshot or stable identity");
        model.Clear();
        check(model.A == null && model.B == null && model.Measurements.Count == 1, "Clear removes only the working measurement");
        model.SetMode(1); model.Pick(0, 25_000, 1);
        check(model.KeepMeasurement() == null && !model.ShowMeasurement(pulse.Id) && model.HasPendingEdit, "Keep and Show refuse to silently commit or overwrite a pending pair");
        model.Pick(8, 38_000, 1); var delay = model.KeepMeasurement()!;
        model.EditMeasurement(delay.Id, "Reset delay", "SCL to RESET_N");
        check(!delay.LocalPulse && delay.A.Channel == 0 && delay.B.Channel == 8 && delay.B.Sample - delay.A.Sample == 13_000, "Cross-channel kept intervals preserve chosen sources and signed samples without a frequency claim");
        var packet = board.Packets.Single(p => p.Id == "ctl-2"); model.SelectProtocol(packet, 9); model.SetGroup(model.Groups.Single(g => g.Name == "UART + SPI")); model.SetTab(true);
        check(model.ShowMeasurement(pulse.Id) && !model.PacketsTab && model.Group.Channels.Contains(0) && model.Start <= pulse.A.Sample && model.End >= pulse.B.Sample
            && model.SelectedProtocolIndex == 9 && model.Selected?.Id == packet.Id && model.PinnedMeasurement?.WidthSamples == 63,
            "Show restores a working copy, fits its source across Groups/tabs and preserves the selected protocol element");
        model.EditEndpointTime(1, 510m); model.Clear(); model.ShowMeasurement(pulse.Id);
        check(model.B == pulse.B && model.Measurements.Count == 2 && model.PinnedMeasurement != null, "Editing and clearing a recalled working copy never changes kept evidence");
        model.BeginDrag(); check(!model.ShowMeasurement(delay.Id) && model.KeepMeasurement() == null, "A pending drag blocks kept-result replacement"); model.CancelPick();
        var beforeInvalid = model.Measurements[0];
        check(!model.EditMeasurement(pulse.Id, " ", "") && !model.EditMeasurement(pulse.Id, "x\ny", "") && !model.EditMeasurement(pulse.Id, "x", new('x', 2049)) && model.Measurements[0] == beforeInvalid,
            "Invalid or excessive metadata is rejected without mutating a kept result");
        model.RemoveMeasurement(pulse.Id); var restored = model.UndoMeasurementRemoval();
        check(restored?.Id == pulse.Id && model.Measurements[0] == beforeInvalid && !model.CanUndoMeasurementRemoval, "Undo restores removed identity, note, exact sources and order");
        model.Clear(); model.SetMode(2); model.Pick(0, 31_500, 0); model.Pick(8, 25_000, 0); var reverse = model.KeepMeasurement()!;
        model.EditEndpointTime(1, 630m); var coincident = model.KeepMeasurement()!;
        check(reverse.B.Sample < reverse.A.Sample && !reverse.A.IsEdge && coincident.A.Sample == coincident.B.Sample && !coincident.LocalPulse,
            "Reversed and coincident position intervals remain valid time results");

        var finite = new Capture(1000, 100, [new Signal(0, "Clock", false, [new(10, true), new(20, false)])], []);
        var end = new Workbench(); end.Load(finite); end.Pick(0, 15, 0); var finiteResult = end.KeepMeasurement()!; end.Clear(); end.ShowMeasurement(finiteResult.Id);
        check(end.PinnedMeasurement is { WidthSamples: 10, Cycle: null } && TimingText.Local(end.PinnedMeasurement, 1000).Contains("T/f unavailable"), "A complete pulse without a complete cycle remains keepable without inventing frequency");
        var gap = finite with { Gaps = [new(30, 40)] }; var missing = new Workbench(); missing.Load(gap); missing.SetMode(2); missing.Pick(0, 35, 0); missing.Pick(0, 50, 0);
        check(missing.KeepMeasurement() is { A.IsEdge: false, A.Sample: 35 }, "A chosen position inside missing coverage remains a time coordinate");
        var document = new AnalysisDocument(AnalysisDocument.FileFormat, 3, "Board bring-up", board, model.SnapshotState(new(null, 0), 0));
        var corrupt = new TimingResult[] {
            pulse with { Id = "not-an-id" }, pulse with { B = pulse.B with { Sample = pulse.B.Sample + 1 } },
            pulse with { B = pulse.B with { Channel = 8 } }, pulse with { A = pulse.A with { IsEdge = false } },
            reverse with { A = reverse.A with { Channel = 31, Sample = board.SampleCount } }, pulse with { Name = new('x', 81) }
        };
        int rejected = 0;
        foreach (var item in corrupt) try { AnalysisFile.Validate(document with { State = document.State with { Measurements = [item] } }); } catch (InvalidDataException) { rejected++; }
        check(rejected == corrupt.Length, "Forged local pulses, source bounds, identities and metadata fail validation before state replacement");
        bool duplicate = false; try { AnalysisFile.Validate(document with { State = document.State with { Measurements = [pulse, pulse] } }); } catch (InvalidDataException) { duplicate = true; }
        check(duplicate, "Duplicate timing result identities are rejected");
        var full = Enumerable.Range(0, TimingResult.MaximumCount).Select(i => pulse with { Id = Guid.NewGuid().ToString("N"), Name = "Result " + i }).ToArray();
        var bounded = new Workbench(); bounded.RestoreAnalysis(document with { State = document.State with { Measurements = full } });
        check(bounded.KeepMeasurement() == null && bounded.Measurements.Count == TimingResult.MaximumCount, "The retained-result limit rejects additions without replacing existing notes");
        bool excess = false; try { AnalysisFile.Validate(document with { State = document.State with { Measurements = [..full, pulse] } }); } catch (InvalidDataException) { excess = true; }
        check(excess, "A file exceeding the retained-result bound is rejected before loading");
        string path = Path.Combine(Path.GetTempPath(), "dsview2-results-" + Guid.NewGuid().ToString("N") + ".dsview2");
        try
        {
            await AnalysisFile.WriteAsync(path, document); var reopened = await AnalysisFile.ReadAsync(path); var loaded = new Workbench(); loaded.RestoreAnalysis(reopened);
            check(loaded.Measurements.SequenceEqual(model.Measurements) && reopened.Version == 3 && loaded.SelectedProtocolIndex == 9, "Version 3 round trip retains every result, note, source, kind and separate byte selection");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            foreach (int version in new[] { 1, 2 })
            {
                var legacy = json.DeepClone(); legacy["version"] = version; legacy["state"]!.AsObject().Remove("measurements");
                if (version == 1) legacy["state"]!.AsObject().Remove("selectedProtocolIndex");
                await File.WriteAllTextAsync(path, legacy.ToJsonString()); var old = await AnalysisFile.ReadAsync(path);
                await AnalysisFile.WriteAsync(path, old); var upgraded = await AnalysisFile.ReadAsync(path);
                check(old.State.Measurements is { Length: 0 } && upgraded.Version == 3 && upgraded.State.Measurements is { Length: 0 }, $"Version {version} without results migrates to empty and writes complete version 3 evidence");
            }
            foreach (bool absent in new[] { true, false })
            {
                var bad = json.DeepClone(); if (absent) bad["state"]!.AsObject().Remove("measurements"); else bad["state"]!["measurements"] = null;
                await File.WriteAllTextAsync(path, bad.ToJsonString()); bool failed = false;
                try { await AnalysisFile.ReadAsync(path); } catch (InvalidDataException) { failed = true; }
                check(failed, $"Version 3 {(absent ? "missing" : "null")} results are rejected instead of silently losing notes");
            }
            bool oldWithResults = false; try { AnalysisFile.Validate(document with { Version = 2 }); } catch (InvalidDataException) { oldWithResults = true; }
            check(oldWithResults, "A legacy version cannot silently carry new timing records");
            loaded.RemoveMeasurement(pulse.Id); loaded.Load(finite);
            check(loaded.Measurements.Count == 0 && !loaded.CanUndoMeasurementRemoval, "Replacing the capture clears its results and undo history rather than reusing foreign sample coordinates");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
