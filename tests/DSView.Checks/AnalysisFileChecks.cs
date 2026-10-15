// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class AnalysisFileChecks
{
    public static async Task RunAsync(Action<bool, string> check, Capture board, Capture dense)
    {
        string directory = Path.Combine(Path.GetTempPath(), "dsview2-analysis-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string file = Path.Combine(directory, "roundtrip.dsview2");
        try
        {
            var model = new Workbench(); model.Load(board); model.SetRange(24_000, 40_000);
            model.SetMode(1); model.Pick(0, 25_000, 1); model.Pick(8, 38_000, 1);
            model.EditEndpointTime(1, 760.02m); model.Colors[0] = "#FFFF00";
            var issue = model.Issues.First(i => i.Packet.Id == "ctl-2"); model.SelectIssue(issue);
            model.SetTab(true); model.Search("unmatched query");
            var state = model.SnapshotState(new(0, 10), 0);
            var document = new AnalysisDocument(AnalysisDocument.FileFormat, 1, "Board bring-up", board, state);
            await AnalysisFile.WriteAsync(file, document); var reopened = await AnalysisFile.ReadAsync(file);
            var restored = new Workbench(); restored.RestoreAnalysis(reopened);
            check(AnalysisFile.StateKey(restored.SnapshotState(state.Vertical, state.EdgeChannel)) == AnalysisFile.StateKey(state)
                && restored.A is { Channel: 0, Sample: 25_000, IsEdge: true } && restored.B is { Channel: 8, Sample: 38_001, IsEdge: false }, "Analysis round trip retains signed cross-channel edge/position timing, Group, colors, query, tab and viewport");
            check(ReferenceEquals(restored.Selected, reopened.Capture.Packets.Single(p => p.Id == "ctl-2")) && restored.SelectedIssue != null
                && restored.Issues.Contains(restored.SelectedIssue) && ReferenceEquals(restored.SelectedIssue.Packet, restored.Selected) && restored.Details,
                "Restored packet and diagnostic selection bind to the loaded capture graph, even outside search results");
            check(reopened.Capture.ProtocolItems(restored.Selected!).Select(i => (i.Label, i.Span, i.Kind)).SequenceEqual(board.ProtocolItems(issue.Packet).Select(i => (i.Label, i.Span, i.Kind)))
                && reopened.Capture.Packets.Single(p => p.Id == "ctl-2").Fields.SelectMany(f => f.Raw).SequenceEqual(issue.Packet.Fields.SelectMany(f => f.Raw)), "Saved decoded bytes, fields and separate protocol events retain their exact source spans");

            restored.SetTab(false); restored.Clear(); restored.Pick(0, 25_025, 1);
            var local = document with { Version = AnalysisDocument.CurrentVersion, Capture = reopened.Capture, State = restored.SnapshotState(new(null, 0), 0) };
            await AnalysisFile.WriteAsync(file, local); restored.RestoreAnalysis(await AnalysisFile.ReadAsync(file));
            check(restored.PinnedMeasurement is { WidthSamples: 63, PeriodSamples: 125, HighSamples: 62 } && TimingText.Local(restored.PinnedMeasurement, restored.Capture!.SampleRate).Contains("400 kHz"), "Restoring a local pulse recomputes width and frequency from saved raw samples");
            restored.Arm(1);
            check(restored.HasPendingEdit && restored.SnapshotState(new(null, 0), 0).B == local.State.B, "An unfinished measurement remains a draft and the committed snapshot remains identifiable");
            restored.CancelPick();

            var before = restored.Capture; var beforeState = AnalysisFile.StateKey(restored.SnapshotState(new(null, 0), 0));
            bool rejected = false;
            try { restored.RestoreAnalysis(local with { State = local.State with { B = local.State.B! with { Sample = 25_064 } } }); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && ReferenceEquals(before, restored.Capture) && beforeState == AnalysisFile.StateKey(restored.SnapshotState(new(null, 0), 0)), "Invalid local evidence is rejected before any current model state changes");

            var denseModel = new Workbench(); denseModel.Load(dense); denseModel.ShowAll(); denseModel.SetRange(2_036_700, 2_036_900);
            denseModel.SetMode(1); denseModel.Pick(0, 2_036_800, 0); denseModel.Pick(31, 2_036_857, 0);
            var denseDoc = new AnalysisDocument(AnalysisDocument.FileFormat, 1, "Dense signals", dense, denseModel.SnapshotState(new(21, 37), 0));
            await AnalysisFile.WriteAsync(file, denseDoc); var denseRoundtrip = await AnalysisFile.ReadAsync(file);
            check(dense.Signals.Zip(denseRoundtrip.Capture.Signals).All(p => p.First.Id == p.Second.Id && p.First.Edges.SequenceEqual(p.Second.Edges))
                && denseRoundtrip.State.A == denseDoc.State.A && denseRoundtrip.State.B == denseDoc.State.B && denseRoundtrip.State.Vertical == denseDoc.State.Vertical,
                "All 1,048,576 Dense transitions and far-channel A/B plus vertical anchor survive a self-contained file round trip");

            var completeGaps = await DemoCaptureSource.LoadAsync("timing-gaps");
            var gaps = completeGaps with { Signals = completeGaps.Signals.Where(s => s.Id is 0 or 1).ToArray() };
            var gapModel = new Workbench(); gapModel.Load(gaps);
            gapModel.SetGroup(gapModel.Groups.Single(g => g.Name == "UART + SPI"));
            var gapDoc = new AnalysisDocument(AnalysisDocument.FileFormat, 1, "Timing + gaps", gaps, gapModel.SnapshotState(new(null, 0), null));
            await AnalysisFile.WriteAsync(file, gapDoc); var gapRoundtrip = await AnalysisFile.ReadAsync(file); restored.RestoreAnalysis(gapRoundtrip);
            check(restored.Capture!.Gaps.SequenceEqual(gaps.Gaps) && restored.Group.Name == "UART + SPI" && restored.Signals.Count == 0, "Saved missing coverage and an intentionally empty active Group are restored without substituting a different scope");

            var eof = new Capture(1000, 100, [new Signal(0, "SCL", false, []), new Signal(1, "SDA", true, [])],
                [new Packet("eof", "i2c", new(90, 100), [], "Address unknown", "Incomplete address", "Capture ended", null, [])
                { I2c = new(false, []) { AddressObserved = false, Completion = I2cCompletion.CaptureEnded },
                    Annotations = [new("START", new(90, 90), "point"), new("partial", new(95, 110), "data")] }]);
            var eofModel = new Workbench(); eofModel.Load(eof);
            var eofDoc = new AnalysisDocument(AnalysisDocument.FileFormat, 1, "Saved native EOF", eof, eofModel.SnapshotState(new(null, 0), 0));
            await AnalysisFile.WriteAsync(file, eofDoc); var eofRoundtrip = await AnalysisFile.ReadAsync(file);
            var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            check(json["capture"]!["issues"] == null && json["capture"]!["packets"]![0]!["hex"] == null, "Saved file excludes derived issue graphs and formatted byte copies");
            var missingCoverage = json.DeepClone(); missingCoverage["capture"]!.AsObject().Remove("gaps");
            await File.WriteAllTextAsync(file, missingCoverage.ToJsonString()); bool missingRejected = false;
            try { await AnalysisFile.ReadAsync(file); } catch (System.Text.Json.JsonException) { missingRejected = true; }
            check(missingRejected, "Missing required coverage metadata is rejected instead of silently declaring complete samples");
            var missingLevel = json.DeepClone(); missingLevel["capture"]!["signals"]![1]!.AsObject().Remove("initial");
            await File.WriteAllTextAsync(file, missingLevel.ToJsonString()); bool levelRejected = false;
            try { await AnalysisFile.ReadAsync(file); } catch (System.Text.Json.JsonException) { levelRejected = true; }
            check(levelRejected, "Missing initial level is rejected instead of turning a constant-high signal low");
            await AnalysisFile.WriteAsync(file, eofDoc);
            check(eofRoundtrip.Capture.Packets[0].I2c is { AddressObserved: false, Completion: I2cCompletion.CaptureEnded }
                && eofRoundtrip.Capture.Packets[0].Annotations is [ { Label: "START" }, { Span.End: 110 } ] && eofRoundtrip.Capture.SampleCount == 100,
                "Saved incomplete decode preserves point-only START and annotation extent beyond EOF without extending raw coverage or running a decoder");

            var saved = await File.ReadAllBytesAsync(file);
            bool badVersion = false; try { await AnalysisFile.WriteAsync(file, eofDoc with { Version = 99 }); } catch (InvalidDataException) { badVersion = true; }
            check(badVersion && Enumerable.SequenceEqual(saved, await File.ReadAllBytesAsync(file)), "Unsupported version cannot overwrite a previous valid analysis");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool cancelledWrite = false;
            try { await AnalysisFile.WriteAsync(file, document, cancelled.Token); } catch (OperationCanceledException) { cancelledWrite = true; }
            check(cancelledWrite && Enumerable.SequenceEqual(saved, await File.ReadAllBytesAsync(file)) && Directory.GetFiles(directory, "*.tmp").Length == 0, "Cancelled save preserves the previous file and leaves no temporary output");
            if (OperatingSystem.IsWindows())
            {
                bool locked = false;
                using (var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    try { await AnalysisFile.WriteAsync(file, document); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { locked = true; }
                check(locked && Enumerable.SequenceEqual(saved, await File.ReadAllBytesAsync(file)) && Directory.GetFiles(directory, "*.tmp").Length == 0, "Failed Windows destination replacement preserves the old analysis and removes only the new temporary file");
            }
            bool invalid = false; await File.WriteAllTextAsync(file, "{\"format\":\"DSView2 analysis\"");
            try { await AnalysisFile.ReadAsync(file); } catch (System.Text.Json.JsonException) { invalid = true; }
            check(invalid, "Truncated analysis is rejected instead of partially loading data");
            using (var oversized = new FileStream(file, FileMode.Create)) oversized.SetLength(AnalysisFile.MaximumBytes + 1);
            bool bounded = false; try { await AnalysisFile.ReadAsync(file); } catch (InvalidDataException) { bounded = true; }
            check(bounded, "Oversized analysis is rejected before deserialization");

            var corruptions = new[]
            {
                document with { Capture = board with { Signals = [board.Signals[0], board.Signals[0]] } },
                document with { Capture = board with { Signals = [board.Signals[0] with { Edges = [new(-1, true)] }] } },
                document with { State = state with { Start = double.NaN } },
                document with { State = state with { SelectedPacket = "missing" } },
                document with { State = state with { Colors = new() { [0] = "not a color" } } },
                document with { State = state with { Groups = [state.Groups[0], state.Groups[0]] } },
                document with { State = state with { A = state.A! with { Sample = 25_001 } } }
            };
            int rejectedCount = 0; foreach (var corrupt in corruptions) try { AnalysisFile.Validate(corrupt); } catch (InvalidDataException) { rejectedCount++; }
            check(rejectedCount == corruptions.Length, "Invalid identities, transitions, viewport, selection, colors, Groups and edge claims are rejected");
        }
        finally { foreach (var path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }
}
