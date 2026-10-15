// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;

internal static class MeasurementRecoveryChecks
{
    public static void Run(Action<bool, string> check, Capture dense)
    {
        var last = new SignalRow(1640, 1692);
        var nearLast = new SignalRow(1588, 1640);
        var first = new SignalRow(28, 80);
        check(SignalViewport.RevealOffset([last], 0, 600, 1708) == 1092,
            "Fit reveals the complete D31 row from the top of a 32-channel view");
        check(SignalViewport.RevealOffset([nearLast, last], 0, 600, 1708) == 1092
            && SignalViewport.RevealOffset([last, nearLast], 0, 600, 1708) == 1092,
            "Nearby endpoint rows are revealed together regardless of A/B order");
        check(SignalViewport.RevealOffset([first, last], 500, 600, 1708) == 28
            && SignalViewport.RevealOffset([last, first], 500, 600, 1708) == 1092,
            "Widely separated endpoints reveal A without pretending both rows fit");
        check(SignalViewport.RevealOffset([nearLast, last], 1100, 600, 1708) == 1100
            && SignalViewport.RevealOffset([], 120, 600, 1708) == 120,
            "Visible source rows and absent requests do not move the vertical view");
        check(SignalViewport.Visibility(last, 0, 600) == RowVisibility.Below
            && SignalViewport.Visibility(first, 1092, 600) == RowVisibility.Above
            && SignalViewport.Visibility(last, 1092, 600) == RowVisibility.Visible
            && SignalViewport.Visibility(last, 1091, 600) == RowVisibility.Below,
            "Offscreen and clipped source rows have directional recovery state");
        check(SignalViewport.RevealOffset([last], 0, 300, 1708) == 1392
            && SignalViewport.RevealOffset([new(1680, 1732)], 0, 600, 1708) == 1108,
            "Reveal uses the resized viewport and clamps to the actual scroll extent");

        var vm = new Workbench(); vm.Load(dense); vm.ShowAll();
        var cycleView = new Workbench();
        cycleView.Load(new Capture(1_000_000, 1000,
            [new(0, "Data", false, []), new(1, "Clock", false, [new(10, true), new(20, false), new(500, true)]), new(8, "Control", false, [])], []));
        cycleView.ShowAll(); cycleView.Pick(1, 15, 0); cycleView.FitMarkers();
        check(cycleView.IsAllSignals && cycleView.A?.Sample == 10 && cycleView.B?.Sample == 20
            && cycleView.Start <= 10 && cycleView.End >= 500 && cycleView.PinnedMeasurement?.PeriodSamples == 490,
            "Local Fit keeps an already suitable Group and includes the complete asymmetric cycle without moving pulse markers");
        var rawGroup = cycleView.Groups[0]; cycleView.SetGroup(rawGroup);
        check(cycleView.Decoders.Count == 0 && rawGroup.Decoders.Contains("i2c"),
            "A raw capture hides empty decoder rows without erasing reusable Group configuration");
        cycleView.Load(new Capture(1_000_000, 1000, [new(1, "Finite pulse", false, [new(10, true), new(20, false)])], []));
        cycleView.Pick(1, 15, 0); cycleView.FitMarkers();
        check(cycleView.PinnedMeasurement?.Cycle == null && cycleView.Start == 5 && cycleView.End == 25,
            "Incomplete-cycle Fit frames only observed pulse evidence without fabricating a period");
        int[] requested = []; vm.MeasurementFocusRequested += channels => requested = channels.ToArray();
        vm.Pick(31, 1_004_200, 0);
        check(requested.SequenceEqual(new[] { 31 }) && vm.Start == 0 && vm.End == dense.SampleCount,
            "Pinning a local pulse keeps its source row visible without changing the time range");
        var origin = (vm.A, vm.B, vm.PinnedMeasurement); var groups = vm.Groups.ToArray();
        vm.FitMarkers();
        check(requested.SequenceEqual(new[] { 31 }) && (vm.A, vm.B, vm.PinnedMeasurement) == origin
            && vm.IsAllSignals && vm.Groups.SequenceEqual(groups)
            && vm.Start <= vm.A!.Sample && vm.End >= vm.B!.Sample,
            "Fitting a D31 pulse requests its row while preserving measurement and saved Groups");

        vm.Clear(); vm.SetRange(0, dense.SampleCount); vm.SetMode(2); vm.Pick(0, 1000, 0); vm.Pick(31, 4000, 0);
        var pair = (vm.A, vm.B, vm.Mode); vm.FitMarkers();
        check(requested.SequenceEqual(new[] { 0, 31 }) && (vm.A, vm.B, vm.Mode) == pair
            && vm.IsAllSignals && vm.Groups.SequenceEqual(groups) && vm.Start == 550 && vm.End == 4450,
            "Cross-channel Fit requests both sources in endpoint order without creating a Group");
        vm.ShowEndpoint(1); vm.Previous();
        check(vm.Start == 0 && vm.End == dense.SampleCount && (vm.A, vm.B, vm.Mode) == pair,
            "Vertical-only endpoint recovery does not insert a duplicate time/Group history entry");
        vm.SetRange(0, 100); int? revealed = null; vm.SignalFocusRequested += channel => revealed = channel;
        vm.ShowEndpoint(1);
        check(revealed == 31 && (vm.A, vm.B, vm.Mode) == pair && vm.Start == 3950 && vm.End == 4050,
            "Show B reveals its source and pans only when needed without changing either endpoint");
        vm.Previous();
        check(vm.Start == 0 && vm.End == 100 && (vm.A, vm.B, vm.Mode) == pair,
            "Previous restores the time range preceding explicit endpoint recovery");
        vm.SetRange(900, 4100); vm.ShowEndpoint(0);
        check(revealed == 0 && vm.Start == 900 && vm.End == 4100 && (vm.A, vm.B, vm.Mode) == pair,
            "Show A preserves an already suitable time range and both endpoint identities");
        vm.SetGroup(vm.Groups[0]); vm.ShowEndpoint(1);
        check(vm.IsAllSignals && vm.Start == 900 && vm.End == 4100 && revealed == 31
            && (vm.A, vm.B, vm.Mode) == pair && vm.Groups.SequenceEqual(groups),
            "A hidden endpoint is recovered through an existing Group without rewriting saved membership");
        vm.Clear(); requested = []; revealed = null; vm.FitMarkers(); vm.ShowEndpoint(0);
        check(requested.Length == 0 && revealed == null, "Empty measurement recovery does not navigate or request a source row");
        vm.FocusProbe();
        check(revealed == vm.Probe?.Channel && revealed != null && vm.A == null && vm.B == null,
            "Entering keyboard picking reveals the probe's source without creating a measurement");
        var events = new List<string>(); vm.Changed += () => events.Add("changed");
        vm.SignalFocusRequested += channel => events.Add($"focus:{channel}");
        vm.Load(new Capture(1_000, 100, [new(0, "Quiet", false, [])], [])); events.Clear();
        vm.MoveProbe(0, 1);
        check(events.Last() == "focus:0" && vm.Hint.Contains("No further") && vm.A == null,
            "No-further-edge feedback is followed by a source reveal, not a stale cancelled request");
        events.Clear(); vm.CommitProbe();
        check(events.Last() == "focus:0" && vm.Hint.Length > 0 && vm.A == null,
            "Unavailable keyboard pulse feedback still reveals its source without inventing timing");
        vm.SetMode(2); events.Clear(); vm.CommitProbe();
        check(events.Last() == "focus:0" && vm.A is { Channel: 0, IsEdge: false } && vm.Armed == 1,
            "Keyboard endpoint selection requests its source after the final pending-B notification");
    }
}
