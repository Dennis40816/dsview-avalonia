// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var capture = await DemoCaptureSource.LoadAsync();
var clock = capture.MeasureAt(0, 25025);
Check(clock is { High: false, WidthSamples: 63, PeriodSamples: 125, HighSamples: 62 } && TimingText.Local(clock, capture.SampleRate).Contains("400 kHz"), "Low width and complete same-polarity cycle yield 1.26 us and 400 kHz");
Check(capture.MeasureAt(0, 0).Pulse == null && capture.MeasureAt(0, capture.SampleCount - 1).Pulse == null, "Capture boundaries never substitute a remote pulse");
var smallSignal = new Signal(0, "Test clock", false, [new(10, true), new(30, false), new(60, true), new(80, false), new(110, true)]);
var small = new Capture(1000, 125, [smallSignal], []);
Check(small.MeasureAt(0, 20) is { High: true, WidthSamples: 20, PeriodSamples: 50 } && TimingText.Local(small.MeasureAt(0, 20), small.SampleRate).Contains("20 Hz"), "Changed-rate fixture: 20 ms high pulse, 50 ms period, 20 Hz");
Check(small.MeasureAt(0, 30) is { High: false, WidthSamples: 30, PeriodSamples: 50 }, "Exact transition selects the following pulse");
Check(small.MeasureAt(0, 9).Pulse == null && small.MeasureAt(0, 110).Pulse == null && small.MeasureAt(0, 125).Pulse == null, "Both boundaries and out-of-capture positions remain incomplete");
Check((small with { Signals = [smallSignal with { Edges = [new(10, true), new(30, false)] }] }).MeasureAt(0, 20) is { WidthSamples: 20, PeriodSamples: null }, "Two transitions provide width but no invented period");
Check((small with { Gaps = [new(15, 16)] }).MeasureAt(0, 20).Pulse == null, "Missing samples invalidate a pulse");
Check((small with { Gaps = [new(45, 50)] }).MeasureAt(0, 20) is { WidthSamples: 20, PeriodSamples: null }, "Cycle-only gap preserves width but invalidates frequency");
Check((small with { Gaps = [new(0, 10)] }).MeasureAt(0, 20).PeriodSamples == 50 && (small with { Gaps = [new(30, 31)] }).MeasureAt(0, 20).Pulse == null, "Gap end is observable but a bounding edge at gap start is missing");
Check((small with { Signals = [new(0, "Quiet", true, [])] }).MeasureAt(0, 20).Pulse == null, "Quiet signal has no invented pulse");
Check(capture.Signals.Count == 32 && capture.Signals.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, 32)), "32 physical channels in source order");
Check(capture.Signals.All(s => s.Edges.Zip(s.Edges.Skip(1)).All(p => p.First.Sample < p.Second.Sample && p.First.High != p.Second.High)), "Transition lists are ordered and contain real state changes");
var vm = new Workbench(); vm.Load(capture);
vm.Pick(0, 25025, 5);
Check(vm.A is { Sample: 25000, IsEdge: true } && vm.B is { Sample: 25063, IsEdge: true }, "Adjacent SCL edges preserve 20 ns samples and 1.26 us pulse width");
Check(vm.EditEndpoint(0, 500) && vm.Mode == 0 && vm.A!.IsEdge, "Unchanged endpoint text preserves edge metadata and mode");
var pinned = (vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement);
capture.MeasureAt(8, 38500); vm.Pick(0, 0, 5);
Check((vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement) == pinned, "Pure preview and incomplete click preserve pinned timing state");
vm.SetMode(1); vm.Pick(0, 25063, 5); vm.CancelPick();
Check((vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement) == pinned, "Cancel two-point selection restores prior endpoints and local result");
vm.Pick(0, 25188, 5, 1);
Check(vm.Mode == 1 && vm.PinnedMeasurement == null && vm.A?.Sample == 25000 && vm.B?.Sample == 25188, "Dragging a quick marker becomes manual and preserves the other endpoint");
vm.SetMode(1); vm.Pick(0, 25000, 5); vm.SetRange(0, 50000); vm.Pick(8, 38000, 5);
Check(vm.A is { Channel: 0, Sample: 25000 } && vm.B is { Channel: 8, Sample: 38000 }, "Manual edges can span channels");
var fixedB = vm.B; vm.Arm(0); vm.Pick(0, 25063, 5);
Check(vm.A?.Sample == 25063 && vm.B == fixedB && vm.Armed == null, "Replacing A completes without arming or changing B");
vm.Pick(8, 38750, 5); Check(vm.B == fixedB, "A later ordinary click cannot overwrite B");
var old = vm.B; vm.Arm(1); vm.Pick(8, 20000, 4); Check(vm.B == old, "Missed edge preserves previous endpoint");
vm.SetMode(2); vm.Pick(1, 29006, 0); Check(vm.A is { Channel: 1, Sample: 29006, IsEdge: false }, "Position endpoints retain integer samples");
var packet = capture.Packets.First(p => p.Id == "ctl-2"); var items = capture.ProtocolItems(packet);
Check(items.Count == 16 && items.Count(i => i.Kind == "ack") == 7 && items.Where(i => i.Kind == "ack").All(i => i.Span.End - i.Span.Start == 125), "I2C ACKs have separate one-clock source spans");
Check(items.First().Span.Start == items.First().Span.End && items.Last().Span.Start == items.Last().Span.End, "START and STOP are points");
Check(packet.Value == 0x1235 && packet.Error != null && items.Where(i => i.Kind == "ack").All(i => i.Label == "ACK"), "Application checksum error does not change bus ACKs");
vm.Select(packet); var before = (vm.Start, vm.End, vm.A, vm.B);
vm.SetGroup(new("Reset only", [8], [])); Check(!vm.Packets.Any() && !vm.SelectionInGroup && (vm.Start, vm.End, vm.A, vm.B) == before, "Raw-only group has no unrelated packets and preserves timing context");
vm.ShowAll(); Check(vm.Signals.Count == 32 && vm.Packets.Count() == 12, "Explicit All signals exposes the complete source");
vm.SetRange(-400, 600); Check(vm.Start == 0 && vm.End == 1000, "Panning clamps without shrinking viewport");
vm.SetRange(49500, 50500); Check(vm.Start == 49000 && vm.End == 50000, "Right capture boundary preserves viewport span");
vm.Search("0x1235"); Check(vm.Packets.Single().Id == "ctl-2", "Hex search matches interpreted payload");
vm.Load(small);
Check(vm.A == null && vm.B == null && vm.PinnedMeasurement == null && vm.Selected == null && vm.Armed == null && vm.Start == 0 && vm.End == 125, "Replacing capture clears old source measurements, selection and view");
vm.Previous(); Check(vm.Start == 0 && vm.End == 125, "Old-source navigation history cannot return after loading another capture");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await DemoCaptureSource.LoadAsync(cancelled.Token); throw new Exception("Cancellation was ignored"); } catch (OperationCanceledException) { Check(true, "Background source loading honors cancellation"); }
Check(capture.Activity(8, 0, 7250).Edges == 0 && capture.Activity(8, 7250, 38000).Edges == 1 && capture.Activity(8, 38000, 38750).Edges == 1 && capture.Activity(8, 38750, 50000).Edges == 1, "Activity uses half-open windows without counting boundary edges twice");
Check(capture.Activity(8, 100, 7250) is { Edges: 0, Complete: true, ConstantLevel: false } && capture.Activity(8, 7251, 38000).ConstantLevel == true && capture.Activity(8, 38001, 38750).ConstantLevel == false, "Constant levels follow observed transitions, not just initial state");
Check(capture.Activity(8, 0, 50000).Edges == 3 && capture.Activity(8, 23850, 33275) is { Edges: 0, ConstantLevel: true } && capture.Activity(9, 0, 50000) is { Edges: 0, Complete: true, ConstantLevel: true }, "RESET is active in capture but quiet in packet view; PWR_GOOD is quiet throughout");
var missing = capture with { Gaps = [new(38000, 38750)] };
Check(missing.Activity(8, 37000, 40000) is { Edges: 1, Complete: false, ConstantLevel: null } && missing.Activity(8, 38750, 40000) is { Edges: 1, Complete: true }, "Missing edges are excluded, while a transition at gap end remains observed");
var uncertain = capture with { Gaps = [new(100, 200)] };
Check(uncertain.Activity(9, 200, 300) is { Edges: 0, Complete: true, ConstantLevel: null } && uncertain.UnknownLevelSpans(capture.Signals[9]).Single() == new SourceSpan(100, 50000), "Complete coverage after a gap does not invent a constant level");
var restored = uncertain with { Signals = [new Signal(9, "Recovered", true, [new(150, false), new(250, false)])] };
Check(restored.Activity(9, 201, 250).ConstantLevel == null && restored.Activity(9, 251, 300).ConstantLevel == false && restored.UnknownLevelSpans(restored.Signals[0]).Single().End == 250, "Only a post-gap observed transition restores level evidence");
Check(!capture.Activity(8, 7250, 7250).Complete && !capture.Activity(99, 0, 100).Complete && !capture.Activity(8, -1, 100).Complete, "Invalid activity windows never masquerade as quiet data");
var resetBins = capture.ActivityBins([capture.Signals[8]], 10);
Check(resetBins.Select((active, index) => (active, index)).Where(x => x.active).Select(x => x.index).SequenceEqual(new[] { 1, 7 }) && !capture.ActivityBins([capture.Signals[9]], 10).Any(x => x), "Raw-only overview shows sparse RESET transitions without filling quiet intervals");
Check(capture.Signals.Where(s => new[] { 0, 1, 8 }.Contains(s.Id)).Sum(s => capture.Activity(s.Id, 0, 50000).Edges) == 487 && capture.Signals.Sum(s => capture.Activity(s.Id, 0, 50000).Edges) == 893, "Raw activity includes undecoded controls and all captured channels");
var issuesVm = new Workbench(); issuesVm.Load(capture);
var checksum = issuesVm.Issues.Single(i => i.Diagnostic.Id == "ctl-2/checksum");
Check(issuesVm.Issues.Count == 2 && issuesVm.Issues[0].Diagnostic.Layer == "UART RX" && checksum.Diagnostic.Layer == "DemoFrame" && checksum.Diagnostic.Spans.Single() == new SourceSpan(31375, 32375) && issuesVm.Issues[0].Diagnostic.Spans.Single() == issuesVm.Issues[0].Packet.Span, "Diagnostics own explicit layers and honest field or packet evidence");
var nackPacket = packet with { I2c = new(false, [false, true, true, true, true, true, true]) };
Check(capture.ProtocolItems(nackPacket).First(i => i.Kind == "ack").Label == "NACK" && capture.ProtocolItems(packet with { I2c = null }).All(i => i.Kind != "ack" || i.Label == "?"), "I2C ACK/NACK/unknown comes from wire metadata, not diagnostic type");
issuesVm.Pick(0, 25025, 5); issuesVm.SetRange(23850, 33275); issuesVm.Search("no matches");
var issueOrigin = (issuesVm.Start, issuesVm.End, issuesVm.Group, issuesVm.A, issuesVm.B, issuesVm.PinnedMeasurement);
issuesVm.SelectIssue(issuesVm.Issues[0]);
Check((issuesVm.Start, issuesVm.End, issuesVm.Group, issuesVm.A, issuesVm.B, issuesVm.PinnedMeasurement) == issueOrigin && issuesVm.Issues.Count == 2 && issuesVm.Details, "Inspecting an issue does not navigate; global issues ignore Group and search filters");
issuesVm.LocateIssue(checksum);
Check(issuesVm.Start == 31175 && issuesVm.End == 32575 && issuesVm.A == issueOrigin.A && issuesVm.B == issueOrigin.B && issuesVm.PinnedMeasurement == issueOrigin.PinnedMeasurement, "Explicit issue navigation frames its field and preserves pinned measurement");
issuesVm.Previous(); Check((issuesVm.Start, issuesVm.End, issuesVm.Group) == (issueOrigin.Start, issueOrigin.End, issueOrigin.Group), "Previous restores the Group and time before issue navigation");
issuesVm.SetGroup(issuesVm.Groups[1]); var revealOrigin = (issuesVm.Start, issuesVm.End, issuesVm.A, issuesVm.B); int? revealed = null; issuesVm.SignalFocusRequested += channel => revealed = channel;
issuesVm.ShowSignal(8);
Check(revealed == 8 && issuesVm.Group.Channels.Contains(8) && (issuesVm.Start, issuesVm.End, issuesVm.A, issuesVm.B) == revealOrigin, "Show signal reveals a raw source without changing time or endpoints");
issuesVm.SetMode(1); issuesVm.Pick(0, 25063, 5); issuesVm.LocateIssue(issuesVm.Issues[0]); issuesVm.CancelPick();
Check(issuesVm.A == issueOrigin.A && issuesVm.B == issueOrigin.B && issuesVm.PinnedMeasurement == issueOrigin.PinnedMeasurement, "Issue navigation preserves pending measurement cancellation origin");
var stale = checksum; issuesVm.Load(capture with { Packets = capture.Packets.Select(p => p with { }).ToArray() }); issuesVm.LocateIssue(stale);
Check(issuesVm.Selected == null && issuesVm.SelectedIssue == null && issuesVm.Start == 0 && issuesVm.End == 50000, "Stale issue identity cannot navigate a replacement capture with equal packet values");
var gapVm = new Workbench(); gapVm.Load(missing); gapVm.SetMode(1); gapVm.Pick(8, 38000, 5);
Check(gapVm.A == null && gapVm.Armed == 0, "Manual edge snapping rejects an unobserved transition");
gapVm.Pick(8, 38000, 800); Check(gapVm.A is { Sample: 38750, IsEdge: true }, "Snapping can choose an observed neighboring edge within tolerance");
gapVm.SetRange(37000, 38000); gapVm.StepEdge(8, 1); Check((gapVm.Start + gapVm.End) / 2 == 38750, "Edge navigation skips missing transitions");
for (int i = 0; i < issuesVm.Groups.Count; i++) issuesVm.Groups[i] = issuesVm.Groups[i] with { Decoders = [] };
issuesVm.SetGroup(issuesVm.Groups[1]); issuesVm.LocateIssue(issuesVm.Issues.Single(i => i.Diagnostic.Layer == "DemoFrame"));
Check(issuesVm.Group.Decoders.Contains("i2c") && issuesVm.Signals.Select(s => s.Id).SequenceEqual(new[] { 0, 1 }) && issuesVm.Groups.Contains(issuesVm.Group), "Locate recreates a usable source Group when customization removed every decoder Group");
var alternate = await DemoCaptureSource.LoadAsync("timing-gaps");
Check(alternate.SampleRate == 10_000_000 && alternate.SampleCount == 80_000 && alternate.Gaps.SequenceEqual(new[] { new SourceSpan(40000, 45000) }) && alternate.Signals.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, 32)), "Second embedded capture uses its own rate, duration, missing interval and physical channels");
var altRead = alternate.Packets.Single(p => p.Id == "alt-read-1");
Check(altRead.Span == new SourceSpan(10000, 16500) && TimingText.Packet(altRead, alternate.SampleRate).Contains("1 ms") && !TimingText.Packet(altRead, alternate.SampleRate).Contains("200 µs"), "Packet-list timestamps use the active sample rate");
var altTiming = alternate.MeasureAt(0, 10120);
Check(altTiming is { High: false, WidthSamples: 50, PeriodSamples: 100 } && altTiming.Pulse == new SourceSpan(10100, 10150) && TimingText.Local(altTiming, alternate.SampleRate).Contains("100 kHz"), "Second capture: low 5 us, period 10 us, 100 kHz from stored samples");
var altVm = new Workbench(); altVm.Load(alternate); altVm.SetMode(1); altVm.Pick(0, 10100, 0); altVm.Pick(8, 17000, 0);
Check(altVm.A is { Channel: 0, Sample: 10100 } && altVm.B is { Channel: 8, Sample: 17000 } && altVm.Us(altVm.B.Sample - altVm.A.Sample) == 690 && altVm.PinnedMeasurement == null, "Changed-data cross-channel edge interval is +690 us without a frequency claim");
altVm.SetMode(1); altVm.Pick(8, 17000, 0); altVm.Pick(0, 10100, 0);
Check(altVm.Us(altVm.B!.Sample - altVm.A!.Sample) == -690, "Reversing changed-data endpoints preserves signed delay");
Check(altVm.EditEndpoint(1, 1012.3) && altVm.B is { Sample: 10123, IsEdge: false } && altVm.Hint.Contains("100 ns"), "Changed-rate position editing uses the 100 ns sample grid");
var altItems = alternate.ProtocolItems(altRead);
Check(altRead.I2c is { Read: true } && altRead.Address == "0x51" && altRead.Hex == "A5 21 02 78 56 00" && altRead.Value == 0x5678 && altItems.Count == 16 && altItems[1].Span == new SourceSpan(10100, 10900) && altItems[2].Span == new SourceSpan(10900, 11000) && altItems[^2].Span == new SourceSpan(16300, 16400), "Changed I2C direction, bytes and annotation spans come from fixture evidence");
var altNack = alternate.Packets.Single(p => p.Id == "alt-address-nack");
Check(altItems[^2].Label == "NACK" && TimingText.I2cStatus(altRead) == "I²C · Read ends with NACK" && altRead.Diagnostics.All(d => d.Layer == "DemoFrame") && altNack.Diagnostics.Single().Layer == "I²C" && altNack.Bytes.Length == 0 && TimingText.I2cStatus(altNack) == "I²C · Address NACK", "Normal final-read NACK and address rejection remain distinct");
Check(altRead.Diagnostics.Single().Spans.Single() == new SourceSpan(15500, 16300) && altRead.Fields.Single(f => f.Name == "Payload").Spans.SequenceEqual(new[] { new SourceSpan(13700, 14500), new SourceSpan(14600, 15400) }) && altRead.Bytes[..^1].Aggregate(0, (a, b) => a ^ b) == 0xA8, "Changed application field evidence and declared XOR checksum answer agree");
bool Level(Signal signal, long sample) { int index = signal.LowerBound(sample + 1); return index > 0 ? signal.Edges[index - 1].High : signal.Initial; }
bool consistentWire = true;
foreach (var transfer in new[] { altRead, altNack })
{
    byte[] expectedBytes = [(byte)(transfer.I2c!.Read ? 0xA3 : 0xA2), .. transfer.Bytes];
    for (int cell = 0; cell < expectedBytes.Length; cell++)
    for (int bit = 0; bit < 9; bit++)
    {
        long rising = transfer.WireCells[cell].Data.Start + bit * 100 + 50;
        bool expected = bit == 8 ? transfer.I2c.Acknowledged[cell] == false : (expectedBytes[cell] & (1 << (7 - bit))) != 0;
        consistentWire &= alternate.Signals[0].Edges.Any(e => e.Sample == rising && e.High) && Level(alternate.Signals[1], rising) == expected;
    }
    consistentWire &= Level(alternate.Signals[0], transfer.Span.End) && alternate.Signals[1].Edges.Any(e => e.Sample == transfer.Span.End && e.High);
}
Check(consistentWire, "Synthetic fixture consistency: stored SCL rising edges and SDA values match declared bytes, ACKs and STOP");
Check(alternate.Signals[8].Edges.Select(e => e.Sample).SequenceEqual(new long[] { 9000, 17000, 18000, 47000 }) && alternate.UnknownLevelSpans(alternate.Signals[8]).Single() == new SourceSpan(40000, 47000), "Missing transition is omitted and explicit post-gap transition survives equal observed levels");
Check(alternate.Activity(8, 45000, 47000) is { Edges: 0, Complete: true, ConstantLevel: null } && alternate.Activity(8, 47001, 80000).ConstantLevel == true && alternate.Activity(9, 45000, 80000).ConstantLevel == null, "Post-gap activity distinguishes unknown level from recovered High");
Check(alternate.MeasureAt(9, 42000).Reason == "Missing samples at this position" && alternate.MeasureAt(8, 46000).Pulse == null, "Quiet missing samples and unsupported recovery pulse never become valid timing");
altVm.SetMode(2); altVm.Pick(9, 42000, 0);
Check(altVm.A is { Sample: 42000, IsEdge: false } && !alternate.HasSample(altVm.A.Sample), "A position inside missing coverage remains a time coordinate, never an observed edge");
altVm.Search("unmatched prior capture"); altVm.SetTab(true); altVm.Load(capture); altVm.CancelPick(); altVm.Previous();
Check(altVm.Query == "" && !altVm.PacketsTab && altVm.A == null && altVm.B == null && altVm.SelectedIssue == null && altVm.Start == 0 && altVm.End == 50000 && altVm.Signals[0].Edges.Count == capture.Signals[0].Edges.Count, "Opening another capture clears search, tab, pending picks and history and refreshes source scope");
Check(alternate.ProtocolItems(altRead with { WireCells = [] }).Count == 1, "Missing detailed timing metadata does not fabricate byte or ACK extents");
Console.WriteLine($"{passed} focused checks passed.");
