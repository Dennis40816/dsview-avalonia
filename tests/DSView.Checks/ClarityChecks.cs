// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using DSView.Desktop;
using DSView.Domain;

internal static class ClarityChecks
{
    public static void Run(Action<bool, string> check, Capture board, Capture dense)
    {
        var ns = TimeAxis.Create(0, 10, 50_000_000, 600);
        check(ns.Unit == "ns" && ns.RangeText == "0–200 ns" && ns.Ticks.Select(t => t.Label).Distinct().Count() == ns.Ticks.Count, "Sample-scale ruler uses distinct nanosecond labels");
        check(TimeAxis.FormatRange(0, 80_000, 10_000_000) == "0–8 ms" && TimeAxis.FormatRange(0, 6_000_000, 50_000_000) == "0–120 ms", "Long recording ranges use a single readable millisecond unit");
        var offset = TimeAxis.Create(1_000_000_010, 1_000_000_210, 1_000_000_000, 600);
        check(offset.Unit == "ns" && offset.OriginText == "1 s" && offset.RangeText == "1 s + (10–210 ns)" && offset.Ticks.Select(t => t.Sample).SequenceEqual(new[] { 1_000_000_050d, 1_000_000_100d, 1_000_000_150d, 1_000_000_200d }), "Fine ruler near a large timestamp keeps exact source positions and declares its offset");
        var longStart = TimeAxis.Create(1_000_000_000_000_011d, 1_000_000_000_000_021d, 1_000_000_000, 600);
        check(longStart.RangeText == "1000000 s + (11–21 ns)" && longStart.Ticks.Select(t => t.Sample).SequenceEqual(new[] { 1_000_000_000_000_012d, 1_000_000_000_000_014d, 1_000_000_000_000_016d, 1_000_000_000_000_018d, 1_000_000_000_000_020d }), "Ruler conversion preserves integer sample digits beyond 15 decimal digits");
        var wide = TimeAxis.Create(0, long.MaxValue, 50_000_000, 600);
        check(wide.Ticks.Count is > 0 and <= 64 && wide.Ticks.All(t => double.IsFinite(t.Sample)), "Formatting the full Int64 capture extent does not overflow; per-sample precision above 2^53 is not asserted");
        check(TimeAxis.FormatRange(999, 1001, 1_000_000_000) == "999–1001 ns", "A range crossing a unit boundary never mixes endpoint units");
        bool bounded = true;
        foreach (double width in new[] { 1d, 80, 600, 1600 })
        foreach (var range in new[] { (0d, 10d), (12.25, 321.75), (1_000_000_010.25, 1_000_000_210.75) })
        {
            var axis = TimeAxis.Create(range.Item1, range.Item2, 1_000_000_000, width);
            bounded &= axis.Ticks.Count is > 0 and <= 64 && axis.Ticks.All(t => double.IsFinite(t.Sample) && t.Sample >= range.Item1 && t.Sample <= range.Item2)
                && axis.Ticks.Zip(axis.Ticks.Skip(1)).All(p => p.First.Sample < p.Second.Sample && p.First.Label != p.Second.Label);
        }
        check(bounded, "Fractional and narrow viewports produce finite ordered distinct ticks with bounded work");
        bool roundTrips = true;
        foreach (long rate in new long[] { 3_000_000, 50_000_000, 2_000_000_000 })
        foreach (long sample in new long[] { 1, 7, 125, 100_001 })
        {
            var source = new Capture(rate, 200_000, [new(0, "Test", false, [new(sample, true)])], []);
            var work = new Workbench(); work.Load(source); work.SetMode(1); work.Pick(0, sample, 1);
            var a = work.A; var armed = work.Armed;
            decimal value = decimal.Parse(TimeAxis.Microseconds(sample, rate), CultureInfo.InvariantCulture);
            roundTrips &= work.EditEndpointTime(0, value) && work.A == a && work.Armed == armed && work.A is { IsEdge: true };
        }
        check(roundTrips, "Stable microsecond input round-trips samples and preserves pending B at decimal and nondecimal rates");
        const long farSample = 4_500_000_000_000_011;
        var farSource = new Capture(1_000_000_000, farSample + 100, [new(0, "Far edge", false, [new(farSample, true)])], []);
        var precise = new Workbench(); precise.Load(farSource); precise.SetMode(1); precise.Pick(0, farSample, 1);
        check(precise.EditEndpointTime(0, decimal.Parse(TimeAxis.Microseconds(farSample, farSource.SampleRate), CultureInfo.InvariantCulture)) && precise.A is { Sample: farSample, IsEdge: true } && precise.Armed == 1, "Decimal endpoint input preserves a large sample and pending selection without double rounding");
        var lastSource = new Capture(3_000_000, 12, [new(0, "Last edge", false, [new(11, true)])], []);
        precise.Load(lastSource); precise.SetMode(1); precise.Pick(0, 11, 1);
        check(precise.EditEndpointTime(0, decimal.Parse(TimeAxis.Microseconds(11, lastSource.SampleRate), CultureInfo.InvariantCulture)) && precise.A is { Sample: 11, IsEdge: true } && !precise.EditEndpointTime(0, 4m), "Rounded display of the final sample remains valid while a time beyond the capture is rejected");
        check(TimingText.Duration(.000690) == "690 µs" && TimingText.Duration(-.000690) == "-690 µs" && TimingText.Duration(-0d) == "0 µs", "Signed measurement durations remain independent of ruler offsets");

        var vm = new Workbench(); check(vm.PacketView().EmptyReason == PacketEmptyReason.NoCapture, "No capture is distinct from an empty decoded result");
        vm.Load(dense); vm.Search("missing");
        check(vm.PacketView() is { EmptyReason: PacketEmptyReason.NoRecords, Items.Count: 0 }, "No-record capture explanation takes precedence over Group and search");
        vm.Load(board); vm.Pick(0, 25025, 5); vm.SelectIssue(vm.Issues.First(i => i.Packet.Decoder == "i2c")); vm.SetTab(true);
        var selected = vm.Selected; var issue = vm.SelectedIssue; var pinned = vm.PinnedMeasurement; var beforeA = vm.A; var beforeB = vm.B; var start = vm.Start; var end = vm.End;
        vm.Search("no-such-packet-query");
        check(vm.PacketView().EmptyReason == PacketEmptyReason.Search && vm.PacketSelectionContext.Contains("outside this search") && ReferenceEquals(vm.Selected, selected) && vm.Details, "An excluded selected packet remains inspectable and explains its search exclusion");
        vm.Search("");
        check(vm.PacketView().Items.Any(p => ReferenceEquals(p, selected)) && vm.PacketSelectionContext == "" && ReferenceEquals(vm.SelectedIssue, issue) && ReferenceEquals(vm.PinnedMeasurement, pinned) && vm.A == beforeA && vm.B == beforeB && vm.Start == start && vm.End == end, "Clearing search restores the same row without losing diagnostic, timing or navigation context");
        vm.SetGroup(new("Raw clocks", [0, 1], [])); vm.Search("no-such-packet-query");
        check(vm.PacketView().EmptyReason == PacketEmptyReason.Group && vm.PacketSelectionContext == "Selected packet is outside this Group.", "Empty decoder scope takes precedence over search even when source channels are in the Group");
        vm.ShowAllPackets();
        check(vm.IsAllSignals && vm.PacketsTab && vm.Query == "" && vm.PacketView().Items.Count == board.Packets.Count && ReferenceEquals(vm.Selected, selected) && ReferenceEquals(vm.SelectedIssue, issue) && ReferenceEquals(vm.PinnedMeasurement, pinned) && vm.A == beforeA && vm.B == beforeB && vm.Start == start && vm.End == end, "Show all packets explicitly clears both filters while retaining the Packet tab and analysis");
        vm.Search("no-such-packet-query"); vm.Load(dense);
        check(vm.PacketView().EmptyReason == PacketEmptyReason.NoRecords && vm.Selected == null && vm.PacketSelectionContext == "" && vm.RangeText == "0–120 ms", "Replacing capture refreshes result state and time scale without retaining stale selection");
    }
}
